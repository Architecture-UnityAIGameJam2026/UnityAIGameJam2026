using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 플레이어의 각성 효과 묶음. PlayerStats.awakenings 중 파츠가 반환된 것만 켜진다.
    /// 무기와 이벤트 채널이 시점(처치 / 공격 / 명중)을 알려 주면 켜진 각성에 전달한다.
    /// (새 기획서의 각성 효과는 P20에서 추가한다. 지금은 등록된 각성이 없다)
    /// </summary>
    public class AwakeningSet : NetworkBehaviour
    {
        [SerializeField] PlayerEvent enemyKilled;

        Player me;
        CrossHudState hud;

        public Player Me => me;
        Awakening[] All => me.Stats.awakenings;
        public bool IsActive(Awakening a) => hud != null && hud.IsReturned(a.part);
        public Awakening[] Active => All.Where(IsActive).ToArray();
        public float ReloadMultiplier => Active.Aggregate(1f, (m, a) => m * a.ReloadMultiplier);

        void Awake() => me = GetComponent<Player>();

        public override void OnNetworkSpawn()
        {
            hud = FindFirstObjectByType<CrossHudState>();
            if (IsServer) enemyKilled.Raised += OnEnemyKilled;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) enemyKilled.Raised -= OnEnemyKilled;
        }

        // ---- 서버: 시점 전달 ----
        void OnEnemyKilled(Player killer)
        {
            if (killer == me) foreach (var a in Active) a.OnKill(this);
        }

        public void NotifyBasicAttack() { foreach (var a in Active) a.OnBasicAttack(this); }
        public void NotifyHit(Vector3 point) { foreach (var a in Active) a.OnHit(this, point); }

        // ---- 내 클라이언트에서만 바꿀 수 있는 것 (이동은 소유자 권한) ----
        [Rpc(SendTo.Owner)]
        public void ResetDashRpc() => me.Motor.ResetDash();

        // ---- 연출 ----
        public void PlayEffect(Awakening a, Vector3 position, Quaternion rotation, float scale) =>
            EffectRpc(Array.IndexOf(All, a), position, rotation, scale);

        [Rpc(SendTo.ClientsAndHost)]
        void EffectRpc(int index, Vector3 position, Quaternion rotation, float scale)
        {
            var prefab = All[index].effect;
            if (prefab == null) return;
            var fx = Instantiate(prefab, position, rotation);
            fx.transform.localScale *= scale;
            Destroy(fx, 0.35f);
        }
    }
}
