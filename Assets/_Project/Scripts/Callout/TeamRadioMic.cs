using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace OnYourMark
{
    // Push-to-talk. Only the host needs an STT credential. Clients send bounded PCM chunks.
    public partial class CalloutDirector
    {
        const int SampleRate = 16000, MaxSamples = SampleRate * 6, Chunk = 896;
        AudioClip recording;
        string device, micStatus;
        float recordStart, statusUntil;
        bool sending;
        int speechSequence;
        sealed class Incoming
        {
            public byte[] pcm;
            public int received, sequence;
            public double started;
            public Player owner;
        }
        readonly Dictionary<ulong, Incoming> incoming = new();
        readonly HashSet<ulong> transcribing = new();
        readonly Dictionary<ulong, double> micAllowed = new();
        [Serializable] class Transcript { public string text; }

        void MicUpdate()
        {
            if (!IsClient || !Player.Local || !Player.Local.Partner || Keyboard.current == null) { StopMic(); return; }
            var key = Keyboard.current.vKey;
            if (!recording && !sending && key.wasPressedThisFrame)
            {
                if (Microphone.devices.Length == 0) { Notice("사용 가능한 마이크가 없습니다."); return; }
                device = Microphone.devices[0];
                recording = Microphone.Start(device, false, 7, SampleRate);
                recordStart = Time.realtimeSinceStartup;
                if (!recording) Notice("마이크를 열 수 없습니다. Windows 마이크 권한을 확인하세요.");
            }
            if (recording && (!key.isPressed || Time.realtimeSinceStartup - recordStart >= 6f))
            {
                int frames = Math.Min(MaxSamples, Microphone.GetPosition(device));
                if (frames < SampleRate / 5) { StopMic(); return; }
                var samples = new float[frames * recording.channels];
                if (!recording.GetData(samples, 0)) { StopMic(); Notice("녹음 데이터를 읽지 못했습니다."); return; }
                int channels = recording.channels;
                StopMic();
                var bytes = new byte[frames * 2];
                for (int i = 0; i < frames; i++)
                {
                    float v = 0; for (int c = 0; c < channels; c++) v += samples[i * channels + c];
                    short s = (short)(Mathf.Clamp(v / channels, -1, 1) * 32767);
                    bytes[i * 2] = (byte)s; bytes[i * 2 + 1] = (byte)(s >> 8);
                }
                StartCoroutine(SendMic(bytes));
            }
        }
        void StopMic()
        {
            if (!recording) return;
            Microphone.End(device); Destroy(recording); recording = null;
        }
        void Notice(string text) { micStatus = text; statusUntil = Time.unscaledTime + 4; }
        void OnGUI()
        {
            if (!IsSpawned || !Player.Local || !Player.Local.Partner) return;
            string text = recording ? "● 녹음 중 · V를 놓아 전송" : Time.unscaledTime < statusUntil ? micStatus : "V 길게 · 팀 무전";
            GUI.Box(new Rect(16, Screen.height - 48, 330, 32), text);
        }
        IEnumerator SendMic(byte[] data)
        {
            sending = true; Notice("음성 인식 중…");
            int seq = ++speechSequence;
            for (int offset = 0; offset < data.Length && IsSpawned; offset += Chunk)
            {
                var part = new byte[Math.Min(Chunk, data.Length - offset)];
                Buffer.BlockCopy(data, offset, part, 0, part.Length);
                MicRpc(seq, data.Length, offset, part);
                if ((offset / Chunk) % 6 == 5) yield return null;
            }
            sending = false;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void MicRpc(int seq, int length, int offset, byte[] part, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (part == null || part.Length < 1 || part.Length > Chunk || length < SampleRate / 5 * 2 || length > MaxSamples * 2 ||
                (length & 1) != 0 || offset < 0 || offset > length - part.Length || !NetworkManager.ConnectedClients.TryGetValue(id, out var client) ||
                !client.PlayerObject || !client.PlayerObject.TryGetComponent<Player>(out var owner) || !owner.Partner) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (offset == 0)
            {
                if (transcribing.Contains(id) || micAllowed.TryGetValue(id, out var allowed) && now < allowed) return;
                micAllowed[id] = now + 2;
                incoming[id] = new Incoming { pcm = new byte[length], sequence = seq, started = now, owner = owner };
            }
            if (!incoming.TryGetValue(id, out var input) || input.owner != owner || input.sequence != seq ||
                input.received != offset || input.pcm.Length != length || now - input.started > 12) return;
            Buffer.BlockCopy(part, 0, input.pcm, offset, part.Length); input.received += part.Length;
            if (input.received != length) return;
            incoming.Remove(id); StartCoroutine(Transcribe(id, owner, input.pcm));
        }
        static byte[] Wave(byte[] pcm)
        {
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(pcm.Length + 36);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
            return output.ToArray();
        }
        IEnumerator Transcribe(ulong id, Player speaker, byte[] pcm)
        {
            if (!settings || !settings.TryGetSpeechKey(out var key)) { MicFeedbackRpc(id, "호스트의 음성 인식 키 설정을 확인하세요."); yield break; }
            transcribing.Add(id);
            try
            {
                var form = new List<IMultipartFormSection>{new MultipartFormDataSection("model_id","scribe_v2"),
                    new MultipartFormFileSection("file",Wave(pcm),"radio.wav","audio/wav")};
                using var req = UnityWebRequest.Post("https://api.elevenlabs.io/v1/speech-to-text", form);
                req.SetRequestHeader("xi-api-key", key); req.timeout = 15; yield return req.SendWebRequest();
                if (!speaker || !speaker.IsSpawned) yield break;
                if (req.result != UnityWebRequest.Result.Success) { MicFeedbackRpc(id, "음성 인식 실패 (" + req.responseCode + ")"); yield break; }
                string heard = JsonUtility.FromJson<Transcript>(req.downloadHandler.text)?.text?.Trim();
                if (string.IsNullOrEmpty(heard)) { MicFeedbackRpc(id, "인식된 말이 없습니다."); yield break; }
                if (heard.Length > 450) heard = heard.Substring(0, 450);
                QueueRadio("Player radio from " + speaker.Role + ": " + heard, 4);
                MicFeedbackRpc(id, heard);
            }
            finally { transcribing.Remove(id); }
        }
        [Rpc(SendTo.ClientsAndHost)]
        void MicFeedbackRpc(ulong client, string text)
        {
            if (NetworkManager.LocalClientId == client) Notice(text);
        }
    }
}
