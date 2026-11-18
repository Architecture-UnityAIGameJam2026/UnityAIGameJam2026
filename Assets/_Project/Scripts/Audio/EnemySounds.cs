using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 적에게서 나는 소리. 모든 클라이언트에서 동기화된 상태 변화를 보고 소리를 낸다.
    /// 등장 · 피격 · 사망, Rusher 이동 루프 · 자폭 카운트다운, 레이저 적 충전.
    /// (Rusher 폭발 · 레이저 발사는 각 적의 연출 RPC에서 낸다)
    /// </summary>
    public class EnemySounds : NetworkBehaviour
    {
        Health health;
        Rusher rusher;
        HoverLaser laser;
        AudioSource moveLoop;
        float lastHealth, lastHit;
        bool wasFusing, wasCharging;

        void Awake()
        {
            health = GetComponent<Health>();
            rusher = GetComponent<Rusher>();
            laser = GetComponent<HoverLaser>();
            if (rusher) moveLoop = Sfx.CreateLoop(SoundId.RusherMove, transform);
        }

        public override void OnNetworkSpawn()
        {
            lastHealth = health.Current;
            wasFusing = wasCharging = false;
            Sfx.Play(SoundId.EnemySpawn, transform.position + Vector3.up);
        }

        public override void OnNetworkDespawn()
        {
            moveLoop.SetPlaying(false);
            if (health && health.IsDead && !(rusher && wasFusing)) Sfx.Play(SoundId.EnemyDeath, transform.position + Vector3.up);
        }

        void Update()
        {
            if (!IsSpawned) return;
            float hp = health.Current;
            if (hp < lastHealth - 0.01f && hp > 0f && Time.time - lastHit > 0.08f) { lastHit = Time.time; Sfx.Play(SoundId.EnemyHit, transform.position + Vector3.up); }
            lastHealth = hp;

            if (rusher)
            {
                bool fusing = rusher.FuseVisible;
                if (fusing && !wasFusing) Sfx.Play(SoundId.RusherCountdown, transform.position + Vector3.up);
                moveLoop.SetPlaying(!fusing && !health.IsDead);
                wasFusing = fusing;
            }
            if (laser)
            {
                bool charging = laser.Charging;
                if (charging && !wasCharging) Sfx.Play(SoundId.LaserCharge, transform.position);
                wasCharging = charging;
            }
        }
    }
}
