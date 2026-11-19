using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 플레이어 한 명에게서 나는 소리. 모든 클라이언트에서 같은 방식으로 상태 변화를 보고 소리를 낸다
    /// (원격 플레이어 · 동료 AI 포함, 위치 변화와 동기화된 값만 쓴다).
    /// 발소리 · 점프 / 착지 · 제트 분사 · 대쉬 · 실드 유지 · 피격 · 빈사 · 소생 · 패링 시작 · 무기 변형 · 궁극기 발동,
    /// 내 화면 전용: 줌 · 맵 밖 복귀 · 빈사 경고 · Q 준비 완료.
    /// </summary>
    public class PlayerSounds : MonoBehaviour
    {
        [SerializeField] LayerMask groundMask;
        [Tooltip("이 속도(m/s)를 넘으면 발소리")]
        [SerializeField] float footstepSpeed = 1.5f;
        [Tooltip("이 속도(m/s)를 넘어서는 순간을 대쉬로 본다")]
        [SerializeField] float dashSpeed = 11f;
        [Tooltip("이 시간 이상 떠 있다가 내려오면 착지음")]
        [SerializeField] float landingAirTime = 0.35f;

        Player player;
        Parry parry;
        WeaponModes modes;
        Reviver reviver;
        AudioSource footsteps, jetpack, shield, downed, revive;
        Vector3 lastPosition;
        float speed, airTime, lastHealth = -1f, lastHurt;
        bool wasAirborne, wasDashing, wasDead, wasParry, wasSwitching, wasZoom, wasUltimate, wasReady, initialized;

        void Awake()
        {
            player = GetComponent<Player>();
            parry = GetComponent<Parry>();
            modes = GetComponent<WeaponModes>();
            reviver = GetComponent<Reviver>();
            footsteps = Sfx.CreateLoop(SoundId.Footsteps, transform);
            jetpack = Sfx.CreateLoop(SoundId.JetpackThrust, transform);
            shield = Sfx.CreateLoop(SoundId.ShieldHold, transform);
            revive = Sfx.CreateLoop(SoundId.ReviveProgress, transform);
            downed = Sfx.CreateLoop(SoundId.DownedWarning, transform);
        }

        void OnEnable() { if (player && player.Motor) player.Motor.Recovered += OnRecovered; }
        void OnDisable() { if (player && player.Motor) player.Motor.Recovered -= OnRecovered; }

        bool Mine => player == Player.Local;   // 이 화면의 사람 플레이어

        void Update()
        {
            if (!player || !player.IsSpawned) { Silence(); initialized = false; return; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 pos = transform.position;
            if (!initialized) { initialized = true; lastPosition = pos; lastHealth = player.Health.Current; wasDead = player.Health.IsDead; }

            Vector3 delta = pos - lastPosition; lastPosition = pos;
            float vertical = delta.y / dt;
            delta.y = 0f;
            speed = Mathf.Lerp(speed, delta.magnitude / dt, 1f - Mathf.Exp(-12f * dt));
            bool dead = player.Health.IsDead;
            bool airborne = !Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, 0.5f, groundMask, QueryTriggerInteraction.Ignore);
            bool dashing = speed > dashSpeed;

            // 이동
            footsteps.SetPlaying(!dead && !airborne && speed > footstepSpeed && !dashing);
            if (footsteps.isPlaying) footsteps.pitch = Mathf.Clamp(speed / 6f, 0.8f, 1.25f);
            if (airborne && !wasAirborne && vertical > 2f) Sfx.Play(SoundId.Jump, pos);
            if (!airborne && wasAirborne && airTime > landingAirTime) Sfx.Play(SoundId.Landing, pos);
            airTime = airborne ? airTime + dt : 0f;
            bool flying = Mine ? player.Motor.IsFlying : airborne && vertical > -5f && airTime > 0.25f;
            jetpack.SetPlaying(!dead && flying);
            if (dashing && !wasDashing) Sfx.Play(SoundId.DashIgnite, pos);

            // 체력 · 실드 · 빈사 · 소생
            float hp = player.Health.Current;
            if (hp < lastHealth - 0.01f && !dead && Time.time - lastHurt > 0.2f) { lastHurt = Time.time; Sfx.Play(SoundId.PlayerHit, pos + Vector3.up); }
            if (!dead && wasDead) Sfx.Play(SoundId.ReviveComplete, pos + Vector3.up);
            shield.SetPlaying(!dead && player.Health.Shield > 0f);
            downed.SetPlaying(dead && Mine);
            revive.SetPlaying(reviver && reviver.Progress01 > 0f);

            // 스킬
            bool parrying = parry && parry.IsActive;
            if (parrying && !wasParry) Sfx.Play(SoundId.ParryStart, pos + Vector3.up);
            bool switching = modes && modes.IsSwitching;
            if (switching && !wasSwitching) Sfx.Play(SoundId.WeaponTransform, pos + Vector3.up);
            bool zoom = Mine && player.Motor.IsZooming;
            if (zoom && !wasZoom) Sfx.Play(SoundId.ZoomFocus);
            UltimateSounds(pos);

            wasAirborne = airborne; wasDashing = dashing; wasDead = dead; lastHealth = hp;
            wasParry = parrying; wasSwitching = switching; wasZoom = zoom;
        }

        void UltimateSounds(Vector3 pos)
        {
            var u = player.Ultimate;
            if (!u) return;
            bool active = u.IsActive;
            if (active && !wasUltimate)
            {
                if (u.Data is NanoBoostUltimate) Sfx.Play(u.ActiveLeft < u.Data.duration * 0.6f ? SoundId.NanoBoostMini : SoundId.NanoBoost, pos + Vector3.up);
                else Sfx.Play(SoundId.SmartLockOn, pos + Vector3.up);
            }
            wasUltimate = active;

            // 내가 Q로 발동할 수 있는 궁극기가 막 찼을 때 (3번 파츠 반환 전에는 파트너의 궁극기)
            if (!Mine) return;
            var trigger = u.Crossed ? (player.Partner ? player.Partner.Ultimate : null) : u;
            bool ready = trigger && trigger.IsReady && !trigger.IsActive;
            if (ready && !wasReady) Sfx.Play(SoundId.UltimateReady);
            wasReady = ready;
        }

        void OnRecovered() { if (Mine) Sfx.Play(SoundId.OutOfBoundsReturn); }

        void Silence()
        {
            foreach (var s in new[] { footsteps, jetpack, shield, downed, revive }) s.SetPlaying(false);
        }
    }
}
