using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// P1 근접 참격. 내 클라이언트는 입력과 쿨타임만 보고, 선딜 후 범위 판정은 서버가 한다.
    /// 적중한 적 수만큼 검기 칸(AmmoPool)이 찬다.
    /// </summary>
    public class MeleeWeapon : NetworkBehaviour
    {
        [SerializeField] MeleeData data;
        [SerializeField] InputActionReference attack;
        [SerializeField] AmmoPool gauge;
        [SerializeField] GameObject slashEffect;

        float readyTime;
        Player me;
        AwakeningSet awakening;

        void Awake()
        {
            me = GetComponent<Player>();
            awakening = GetComponent<AwakeningSet>();
        }

        void Update()
        {
            if (!IsOwner || !NetworkObject.IsPlayerObject || !CursorLock.IsLocked || !attack.action.WasPressedThisFrame() || Time.time < readyTime) return;
            readyTime = Time.time + data.windup + data.recovery;
            SlashRpc();
        }

        public float Reach => data.boxSize.z;
        public bool BotSlash()
        {
            if (!IsServer || !me.IsBot || me.Health.IsDead || Time.time < readyTime) return false;
            readyTime = Time.time + data.windup + data.recovery;
            StartCoroutine(Slash());
            return true;
        }

        [Rpc(SendTo.Server)]
        void SlashRpc() => StartCoroutine(Slash());

        IEnumerator Slash()
        {
            yield return new WaitForSeconds(data.windup);

            // 박스 중심: 가슴 높이에서 전방으로 박스 길이의 절반
            Vector3 center = transform.position + Vector3.up * 1f + transform.forward * (data.boxSize.z * 0.5f);
            var targets = new HashSet<Health>();
            foreach (var c in Physics.OverlapBox(center, data.boxSize * 0.5f, transform.rotation, data.hitMask))
                if (Health.Of(c) is { IsDead: false } h) targets.Add(h);

            int before = gauge.Current;
            bool groundKill = false;
            foreach (var h in targets)
            {
                bool ground = h.GetComponent<Rusher>();
                h.Damage(data.damage, me);
                if (ground && (!h || h.IsDead)) groundKill = true;
            }
            gauge.Add(targets.Count * data.gaugePerHit);
            if (gauge.Current > before) me.ReportAction("slash", "gain");
            if (targets.Count == 2 && gauge.Current - before == 2 * data.gaugePerHit) me.ReportAction("slash", "double");
            if (groundKill) me.ReportAction("slash", "ground kill");
            awakening.NotifyBasicAttack();
            ShowSlashRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowSlashRpc() => StartCoroutine(Flash(slashEffect, 0.12f));

        public static IEnumerator Flash(GameObject effect, float seconds)
        {
            effect.SetActive(true);
            yield return new WaitForSeconds(seconds);
            effect.SetActive(false);
        }
    }
}
