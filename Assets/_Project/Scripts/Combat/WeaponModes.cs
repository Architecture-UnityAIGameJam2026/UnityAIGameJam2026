using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// P2 E 스킬 — 무기 모드 전환 (따발총 ↔ 레일건).
    /// E를 누르면 windup 동안 변형하고(사격 · 줌 · 대쉬 불가, 감속), 끝나면 모드가 바뀐다.
    /// 탄창은 모드마다 따로 남아 있고, 바뀐 모드의 장탄수가 AmmoPool 용량이 된다.
    /// </summary>
    public class WeaponModes : NetworkBehaviour
    {
        [Tooltip("0: 따발총, 1: 레일건 (시작은 0)")]
        [SerializeField] HitscanData[] modes;
        [SerializeField] WeaponSwitchData data;
        [SerializeField] InputActionReference skill;
        [SerializeField] AmmoPool magazine;

        readonly NetworkVariable<int> mode = new();
        readonly NetworkVariable<double> switchEndsAt = new();
        readonly NetworkVariable<double> readyAt = new();

        Player me;
        int[] stored;      // 서버: 모드별 남은 탄
        bool pending;      // 서버: 변형 진행 중


        double Now => NetworkManager.ServerTime.Time;
        public HitscanData Current => modes[Mathf.Clamp(mode.Value, 0, modes.Length - 1)];
        public int ModeIndex => mode.Value;
        public bool IsSwitching => IsSpawned && Now < switchEndsAt.Value;
        public float SwitchLeft => IsSpawned ? Mathf.Max(0f, (float)(switchEndsAt.Value - Now)) : 0f;
        public float CooldownLeft => IsSpawned ? Mathf.Max(0f, (float)(readyAt.Value - Now)) : 0f;
        public HitscanData Next => modes[(mode.Value + 1) % modes.Length];
        public WeaponSwitchData Data => data;

        /// <summary>실제 변형 선딜레이: 3번 파츠 반환 후 한계 돌파 각성이 있으면 그 값 (0.4초)</summary>
        public float Windup
        {
            get
            {
                float o = GetComponent<AwakeningSet>().SwitchWindupOverride;
                return o > 0f ? o : data.windup;
            }
        }

        void Awake() => me = GetComponent<Player>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            stored = new int[modes.Length];
            for (int i = 0; i < modes.Length; i++) stored[i] = modes[i].magazine;
            mode.Value = 0;
            magazine.SetMagazine(modes[0].magazine, stored[0]);
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                bool switching = IsSwitching;
                me.Motor.ZoomAllowed = Current.allowZoom && !switching;
                me.Motor.DashLocked = switching;
                me.Motor.SpeedOverride = switching ? data.moveSpeedWhileSwitching : -1f;

                if (CursorLock.IsLocked && skill.action.WasPressedThisFrame() && !switching && CooldownLeft <= 0f)
                    SwitchRpc();
            }

            if (IsServer && pending && Now >= switchEndsAt.Value) Complete();
        }

        [Rpc(SendTo.Server)]
        void SwitchRpc()
        {
            if (pending || CooldownLeft > 0f || me.Health.IsDead) return;
            pending = true;
            switchEndsAt.Value = Now + Windup;
        }

        void Complete()
        {
            pending = false;
            stored[mode.Value] = magazine.Current;
            mode.Value = (mode.Value + 1) % modes.Length;
            magazine.SetMagazine(Current.magazine, stored[mode.Value]);
            readyAt.Value = Now + data.cooldown;
        }
    }
}
