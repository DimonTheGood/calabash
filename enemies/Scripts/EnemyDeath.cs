using Calabash.Audio;
using Calabash.Combat;
using UnityEngine;

namespace Calabash.Enemies
{
    /// <summary>
    /// Смерть врага — четыре разных добивания, по тому, каким ударом убит:
    ///   Launch  — «Кувалда», рывок, третий удар серии, волна: тело улетает кувырком, бьётся о землю или стену, скользит;
    ///   Skyward — апперкот: подбрасывает волчком и в верхней точке рвёт огненным хлопком;
    ///   Ash     — сгорел: застывает, чернеет в уголь и осыпается пеплом, на снегу остаётся гарь;
    ///   Drop    — лёгкий удар: закручивает на месте и роняет.
    /// При любом добивании из точки удара летят брызги туши. Компонент вешает Enemy.Die и сам убирает тело.
    /// </summary>
    public class EnemyDeath : MonoBehaviour
    {
        public enum Style { Drop, Launch, Skyward, Ash }

        const float Center = 0.9f;            // центр тела над ступнями, м: вокруг него крутится кувырок
        const float Gravity = 24f;

        Style style;
        Animator animator;
        ParticleSystem burnFx;
        Material ink;
        bool boss;

        Vector3 c, vel, axis, flat;
        Quaternion baseRot;
        float angle, spin, age, lieAngle;
        int bounces;
        bool settled, gone;
        float settledAt;
        Renderer[] rends;
        MaterialPropertyBlock block;
        Color[] baseColors;
        Transform[] limbs;
        float[] limbSeed;
        Vector3 startScale;

        public static EnemyDeath Begin(Enemy e, in HitInfo hit, bool burning, ParticleSystem burnFx, Material ink)
        {
            var d = e.gameObject.AddComponent<EnemyDeath>();
            d.animator = e.animator;
            d.burnFx = burnFx;
            d.ink = ink;
            d.boss = e.kind == EnemyKind.Executioner;
            d.Init(hit, burning);
            return d;
        }

        static Style Pick(in HitInfo hit, bool burning, bool boss)
        {
            if (hit.kind == AttackKind.None) return Style.Ash;                     // догорел сам
            if (boss) return burning ? Style.Ash : Style.Drop;                     // палача не подбросить
            if (hit.kind == AttackKind.Uppercut) return Style.Skyward;
            if (hit.kind == AttackKind.Heavy || hit.kind == AttackKind.DashStrike || hit.kind == AttackKind.SlamWave || hit.kind == AttackKind.Light3)
                return Style.Launch;
            return burning ? Style.Ash : Style.Drop;
        }

        void Init(in HitInfo hit, bool burning)
        {
            style = Pick(hit, burning, boss);
            var agent = GetComponent("NavMeshAgent") as Behaviour;
            if (agent != null) agent.enabled = false;

            flat = hit.direction; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) flat = -transform.forward;
            flat.Normalize();
            axis = Vector3.Cross(Vector3.up, flat).normalized;
            baseRot = transform.rotation;
            c = transform.position + Vector3.up * Center;
            startScale = transform.localScale;
            rends = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();

            Vector3 from = hit.kind == AttackKind.None ? c : hit.point;
            InkSplash(from, flat, style == Style.Launch || style == Style.Skyward ? 26 : 14);
            if (PlayerTarget.CameraFx != null) PlayerTarget.CameraFx.AddShake(style == Style.Drop ? 0.05f : 0.1f);

            // удар Ждана — огонь: в полёте тело горит, иначе ночью его не видно
            if ((style == Style.Launch || style == Style.Skyward) && burnFx != null)
            {
                if (!burnFx.isPlaying) burnFx.Play();
                var em = burnFx.emission; em.rateOverTime = 70f;
            }
            switch (style)
            {
                case Style.Launch:
                {
                    // недалеко и высоко: кувырок должен быть виден, а не улететь точкой в темноту
                    float power = Mathf.Clamp(new Vector2(hit.knockback.x, hit.knockback.z).magnitude * 0.6f, 6.5f, 10.5f);
                    vel = flat * power + Vector3.up * Mathf.Clamp(hit.knockback.y + 5f, 6.5f, 9.5f);
                    spin = 540f;
                    lieAngle = 90f;                                   // ложится на спину, головой от удара
                    FreezePose("Hit");
                    CollectLimbs();
                    CalabashSfx.Play("punch_heavy", c, 0.9f, 0.8f);
                    break;
                }
                case Style.Skyward:
                    vel = flat * 1.8f + Vector3.up * 14f;
                    axis = Quaternion.AngleAxis(Random.Range(-35f, 35f), Vector3.up) * axis;
                    spin = 900f;
                    FreezePose("Hit");
                    CollectLimbs();
                    CalabashSfx.Play("swing", c, 0.9f, 0.6f);
                    break;
                case Style.Ash:
                    FreezePose("Hit");
                    baseColors = new Color[rends.Length];
                    for (int i = 0; i < rends.Length; i++)
                    {
                        var m = rends[i].sharedMaterial;
                        baseColors[i] = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray;
                    }
                    if (burnFx != null)
                    {
                        if (!burnFx.isPlaying) burnFx.Play();
                        var em = burnFx.emission; em.rateOverTime = 110f;
                    }
                    CalabashSfx.Play("fire_burst", c, 0.9f, 0.85f);
                    break;
                case Style.Drop:
                    vel = flat * (boss ? 1.5f : 4.5f);
                    spin = boss ? 0f : 760f * (Random.value < 0.5f ? -1f : 1f);
                    if (animator != null) animator.speed = boss ? 0.8f : 1.35f;
                    break;
            }
        }

        void FreezePose(string clip)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(clip, 0.04f, 0, 0.05f);
            Invoke(nameof(StopAnimator), 0.12f);
        }

        void StopAnimator() { if (animator != null) animator.speed = 0f; }

        void CollectLimbs()
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("UpperArm") || t.name.StartsWith("Forearm") || t.name.StartsWith("Thigh") || t.name.StartsWith("Shin") || t.name == "Head")
                    list.Add(t);
            limbs = list.ToArray();
            limbSeed = new float[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) limbSeed[i] = Random.value * 30f;
        }

        void Update()
        {
            if (gone) return;
            float dt = Time.deltaTime;
            age += dt;
            switch (style)
            {
                case Style.Launch:
                case Style.Skyward:
                {
                    // мелкими шагами: при просадке FPS тело не должно пролететь сквозь землю или стену
                    int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.02f), 1, 20);
                    for (int i = 0; i < n && !gone; i++) Fly(dt / n, style == Style.Skyward);
                    break;
                }
                case Style.Ash: Ash(dt); break;
                case Style.Drop: Drop(dt); break;
            }
        }

        void LateUpdate()
        {
            // руки и ноги болтаются в полёте, после приземления замирают
            if (limbs == null || settled || gone) return;
            float t = Time.time * 13f;
            for (int i = 0; i < limbs.Length; i++)
            {
                float s = limbSeed[i];
                limbs[i].localRotation *= Quaternion.Euler(Mathf.Sin(t + s) * 34f, Mathf.Sin(t * 0.7f + s * 2f) * 18f, Mathf.Cos(t * 1.3f + s) * 26f);
            }
        }

        // ------------------------------------------------------------------ полёт
        void Fly(float dt, bool skyward)
        {
            if (settled) { Rest(dt); return; }
            vel.y -= Gravity * dt;
            Vector3 step = vel * dt;

            if (skyward && vel.y <= 0f) { Burst(); return; }

            // стена на пути: тело шлёпается о неё и сползает
            Vector3 hstep = new Vector3(step.x, 0f, step.z);
            if (hstep.sqrMagnitude > 1e-6f && Solid(c, hstep.normalized, hstep.magnitude + 0.45f, out RaycastHit wall))
            {
                Vector3 n = wall.normal; n.y = 0f;
                if (n.sqrMagnitude > 0.01f)
                {
                    vel = Vector3.Reflect(new Vector3(vel.x, 0f, vel.z), n.normalized) * 0.22f + Vector3.up * Mathf.Min(vel.y, 1f);
                    spin *= 0.4f;
                    step = Vector3.zero;
                    InkSplash(wall.point, n.normalized, 18);
                    Thud(0.14f);
                }
            }
            c += step;
            angle += spin * dt;

            float ground = GroundY(c + Vector3.up * Mathf.Abs(step.y));
            float rest = ground + 0.28f;                                  // лёжа: центр тела у самой земли
            if (c.y <= rest && vel.y < 0f)
            {
                c.y = rest;
                bounces++;
                if (bounces == 1 && vel.magnitude > 5f)
                {
                    vel = new Vector3(vel.x * 0.5f, -vel.y * 0.32f, vel.z * 0.5f);
                    spin *= 0.5f;
                    InkSplash(new Vector3(c.x, ground + 0.05f, c.z), Vector3.up, 16);
                    Thud(0.1f);
                }
                else
                {
                    settled = true;
                    settledAt = age;
                    vel = new Vector3(vel.x, 0f, vel.z) * 0.6f;
                    // доворачиваем до ближайшего «лежит»: на спине или лицом в снег
                    float a = Mathf.Repeat(angle, 360f);
                    lieAngle = Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < Mathf.Abs(Mathf.DeltaAngle(a, 270f)) ? 90f : 270f;
                    CalabashSfx.Play("land", c, 0.8f, 0.8f);
                }
            }
            Place();
        }

        /// <summary>Лежит: скользит по снегу, доворачивается, потом уходит в снег.</summary>
        void Rest(float dt)
        {
            float t = age - settledAt;
            vel = Vector3.MoveTowards(vel, Vector3.zero, 14f * dt);
            if (vel.sqrMagnitude > 0.01f && !Solid(c, vel.normalized, vel.magnitude * dt + 0.4f, out _)) c += vel * dt;
            angle = Mathf.MoveTowardsAngle(angle, lieAngle, 500f * dt);
            if (t > 3f) c += Vector3.down * dt * 0.35f;
            Place();
            if (t > 5.5f) Remove();
        }

        void Place()
        {
            Quaternion r = Quaternion.AngleAxis(angle, axis) * baseRot;
            transform.SetPositionAndRotation(c - r * Vector3.up * Center, r);
        }

        /// <summary>Апперкот: в верхней точке тело рвёт огнём.</summary>
        void Burst()
        {
            if (PlayerTarget.Fx != null)
            {
                PlayerTarget.Fx.Impact(c, Vector3.up, 1.7f);
                PlayerTarget.Fx.Impact(c + Vector3.up * 0.3f, -flat, 1.1f);
            }
            InkSplash(c, Vector3.up, 40, 9f);
            CalabashSfx.Play("explosion", c, 0.9f, 1.25f);
            if (PlayerTarget.CameraFx != null) PlayerTarget.CameraFx.AddShake(0.12f);
            Hide();
            Destroy(gameObject, 1.5f);
        }

        // ------------------------------------------------------------------ пепел
        void Ash(float dt)
        {
            const float charTime = 0.55f, hold = 0.95f, crumble = 0.55f;
            float k = Mathf.Clamp01(age / charTime);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null || rends[i] is ParticleSystemRenderer) continue;
                rends[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", Color.Lerp(baseColors[i], new Color(0.02f, 0.02f, 0.02f, baseColors[i].a), k));
                rends[i].SetPropertyBlock(block);
            }
            // судорога, пока чернеет
            if (age < charTime) transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(age * 60f) * 4f * (1f - k), 0f, Mathf.Cos(age * 47f) * 4f * (1f - k));
            if (age < hold) return;

            float q = Mathf.Clamp01((age - hold) / crumble);
            if (!settled)
            {
                settled = true;
                InkSplash(c, Vector3.up, boss ? 60 : 34, 3.5f, 0.5f);
                if (PlayerTarget.Fx != null) PlayerTarget.Fx.Impact(transform.position + Vector3.up * 0.15f, Vector3.up, boss ? 1.6f : 1f);
                CalabashSfx.Play("cloth", c, 0.9f, 0.6f);
                if (burnFx != null) burnFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            float e = q * q;
            transform.localScale = new Vector3(startScale.x * (1f + 0.25f * e), startScale.y * Mathf.Lerp(1f, 0.03f, e), startScale.z * (1f + 0.25f * e));
            if (q >= 1f && !gone) { Hide(); Destroy(gameObject, 1.5f); }
        }

        // ------------------------------------------------------------------ лёгкий удар
        void Drop(float dt)
        {
            // закрутило и отбросило на шаг; сама анимация Death роняет тело
            float k = Mathf.Clamp01(age / 0.5f);
            float w = spin * (1f - k) * (1f - k);
            transform.Rotate(Vector3.up, w * dt, Space.World);
            vel = Vector3.MoveTowards(vel, Vector3.zero, 9f * dt);
            if (vel.sqrMagnitude > 0.01f && !Solid(transform.position + Vector3.up * Center, vel.normalized, vel.magnitude * dt + 0.45f, out _))
                transform.position += vel * dt;
            if (age > 4f) transform.position += Vector3.down * dt * 0.6f;
            if (age > 6.5f) Remove();
        }

        // ------------------------------------------------------------------ общее
        void Thud(float shake)
        {
            CalabashSfx.Play("punch_heavy", c, 0.8f, 0.6f);
            float d = PlayerTarget.Transform != null ? Vector3.Distance(PlayerTarget.Transform.position, c) : 10f;
            if (PlayerTarget.CameraFx != null) PlayerTarget.CameraFx.AddShake(shake * Mathf.Clamp01(1f - d / 14f));
        }

        void Hide()
        {
            gone = true;
            foreach (var r in rends)
                if (r != null && !(r is ParticleSystemRenderer)) r.enabled = false;
            foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
            if (burnFx != null) burnFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Remove()
        {
            gone = true;
            Destroy(gameObject);
        }

        /// <summary>Есть ли на пути стена (жители, враги и сам Ждан — не стена).</summary>
        bool Solid(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            bool found = false;
            foreach (var h in Physics.SphereCastAll(from, 0.3f, dir, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Skip(h.collider) || h.distance <= 0f || h.normal.y > 0.6f) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            return found;
        }

        float GroundY(Vector3 p)
        {
            float y = p.y - 50f;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Skip(h.collider) || h.normal.y < 0.4f) continue;
                if (h.point.y > y) y = h.point.y;
            }
            return y;
        }

        bool Skip(Collider col)
        {
            if (col.transform.IsChildOf(transform)) return true;
            if (PlayerTarget.Transform != null && col.transform.IsChildOf(PlayerTarget.Transform)) return true;
            return col.GetComponentInParent<IDamageable>() != null;
        }

        /// <summary>Брызги туши: чёрные комки летят по направлению удара и падают в снег.</summary>
        void InkSplash(Vector3 p, Vector3 dir, int count, float speed = 6f, float spread = 0.35f)
        {
            if (ink == null) return;
            var go = new GameObject("InkSplash");
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.up));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.15f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.gravityModifier = 2.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = Mathf.Lerp(18f, 80f, spread); sh.radius = 0.12f;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.sharedMaterial = ink;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            Destroy(go, 1.6f);
        }
    }
}
