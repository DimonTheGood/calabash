using Calabash.Audio;
using Calabash.Combat;
using Calabash.Movement;
using UnityEngine;

namespace Calabash.Enemies
{
    /// <summary>
    /// Ждан глазами врагов: где он, куда бежит, и как по нему попасть.
    /// Урон гасит топливо (ZhdanFuel). Во время дэша Ждан неуязвим — удар уходит в пустоту (уклонение).
    /// После попадания короткая неуязвимость, чтобы толпа не снимала всё топливо за кадр.
    /// </summary>
    public static class PlayerTarget
    {
        public static ZhdanMovement Movement { get; private set; }
        public static ZhdanFuel Fuel { get; private set; }
        public static ZhdanCameraFx CameraFx { get; private set; }
        public static Transform Transform { get; private set; }

        /// <summary>Сколько секунд после попадания по Ждану не бьют.</summary>
        public static float graceTime = 0.45f;
        /// <summary>Множитель урона по Ждану (сложность).</summary>
        public static float damageMultiplier = 1f;

        /// <summary>Попадание: урон, вода ли это, откуда.</summary>
        public static event System.Action<float, bool, Vector3> Hurt;
        /// <summary>Ждан ушёл дэшем от удара.</summary>
        public static event System.Action<Vector3> Dodged;

        static float graceUntil;

        public static bool Find()
        {
            if (Movement != null) return true;
            Movement = Object.FindAnyObjectByType<ZhdanMovement>();
            if (Movement == null) return false;
            Transform = Movement.transform;
            Fuel = Movement.GetComponent<ZhdanFuel>();
            CameraFx = Movement.GetComponentInChildren<ZhdanCameraFx>();
            graceUntil = 0f;
            return true;
        }

        public static void Reset()
        {
            Movement = null; Fuel = null; CameraFx = null; Transform = null;
            Hurt = null; Dodged = null;
            graceUntil = 0f;
        }

        public static bool Alive => Find() && (Fuel == null || !Fuel.IsEmpty);
        public static Vector3 Position => Find() ? Transform.position : Vector3.zero;
        /// <summary>Середина тела — для прицела стрел и проверки видимости.</summary>
        public static Vector3 Chest => Position + Vector3.up * 1.2f;
        public static Vector3 Velocity => Find() ? Movement.Velocity : Vector3.zero;

        /// <summary>Принадлежит ли коллайдер Ждану.</summary>
        public static bool Is(Collider c) => Find() && c != null && c.transform.IsChildOf(Transform);

        /// <summary>Ударить Ждана. Возвращает true, если попало (не дэш и не неуязвимость после удара).</summary>
        public static bool Damage(float amount, bool water, Vector3 from, float knockback, float knockUp = 2f)
        {
            if (!Alive) return false;
            if (Movement.IsInvulnerable)
            {
                Dodged?.Invoke(from);
                return false;
            }
            if (Time.time < graceUntil) return false;
            graceUntil = Time.time + graceTime;

            float dmg = amount * damageMultiplier;
            if (Fuel != null) Fuel.Drain(dmg, water);
            Vector3 dir = Position - from; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : -Transform.forward;
            if (knockback > 0f) Movement.AddImpulse(dir * knockback + Vector3.up * knockUp);
            if (CameraFx != null)
            {
                CameraFx.AddShake(Mathf.Clamp(0.04f + dmg * 0.006f, 0.05f, 0.25f));
                Vector3 local = Transform.InverseTransformDirection(dir);
                CameraFx.Punch(new Vector3(-6f, local.x > 0 ? 5f : -5f, local.x > 0 ? -4f : 4f));
            }
            if (water) CalabashSfx.Play2D("splash", 1f, 0.9f);
            Hurt?.Invoke(dmg, water, from);
            return true;
        }
    }
}
