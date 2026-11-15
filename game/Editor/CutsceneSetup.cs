using System.Collections.Generic;
using Calabash.Animation;
using Calabash.Animation.EditorTools;
using Calabash.Combat.EditorTools;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Calabash.Game.EditorTools
{
    /// <summary>
    /// Сцена Execution — казнь Ждана на костре, катсцена на движке (ExecutionCutscene).
    /// Та же деревня ночью: у колодца столб на хворосте, вокруг толпа с факелами, поп, царь на помосте, палач.
    /// Вызывается после сборки деревни (контроллеры анимаций общие).
    /// </summary>
    public static class CutsceneSetup
    {
        public const string SceneName = "Execution";

        // кто стоит в толпе; у кого факел
        static readonly (string cid, bool torch)[] Crowd =
        {
            ("guard", true), ("peasant", true), ("agafya", false), ("bucketguard", true), ("efim", false),
            ("peasant", true), ("archer", true), ("guard", true), ("peasant", false), ("agafya", true),
            ("bucketguard", false), ("peasant", true), ("archer", true), ("efim", false),
        };

        public static string BuildExecution()
        {
            string mapPath = ThirdPersonSetup.FindModelPath(VillageSceneBuilder.MapModelName);
            if (mapPath == null || !CharacterAnimSetup.Available) return null;
            string root = GameSetup.RootFolder();
            ThirdPersonSetup.EnsureFolder($"{root}/Scenes");
            ThirdPersonSetup.EnsureFolder($"{root}/Materials");

            MovementTestSceneBuilder.CreateEnvironmentScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MovementTestSceneBuilder.SetupAtmosphere(VillageSceneBuilder.NightFog, 5f, 50f);
            VillageSceneBuilder.SetupNight();
            RenderSettings.ambientLight *= 1.5f;      // в катсцене лица должны читаться

            VillageSceneBuilder.PlaceMap(mapPath, out Vector3 spawn, out Vector3 along, out Vector3 side, out Bounds road, out _, out _);

            // столб — на дороге в 9 м от колодца, толпа со стороны колодца
            Vector3 P = Free(Ground(spawn + along * 9f), 4f);
            Vector3 F = -along;
            Vector3 R = Vector3.Cross(Vector3.up, F).normalized;

            Material wood = Mat($"{root}/Materials/M_PyreWood.mat", new Color32(0x2A, 0x22, 0x1C, 0xFF));
            Material dark = Mat($"{root}/Materials/M_TorchWood.mat", new Color32(0x1A, 0x16, 0x12, 0xFF));
            Material flame = FindMat("M_FX_Flame"), small = FindMat("M_FX_SmallFlame"), sparks = FindMat("M_FX_Sparks"), smoke = FindMat("M_FX_Smoke");

            var stage = new GameObject("Execution").transform;
            Transform stake = BuildPyre(stage, P, wood);

            // Ждан у столба, на хворосте
            Animator zhdan = Hero(stage, P + F * 0.32f + Vector3.up * 0.55f, F);

            var crowd = new List<Animator>();
            Animator Npc(string cid, Vector3 pos, Vector3 face)
            {
                var go = CharacterAnimSetup.CreateNpc(cid, stage, pos, face, VillagerNpc.Role.Stander, null, 9999f);
                if (go == null) return null;
                Object.DestroyImmediate(go.GetComponent<VillagerNpc>());
                Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
                return go.GetComponentInChildren<Animator>();
            }
            Vector3 Face(Vector3 from) { var d = P - from; d.y = 0f; return d; }

            // главные лица
            Vector3 gp = Spot(P + F * 5.2f - R * 2.2f);
            Animator gavrila = Npc("gavrila", gp, Face(gp));
            Vector3 np = Spot(P + F * 2.8f + R * 2.0f);
            Animator nikodim = Npc("nikodim", np, Face(np) * 0.3f + F);   // вполоборота к толпе
            Vector3 kp = Spot(P + F * 11.5f + R * 0.5f);
            Platform(stage, kp, wood);
            Animator king = Npc("king", kp + Vector3.up * 0.8f, Face(kp));
            Vector3 kFrom = Spot(P + F * 12.5f + R * 5.5f), kTo = P + F * 1.9f + R * 0.4f;
            kTo.y = kFrom.y;
            Animator korney = Npc("korney", kFrom, kTo - kFrom);
            Transform korneyTorch = korney != null ? Torch(korney, dark, small, 14f) : null;

            // жаровни: у помоста царя и по бокам костра — свет на главных лиц до поджога
            Brazier(stage, Ground(kp + R * 1.7f + F * 0.6f), dark, small);
            Brazier(stage, Ground(kp - R * 1.7f + F * 0.6f), dark, small);
            Brazier(stage, Free(Ground(P + F * 2.2f + R * 2.4f), 1.5f), dark, small);
            Brazier(stage, Free(Ground(P + F * 2.2f - R * 2.4f), 1.5f), dark, small);

            // толпа: дуга перед столбом, два ряда
            int i = 0;
            foreach (var (cid, torch) in Crowd)
            {
                int row = i % 2;
                float rad = row == 0 ? 6.5f : 8.6f;
                float ang = Mathf.Lerp(-75f, 75f, (i / 2) / 6f) + (row == 0 ? 0f : 8f);
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * F;
                Vector3 pos = Spot(P + dir * rad);
                i++;
                if ((pos - kp).sqrMagnitude < 2.5f * 2.5f || (pos - gp).sqrMagnitude < 1.2f || (pos - np).sqrMagnitude < 1.2f) continue;
                var a = Npc(cid, pos, Face(pos));
                if (a == null) continue;
                crowd.Add(a);
                if (torch) Torch(a, dark, small, 14f);
            }

            // огонь костра, свет, звук
            ParticleSystem fire = Flames(stage, P, flame);
            ParticleSystem embers = Embers(stage, P, sparks);
            ParticleSystem smk = Smoke(stage, P, smoke);
            var lightGo = new GameObject("PyreLight");
            lightGo.transform.SetParent(stage, false);
            lightGo.transform.position = P + Vector3.up * 2.6f + F * 1.6f;
            var pyreLight = lightGo.AddComponent<Light>();
            pyreLight.type = LightType.Point;
            pyreLight.color = new Color(1f, 0.68f, 0.4f);
            pyreLight.range = 24f;
            pyreLight.intensity = 0f;
            pyreLight.shadows = LightShadows.Soft;
            pyreLight.shadowNearPlane = 1.2f;
            pyreLight.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Low;
            var snd = lightGo.AddComponent<AudioSource>();
            snd.clip = FindClip("fire_loop_01");
            snd.loop = true;
            snd.playOnAwake = false;
            snd.spatialBlend = 0.5f;
            snd.volume = 0f;

            // камера
            var camGo = new GameObject("CutsceneCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = VillageSceneBuilder.NightFog;
            camGo.AddComponent<AudioListener>();
            // слабый заполняющий свет от камеры: без него в ночи не видно лиц на крупных планах
            var fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(camGo.transform, false);
            fillGo.transform.localPosition = new Vector3(0.4f, 0.6f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.92f, 0.82f);
            fill.range = 14f;
            fill.intensity = 9f;
            fill.shadows = LightShadows.None;
            camGo.transform.position = P + F * 15f + Vector3.up * 8f;
            camGo.transform.LookAt(P);
            VillageSceneBuilder.NightGloom(Calabash.Style.EditorTools.MangaGloomSetup.AddTo(cam));
            VillageSceneBuilder.AddSnow(camGo.transform, CombatTestSceneBuilder.RootFolder());

            Calabash.Audio.EditorTools.CalabashAudioSetup.Setup(null, "menu");

            var cs = new GameObject("Cutscene").AddComponent<ExecutionCutscene>();
            cs.cam = cam;
            cs.stake = stake;
            cs.front = F;
            cs.zhdan = zhdan;
            cs.gavrila = gavrila;
            cs.nikodim = nikodim;
            cs.king = king;
            cs.korney = korney;
            cs.crowd = crowd.ToArray();
            cs.korneyTorch = korneyTorch;
            cs.korneyFrom = kFrom;
            cs.korneyTo = kTo;
            cs.pyreFire = fire;
            cs.pyreEmbers = embers;
            cs.pyreSmoke = smk;
            cs.pyreLight = pyreLight;
            cs.pyreSound = snd;

            string path = $"{root}/Scenes/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            GameSetup.AddToBuild(path, 1);
            Debug.Log($"[Катсцена] Казнь: толпа {crowd.Count}, столб {P}");
            return path;

            Vector3 Spot(Vector3 p) => Free(Ground(p), 3f);
        }

        // ------------------------------------------------------------------ место
        internal static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }

        /// <summary>Ближайшее свободное место (по спирали до maxR м), иначе само p.</summary>
        static Vector3 Free(Vector3 p, float maxR)
        {
            if (VillageSceneBuilder.IsFree(p)) return p;
            for (float r = 0.6f; r <= maxR; r += 0.6f)
                for (int a = 0; a < 12; a++)
                {
                    Vector3 q = Ground(p + Quaternion.AngleAxis(a * 30f, Vector3.up) * Vector3.forward * r);
                    if (VillageSceneBuilder.IsFree(q)) return q;
                }
            return p;
        }

        // ------------------------------------------------------------------ реквизит
        static Transform BuildPyre(Transform parent, Vector3 p, Material wood)
        {
            var root = new GameObject("Pyre").transform;
            root.SetParent(parent, false);
            root.position = p;
            var pole = Prim(PrimitiveType.Cylinder, root, "Stake", new Vector3(0f, 1.9f, 0f), new Vector3(0.24f, 1.9f, 0.24f), wood);
            Prim(PrimitiveType.Cylinder, root, "Cross", new Vector3(0f, 2.75f, 0f), new Vector3(0.12f, 0.75f, 0.12f), wood)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var rnd = new System.Random(7);
            for (int ring = 0; ring < 3; ring++)
            {
                int n = 10 + ring * 5;
                float rad = 0.55f + ring * 0.42f;
                for (int k = 0; k < n; k++)
                {
                    float a = (k + (float)rnd.NextDouble() * 0.5f) / n * 360f;
                    var log = Prim(PrimitiveType.Cylinder, root, "Log", Vector3.zero, new Vector3(0.13f, 0.7f, 0.13f), wood).transform;
                    Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward;
                    log.localPosition = dir * rad + Vector3.up * (0.45f - ring * 0.12f);
                    log.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f - (35f + ring * 12f) + (float)rnd.NextDouble() * 10f, 0f, 0f);
                }
            }
            // настил, на котором стоит Ждан
            Prim(PrimitiveType.Cylinder, root, "Deck", new Vector3(0f, 0.5f, 0f), new Vector3(1.0f, 0.05f, 1.0f), wood);
            return root;
        }

        static void Platform(Transform parent, Vector3 p, Material wood)
        {
            Prim(PrimitiveType.Cube, parent, "KingPlatform", p + Vector3.up * 0.4f, new Vector3(2.4f, 0.8f, 2.4f), wood);
        }

        internal static GameObject Prim(PrimitiveType t, Transform parent, string name, Vector3 local, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        internal static Animator Hero(Transform parent, Vector3 pos, Vector3 facing)
        {
            string path = CharacterAnimSetup.PrepareModel(CharacterAnimSetup.HeroModel, true);
            if (path == null) return null;
            var go = new GameObject("Zhdan_Bound");
            go.transform.SetParent(parent, false);
            facing.y = 0f;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing.normalized));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, CharacterAnimSetup.ModelYaw, 0f);
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = CharacterAnimSetup.HeroController(path);
            anim.applyRootMotion = false;
            return anim;
        }

        /// <summary>Факел в правой руке: палка, пламя, дрожащий свет.</summary>
        static Transform Torch(Animator who, Material stick, Material flame, float intensity)
        {
            Transform hand = FindDeep(who.transform, "Hand.R");
            var t = new GameObject("Torch").transform;
            if (hand != null)
            {
                t.SetParent(hand, false);
                t.localPosition = Vector3.zero;
                float ls = hand.lossyScale.x;
                if (ls > 1e-4f) t.localScale = Vector3.one / ls;     // кости бывают в масштабе 100 или 0,01
            }
            else
            {
                t.SetParent(who.transform.parent != null ? who.transform.parent : who.transform, false);
                t.localPosition = new Vector3(0.35f, 0.95f, 0.3f);
            }
            t.rotation = Quaternion.identity;
            var s = Prim(PrimitiveType.Cylinder, t, "Stick", new Vector3(0f, 0.08f, 0f), new Vector3(0.05f, 0.3f, 0.05f), stick);
            s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var f = new GameObject("Flame");
            f.transform.SetParent(t, false);
            f.transform.localPosition = new Vector3(0f, 0.38f, 0f);
            var ps = f.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xD1, 0x66, 0xFF), new Color32(0xFF, 0x7A, 0x18, 0xFF));
            main.gravityModifier = -0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.prewarm = true;
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.06f;
            Flipbook(ps, flame, 16, 4);

            var l = new GameObject("Light");
            l.transform.SetParent(t, false);
            l.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var lamp = l.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.7f, 0.42f);
            lamp.range = 12f;
            lamp.intensity = intensity;
            lamp.shadows = LightShadows.None;
            l.AddComponent<TorchFlicker>().intensity = intensity;
            return t;
        }

        /// <summary>Жаровня на треноге: чаша с огнём и дрожащий свет.</summary>
        static void Brazier(Transform parent, Vector3 p, Material metal, Material flame)
        {
            var root = new GameObject("Brazier").transform;
            root.SetParent(parent, false);
            root.position = p;
            Prim(PrimitiveType.Cylinder, root, "Leg", new Vector3(0f, 0.5f, 0f), new Vector3(0.08f, 0.5f, 0.08f), metal);
            Prim(PrimitiveType.Cylinder, root, "Bowl", new Vector3(0f, 1.05f, 0f), new Vector3(0.6f, 0.08f, 0.6f), metal);
            var ps = NewPs(root, "Fire", p + Vector3.up * 1.15f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xD1, 0x66, 0xFF), new Color32(0xFF, 0x7A, 0x18, 0xFF));
            main.gravityModifier = -0.4f;
            main.maxParticles = 60;
            main.prewarm = true;
            main.playOnAwake = true;
            var em = ps.emission; em.rateOverTime = 30f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.22f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Flipbook(ps, flame, 16, 4);
            var l = new GameObject("Light");
            l.transform.SetParent(root, false);
            l.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var lamp = l.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.7f, 0.42f);
            lamp.range = 10f;
            lamp.shadows = LightShadows.None;
            l.AddComponent<TorchFlicker>().intensity = 16f;
        }

        /// <summary>Огонь на теле актёра (Ждан в концовках): языки пламени по фигуре и тёплый свет.</summary>
        internal static (ParticleSystem, Light) BodyFire(Transform actor, Material flame)
        {
            var ps = NewPs(actor, "BodyFire", actor.position + Vector3.up * 1f);
            ps.transform.SetParent(actor, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xD1, 0x66, 0xFF), new Color32(0xFF, 0x7A, 0x18, 0xFF));
            main.gravityModifier = -0.5f;
            main.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = 90f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.55f, 1.7f, 0.4f);
            Flipbook(ps, flame, 16, 4);
            var l = new GameObject("BodyLight");
            l.transform.SetParent(actor, false);
            l.transform.localPosition = new Vector3(0f, 1.4f, 0.8f);
            var lamp = l.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.68f, 0.4f);
            lamp.range = 12f;
            lamp.intensity = 14f;
            lamp.shadows = LightShadows.None;
            return (ps, lamp);
        }

        // ------------------------------------------------------------------ огонь костра
        static ParticleSystem Flames(Transform parent, Vector3 p, Material m)
        {
            var ps = NewPs(parent, "PyreFire", p + Vector3.up * 0.3f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xD1, 0x66, 0xFF), new Color32(0xC2, 0x4E, 0x00, 0xFF));
            main.gravityModifier = -0.35f;
            main.maxParticles = 700;
            var em = ps.emission; em.rateOverTime = 170f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.3f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Flipbook(ps, m, 16, 4);
            return ps;
        }

        static ParticleSystem Embers(Transform parent, Vector3 p, Material m)
        {
            var ps = NewPs(parent, "PyreEmbers", p + Vector3.up * 1f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xE0, 0x90, 0xFF), new Color32(0xFF, 0x7A, 0x18, 0xFF));
            main.gravityModifier = -0.1f;
            main.maxParticles = 300;
            var em = ps.emission; em.rateOverTime = 50f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25f; sh.radius = 1f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Flipbook(ps, m, 4, 2);
            return ps;
        }

        static ParticleSystem Smoke(Transform parent, Vector3 p, Material m)
        {
            var ps = NewPs(parent, "PyreSmoke", p + Vector3.up * 3f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startColor = new Color(0.12f, 0.11f, 0.1f, 0.7f);
            main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = 12f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.8f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Flipbook(ps, m, 8, 8);
            return ps;
        }

        internal static ParticleSystem NewPs(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            return ps;
        }

        internal static void Flipbook(ParticleSystem ps, Material m, int x, int y)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true; tsa.numTilesX = x; tsa.numTilesY = y;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ ассеты
        internal static Material Mat(string path, Color c)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material FindMat(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Material"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Material>(p);
            }
            return null;
        }

        static AudioClip FindClip(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:AudioClip"))
                return AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            return null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
