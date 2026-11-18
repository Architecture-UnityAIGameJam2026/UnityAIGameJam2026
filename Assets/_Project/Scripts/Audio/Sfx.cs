using System.Collections.Generic;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 효과음 재생 창구. 각 클라이언트에서 자기 화면 기준으로 소리를 낸다 (네트워크로 보내는 건 SoundRelay).
    /// - Play(id, 위치): 한 번 재생 (3D 설정이면 그 위치에서)
    /// - CreateLoop(id, 붙일 대상): 반복 소리용 AudioSource를 만들어 준다 (켜고 끄는 건 호출한 쪽)
    /// 한 번 소리는 미리 만든 AudioSource 묶음을 돌려 쓴다.
    /// </summary>
    public static class Sfx
    {
        const int PoolSize = 24;
        static SoundLibrary library;
        static readonly List<AudioSource> pool = new();
        static readonly Dictionary<SoundId, float> lastPlayed = new();
        static int next;

        static SoundLibrary Library => library ? library : library = Resources.Load<SoundLibrary>("SoundLibrary");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pool.Clear(); lastPlayed.Clear(); next = 0; }

        public static void Play(SoundId id) => Play(id, Camera.main ? Camera.main.transform.position : Vector3.zero);

        public static void Play(SoundId id, Vector3 position, float volumeScale = 1f)
        {
            var e = Library ? Library.Get(id) : null;
            if (e == null || !e.clip) return;
            if (e.minInterval > 0f && lastPlayed.TryGetValue(id, out var t) && Time.unscaledTime - t < e.minInterval) return;
            lastPlayed[id] = Time.unscaledTime;

            var src = NextSource();
            src.transform.position = position;
            Apply(src, e);
            src.pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
            src.PlayOneShot(e.clip, e.volume * volumeScale);
        }

        /// <summary>target에 붙는 반복 재생 AudioSource. 처음엔 멈춘 상태다.</summary>
        public static AudioSource CreateLoop(SoundId id, Transform target)
        {
            var e = Library ? Library.Get(id) : null;
            var go = new GameObject($"Loop_{id}");
            go.transform.SetParent(target, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.loop = true;
            if (e != null) { src.clip = e.clip; Apply(src, e); src.volume = e.volume; }
            return src;
        }

        /// <summary>반복 소리를 켜거나 끈다 (이미 그 상태면 그대로).</summary>
        public static void SetPlaying(this AudioSource loop, bool on)
        {
            if (!loop || !loop.clip) return;
            if (on && !loop.isPlaying) loop.Play();
            else if (!on && loop.isPlaying) loop.Stop();
        }

        static void Apply(AudioSource src, SoundLibrary.Entry e)
        {
            src.spatialBlend = e.spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = e.minDistance;
            src.maxDistance = e.maxDistance;
            src.dopplerLevel = 0f;
        }

        static AudioSource NextSource()
        {
            pool.RemoveAll(s => !s);
            if (pool.Count < PoolSize)
            {
                var go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                var s = go.AddComponent<AudioSource>(); s.playOnAwake = false;
                pool.Add(s);
                return s;
            }
            next = (next + 1) % pool.Count;
            return pool[next];
        }
    }
}
