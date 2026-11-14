using UnityEngine;

namespace Calabash.Movement
{
    /// <summary>Для тестовой сцены: R — вернуться на старт, падение ниже killY — тоже.</summary>
    public class TestRespawn : MonoBehaviour
    {
        public ZhdanMovement movement;
        public FirstPersonLook look;
        [Tooltip("Точка респауна (пусто — стартовая позиция героя)")]
        public Transform spawnPoint;
        public float killY = -30f;

        Vector3 startPos;
        float startYaw;

        void Awake()
        {
            if (movement == null) movement = GetComponent<ZhdanMovement>();
            if (look == null) look = GetComponent<FirstPersonLook>();
            startPos = spawnPoint != null ? spawnPoint.position : transform.position;
            startYaw = spawnPoint != null ? spawnPoint.eulerAngles.y : transform.eulerAngles.y;
        }

        void Update()
        {
            bool pressed;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            pressed = kb != null && kb.rKey.wasPressedThisFrame;
#else
            pressed = Input.GetKeyDown(KeyCode.R);
#endif
            if (pressed || transform.position.y < killY) Respawn();
        }

        public void Respawn()
        {
            movement.Teleport(startPos, startYaw);
            if (look != null) look.SetLook(startYaw, 0f);
        }
    }
}
