using Calabash.Audio;
using UnityEngine;

namespace Calabash.Enemies
{
    /// <summary>
    /// Снаряд врага: стрела (летит прямо, втыкается) или ведро воды (летит дугой, разливается лужей брызг).
    /// Попадание проверяется лучом-сферой каждый кадр, поэтому быстрая стрела не пролетает сквозь Ждана.
    /// </summary>
    public class EnemyProjectile : MonoBehaviour
    {
        public float damage = 10f;
        public bool water;
        public float radius = 0.15f;
        public float gravity = 2f;
        [Tooltip("Ведро: при падении бьёт водой всех в этом радиусе")]
        public float splashRadius = 0f;
        public float knockback = 3f;
        public float life = 4f;
        public Material splashMaterial;
        public GameObject owner;

        Vector3 velocity;
        bool done;
        float age;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        public void Launch(Vector3 v) => velocity = v;

        void Update()
        {
            if (done) return;
            float dt = Time.deltaTime;
            age += dt;
            if (age > life) { Destroy(gameObject); return; }
            velocity.y -= gravity * dt;
            Vector3 step = velocity * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f)
            {
                int n = Physics.SphereCastNonAlloc(transform.position, radius, step / dist, hits, dist, ~0, QueryTriggerInteraction.Ignore);
                RaycastHit best = default; float bestD = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i].collider;
                    if (owner != null && c.transform.IsChildOf(owner.transform)) continue;
                    if (c.GetComponentInParent<Enemy>() != null) continue;      // свои не мешают
                    if (hits[i].distance < bestD) { bestD = hits[i].distance; best = hits[i]; }
                }
                if (bestD < float.MaxValue)
                {
                    transform.position += step / dist * bestD;
                    Impact(best);
                    return;
                }
                transform.position += step;
                transform.rotation = Quaternion.LookRotation(step / dist);
            }
        }

        void Impact(RaycastHit hit)
        {
            done = true;
            Vector3 p = hit.point == Vector3.zero ? transform.position : hit.point;
            bool player = PlayerTarget.Is(hit.collider);
            if (splashRadius > 0f)
            {
                // ведро: брызги во все стороны, бьёт и того, кто рядом
                if (player || (PlayerTarget.Position - p).sqrMagnitude < splashRadius * splashRadius)
                    PlayerTarget.Damage(damage, water, p - velocity.normalized, knockback);
                CalabashSfx.Play("splash", p, 1f);
                CalabashSfx.Play("metal_clank", p, 0.6f);
                Splash(p);
                Destroy(gameObject);
                return;
            }
            if (player)
            {
                PlayerTarget.Damage(damage, water, p - velocity.normalized * 2f, knockback, 1f);
                CalabashSfx.Play("blade", p, 0.9f);
                Destroy(gameObject);
                return;
            }
            // стрела втыкается и торчит
            CalabashSfx.Play("wood_hit", p, 0.7f, 1.2f);
            transform.SetParent(hit.collider.transform, true);
            Destroy(gameObject, 6f);
        }

        void Splash(Vector3 p)
        {
            var go = new GameObject("Splash");
            go.transform.position = p + Vector3.up * 0.1f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = 1.6f;
            main.startColor = (Color)new Color32(0xF2, 0xF0, 0xEA, 0xFF);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = splashMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }
    }
}
