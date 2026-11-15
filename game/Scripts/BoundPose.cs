using UnityEngine;

namespace Calabash.Game
{
    /// <summary>
    /// Ждан, привязанный к столбу: поверх анимации заводит руки за столб к узлу (anchor),
    /// а когда костёр горит — дёргает грудь и голову (struggle). weight 0 — руки свободны.
    /// Вешает и настраивает game/Editor/CutsceneSetup, управляет ExecutionCutscene.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class BoundPose : MonoBehaviour
    {
        public Transform upperL, foreL, handL;
        public Transform upperR, foreR, handR;
        public Transform chest, head;
        [Tooltip("Узел за столбом: сюда сведены запястья. Ось X — вбок")] public Transform anchor;
        [Range(0f, 1f)] public float weight = 1f;
        [Range(0f, 1f)] public float struggle;

        float seed;

        void Awake() => seed = Random.value * 50f;

        void LateUpdate()
        {
            float t = Time.unscaledTime + seed;
            if (struggle > 0.001f)
            {
                float a = Mathf.PerlinNoise(t * 3.1f, 0.3f) - 0.5f, b = Mathf.PerlinNoise(t * 2.3f, 7.7f) - 0.5f, c = Mathf.PerlinNoise(t * 5.3f, 2.1f) - 0.5f;
                if (chest != null) chest.rotation = Quaternion.Euler(a * 14f * struggle, b * 22f * struggle, c * 8f * struggle) * chest.rotation;
                if (head != null) head.rotation = Quaternion.Euler((c * 40f - 12f) * struggle, a * 50f * struggle, b * 20f * struggle) * head.rotation;
            }
            else if (head != null && weight > 0.5f)
                head.rotation = Quaternion.Euler(14f, 0f, 0f) * head.rotation;     // до костра — голова опущена
            if (weight <= 0.001f || anchor == null) return;
            Arm(upperL, foreL, handL, -1f);
            Arm(upperR, foreR, handR, 1f);
        }

        void Arm(Transform upper, Transform fore, Transform hand, float side)
        {
            if (upper == null || fore == null || hand == null) return;
            Vector3 wrist = anchor.position + anchor.right * side * 0.05f;
            Vector3 elbow = Vector3.Lerp(upper.position, wrist, 0.5f) + anchor.right * side * 0.12f - Vector3.up * 0.04f;
            Aim(upper, fore, elbow);
            Aim(fore, hand, wrist);
        }

        void Aim(Transform bone, Transform child, Vector3 target)
        {
            Vector3 cur = child.position - bone.position, want = target - bone.position;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.Slerp(bone.rotation, Quaternion.FromToRotation(cur, want) * bone.rotation, weight);
        }
    }
}
