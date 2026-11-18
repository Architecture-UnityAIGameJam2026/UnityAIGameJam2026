using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 마우스 커서 잠금. Esc로 풀고, 게임 화면을 클릭하면 다시 잠근다.
    /// 커서가 풀린 동안에는 카메라 회전 입력도 함께 끈다 (PlayerMotor도 IsLocked를 보고 조작을 멈춤).
    /// </summary>
    public class CursorLock : MonoBehaviour
    {
        [SerializeField] InputActionReference unlock, relock;
        [Tooltip("커서가 풀렸을 때 함께 끌 입력 (카메라 회전 등)")]
        [SerializeField] Behaviour[] lookInputs;

        public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

        void OnEnable()
        {
            unlock.action.Enable();
            relock.action.Enable();
            SetLocked(true);
        }

        void OnDisable() => SetLocked(false);

        void Update()
        {
            if (unlock.action.WasPressedThisFrame()) SetLocked(false);
            else if (!IsLocked && relock.action.WasPressedThisFrame()) SetLocked(true);
        }

        void SetLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            foreach (var input in lookInputs) input.enabled = locked;
        }
    }
}
