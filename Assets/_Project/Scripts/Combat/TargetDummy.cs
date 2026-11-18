using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>테스트용 허수아비. 체력이 0이 되면 잠시 뒤 다시 채운다.</summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : NetworkBehaviour
    {
        [SerializeField] float respawnDelay = 2f;

        public override void OnNetworkSpawn()
        {
            if (IsServer) GetComponent<Health>().Died += _ => StartCoroutine(Revive());
        }

        IEnumerator Revive()
        {
            yield return new WaitForSeconds(respawnDelay);
            GetComponent<Health>().Restore();
        }
    }
}
