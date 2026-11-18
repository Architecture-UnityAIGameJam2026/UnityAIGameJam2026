using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 단말기 해금. 웨이브 사이에만 켜지고, 두 플레이어가 모두 단말기 영역 안에서
    /// 동시에 E를 누르고 있으면 진행도가 차서 남은 상대 파츠를 전부 돌려받는다.
    /// 한 명이라도 떼거나 영역을 벗어나면 진행도는 0으로 돌아간다.
    /// </summary>
    public class UnlockTerminal : NetworkBehaviour
    {
        [SerializeField] UnlockConfig config;
        [SerializeField] InputActionReference interact;
        [SerializeField] Collider area;
        [SerializeField] Renderer screen;
        [SerializeField] Color lockedColor = new(0.35f, 0.35f, 0.38f);
        [SerializeField] Color readyColor = new(0.95f, 0.45f, 0.1f);

        readonly NetworkVariable<float> progress = new();
        readonly HashSet<ulong> holders = new();   // 서버: E를 누르고 있는 클라이언트

        CrossHudState state;
        WaveManager waves;

        public bool IsAvailable => waves != null && waves.BetweenWaves && !state.AllReturned;
        public float Progress01 => progress.Value / config.terminalHoldTime;
        public bool IsInside(Player p) => area.bounds.Contains(p.transform.position + Vector3.up * 0.5f);

        public override void OnNetworkSpawn()
        {
            state = FindFirstObjectByType<CrossHudState>();
            waves = FindFirstObjectByType<WaveManager>();
        }

        void Update()
        {
            if (!IsSpawned) return;
            screen.material.color = IsAvailable ? readyColor : lockedColor;

            if (Player.Local != null && CursorLock.IsLocked)
            {
                if (interact.action.WasPressedThisFrame()) HoldRpc(true);
                if (interact.action.WasReleasedThisFrame()) HoldRpc(false);
            }

            if (!IsServer) return;
            progress.Value = EveryoneHolding() ? progress.Value + Time.deltaTime : 0f;
            if (progress.Value >= config.terminalHoldTime)
            {
                for (int i = 0; i < 3; i++) state.Return((HudPart)i);
                progress.Value = 0f;
            }
        }

        bool EveryoneHolding()
        {
            if (!IsAvailable || Player.All.Count < 2) return false;
            foreach (var p in Player.All)
                if (!holders.Contains(p.OwnerClientId) || !IsInside(p)) return false;
            return true;
        }

        [Rpc(SendTo.Server)]
        void HoldRpc(bool down, RpcParams rpc = default)
        {
            if (down) holders.Add(rpc.Receive.SenderClientId);
            else holders.Remove(rpc.Receive.SenderClientId);
        }
    }
}
