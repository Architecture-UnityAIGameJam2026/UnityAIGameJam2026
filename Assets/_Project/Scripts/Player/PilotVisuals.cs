using OYM;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// Uses the art prefab's rig and weapon while the network player owns movement, input and hits.
    public partial class PilotVisuals : NetworkBehaviour
    {
        [SerializeField] ZeroGFlight pilot;

        Animator animator;
        FirstPersonRig view;
        Vector3 previousPosition;
        int nextSlash;

        public override void OnNetworkSpawn()
        {
            weaponAudio.spatialBlend = IsOwner && NetworkObject.IsPlayerObject ? 0f : 1f;
            if (IsOwner && NetworkObject.IsPlayerObject)
            {
                view = Instantiate(pilot.fpRig, transform, false);
                view.flight = null;
                view.ViewCamera = Camera.main.transform;
                view.gameObject.SetActive(true);
                animator = view.animator;
            }
            else
            {
                animator = Instantiate(pilot.animator, transform, false);
                GetComponent<DownedLock>().SetBody(animator.transform);
            }

            animator.SetLayerWeight(1, 1f);
            animator.SetBool(pilot is KestrelController ? "Combat" : "Aim", true);
            previousPosition = transform.position;
        }

        void Update()
        {
            if (!IsSpawned || !animator || Time.deltaTime <= 0f) return;
            float speed = (transform.position - previousPosition).magnitude / Time.deltaTime;
            previousPosition = transform.position;
            animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
            if (view && pilot is KestrelController)
                view.ikWeight = Mathf.MoveTowards(view.ikWeight, animator.GetCurrentAnimatorStateInfo(1).IsTag("Attack") ? 0f : 1f, 10f * Time.deltaTime);
        }

        public void Slash()
        {
            if (!animator) return;
            animator.SetInteger("AttackType", nextSlash);
            animator.SetTrigger("Attack");
            nextSlash = (nextSlash + 1) % 3;
        }

        public void Recoil(bool automatic)
        {
            if (view) view.Kick(1f);
        }

        public override void OnNetworkDespawn()
        {
            StopWatching();
            if (view) Destroy(view.gameObject);
            else if (animator) Destroy(animator.gameObject);
            animator = null;
            view = null;
            nextSlash = 0;
        }
    }
}
