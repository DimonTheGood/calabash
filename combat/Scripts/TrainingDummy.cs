using UnityEngine;

namespace Calabash.Combat
{
    /// <summary>
    /// Тренировочный манекен: принимает удары, вспыхивает, отлетает (если не закреплён),
    /// «умирает» и через пару секунд встаёт на место. Пример реализации IDamageable для будущих врагов.
    /// </summary>
    public class TrainingDummy : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        [Tooltip("Закреплён (мешок) — не отлетает от ударов")]
        public bool anchored = false;
        [Tooltip("Множитель отбрасывания (лёгкий/тяжёлый противник)")]
        public float knockbackMultiplier = 1f;
        [Tooltip("Через сколько секунд после смерти встаёт")]
        public float respawnDelay = 2.5f;
        [Tooltip("Ниже этой высоты — вернуть на место")]
        public float killY = -30f;

        [Header("Вид")]
        public Renderer body;
        public TextMesh label;
        public Color baseColor = new Color32(0xB4, 0xB2, 0xAC, 0xFF);
        [Tooltip("Цвет вспышки при ударе (кровь из style bible)")]
        public Color hitColor = new Color32(0xA3, 0x12, 0x1C, 0xFF);

        float hp;
        bool initialized;
        Rigidbody rb;
        Collider[] colliders;
        Renderer[] renderers;
        Vector3 homePos;
        Quaternion homeRot;
        float flashTimer, deadTimer, lastDamageShown, lastDamageTimer;
        MaterialPropertyBlock mpb;

        public float Health { get { Init(); return hp; } }
        public bool IsAlive => Health > 0f;
        public int HitCount { get; private set; }
        public HitInfo LastHit { get; private set; }
        public float TotalDamage { get; private set; }

        void Awake() => Init();

        void Init()
        {
            if (initialized) return;
            initialized = true;
            hp = maxHealth;
            rb = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>();
            renderers = GetComponentsInChildren<Renderer>();
            homePos = transform.position;
            homeRot = transform.rotation;
            UpdateLabel();
        }

        public HitResult TakeHit(in HitInfo hit)
        {
            Init();
            if (hp <= 0f) return default;
            float dealt = Mathf.Min(hp, Mathf.Max(0f, hit.damage));
            hp -= dealt;
            HitCount++;
            TotalDamage += dealt;
            LastHit = hit;
            flashTimer = 0.12f;
            lastDamageShown = dealt;
            lastDamageTimer = 0.8f;

            if (!anchored && rb != null && !rb.isKinematic)
                rb.linearVelocity = hit.knockback * knockbackMultiplier;

            bool killed = hp <= 0f;
            if (killed) Die();
            UpdateLabel();
            return new HitResult { damageDealt = dealt, killed = killed };
        }

        void Die()
        {
            deadTimer = respawnDelay;
            foreach (var c in colliders) if (c != null) c.enabled = false;
            foreach (var r in renderers) if (r != null && r != (Renderer)labelRenderer) r.enabled = false;
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; wasDynamic = true; }
        }

        bool wasDynamic;
        MeshRenderer labelRenderer => label != null ? label.GetComponent<MeshRenderer>() : null;

        void Respawn()
        {
            hp = maxHealth;
            transform.SetPositionAndRotation(homePos, homeRot);
            foreach (var c in colliders) if (c != null) c.enabled = true;
            foreach (var r in renderers) if (r != null) r.enabled = true;
            if (rb != null)
            {
                if (wasDynamic) { rb.isKinematic = false; wasDynamic = false; }
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            UpdateLabel();
        }

        void Update()
        {
            Init();
            if (hp <= 0f)
            {
                deadTimer -= Time.deltaTime;
                if (deadTimer <= 0f) Respawn();
            }
            else if (transform.position.y < killY) Respawn();

            if (lastDamageTimer > 0f)
            {
                lastDamageTimer -= Time.deltaTime;
                if (lastDamageTimer <= 0f) UpdateLabel();
            }

            if (body != null)
            {
                flashTimer -= Time.deltaTime;
                if (mpb == null) mpb = new MaterialPropertyBlock();
                Color c = flashTimer > 0f ? hitColor : baseColor;
                body.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                body.SetPropertyBlock(mpb);
            }
        }

        void UpdateLabel()
        {
            if (label == null) return;
            if (hp <= 0f) { label.text = "—"; return; }
            string dmg = lastDamageTimer > 0f ? $"  −{lastDamageShown:0}" : "";
            label.text = $"{hp:0}/{maxHealth:0}{dmg}";
        }
    }
}
