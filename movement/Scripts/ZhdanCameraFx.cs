using UnityEngine;

namespace Calabash.Movement
{
    /// <summary>
    /// Ощущение скорости: высота глаз (опускается в слайде), FOV от скорости и дэша,
    /// крен в слайде, «присест» камеры при приземлении и тряска от граунд-слэма.
    /// Вешается на Camera; родитель камеры — шарнир тангажа (cameraPivot из FirstPersonLook).
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Camera))]
    public class ZhdanCameraFx : MonoBehaviour
    {
        public ZhdanMovement movement;
        [Tooltip("Шарнир, который поднимается/опускается (обычно родитель камеры)")]
        public Transform eyePivot;

        [Header("Высота глаз")]
        [Tooltip("Глаза ниже макушки на столько, м")]
        public float eyeBelowTop = 0.18f;
        [Tooltip("Скорость сглаживания высоты глаз")]
        public float eyeLerpSpeed = 18f;

        [Header("FOV")]
        public float baseFov = 95f;
        [Tooltip("Добавка FOV на максимальной скорости")]
        public float speedFovBonus = 12f;
        [Tooltip("Скорость, на которой добавка максимальна, м/с")]
        public float speedForMaxFov = 40f;
        [Tooltip("Добавка FOV во время дэша")]
        public float dashFovKick = 8f;
        public float fovLerpSpeed = 10f;

        [Header("Крен в слайде")]
        public float slideRoll = 4f;
        public float rollLerpSpeed = 10f;

        [Header("Приземление и слэм")]
        [Tooltip("Просадка камеры на м/с удара")]
        public float landDipPerSpeed = 0.006f;
        public float maxLandDip = 0.3f;
        [Tooltip("Тряска на метр высоты слэма")]
        public float slamShakePerMeter = 0.02f;
        public float maxSlamShake = 0.35f;
        public float shakeDecay = 4f;

        /// <summary>Базовое смещение камеры от шарнира (вид от третьего лица задаёт ZhdanCameraMode).</summary>
        [System.NonSerialized] public Vector3 baseLocalOffset;

        Camera cam;
        float eyeY, roll, dip, shake;
        Vector3 punchEuler;

        /// <summary>Встряхнуть камеру (удар, взрыв). amount — амплитуда в метрах.</summary>
        public void AddShake(float amount) => shake = Mathf.Max(shake, amount);

        /// <summary>Толчок взгляда в градусах (x — кивок, y — поворот, z — крен); быстро возвращается.</summary>
        public void Punch(Vector3 euler) => punchEuler += euler;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (eyePivot == null) eyePivot = transform.parent;
            if (movement == null) movement = GetComponentInParent<ZhdanMovement>();
            if (movement != null) eyeY = movement.standingHeight - eyeBelowTop;
        }

        void OnEnable()
        {
            if (movement == null) return;
            movement.onLand.AddListener(OnLand);
            movement.onSlamLand.AddListener(OnSlamLand);
        }

        void OnDisable()
        {
            if (movement == null) return;
            movement.onLand.RemoveListener(OnLand);
            movement.onSlamLand.RemoveListener(OnSlamLand);
        }

        void OnLand(float impactSpeed) => dip = Mathf.Min(maxLandDip, impactSpeed * landDipPerSpeed);
        void OnSlamLand(float height) => shake = Mathf.Min(maxSlamShake, slamShakePerMeter * Mathf.Max(height, 5f));

        void LateUpdate()
        {
            if (movement == null) return;
            float dt = Time.deltaTime;

            float targetEye = movement.CurrentHeight - eyeBelowTop;
            eyeY = Mathf.Lerp(eyeY, targetEye, 1f - Mathf.Exp(-eyeLerpSpeed * dt));
            dip = Mathf.Lerp(dip, 0f, 1f - Mathf.Exp(-10f * dt));
            shake = Mathf.MoveTowards(shake, 0f, shakeDecay * shake * dt + 0.05f * dt);
            if (eyePivot != null)
            {
                var p = eyePivot.localPosition;
                eyePivot.localPosition = new Vector3(p.x, eyeY - dip, p.z);
            }

            float speed01 = Mathf.Clamp01(movement.HorizontalSpeed / Mathf.Max(1f, speedForMaxFov));
            float targetFov = baseFov + speedFovBonus * speed01 + (movement.IsDashing ? dashFovKick : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovLerpSpeed * dt));

            roll = Mathf.Lerp(roll, movement.IsSliding ? slideRoll : 0f, 1f - Mathf.Exp(-rollLerpSpeed * dt));
            Vector3 shakeOffset = shake > 0.001f ? Random.insideUnitSphere * shake : Vector3.zero;
            transform.localPosition = baseLocalOffset + shakeOffset;
            punchEuler = Vector3.Lerp(punchEuler, Vector3.zero, 1f - Mathf.Exp(-14f * dt));
            transform.localRotation = Quaternion.Euler(punchEuler.x, punchEuler.y, roll + punchEuler.z);
        }
    }
}
