using OYM;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    public partial class PilotVisuals
    {
        readonly NetworkVariable<Vector2> eye = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        Transform eyePivot;
        Player pilotPlayer;
        CinemachineCamera observer;
        FirstPersonRig observerHands;
        PilotVisuals observed;
        float nextEyeUpdate;
        AudioSource weaponAudio;
        AudioClip bladeAudio, carbineAudio, railAudio;

        void Awake()
        {
            pilotPlayer = GetComponent<Player>();
            eyePivot = transform.Find("CameraTarget");
            weaponAudio = gameObject.AddComponent<AudioSource>();
            weaponAudio.playOnAwake = false;
            weaponAudio.rolloffMode = AudioRolloffMode.Linear;
            weaponAudio.maxDistance = 85f;
            weaponAudio.minDistance = 4f;
            bladeAudio = Resources.Load<AudioClip>("CombatAudio/Blade");
            carbineAudio = Resources.Load<AudioClip>("CombatAudio/Carbine");
            railAudio = Resources.Load<AudioClip>("CombatAudio/Rail");
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || !NetworkObject.IsPlayerObject) return;
            if (!eyePivot || !Camera.main) return;
            if (Time.unscaledTime >= nextEyeUpdate && !observer)
            {
                nextEyeUpdate = Time.unscaledTime + .08f;
                var pose = new Vector2(Mathf.DeltaAngle(0,eyePivot.localEulerAngles.x),Camera.main.fieldOfView);
                if ((eye.Value - pose).sqrMagnitude > .04f) eye.Value = pose;
            }
            var partner = pilotPlayer.Partner;
            if (!pilotPlayer.Health.IsDead || !partner || (observed && partner != observed.pilotPlayer)) StopWatching();
            if (pilotPlayer.Health.IsDead && partner && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            {
                if (observer) StopWatching();
                else Watch(partner.GetComponent<PilotVisuals>());
            }
            if (!observer || !observed) return;
            observer.transform.SetPositionAndRotation(observed.eyePivot.position,
                observed.transform.rotation * Quaternion.Euler(observed.eye.Value.x,0,0));
            if (observed.eye.Value.y > 1) observer.Lens.FieldOfView = observed.eye.Value.y;
        }
        void Watch(PilotVisuals partner)
        {
            if (!partner || !partner.animator) return;
            observed = partner;
            observer = new GameObject("Partner first-person camera").AddComponent<CinemachineCamera>();
            observer.Lens = GetComponentInChildren<CinemachineCamera>(true).Lens;
            observer.Priority = 100;
            if (view) view.gameObject.SetActive(false);
            observed.animator.gameObject.SetActive(false);
            observerHands = Instantiate(partner.pilot.fpRig,transform,false);
            observerHands.flight = null; observerHands.ViewCamera = Camera.main.transform;
            observerHands.gameObject.SetActive(true);
        }
        void StopWatching()
        {
            if (observer) { observer.gameObject.SetActive(false); Destroy(observer.gameObject); observer = null; }
            if (observerHands) { Destroy(observerHands.gameObject); observerHands = null; }
            if (observed && observed.animator) observed.animator.gameObject.SetActive(true);
            observed = null;
            if (view && !view.gameObject.activeSelf) view.gameObject.SetActive(true);
        }
    }
}
