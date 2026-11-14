using System.IO;
using Calabash.Movement;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Calabash.Combat.EditorTools
{
    /// <summary>
    /// Сцена CombatTest: полигон мувмента + арена с манекенами + герой с кулаками.
    /// Меню: Calabash → Combat → Собрать тестовую арену.
    /// </summary>
    public static class CombatTestSceneBuilder
    {
        [MenuItem("Calabash/Combat/Собрать тестовую арену")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = Build();
            EditorSceneManager.OpenScene(path);
            Debug.Log($"[Combat] Арена собрана: {path}. Жми Play.");
        }

        public static void BuildBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }

        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("ZhdanCombat t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p) == "ZhdanCombat.cs")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Combat";
        }

        public static string Build()
        {
            string root = RootFolder();
            foreach (string sub in new[] { "Scenes", "Prefabs" })
                if (!AssetDatabase.IsValidFolder($"{root}/{sub}")) AssetDatabase.CreateFolder(root, sub);

            var scene = MovementTestSceneBuilder.CreateEnvironmentScene();
            Material block = MovementTestSceneBuilder.BlockMaterial;
            Material wall = MovementTestSceneBuilder.WallMaterial;
            Material ink = MovementTestSceneBuilder.InkMaterial;
            Material fire = MovementTestSceneBuilder.FireMaterial;

            // арена справа от старта
            var arena = new GameObject("Arena").transform;
            MovementTestSceneBuilder.Label(arena, "АРЕНА: манекены встают через 2,5 с", new Vector3(14, 4.5f, -12));

            // свободные манекены — комбо, тяжёлый, отбрасывание
            for (int i = 0; i < 3; i++)
                CreateDummy(arena, $"Dummy_{i}", new Vector3(9 + i * 4, 0, -16), false, 100f, block, ink);

            // мешок — закреплён, много здоровья: отрабатывать комбо
            CreateDummy(arena, "Sandbag", new Vector3(8, 0, -24), true, 400f, wall, ink);
            MovementTestSceneBuilder.Label(arena, "Мешок: комбо", new Vector3(8, 3.2f, -24));

            // дорожка для тарана: разгон, дэш, ЛКМ
            for (int i = 0; i < 3; i++)
                CreateDummy(arena, $"RamDummy_{i}", new Vector3(22, 0, -18 - i * 5), false, 60f, block, ink);
            MovementTestSceneBuilder.Block(arena, "RamLane", new Vector3(22, 0.01f, -26), new Vector3(1, 0.02f, 24), ink, collider: false);
            MovementTestSceneBuilder.Label(arena, "Таран: Shift → ЛКМ", new Vector3(22, 3.2f, -12));

            // площадка для слэма в толпу
            MovementTestSceneBuilder.Block(arena, "SlamPad", new Vector3(14, 2, -36), new Vector3(4, 4, 4), block);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                CreateDummy(arena, $"CrowdDummy_{i}", new Vector3(14 + Mathf.Cos(a) * 3.2f, 0, -30 + Mathf.Sin(a) * 2.2f), false, 50f, block, ink);
            }
            MovementTestSceneBuilder.Label(arena, "С площадки: прыжок, Ctrl — удар оземь", new Vector3(14, 6.5f, -36));

            string dummyPrefab = $"{root}/Prefabs/TrainingDummy.prefab";
            var proto = CreateDummy(null, "TrainingDummy", Vector3.zero, false, 100f, block, ink);
            PrefabUtility.SaveAsPrefabAsset(proto, dummyPrefab);
            Object.DestroyImmediate(proto);

            // герой с боёвкой
            GameObject player = MovementTestSceneBuilder.CreatePlayer();
            player.name = "Zhdan_FPS_Combat";
            AddCombat(player, ink, fire);
            player.transform.SetPositionAndRotation(new Vector3(6f, 0.05f, -8f), Quaternion.Euler(0, 140f, 0));
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, $"{root}/Prefabs/Zhdan_FPS_Combat.prefab", InteractionMode.AutomatedAction);

            string scenePath = $"{root}/Scenes/CombatTest.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            return scenePath;
        }

        /// <summary>Добавить боёвку к герою из MovementTestSceneBuilder.CreatePlayer().</summary>
        public static ZhdanCombat AddCombat(GameObject player, Material inkMat, Material fireMat)
        {
            var move = player.GetComponent<ZhdanMovement>();
            var look = player.GetComponent<FirstPersonLook>();
            var fuel = player.AddComponent<ZhdanFuel>();
            var combat = player.AddComponent<ZhdanCombat>();
            combat.input = player.GetComponent<ZhdanInput>();
            combat.fuel = fuel;
            combat.aimSource = look != null ? look.cameraPivot : null;
            var cam = player.GetComponentInChildren<Camera>();
            combat.cameraFx = cam != null ? cam.GetComponent<ZhdanCameraFx>() : null;

            var hud = player.AddComponent<CombatHud>();
            hud.combat = combat;
            hud.fuel = fuel;

            if (cam != null)
            {
                var fistsRoot = new GameObject("Fists").transform;
                fistsRoot.SetParent(cam.transform, false);
                var view = fistsRoot.gameObject.AddComponent<ZhdanFistsView>();
                view.combat = combat;
                view.movement = move;
                view.leftFist = CreateFist(fistsRoot, "Fist_L", view.leftRest, inkMat, fireMat);
                view.rightFist = CreateFist(fistsRoot, "Fist_R", view.rightRest, inkMat, fireMat);
            }
            return combat;
        }

        static Transform CreateFist(Transform parent, string name, Vector3 pos, Material inkMat, Material fireMat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            // руки «несоразмерно большие» (01-protagonist.md), тушь с тлеющими костяшками
            Part(root, "Fist", new Vector3(0, 0, 0), new Vector3(0.14f, 0.12f, 0.17f), inkMat);
            Part(root, "Forearm", new Vector3(0, -0.02f, -0.26f), new Vector3(0.085f, 0.085f, 0.38f), inkMat);
            Part(root, "Knuckles", new Vector3(0, 0.064f, 0.06f), new Vector3(0.12f, 0.012f, 0.04f), fireMat);
            return root;
        }

        static void Part(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>Манекен: пивот в ногах, капсула 1,8 м, глазницы-точки (у живых NPC нет лица — style bible).</summary>
        public static GameObject CreateDummy(Transform parent, string name, Vector3 pos, bool anchored, float hp,
                                             Material bodyMat, Material inkMat)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;

            var col = go.AddComponent<CapsuleCollider>();
            col.height = 1.8f;
            col.radius = 0.4f;
            col.center = new Vector3(0, 0.9f, 0);
            if (!anchored)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 70f;
                rb.linearDamping = 0.3f;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0, 0.9f, 0);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            if (bodyMat != null) body.GetComponent<Renderer>().sharedMaterial = bodyMat;

            for (int s = -1; s <= 1; s += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Cube);
                eye.name = s < 0 ? "Eye_L" : "Eye_R";
                Object.DestroyImmediate(eye.GetComponent<Collider>());
                eye.transform.SetParent(go.transform, false);
                eye.transform.localPosition = new Vector3(0.1f * s, 1.55f, 0.36f);
                eye.transform.localScale = new Vector3(0.06f, 0.06f, 0.04f);
                if (inkMat != null) eye.GetComponent<Renderer>().sharedMaterial = inkMat;
            }

            var labelGo = new GameObject("HP");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0, 2.15f, 0);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.08f;
            tm.fontSize = 40;
            tm.color = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { tm.font = font; labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            labelGo.AddComponent<FaceCamera>();

            var dummy = go.AddComponent<TrainingDummy>();
            dummy.maxHealth = hp;
            dummy.anchored = anchored;
            dummy.body = body.GetComponent<Renderer>();
            dummy.label = tm;
            if (bodyMat != null) dummy.baseColor = bodyMat.color;
            return go;
        }
    }
}
