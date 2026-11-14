using Calabash.Movement;
using UnityEngine;

namespace Calabash.Combat
{
    /// <summary>
    /// Временные кулаки в кадре: процедурно двигает два объекта-кулака под камерой по состоянию боя.
    /// Когда будут модель рук и анимации — заменить этот компонент на Animator с теми же событиями.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class ZhdanFistsView : MonoBehaviour
    {
        public ZhdanCombat combat;
        public ZhdanMovement movement;
        public Transform leftFist;
        public Transform rightFist;

        [Header("Позы (локально к камере, м)")]
        public Vector3 leftRest = new Vector3(-0.32f, -0.3f, 0.5f);
        public Vector3 rightRest = new Vector3(0.32f, -0.3f, 0.5f);
        [Tooltip("Насколько далеко выбрасывается кулак")]
        public float punchReach = 0.45f;
        public float followSpeed = 28f;
        [Tooltip("Покачивание при беге")]
        public float bobAmount = 0.025f;

        float bobPhase;
        Vector3 lPos, rPos;

        void Awake()
        {
            if (combat == null) combat = GetComponentInParent<ZhdanCombat>();
            if (movement == null) movement = GetComponentInParent<ZhdanMovement>();
            lPos = leftRest;
            rPos = rightRest;
        }

        void LateUpdate()
        {
            if (combat == null || movement == null) return;
            float dt = Time.deltaTime;
            Vector3 l = leftRest, r = rightRest;

            // бег и слайд
            float speed = movement.HorizontalSpeed;
            if (movement.IsGrounded && !movement.IsSliding && speed > 1f)
            {
                bobPhase += speed * dt * 0.9f;
                float b = Mathf.Sin(bobPhase) * bobAmount;
                l.y += b; r.y -= b;
            }
            if (movement.IsSliding) { l += new Vector3(-0.05f, -0.06f, -0.05f); r += new Vector3(0.05f, -0.06f, -0.05f); }
            if (movement.IsDashing && combat.CurrentAttack != AttackKind.DashStrike) { l.z -= 0.1f; r.z -= 0.1f; }

            float e = Extension(combat.Phase, combat.PhaseProgress);
            Vector3 fwd = Vector3.forward * punchReach;
            switch (combat.CurrentAttack)
            {
                case AttackKind.Light1:
                    l += fwd * e + Vector3.right * 0.22f * e;
                    break;
                case AttackKind.Light2:
                    r += fwd * e + Vector3.left * 0.22f * e;
                    break;
                case AttackKind.Light3: // размашистый хук правой
                    r += new Vector3(-0.4f * e, 0.08f * e, 0.35f * e);
                    l.z -= 0.08f * e;
                    break;
                case AttackKind.Heavy:
                    if (combat.Phase == AttackPhase.Charging)
                    {
                        float c = combat.Charge01;
                        r += new Vector3(0.05f * c, -0.05f * c, -0.28f * c);
                        if (c >= 1f) r += Random.insideUnitSphere * 0.006f; // дрожит на полном замахе
                    }
                    else r += new Vector3(-0.25f * e, 0.05f * e, 0.65f * e);
                    break;
                case AttackKind.DashStrike: // оба кулака вперёд
                    l += fwd * 1.1f * e + Vector3.right * 0.18f * e;
                    r += fwd * 1.1f * e + Vector3.left * 0.18f * e;
                    break;
                case AttackKind.Uppercut:
                    r += new Vector3(-0.12f * e, -0.15f + 0.6f * e, 0.25f * e);
                    break;
            }
            if (combat.SlamFlash > 0f) { l.y -= 0.3f; r.y -= 0.3f; }

            float k = 1f - Mathf.Exp(-followSpeed * dt);
            lPos = Vector3.Lerp(lPos, l, k);
            rPos = Vector3.Lerp(rPos, r, k);
            if (leftFist != null) leftFist.localPosition = lPos;
            if (rightFist != null) rightFist.localPosition = rPos;
        }

        /// <summary>0 — кулак у тела, 1 — полностью выброшен.</summary>
        static float Extension(AttackPhase phase, float p)
        {
            switch (phase)
            {
                case AttackPhase.Windup: return -0.2f * p;           // чуть отвести назад
                case AttackPhase.Active: return Mathf.Lerp(0.6f, 1f, p);
                case AttackPhase.Recovery: return 1f - p * p;
                default: return 0f;
            }
        }
    }
}
