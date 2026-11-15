using Calabash.Animation;
using Calabash.Animation.EditorTools;
using Calabash.Movement;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Calabash.Combat.EditorTools
{
    /// <summary>
    /// Сцена VillageTest: зимняя деревня из village/ (Village_Map.fbx) + герой с мувментом, боем
    /// и видом от третьего лица + жители + снег + манга-фильтр «мрак» (style/) + звук (audio/)
    /// + враги волнами (enemies/): это и есть игра.
    /// Меню: Calabash → Village → Собрать сцену деревни.
    /// </summary>
    public static class VillageSceneBuilder
    {
        public const string MapModelName = "Village_Map";

        /// <summary>Материалы деревни из village.blend → плоские цвета палитры (ахроматика + тусклый свет в окнах).</summary>
        static readonly (string name, Color32 color)[] VillagePalette =
        {
            ("M_Plaster", new Color32(0xF2, 0xF0, 0xEA, 0xFF)),
            ("M_Snow", new Color32(0xF7, 0xF6, 0xF2, 0xFF)),
            ("M_SnowShade", new Color32(0xD6, 0xD4, 0xCE, 0xFF)),
            ("M_Ground", new Color32(0xEC, 0xEA, 0xE4, 0xFF)),
            ("M_Timber", new Color32(0x1E, 0x1E, 0x1E, 0xFF)),
            ("M_Wood", new Color32(0x3A, 0x3A, 0x3A, 0xFF)),
            ("M_Iron", new Color32(0x0B, 0x0B, 0x0B, 0xFF)),
            ("M_Void", new Color32(0x0B, 0x0B, 0x0B, 0xFF)),
            ("M_Stone", new Color32(0x9A, 0x98, 0x92, 0xFF)),
            ("M_RoofTile", new Color32(0x4A, 0x4A, 0x4A, 0xFF)),
            ("M_Mortar", new Color32(0xBD, 0xBB, 0xB5, 0xFF)),
            ("M_Brick", new Color32(0x6E, 0x6E, 0x6E, 0xFF)),
            ("M_Grime", new Color32(0x2A, 0x2A, 0x2A, 0xFF)),
            ("M_Glow", new Color32(0xFF, 0xD1, 0x66, 0xFF)),
        };

        /// <summary>Без коллизий: мелочь и декор, о который не должно цеплять.</summary>
        static readonly string[] NoColliderParts = { "Crow", "Grass", "RoadStones", "_Snow" };

        [MenuItem("Calabash/Village/Собрать сцену деревни")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = Build();
            if (path == null)
            {
                EditorUtility.DisplayDialog("Деревня", "Не найден Village_Map.fbx. Скопируйте village/fbx/Village_Map.fbx в Assets/Calabash/Art/.", "OK");
                return;
            }
            EditorSceneManager.OpenScene(path);
            Debug.Log($"[Village] Сцена собрана: {path}. Жми Play, V — вид от третьего лица.");
        }

        public static void BuildBatch()
        {
            EditorApplication.Exit(Build() != null ? 0 : 1);
        }

        public static string Build()
        {
            string mapPath = ThirdPersonSetup.FindModelPath(MapModelName);
            if (mapPath == null)
            {
                Debug.LogError("[Village] Village_Map.fbx не найден в проекте");
                return null;
            }
            string root = CombatTestSceneBuilder.RootFolder();
            ThirdPersonSetup.EnsureFolder($"{root}/Scenes");
            ThirdPersonSetup.RemapMaterials(mapPath, VillagePalette, $"{root}/Materials/Village");

            // материалы палитры полигона (кулаки, манекены) — их создаёт сборщик мувмента
            var envScene = MovementTestSceneBuilder.CreateEnvironmentScene();
            Material block = MovementTestSceneBuilder.BlockMaterial;
            Material ink = MovementTestSceneBuilder.InkMaterial;
            Material fire = MovementTestSceneBuilder.FireMaterial;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MovementTestSceneBuilder.SetupAtmosphere(NightFog, 4f, 42f);
            SetupNight();

            // деревня
            GameObject map = PlaceMap(mapPath, out Vector3 spawn, out Vector3 along, out Vector3 side, out Bounds roadB, out bool alongX, out int colliders);
            Debug.Log($"[Village] Объектов с коллизией: {colliders}, дорога {roadB.size}, старт {spawn}");

            // жители с анимациями (animation/); без моделей — манекены, как раньше
            CharacterAnimSetup.BeginBuild();
            int npcs = CharacterAnimSetup.Available ? PlaceVillagers(spawn, along, side, roadB) : 0;
            if (npcs > 0) Debug.Log($"[Village] Жителей: {npcs}");

            // манекены — только в свободных точках дороги, впереди и позади старта
            var arena = new GameObject("Dummies").transform;
            int placed = 0;
            var used = new System.Collections.Generic.List<Vector3>();
            foreach (float dir in npcs > 0 ? new float[0] : new[] { 1f, -1f })
            {
                int here = 0;
                for (float d = 7f; d < 40f && here < 5; d += 3f)
                {
                    foreach (float off in new[] { 1.4f, -1.4f, 0f })
                    {
                        Vector3 p = spawn + along * (d * dir) + side * off;
                        if (!roadB.Contains(new Vector3(p.x, roadB.center.y, p.z)) || !IsFree(p)) continue;
                        bool near = false;
                        foreach (var u in used) if ((u - p).sqrMagnitude < 2.5f * 2.5f) near = true;
                        if (near) continue;
                        CombatTestSceneBuilder.CreateDummy(arena, $"Dummy_{placed}", p, false, dir > 0 ? 100f : 60f, block, ink);
                        used.Add(p);
                        placed++; here++;
                        break;
                    }
                }
            }
            Debug.Log($"[Village] Манекенов: {placed}");

            // герой
            GameObject player = MovementTestSceneBuilder.CreatePlayer();
            player.name = "Zhdan_Village";
            CombatTestSceneBuilder.AddCombat(player, ink, fire);
            CharacterAnimSetup.SetupHero(player);                 // риг, анимации, руки в кадре
            Calabash.Vfx.EditorTools.ZhdanFireSetup.Setup(player);      // огонь: тело, кулаки, удары, слэм
            float yaw = alongX ? 90f : 0f;
            player.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
            var cam = player.GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = NightFog;
                cam.farClipPlane = 200f;
                // «мрак»: манга-фильтр как в рендерах village/renders, G — вкл/выкл; ночью — тёмный вариант
                NightGloom(Calabash.Style.EditorTools.MangaGloomSetup.AddTo(cam));
            }
            var respawn = player.GetComponent<TestRespawn>();
            if (respawn != null) respawn.killY = -20f;
            AddSnow(player.transform, root);

            // звук (audio/) и враги волнами (enemies/)
            Calabash.Audio.EditorTools.CalabashAudioSetup.Setup(player, "menu");
            var area = new Bounds(map.transform.position, Vector3.zero);
            foreach (var r in map.GetComponentsInChildren<Renderer>()) area.Encapsulate(r.bounds);
            Calabash.Enemies.EditorTools.EnemySetup.Setup(area, new[] { "Road", "Street" });
            // ход игры: комикс, пауза, улучшения, смерть → лимб (game/)
            Calabash.Game.EditorTools.GameSetup.SetupVillage();

            string scenePath = $"{root}/Scenes/VillageTest.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AddToBuild(scenePath);
            // лимб, рай и ад — вторая сцена; собирается сейчас же, с теми же контроллерами анимаций
            Calabash.Game.EditorTools.GameSetup.BuildAfterlife();
            // катсцена казни на костре — третья сцена (game/)
            Calabash.Game.EditorTools.CutsceneSetup.BuildExecution();
            // главное меню — отдельная сцена, первая в билде (game/)
            Calabash.Game.EditorTools.MenuSetup.BuildMenu();
            return scenePath;
        }

        /// <summary>Деревня — первая сцена сборки игры.</summary>
        /// <summary>
        /// Поставить карту деревни с коллизией и найти старт у колодца (центр деревни) и направление главной дороги.
        /// Общая для деревни и катсцены казни (game/).
        /// </summary>
        public static GameObject PlaceMap(string mapPath, out Vector3 spawn, out Vector3 along, out Vector3 side,
                                          out Bounds roadB, out bool alongX, out int colliders)
        {
            var mapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(mapPath);
            var map = (GameObject)PrefabUtility.InstantiatePrefab(mapAsset);
            map.name = "Village";
            colliders = 0;
            foreach (var mf in map.GetComponentsInChildren<MeshFilter>())
            {
                GameObjectUtility.SetStaticEditorFlags(mf.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                bool skip = false;
                foreach (string part in NoColliderParts) if (mf.name.Contains(part)) { skip = true; break; }
                if (skip || mf.sharedMesh == null) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                colliders++;
            }

            Transform road = FindDeep(map.transform, "SM_Road");
            roadB = road != null ? road.GetComponent<Renderer>().bounds : new Bounds(Vector3.zero, Vector3.one);
            alongX = roadB.size.x >= roadB.size.z;
            along = alongX ? Vector3.right : Vector3.forward;
            side = alongX ? Vector3.forward : Vector3.right;
            Physics.SyncTransforms();

            // старт — центр деревни (начало координат Blender, у колодца), если там дорога и свободно
            float roadY = roadB.max.y + 0.05f;
            spawn = new Vector3(alongX ? 0f : roadB.center.x, roadY, alongX ? roadB.center.z : 0f);
            if (!roadB.Contains(new Vector3(spawn.x, roadB.center.y, spawn.z)) || !IsFree(spawn))
                spawn = new Vector3(roadB.center.x, roadY, roadB.center.z);
            return map;
        }

        /// <summary>Цвет ночной мглы: туман и небо.</summary>
        public static readonly Color NightFog = new Color(0.025f, 0.027f, 0.035f);

        /// <summary>
        /// Ночь: луны почти нет, мгла в 4–42 м, окружающий свет еле виден.
        /// Главный свет — огонь самого Ждана (ZhdanFireLight) и горящие враги.
        /// </summary>
        public static void SetupNight()
        {
            var moon = Object.FindAnyObjectByType<Light>();
            if (moon != null)
            {
                moon.name = "Moon";
                moon.color = new Color(0.55f, 0.62f, 0.85f);
                moon.intensity = 0.12f;
                moon.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            }
            RenderSettings.ambientLight = new Color(0.07f, 0.075f, 0.095f);
        }

        /// <summary>Манга-фильтр для ночи: тёмное небо с бледными полосами, сильнее виньетка.</summary>
        public static void NightGloom(Calabash.Style.MangaGloom g)
        {
            if (g == null) return;
            g.skyDarken = 0.35f;
            g.skyStreaks = 0.45f;
            g.streakTone = 0.3f;
            g.snowStreaks = 0.35f;
            g.dotInk = 0.12f;
            g.vignette = 0.6f;
            g.keepColorSaturation = 0.42f;   // освещённое огнём остаётся бумагой и тушью, цветной только сам огонь
        }

        static void AddToBuild(string scenePath)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(x => x.path == scenePath);
            list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        /// <summary>
        /// Жители деревни: болтают, молятся, бродят, стоят на посту и ходят дозором.
        /// Точки ищутся на дороге (свободные от домов); кто не поместился — не ставится.
        /// </summary>
        static int PlaceVillagers(Vector3 spawn, Vector3 along, Vector3 side, Bounds road)
        {
            var parent = new GameObject("Villagers").transform;
            int count = 0;
            var used = new System.Collections.Generic.List<Vector3>();
            Vector3? Spot(float d, float off)
            {
                foreach (float dd in new[] { 0f, 1.2f, -1.2f, 2.4f, -2.4f })
                    foreach (float oo in new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f })
                    {
                        Vector3 p = spawn + along * (d + dd) + side * (off + oo);
                        if (!road.Contains(new Vector3(p.x, road.center.y, p.z)) || !IsFree(p)) continue;
                        bool near = false;
                        foreach (var u in used) if ((u - p).sqrMagnitude < 1.3f * 1.3f) near = true;
                        if (near) continue;
                        used.Add(p);
                        return p;
                    }
                return null;
            }
            Vector3[] Route(params (float d, float off)[] pts)
            {
                var list = new System.Collections.Generic.List<Vector3>();
                foreach (var (d, off) in pts)
                {
                    Vector3 p = spawn + along * d + side * off;
                    if (road.Contains(new Vector3(p.x, road.center.y, p.z)) && IsFree(p)) list.Add(p);
                }
                return list.ToArray();
            }
            void Add(string cid, VillagerNpc.Role role, float d, float off, Vector3 facing, float hp, Vector3[] route = null, float speed = 0f)
            {
                Vector3? p = route != null && route.Length > 0 ? route[0] : Spot(d, off);
                if (p == null) { Debug.Log($"[Village] {cid}: нет места у дороги ({d}, {off})"); return; }
                var go = CharacterAnimSetup.CreateNpc(cid, parent, p.Value, facing, role, route, hp);
                if (go == null) return;
                if (speed > 0f) go.GetComponent<VillagerNpc>().walkSpeed = speed;
                count++;
            }

            // у колодца: пекарь Гаврила разговаривает с крестьянином
            Add("gavrila", VillagerNpc.Role.Talker, 7f, 2.2f, along, 80f);
            Add("peasant", VillagerNpc.Role.Talker, 8.6f, 2.2f, -along, 60f);
            // вдова Агафья молится, поп Никодим стоит рядом
            Add("agafya", VillagerNpc.Role.Prayer, -8f, -2.2f, side, 50f);
            Add("nikodim", VillagerNpc.Role.Talker, -8f, -0.6f, -side, 70f);
            // слепой Ефим медленно бредёт по дороге
            Add("efim", VillagerNpc.Role.Wanderer, 0f, 0f, along, 50f, Route((-4f, 1f), (6f, 1f), (14f, 0.6f), (4f, -0.6f)), 0.8f);
            Add("peasant", VillagerNpc.Role.Wanderer, 0f, 0f, along, 60f, Route((12f, -1.2f), (22f, -1.4f), (30f, -0.8f), (18f, 1f)));
            // стража, лучники и палач Корней — враги: их выводит WaveDirector волнами (enemies/)
            return count;
        }

        public static void AddSnow(Transform player, string root)
        {
            var go = new GameObject("Snow");
            go.transform.SetParent(player, false);
            go.transform.localPosition = new Vector3(0f, 14f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.startColor = Color.white;
            main.maxParticles = 5000;
            main.prewarm = true;
            var em = ps.emission;
            em.rateOverTime = 500f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 1f, 60f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-2.4f, -1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            string matPath = $"{root}/Materials/Village/M_SnowFlake.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                mat = new Material(sh) { color = Color.white };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>Свободно ли место под человека ростом 1,8 м (землю и дорогу не считаем).</summary>
        public static bool IsFree(Vector3 feet)
        {
            return !Physics.CheckCapsule(feet + Vector3.up * 0.7f, feet + Vector3.up * 1.6f, 0.6f, ~0, QueryTriggerInteraction.Ignore);
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
