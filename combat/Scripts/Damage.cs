using UnityEngine;

namespace Calabash.Combat
{
    /// <summary>Каким приёмом нанесён удар — враги могут реагировать по-разному (например, апперкот подбрасывает).</summary>
    public enum AttackKind { None, Light1, Light2, Light3, Heavy, DashStrike, Uppercut, SlamWave }

    public struct HitInfo
    {
        public float damage;
        /// <summary>Точка удара в мире (для искр, крови, декалей).</summary>
        public Vector3 point;
        /// <summary>Направление удара (нормализованное).</summary>
        public Vector3 direction;
        /// <summary>Скорость, которую удар сообщает цели, м/с (горизонталь + подброс).</summary>
        public Vector3 knockback;
        public AttackKind kind;
        public GameObject attacker;
    }

    public struct HitResult
    {
        public float damageDealt;
        public bool killed;
    }

    /// <summary>
    /// Всё, что можно ударить: враги, манекены, разрушаемые предметы.
    /// Компонент ищется через GetComponentInParent от задетого коллайдера.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        HitResult TakeHit(in HitInfo hit);
    }
}
