using System.Collections.Generic;
using System.Text;
using Calabash.Movement;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Calabash.Combat.EditorTools
{
    /// <summary>
    /// Самотест боя без Play Mode: временная сцена, синтетический ввод 60 кадров/с,
    /// ZhdanMovement.Simulate + ZhdanCombat.Tick, проверки по каждому приёму.
    /// Меню: Calabash → Combat → Запустить самотест. Из консоли: -executeMethod ...CombatSelfTest.RunBatch
    /// </summary>
    public static class CombatSelfTest
    {
        const float Dt = 1f / 60f;
        static readonly Vector3 O = new Vector3(-5000f, 0f, 5000f);

        [MenuItem("Calabash/Combat/Запустить самотест")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Run(out string report);
            if (ok) Debug.Log(report); else Debug.LogError(report);
            EditorUtility.DisplayDialog("Самотест боя", ok ? "Все проверки прошли." : "Есть провалы — см. Console.", "OK");
        }

        public static void RunBatch()
        {
            bool ok = Run(out string report);
            Debug.Log(report);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static List<GameObject> spawned;
        static StringBuilder log;
        static int fails;

        public static bool Run(out string report)
        {
            spawned = new List<GameObject>();
            log = new StringBuilder("[CombatSelfTest]\n");
            fails = 0;
            Scene prevActive = SceneManager.GetActiveScene();
            bool additive = !string.IsNullOrEmpty(prevActive.path) || prevActive.isDirty;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
            if (!additive) prevActive = default;
            SceneManager.SetActiveScene(scene);
            try
            {
                Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(1200, 1, 300));
                TestLightCombo();
                TestComboReset();
                TestHeavy();
                TestDashStrike();
                TestUppercut();
                TestSlamWave();
                TestKillFeedsFuel();
                TestWallBlocks();
            }
            catch (System.Exception e)
            {
                fails++;
                log.AppendLine("EXCEPTION " + e);
            }
            finally
            {
                if (prevActive.IsValid())
                {
                    SceneManager.SetActiveScene(prevActive);
                    EditorSceneManager.CloseScene(scene, true);
                }
                else foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            }
            log.AppendLine(fails == 0 ? "ИТОГ: всё прошло" : $"ИТОГ: провалов {fails}");
            report = log.ToString();
            return fails == 0;
        }

        // ───────────── окружение ─────────────
        static void Box(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = O + center;
            go.transform.localScale = size;
            spawned.Add(go);
        }

        struct Hero { public ZhdanMovement m; public ZhdanCombat c; public ZhdanFuel f; }

        static Hero SpawnHero(Vector3 local)
        {
            var go = new GameObject("TestZhdan");
            go.transform.position = O + local;
            MovementSelfTest.ConfigureController(go.AddComponent<CharacterController>());
            var h = new Hero { m = go.AddComponent<ZhdanMovement>(), f = go.AddComponent<ZhdanFuel>() };
            h.c = go.AddComponent<ZhdanCombat>();
            h.c.fuel = h.f;
            h.c.hitstopEnabled = false;
            h.c.Init();
            spawned.Add(go);
            Physics.SyncTransforms();
            Step(h, default, default, 10); // встать на пол
            return h;
        }

        static TrainingDummy Dummy(Vector3 local, float hp = 1000f, bool anchored = true)
        {
            var go = CombatTestSceneBuilder.CreateDummy(null, "TestDummy", O + local, anchored, hp, null, null);
            spawned.Add(go);
            Physics.SyncTransforms();
            return go.GetComponent<TrainingDummy>();
        }

        static void Step(Hero h, MovementInput mi, CombatInput ci, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                Physics.SyncTransforms();
                h.m.Simulate(mi, Dt);
                h.c.Tick(ci, Dt);
                mi.jumpPressed = mi.dashPressed = mi.slidePressed = false;
                ci.attackPressed = false;
            }
        }

        static void Attack(Hero h, MovementInput mi = default, int framesAfter = 0)
        {
            Step(h, mi, new CombatInput { attackPressed = true, attackHeld = true });
            if (framesAfter > 0) Step(h, mi, default, framesAfter);
        }

        static void Check(string name, bool cond, string detail)
        {
            if (!cond) fails++;
            log.AppendLine($"{(cond ? "PASS" : "FAIL")}  {name}: {detail}");
        }

        static readonly MovementInput Idle = default;
        static MovementInput Fwd(bool slide = false) => new MovementInput { move = new Vector2(0, 1), slideHeld = slide };

        // ───────────── тесты ─────────────
        static void TestLightCombo()
        {
            var h = SpawnHero(new Vector3(0, 0.05f, 0));
            var front = Dummy(new Vector3(0, 0, 1.5f));
            var back = Dummy(new Vector3(0, 0, -1.5f));
            var kinds = new List<AttackKind>();
            for (int i = 0; i < 3; i++)
            {
                Attack(h, Idle, 14);
                kinds.Add(front.LastHit.kind);
            }
            float expected = h.c.light1.damage + h.c.light2.damage + h.c.light3.damage;
            Check("комбо: три удара", front.HitCount == 3 && Mathf.Approximately(front.TotalDamage, expected),
                  $"попаданий {front.HitCount}, урон {front.TotalDamage:0} (ожидалось {expected:0})");
            Check("комбо: порядок левой → правой → размах",
                  kinds.Count == 3 && kinds[0] == AttackKind.Light1 && kinds[1] == AttackKind.Light2 && kinds[2] == AttackKind.Light3,
                  string.Join(" → ", kinds));
            Check("комбо: размах отбрасывает сильнее джеба", front.LastHit.knockback.magnitude > h.c.light1.knockback + 3f,
                  $"отбрасывание {front.LastHit.knockback.magnitude:0.0} м/с");
            Check("удар не задевает цель за спиной", back.HitCount == 0, $"попаданий сзади {back.HitCount}");
            Check("серия считает попадания", h.c.Series == 3, $"серия {h.c.Series}");
        }

        static void TestComboReset()
        {
            var h = SpawnHero(new Vector3(40, 0.05f, 0));
            var d = Dummy(new Vector3(40, 0, 1.5f));
            Attack(h, Idle, 14);
            Step(h, Idle, default, 60); // пауза дольше comboResetTime
            Attack(h, Idle, 14);
            Check("комбо сбрасывается после паузы", d.LastHit.kind == AttackKind.Light1, $"второй удар — {d.LastHit.kind}");
        }

        static void TestHeavy()
        {
            var h = SpawnHero(new Vector3(80, 0.05f, 0));
            var d = Dummy(new Vector3(80, 0, 1.6f));
            var hold = new CombatInput { heavyHeld = true };
            Step(h, Idle, hold, 5);
            bool slowed = Mathf.Approximately(h.m.speedMultiplier, h.c.chargeMoveSpeedMultiplier) && h.c.Phase == AttackPhase.Charging;
            Step(h, Idle, hold, 60);
            Step(h, Idle, default, 20);
            Check("тяжёлый: замах замедляет бег", slowed && Mathf.Approximately(h.m.speedMultiplier, 1f),
                  $"во время замаха ×{h.c.chargeMoveSpeedMultiplier}, после ×{h.m.speedMultiplier}");
            Check("тяжёлый: полный замах", d.LastHit.kind == AttackKind.Heavy && Mathf.Abs(d.TotalDamage - h.c.heavyMaxDamage) < 0.01f,
                  $"урон {d.TotalDamage:0} (максимум {h.c.heavyMaxDamage})");

            Step(h, Idle, default, 30);
            float before = d.TotalDamage;
            Step(h, Idle, hold, 2);
            Step(h, Idle, default, 20);
            Check("тяжёлый: короткое нажатие — минимальный урон", Mathf.Abs(d.TotalDamage - before - h.c.heavy.damage) < 1f,
                  $"урон {d.TotalDamage - before:0.0} (минимум {h.c.heavy.damage})");
        }

        static void TestDashStrike()
        {
            var h = SpawnHero(new Vector3(120, 0.05f, 0));
            var d = Dummy(new Vector3(120, 0, 7f));
            Step(h, new MovementInput { move = new Vector2(0, 1), dashPressed = true }, default);
            float staminaAfterDash = h.m.Stamina;
            Attack(h, Fwd());
            int frames = 0;
            while (d.HitCount == 0 && frames < 30) { Step(h, Fwd(), default); frames++; }
            Check("таран: попал", d.HitCount == 1 && d.LastHit.kind == AttackKind.DashStrike && Mathf.Approximately(d.TotalDamage, h.c.dashStrike.damage),
                  $"за {frames} кадров, урон {d.TotalDamage:0}, приём {d.LastHit.kind}");
            Check("таран: отдача назад и вверх", !h.m.IsDashing && h.m.Velocity.y > 0f && h.m.Velocity.z < 0f,
                  $"скорость героя {h.m.Velocity}");
            Check("таран: возврат стамины", h.m.Stamina >= staminaAfterDash + h.c.dashStrikeStaminaRefund - 0.01f,
                  $"стамина {staminaAfterDash:0.00} → {h.m.Stamina:0.00}");
            Check("таран: мощное отбрасывание", d.LastHit.knockback.z > h.c.dashStrike.knockback * 0.9f,
                  $"отбрасывание {d.LastHit.knockback}");
        }

        static void TestUppercut()
        {
            var h = SpawnHero(new Vector3(160, 0.05f, -10));
            var d = Dummy(new Vector3(160, 0, 0));
            Step(h, Fwd(), default, 10);
            Step(h, new MovementInput { move = new Vector2(0, 1), slidePressed = true, slideHeld = true }, default);
            int guard = 0;
            while (d.transform.position.z - h.m.transform.position.z > 1.9f && guard++ < 60) Step(h, Fwd(true), default);
            bool wasSliding = h.m.IsSliding;
            float slideSpeed = h.m.HorizontalSpeed;
            Attack(h, Fwd(true), 8);
            Check("апперкот: из слайда", wasSliding && d.LastHit.kind == AttackKind.Uppercut,
                  $"был слайд={wasSliding}, приём {d.LastHit.kind}, попаданий {d.HitCount}");
            Check("апперкот: подбрасывает цель", d.LastHit.knockback.y >= h.c.uppercut.knockUp - 0.01f,
                  $"подброс {d.LastHit.knockback.y:0.0} м/с");
            Check("апперкот: герой подлетает, скорость слайда сохраняется",
                  !h.m.IsSliding && !h.m.IsGrounded && h.m.HorizontalSpeed > 0.8f * slideSpeed,
                  $"в слайде {slideSpeed:0.0} → {h.m.HorizontalSpeed:0.0} м/с, vy {h.m.Velocity.y:0.0}");
        }

        static void TestSlamWave()
        {
            var h = SpawnHero(new Vector3(200, 0.05f, 0));
            var near = Dummy(new Vector3(202.5f, 0, 0));
            var far = Dummy(new Vector3(212f, 0, 0));
            var cc = h.m.GetComponent<CharacterController>();
            cc.enabled = false; h.m.transform.position = O + new Vector3(200, 10, 0); cc.enabled = true;
            Step(h, Idle, default, 2);
            Step(h, new MovementInput { slidePressed = true, slideHeld = true }, default);
            int guard = 0;
            while (!h.m.IsGrounded && guard++ < 120) Step(h, Idle, default);
            float minDamage = h.c.slamWaveDamage + h.c.slamWaveDamagePerMeter * 9f;
            Check("удар оземь: волна задела рядом", near.HitCount == 1 && near.LastHit.kind == AttackKind.SlamWave && near.TotalDamage >= minDamage,
                  $"урон {near.TotalDamage:0.0} (≥ {minDamage:0.0}), подброс {near.LastHit.knockback.y:0.0}");
            Check("удар оземь: отбрасывает от героя", near.LastHit.knockback.x > 0f && near.LastHit.knockback.y > 0f,
                  $"отбрасывание {near.LastHit.knockback}");
            Check("удар оземь: дальний не задет", far.HitCount == 0, $"попаданий {far.HitCount}");
        }

        static void TestKillFeedsFuel()
        {
            var h = SpawnHero(new Vector3(240, 0.05f, 0));
            var d = Dummy(new Vector3(240, 0, 1.5f), hp: 5f);
            h.f.Set(50f);
            int kills = 0;
            h.c.onKill.AddListener(() => kills++);
            Attack(h, Idle, 14);
            float expected = 50f + 5f * h.c.fuelPerDamage + h.c.fuelPerKill;
            Check("убийство кормит огонь", !d.IsAlive && kills == 1 && Mathf.Abs(h.f.Fuel - expected) < 0.01f,
                  $"жив={d.IsAlive}, убийств {kills}, топливо 50 → {h.f.Fuel:0.00} (ожидалось {expected:0.00})");
        }

        static void TestWallBlocks()
        {
            var h = SpawnHero(new Vector3(280, 0.05f, 0));
            Box("Wall", new Vector3(280, 1.5f, 1.0f), new Vector3(3, 3, 0.1f));
            var d = Dummy(new Vector3(280, 0, 1.7f));
            Attack(h, Idle, 14);
            Check("удар не проходит сквозь стену", d.HitCount == 0, $"попаданий {d.HitCount}");
        }
    }
}
