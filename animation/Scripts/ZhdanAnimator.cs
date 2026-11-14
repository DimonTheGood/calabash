using Calabash.Combat;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Animation
{
    /// <summary>
    /// Связывает Animator модели Ждана (SK_Char_hero) с мувментом и боем.
    /// Нижний слой: бег, прыжок, падение, слайд, дэш, слэм — по состоянию ZhdanMovement.
    /// Верхний слой (маска от поясницы вверх): удары. Клип удара не проигрывается сам,
    /// а перематывается по фазе удара ZhdanCombat, поэтому всегда совпадает с попаданием.
    /// В каждом клипе удара 0–0,3 — замах, 0,3–0,5 — удар, 0,5–1 — возврат в стойку.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class ZhdanAnimator : MonoBehaviour
    {
        public ZhdanMovement movement;
        public ZhdanCombat combat;
        public Animator animator;

        [Tooltip("Скорость, на которую рассчитан клип бега, м/с. Быстрее — клип ускоряется")]
        public float runClipSpeed = 9f;
        [Tooltip("Плавность перехода между состояниями нижнего слоя, с")]
        public float crossFade = 0.12f;
        [Tooltip("Как быстро включается и гаснет слой ударов, 1/с")]
        public float upperBlendSpeed = 12f;

        public const string Locomotion = "Locomotion", Air = "Air", Slide = "Slide", Dash = "Dash", Slam = "Slam";
        public const int UpperLayer = 1;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int VSpeedId = Animator.StringToHash("VSpeed");
        static readonly int LocoRateId = Animator.StringToHash("LocoRate");

        string baseState;
        string upperState;
        float upperWeight;

        void Awake()
        {
            if (movement == null) movement = GetComponentInParent<ZhdanMovement>();
            if (combat == null) combat = GetComponentInParent<ZhdanCombat>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void Update()
        {
            if (animator == null || movement == null || animator.runtimeAnimatorController == null) return;
            float dt = Time.deltaTime;

            // нижний слой
            string want = Locomotion;
            if (movement.IsSlamming) want = Slam;
            else if (movement.IsDashing) want = Dash;
            else if (movement.IsSliding) want = Slide;
            else if (!movement.IsGrounded) want = Air;

            float speed = movement.HorizontalSpeed;
            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(VSpeedId, movement.Velocity.y);
            animator.SetFloat(LocoRateId, Mathf.Max(1f, speed / Mathf.Max(0.1f, runClipSpeed)));
            if (want != baseState)
            {
                animator.CrossFadeInFixedTime(want, baseState == null ? 0f : crossFade, 0);
                baseState = want;
            }

            // верхний слой: удар по фазе боя
            string attack = null;
            float t = 0f;
            if (combat != null)
            {
                attack = StateFor(combat.CurrentAttack);
                if (attack != null) t = NormalizedTime(combat.CurrentAttack, combat.Phase, combat.PhaseProgress, combat.Charge01);
            }
            if (attack != null)
            {
                animator.Play(attack, UpperLayer, t);
                upperState = attack;
            }
            else if (upperState != null && upperWeight > 0f)
                animator.Play(upperState, UpperLayer, 1f);           // догасить в конце клипа (в стойке)

            upperWeight = Mathf.MoveTowards(upperWeight, attack != null ? 1f : 0f, upperBlendSpeed * dt);
            if (animator.layerCount > UpperLayer) animator.SetLayerWeight(UpperLayer, upperWeight);
        }

        public static string StateFor(AttackKind kind)
        {
            switch (kind)
            {
                case AttackKind.Light1: return "Jab_L";
                case AttackKind.Light2: return "Jab_R";
                case AttackKind.Light3: return "Hook_R";
                case AttackKind.Heavy: return "Heavy";
                case AttackKind.DashStrike: return "DashStrike";
                case AttackKind.Uppercut: return "Uppercut";
                default: return null;
            }
        }

        /// <summary>Фаза удара → место в клипе: замах 0–0,3, удар 0,3–0,5, возврат 0,5–1.</summary>
        public static float NormalizedTime(AttackKind kind, AttackPhase phase, float progress, float charge)
        {
            switch (phase)
            {
                case AttackPhase.Charging: return 0.3f * charge;
                case AttackPhase.Windup: return kind == AttackKind.Heavy ? 0.3f : 0.3f * progress;
                case AttackPhase.Active: return 0.3f + 0.2f * progress;
                case AttackPhase.Recovery: return 0.5f + 0.5f * progress;
                default: return 1f;
            }
        }
    }
}
