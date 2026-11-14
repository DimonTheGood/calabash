using UnityEngine;

namespace Calabash.Movement
{
    /// <summary>
    /// Процедурные позы статичной модели Ждана (рига пока нет): наклон на бегу, покачивание,
    /// откидывание назад в слайде, рывок вперёд в дэше, сжатие в слэме.
    /// Вешается на объект «Body» (ребёнок героя); двигает дочерний «Pose», в котором лежит модель.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class ZhdanBodyView : MonoBehaviour
    {
        public ZhdanMovement movement;
        [Tooltip("Узел позы — внутри него модель")]
        public Transform pose;

        [Tooltip("Поворот модели вокруг Y, если она смотрит не туда, °")]
        public float modelYawOffset = 0f;
        [Tooltip("Наклон вперёд на полной скорости бега, °")]
        public float runLean = 10f;
        [Tooltip("Наклон вперёд в дэше, °")]
        public float dashLean = 28f;
        [Tooltip("Откидывание назад в слайде, °")]
        public float slideLean = -65f;
        [Tooltip("Наклон в граунд-слэме, °")]
        public float slamLean = 20f;
        [Tooltip("Крен при стрейфе, °")]
        public float strafeRoll = 8f;
        [Tooltip("Покачивание при беге, м")]
        public float bobHeight = 0.06f;
        [Tooltip("Плавность поз")]
        public float poseSpeed = 14f;

        float bobPhase;
        Vector3 euler;
        float bob;

        void Awake()
        {
            if (movement == null) movement = GetComponentInParent<ZhdanMovement>();
            if (pose == null && transform.childCount > 0) pose = transform.GetChild(0);
        }

        void LateUpdate()
        {
            if (movement == null || pose == null) return;
            float dt = Time.deltaTime;
            Vector3 v = movement.Velocity;
            Vector3 local = movement.transform.InverseTransformDirection(new Vector3(v.x, 0f, v.z));
            float run01 = Mathf.Clamp01(movement.HorizontalSpeed / Mathf.Max(1f, movement.runSpeed));

            float pitch = runLean * Mathf.Clamp(local.z / Mathf.Max(1f, movement.runSpeed), -1f, 1f);
            float roll = -strafeRoll * Mathf.Clamp(local.x / Mathf.Max(1f, movement.runSpeed), -1f, 1f);
            float targetBob = 0f;

            if (movement.IsSliding) { pitch = slideLean; roll = 0f; }
            else if (movement.IsDashing) pitch = dashLean;
            else if (movement.IsSlamming) pitch = slamLean;
            else if (movement.IsGrounded && run01 > 0.1f)
            {
                bobPhase += movement.HorizontalSpeed * dt * 0.9f;
                targetBob = Mathf.Abs(Mathf.Sin(bobPhase)) * bobHeight * run01;
            }

            float k = 1f - Mathf.Exp(-poseSpeed * dt);
            euler = Vector3.Lerp(euler, new Vector3(pitch, 0f, roll), k);
            bob = Mathf.Lerp(bob, targetBob, k);
            pose.localRotation = Quaternion.Euler(euler.x, modelYawOffset, euler.z);
            pose.localPosition = new Vector3(0f, bob, 0f);
        }
    }
}
