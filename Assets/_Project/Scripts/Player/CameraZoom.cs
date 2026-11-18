using Unity.Cinemachine;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>우클릭 줌. PlayerStats.zoomMagnification 배율로 FOV를 좁힌다 (P2 Railgun: 2.5배).</summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraZoom : MonoBehaviour
    {
        [SerializeField] PlayerMotor motor;
        [SerializeField] float zoomSpeed = 12f;

        CinemachineCamera vcam;
        float baseFov;

        void Awake()
        {
            vcam = GetComponent<CinemachineCamera>();
            baseFov = vcam.Lens.FieldOfView;
        }

        void LateUpdate()
        {
            float target = motor.IsZooming ? ZoomedFov(baseFov, motor.Stats.zoomMagnification) : baseFov;
            vcam.Lens.FieldOfView = Mathf.Lerp(vcam.Lens.FieldOfView, target, zoomSpeed * Time.deltaTime);
        }

        // 배율 m 줌: tan(FOV/2)를 1/m로 줄인다. 60도 x2.5 -> 약 26도
        static float ZoomedFov(float fov, float m) =>
            2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / m) * Mathf.Rad2Deg;
    }
}
