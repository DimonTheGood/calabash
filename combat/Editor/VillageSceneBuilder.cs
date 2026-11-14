using Calabash.Movement;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Calabash.Combat.EditorTools
{
    /// <summary>
    /// Сцена VillageTest: зимняя деревня из village/ (Village_Map.fbx) + герой с мувментом, боем
    /// и видом от третьего лица + манекены на дороге + снег + манга-фильтр «мрак» (style/).
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
            MovementTestSceneBuilder.SetupAtmosphere(new Color32(0xDD, 0xDB, 0xD5, 0xFF), 15f, 120f);

            // деревня
            var mapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(mapPath);
            var map = (GameObject)PrefabUtility.InstantiatePrefab(mapAsset);
            map.name = "Village";
            int colliders = 0;
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
            Bounds roadB = road != null ? road.GetComponent<Renderer>().bounds : new Bounds(Vector3.zero, Vector3.one);
            Transform hut = FindDeep(map.transform, "SM_House_Hut");
            if (hut != null) Debug.Log($"[Village] Хижина: размер {hut.GetComponent<Renderer>().bounds.size} (в Blender 6,1 × 7,1 × 5,7 м)");
            bool alongX = roadB.size.x >= roadB.size.z;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 side = alongX ? Vector3.forward : Vector3.right;
            Physics.SyncTransforms();

            // старт — центр деревни (начало координат Blender, у колодца), если там дорога и свободно
            float roadY = roadB.max.y + 0.05f;
            Vector3 spawn = new Vector3(alongX ? 0f : roadB.center.x, roadY, alongX ? roadB.center.z : 0f);
            if (!roadB.Contains(new Vector3(spawn.x, roadB.center.y, spawn.z)) || !IsFree(spawn))
                spawn = new Vector3(roadB.center.x, roadY, roadB.center.z);
            Debug.Log($"[Village] Объектов с коллизией: {colliders}, дорога {roadB.size}, старт {spawn}");

            // манекены — только в свободных точках дороги, впереди и позади старта
            var arena = new GameObject("Dummies").transform;
            int placed = 0;
            var used = new System.Collections.Generic.List<Vector3>();
            foreach (float dir in new[] { 1f, -1f })
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
            float yaw = alongX ? 90f : 0f;
            player.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
            var cam = player.GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cam.backgroundColor = new Color32(0xDD, 0xDB, 0xD5, 0xFF);
                cam.farClipPlane = 600f;
                // «мрак»: манга-фильтр как в рендерах village/renders, G — вкл/выкл
                Calabash.Style.EditorTools.MangaGloomSetup.AddTo(cam);
            }
            var respawn = player.GetComponent<TestRespawn>();
            if (respawn != null) respawn.killY = -20f;
            AddSnow(player.transform, root);

            string scenePath = $"{root}/Scenes/VillageTest.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            return scenePath;
        }

        static void AddSnow(Transform player, string root)
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
        static bool IsFree(Vector3 feet)
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
