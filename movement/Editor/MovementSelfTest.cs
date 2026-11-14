using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Calabash.Movement.EditorTools
{
    /// <summary>
    /// Самотест мувмента без Play Mode: во временной сцене гоняет ZhdanMovement.Simulate
    /// с синтетическим вводом (60 кадров/с) и проверяет каждую механику.
    /// Меню: Calabash → Movement → Запустить самотест. Из консоли: -executeMethod ...MovementSelfTest.RunBatch
    /// </summary>
    public static class MovementSelfTest
    {
        const float Dt = 1f / 60f;
        static readonly Vector3 O = new Vector3(5000f, 0f, 5000f); // подальше от содержимого открытой сцены

        [MenuItem("Calabash/Movement/Запустить самотест")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Run(out string report);
            if (ok) Debug.Log(report); else Debug.LogError(report);
            EditorUtility.DisplayDialog("Самотест мувмента", ok ? "Все проверки прошли." : "Есть провалы — см. Console.", "OK");
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
            log = new StringBuilder("[MovementSelfTest]\n");
            fails = 0;
            // Открытую сохранённую сцену не трогаем: тест идёт в дополнительной временной сцене далеко от начала координат.
            // Безымянную (batchmode, пустой редактор) просто заменяем.
            Scene prevActive = SceneManager.GetActiveScene();
            bool additive = !string.IsNullOrEmpty(prevActive.path) || prevActive.isDirty;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                additive ? NewSceneMode.Additive : NewSceneMode.Single);
            if (!additive) prevActive = default;
            SceneManager.SetActiveScene(scene);
            try
            {
                BuildArena();
                TestRun();
                TestAirControl();
                TestSlide();
                TestSlideJump();
                TestDashAndStamina();
                TestDashJump();
                TestSlamAndBounce();
                TestWallJumps();
                TestCoyote();
                TestJumpBuffer();
                TestLowTunnel();
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
                else
                {
                    foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
                }
            }
            log.AppendLine(fails == 0 ? "ИТОГ: всё прошло" : $"ИТОГ: провалов {fails}");
            report = log.ToString();
            return fails == 0;
        }

        // ───────────── арена ─────────────
        static void BuildArena()
        {
            Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(300, 1, 300));
            Box("Wall", new Vector3(0, 15, 10), new Vector3(20, 30, 1));          // грань стены на z = 9.5
            Box("Ledge", new Vector3(45, 2.5f, 0), new Vector3(10, 5, 10));      // верх y=5, край x=50
            Box("TunnelCeiling", new Vector3(-40, 1.3f + 0.25f, 0), new Vector3(4, 0.5f, 20)); // потолок на 1,3 м
        }

        static void Box(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = O + center;
            go.transform.localScale = size;
            spawned.Add(go);
        }

        static ZhdanMovement Spawn(Vector3 local, float yaw = 0f)
        {
            var go = new GameObject("TestZhdan");
            go.transform.SetPositionAndRotation(O + local, Quaternion.Euler(0, yaw, 0));
            var cc = go.AddComponent<CharacterController>();
            ConfigureController(cc);
            var m = go.AddComponent<ZhdanMovement>();
            m.Init();
            spawned.Add(go);
            Physics.SyncTransforms();
            return m;
        }

        public static void ConfigureController(CharacterController cc)
        {
            cc.radius = 0.4f;
            cc.height = 1.8f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.06f;
            cc.minMoveDistance = 0f;
        }

        static void Kill(ZhdanMovement m) => Object.DestroyImmediate(m.gameObject);

        // ───────────── ввод ─────────────
        static MovementInput Hold(Vector2 move, bool slide = false, bool jump = false) =>
            new MovementInput { move = move, slideHeld = slide, jumpHeld = jump };

        static void Step(ZhdanMovement m, MovementInput inp, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                Physics.SyncTransforms();
                m.Simulate(inp, Dt);
            }
        }

        static void Press(ZhdanMovement m, MovementInput inp, bool jump = false, bool dash = false, bool slide = false)
        {
            inp.jumpPressed = jump; inp.jumpHeld |= jump;
            inp.dashPressed = dash;
            inp.slidePressed = slide; inp.slideHeld |= slide;
            Step(m, inp);
        }

        static int StepUntil(ZhdanMovement m, MovementInput inp, System.Func<bool> cond, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (cond()) return i;
                Step(m, inp);
            }
            return cond() ? maxFrames : -1;
        }

        static void MoveBody(ZhdanMovement m, Vector3 local)
        {
            var cc = m.GetComponent<CharacterController>();
            cc.enabled = false;
            m.transform.position = O + local;
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        static void Check(string name, bool cond, string detail)
        {
            if (!cond) fails++;
            log.AppendLine($"{(cond ? "PASS" : "FAIL")}  {name}: {detail}");
        }

        static readonly Vector2 W = new Vector2(0, 1);
        static readonly Vector2 None = Vector2.zero;

        // ───────────── тесты ─────────────
        static void TestRun()
        {
            var m = Spawn(new Vector3(0, 0.1f, -60));
            Step(m, Hold(None), 10);
            Check("стоит на земле", m.IsGrounded, $"grounded={m.IsGrounded}");
            Step(m, Hold(W), 60);
            Check("бег", Mathf.Abs(m.HorizontalSpeed - m.runSpeed) < 0.5f && m.IsGrounded,
                  $"скорость {m.HorizontalSpeed:0.00} при runSpeed {m.runSpeed}");
            Step(m, Hold(None), 20);
            Check("остановка без ввода", m.HorizontalSpeed < 0.5f, $"скорость {m.HorizontalSpeed:0.00}");
            Kill(m);
        }

        static void TestAirControl()
        {
            var m = Spawn(new Vector3(80, 30, -60));
            Step(m, Hold(None), 2);
            Step(m, Hold(new Vector2(1, 0)), 18);
            Check("контроль в воздухе", !m.IsGrounded && m.HorizontalSpeed > 0.8f * m.runSpeed,
                  $"за 0,3 с с нуля набрал {m.HorizontalSpeed:0.0} м/с");
            Kill(m);
        }

        static void TestSlide()
        {
            var m = Spawn(new Vector3(-20, 0.1f, -60));
            Step(m, Hold(None), 10);
            Step(m, Hold(W), 20);
            Press(m, Hold(W), slide: true);
            Step(m, Hold(W, slide: true), 30);
            Check("слайд: состояние и хитбокс", m.IsSliding && Mathf.Abs(m.CurrentHeight - m.slideHeight) < 0.01f,
                  $"sliding={m.IsSliding}, рост {m.CurrentHeight:0.00}");
            Check("слайд: скорость ≥ минимума и выше бега", m.HorizontalSpeed >= m.slideMinSpeed - 0.1f && m.HorizontalSpeed > m.runSpeed,
                  $"{m.HorizontalSpeed:0.0} м/с (бег {m.runSpeed})");
            Step(m, Hold(W), 2);
            Check("слайд: отпустил — встал", !m.IsSliding && Mathf.Abs(m.CurrentHeight - m.standingHeight) < 0.01f,
                  $"sliding={m.IsSliding}, рост {m.CurrentHeight:0.00}");
            Kill(m);
        }

        static void TestSlideJump()
        {
            var m = Spawn(new Vector3(-20, 0.1f, -60));
            Step(m, Hold(None), 10);
            Step(m, Hold(W), 20);
            Press(m, Hold(W), slide: true);
            Step(m, Hold(W, slide: true), 15);
            float s0 = m.HorizontalSpeed;
            Press(m, Hold(None, slide: true), jump: true);
            bool jumped = m.Velocity.y > 5f && !m.IsSliding;
            Step(m, Hold(None), 18);
            Check("слайд-прыжок сохраняет скорость", jumped && !m.IsGrounded && m.HorizontalSpeed >= 0.9f * s0,
                  $"в слайде {s0:0.0} м/с → через 0,3 с в воздухе {m.HorizontalSpeed:0.0} м/с, vy старта ok={jumped}");
            Kill(m);
        }

        static void TestDashAndStamina()
        {
            var m = Spawn(new Vector3(20, 0.1f, -80));
            Step(m, Hold(None), 10);
            Press(m, Hold(W), dash: true);
            Check("дэш: скорость", m.IsDashing && Mathf.Abs(m.HorizontalSpeed - m.dashSpeed) < 0.5f && m.IsInvulnerable,
                  $"dashing={m.IsDashing}, {m.HorizontalSpeed:0.0} м/с, неуязвим={m.IsInvulnerable}");
            Check("дэш: списан заряд", m.Stamina < m.staminaCharges - 0.9f, $"стамина {m.Stamina:0.00}");
            Step(m, Hold(None), 12);
            Check("дэш: закончился", !m.IsDashing && m.HorizontalSpeed <= m.dashExitSpeed + 0.5f,
                  $"dashing={m.IsDashing}, {m.HorizontalSpeed:0.0} м/с");
            Press(m, Hold(W), dash: true); Step(m, Hold(None), 12);
            Press(m, Hold(W), dash: true); Step(m, Hold(None), 12);
            float before = m.Stamina;
            Press(m, Hold(W), dash: true);
            Check("дэш: без стамины не работает", !m.IsDashing, $"стамина перед 4-м дэшем {before:0.00}");
            Step(m, Hold(None), 240);
            Check("стамина восстановилась", Mathf.Abs(m.Stamina - m.staminaCharges) < 0.01f, $"{m.Stamina:0.00}/{m.staminaCharges}");
            Kill(m);
        }

        static void TestDashJump()
        {
            var m = Spawn(new Vector3(-80, 0.1f, -80));
            Step(m, Hold(None), 10);
            Press(m, Hold(W), dash: true);
            Step(m, Hold(W), 3);
            Press(m, Hold(W), jump: true);
            Check("дэш-прыжок", !m.IsDashing && m.Velocity.y > 5f && m.HorizontalSpeed > m.dashExitSpeed + 5f,
                  $"vy {m.Velocity.y:0.0}, гориз. {m.HorizontalSpeed:0.0} м/с, стамина {m.Stamina:0.00}");
            Kill(m);
        }

        static void TestSlamAndBounce()
        {
            var m = Spawn(new Vector3(80, 20, 0));
            float landedHeight = -1f;
            m.onSlamLand.AddListener(h => landedHeight = h);
            Step(m, Hold(None), 1);
            Press(m, Hold(None), slide: true);
            Check("слэм: начался", m.IsSlamming && m.Velocity.y <= -m.slamSpeed + 0.1f, $"slamming={m.IsSlamming}, vy {m.Velocity.y:0.0}");
            int frames = StepUntil(m, Hold(None), () => m.IsGrounded, 120);
            Check("слэм: удар о землю", frames >= 0 && landedHeight > 18f && !m.IsSlamming,
                  $"за {frames} кадров, высота падения {landedHeight:0.0} м");
            Press(m, Hold(None), jump: true);
            Check("слэм: отскок выше обычного прыжка", m.Velocity.y > m.jumpSpeed + 3f,
                  $"vy отскока {m.Velocity.y:0.0} (обычный прыжок {m.jumpSpeed})");

            // без прыжка окно закрывается
            MoveBody(m, new Vector3(80, 20, 20));
            Step(m, Hold(None), 1);
            Press(m, Hold(None), slide: true);
            StepUntil(m, Hold(None), () => m.IsGrounded, 120);
            Step(m, Hold(None), Mathf.CeilToInt(m.slamBounceWindow / Dt) + 2);
            Press(m, Hold(None), jump: true);
            // в кадре прыжка уже применилась гравитация: vy = jumpSpeed − g·dt
            Check("слэм: после окна — обычный прыжок", Mathf.Abs(m.Velocity.y - (m.jumpSpeed - m.gravity * Dt)) < 0.05f, $"vy {m.Velocity.y:0.0}");
            Kill(m);
        }

        static void TestWallJumps()
        {
            var m = Spawn(new Vector3(0, 20, 9.5f - 0.4f - 0.15f)); // в воздухе, в 15 см от стены (грань z=9,5)
            int done = 0;
            for (int i = 0; i < m.maxWallJumps; i++)
            {
                MoveBody(m, new Vector3(0, 20, 9.5f - 0.4f - 0.15f));
                Step(m, Hold(None), 40); // отлетели и начали падать
                MoveBody(m, new Vector3(0, 20, 9.5f - 0.4f - 0.15f));
                Press(m, Hold(None), jump: true);
                if (m.Velocity.y > m.wallJumpUpSpeed - m.gravity * Dt - 0.1f && m.Velocity.z < -m.wallJumpAwaySpeed * 0.9f) done++;
            }
            Check("прыжки от стен", done == m.maxWallJumps && m.WallJumpsLeft == 0,
                  $"удалось {done}/{m.maxWallJumps}, осталось {m.WallJumpsLeft}");
            MoveBody(m, new Vector3(0, 20, 9.5f - 0.4f - 0.15f));
            Step(m, Hold(None), 40);
            MoveBody(m, new Vector3(0, 20, 9.5f - 0.4f - 0.15f));
            Press(m, Hold(None), jump: true);
            Check("прыжки от стен: лимит", m.Velocity.y < 0f, $"vy после лишнего нажатия {m.Velocity.y:0.0}");
            StepUntil(m, Hold(None), () => m.IsGrounded, 300);
            Check("прыжки от стен: сброс при приземлении", m.WallJumpsLeft == m.maxWallJumps, $"осталось {m.WallJumpsLeft}");
            Kill(m);
        }

        static void TestCoyote()
        {
            foreach (int waitFrames in new[] { 4, 15 })
            {
                var m = Spawn(new Vector3(46, 5.1f, 0), 90f);
                Step(m, Hold(None), 10);
                int f = StepUntil(m, Hold(W), () => !m.IsGrounded, 120);
                Step(m, Hold(W), waitFrames);
                Press(m, Hold(W), jump: true);
                bool jumped = m.Velocity.y > 5f;
                bool expect = waitFrames * Dt < m.coyoteTime;
                Check($"койот-тайм ({waitFrames * Dt:0.00} с после края)", f >= 0 && jumped == expect,
                      $"прыгнул={jumped}, ожидалось {expect}");
                Kill(m);
            }
        }

        static void TestJumpBuffer()
        {
            var m = Spawn(new Vector3(80, 3, 40));
            StepUntil(m, Hold(None), () => m.transform.position.y < O.y + 0.7f, 120);
            bool airborne = !m.IsGrounded;
            Press(m, Hold(None), jump: true);
            int f = StepUntil(m, Hold(None), () => m.Velocity.y > 5f, 9);
            Check("буфер прыжка", airborne && f >= 0, $"нажал в воздухе={airborne}, прыгнул через {f} кадров после касания");
            Kill(m);
        }

        static void TestLowTunnel()
        {
            var m = Spawn(new Vector3(-40, 0.1f, -20));
            Step(m, Hold(None), 10);
            Step(m, Hold(W), 20);
            Press(m, Hold(W), slide: true);
            Step(m, Hold(W, slide: true), 30);
            bool inside = Mathf.Abs(m.transform.position.z - O.z) < 9f;
            Step(m, Hold(None), 5);
            Check("низкий туннель: под потолком не встаёт", inside && m.IsSliding && m.CurrentHeight < 1.3f,
                  $"в туннеле={inside}, sliding={m.IsSliding}, рост {m.CurrentHeight:0.00}");
            StepUntil(m, Hold(None), () => m.transform.position.z > O.z + 12f, 120);
            Step(m, Hold(None), 3);
            Check("низкий туннель: выехал и встал", !m.IsSliding && Mathf.Abs(m.CurrentHeight - m.standingHeight) < 0.01f,
                  $"sliding={m.IsSliding}, рост {m.CurrentHeight:0.00}");
            Kill(m);
        }
    }
}
