using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 빈사(HP 0) 상태 처리. 내 캐릭터는 이동 · 공격 · 지원 입력을 끄고,
    /// 모든 화면에서 몸을 눕히고 빈사 표시를 켠다. 소생되면 되돌린다.
    /// </summary>
    public class DownedLock : NetworkBehaviour
    {
        [Tooltip("빈사 동안 끌 내 캐릭터 동작 (이동, 무기, 지원, 각성)")]
        [SerializeField] Behaviour[] disableWhileDowned;
        [SerializeField] Transform body;
        [SerializeField] GameObject downedEffect;

        Health health;
        bool wasDowned;
        Vector3 standingPosition;
        Quaternion standingRotation;

        void Awake()
        {
            health = GetComponent<Health>();
            SetBody(body);
        }

        public void SetBody(Transform visual)
        {
            body = visual;
            standingPosition = body.localPosition;
            standingRotation = body.localRotation;
        }

        void Update()
        {
            if (!IsSpawned) return;
            bool downed = health.IsDead;
            if (downed == wasDowned) return;
            wasDowned = downed;

            if (IsServer && !NetworkObject.IsPlayerObject) GetComponent<PlayerMotor>().enabled = !downed;
            if (IsOwner && NetworkObject.IsPlayerObject) foreach (var b in disableWhileDowned)
                if (b && b is not Ultimate && b is not Parry && b is not WeaponModes) b.enabled = !downed;
            body.localRotation = downed ? Quaternion.Euler(90f, 0f, 0f) * standingRotation : standingRotation;
            body.localPosition = downed ? new Vector3(0f, 0.4f, 0f) : standingPosition;
            downedEffect.SetActive(downed);
        }
    }
}
