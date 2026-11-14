using UnityEngine;

namespace Calabash.Movement
{
    /// <summary>
    /// Обзор от первого лица: рыскание крутит тело (этот transform), тангаж — шарнир камеры.
    /// Esc освобождает курсор, клик ЛКМ захватывает обратно.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public class FirstPersonLook : MonoBehaviour
    {
        [Tooltip("Шарнир камеры (дочерний объект на уровне глаз). Его поднимает/опускает ZhdanCameraFx")]
        public Transform cameraPivot;
        [Tooltip("Ввод (пусто — возьмётся с этого же объекта)")]
        public ZhdanInput input;
        public float minPitch = -89f;
        public float maxPitch = 89f;
        public bool lockCursorOnStart = true;

        float yaw, pitch;

        public float Yaw => yaw;
        public float Pitch => pitch;

        void Awake()
        {
            if (input == null) input = GetComponent<ZhdanInput>();
            yaw = transform.eulerAngles.y;
        }

        void OnEnable()
        {
            if (lockCursorOnStart) SetCursorLocked(true);
        }

        void Update()
        {
            if (input == null) return;
            if (input.UnlockCursorPressed()) SetCursorLocked(false);
            else if (Cursor.lockState != CursorLockMode.Locked && input.LockCursorPressed()) SetCursorLocked(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = input.ReadLookDegrees(Time.deltaTime);
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Выставить взгляд извне (респаун).</summary>
        public void SetLook(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
