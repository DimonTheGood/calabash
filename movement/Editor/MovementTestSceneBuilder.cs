using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Calabash.Movement.EditorTools
{
    /// <summary>
    /// Собирает тестовую сцену-полигон и префаб героя от первого лица.
    /// Меню: Calabash → Movement → Собрать тестовую сцену. Результат кладётся рядом со скриптами:
    /// Scenes/MovementTest.unity, Prefabs/Zhdan_FPS.prefab, Materials/, Textures/.
    /// </summary>
    public static class MovementTestSceneBuilder
    {
        static readonly Color Paper = new Color32(0xF2, 0xF0, 0xEA, 0xFF);
        static readonly Color Gray = new Color32(0x6E, 0x6E, 0x6E, 0xFF);
        static readonly Color LightGray = new Color32(0xB4, 0xB2, 0xAC, 0xFF);
        static readonly Color Ink = new Color32(0x0B, 0x0B, 0x0B, 0xFF);
        static readonly Color Fire = new Color32(0xFF, 0x7A, 0x18, 0xFF);

        static Material matFloor, matBlock, matWall, matInk, matFire;

        [MenuItem("Calabash/Movement/Собрать тестовую сцену")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string scenePath = Build();
            EditorSceneManager.OpenScene(scenePath);
            Debug.Log($"[Movement] Тестовая сцена собрана: {scenePath}. Жми Play.");
        }

        public static void BuildBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }

        /// <summary>Папка модуля (где лежат Scripts/ и Editor/), например Assets/Calabash/Movement.</summary>
        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("ZhdanMovement t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p) == "ZhdanMovement.cs")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Movement";
        }

        public static string Build()
        {
            string root = RootFolder();
            var scene = CreateEnvironmentScene();

            // герой
            GameObject player = CreatePlayer();
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -20f), Quaternion.identity);
            string prefabPath = $"{root}/Prefabs/Zhdan_FPS.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, prefabPath, InteractionMode.AutomatedAction);

            string scenePath = $"{root}/Scenes/MovementTest.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            return scenePath;
        }

        /// <summary>Материалы палитры полигона (после CreateEnvironmentScene). Нужны другим сборщикам (Combat).</summary>
        public static Material BlockMaterial => matBlock;
        public static Material WallMaterial => matWall;
        public static Material InkMaterial => matInk;
        public static Material FireMaterial => matFire;

        /// <summary>Новая сцена со светом, туманом и полигоном, без героя. Используется и модулем Combat.</summary>
        public static UnityEngine.SceneManagement.Scene CreateEnvironmentScene()
        {
            string root = RootFolder();
            foreach (string sub in new[] { "Scenes", "Prefabs", "Materials", "Textures" })
                if (!AssetDatabase.IsValidFolder($"{root}/{sub}")) AssetDatabase.CreateFolder(root, sub);

            Texture2D grid = MakeGridTexture($"{root}/Textures/T_PaperGrid.png");
            matFloor = MakeMaterial($"{root}/Materials/M_PaperGrid.mat", Paper, grid, new Vector2(100, 100));
            matBlock = MakeMaterial($"{root}/Materials/M_Block.mat", LightGray, null, Vector2.one);
            matWall = MakeMaterial($"{root}/Materials/M_Wall.mat", Gray, null, Vector2.one);
            matInk = MakeMaterial($"{root}/Materials/M_Ink.mat", Ink, null, Vector2.one);
            matFire = MakeMaterial($"{root}/Materials/M_Fire.mat", Fire, null, Vector2.one);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupAtmosphere(Paper, 40f, 170f);
            BuildPolygon();
            return scene;
        }

        /// <summary>Солнце, ровный эмбиент, туман. Используется и сценой деревни.</summary>
        public static void SetupAtmosphere(Color fogColor, float fogStart, float fogEnd)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.61f, 0.59f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        // ───────────── полигон ─────────────
        static void BuildPolygon()
        {
            var level = new GameObject("Polygon").transform;
            Block(level, "Floor", new Vector3(0, -0.5f, 0), new Vector3(200, 1, 200), matFloor);

            // старт
            Block(level, "SpawnMark", new Vector3(0, 0.01f, -20), new Vector3(2, 0.02f, 2), matFire, collider: false);
            Label(level, "СТАРТ  ·  R — респаун", new Vector3(0, 3.2f, -16));

            // 1. разгонная прямая с вехами каждые 10 м
            for (int i = 0; i <= 8; i++)
                Block(level, $"Mark_{i * 10}m", new Vector3(-4, 0.01f, -10 + i * 10), new Vector3(1.5f, 0.02f, 0.3f), matInk, collider: false);
            Label(level, "Прямая: вехи каждые 10 м", new Vector3(-4, 2.5f, -8));

            // 2. пролёт для слайд-прыжка: две платформы 1,5 м, пропасть 14 м
            Block(level, "GapA", new Vector3(0, 0.75f, 40), new Vector3(6, 1.5f, 20), matBlock);
            Block(level, "GapPit", new Vector3(0, -0.4f, 57), new Vector3(6, 0.2f, 14), matInk, collider: false);
            Block(level, "GapB", new Vector3(0, 0.75f, 74), new Vector3(6, 1.5f, 20), matBlock);
            Label(level, "Слайд → прыжок: пропасть 14 м", new Vector3(0, 4f, 31));

            // 3. склон для разгона слайда (15°) и площадка наверху
            Ramp(level, "SlideRamp", new Vector3(-30, 0, 0), 15f, 40f, 8f, matBlock);
            float top = 40f * Mathf.Sin(15f * Mathf.Deg2Rad);
            Block(level, "RampTop", new Vector3(-30, top / 2f, 38.6f + 5f), new Vector3(8, top, 10), matBlock);
            Label(level, "Склон 15°: слайд вниз разгоняет", new Vector3(-30, top + 3f, 44f));

            // 4. шахта для прыжков от стен + вышка для слэма
            Block(level, "ShaftWallL", new Vector3(24.5f, 12, 18), new Vector3(1, 24, 10), matWall);
            Block(level, "ShaftWallR", new Vector3(31.5f, 12, 18), new Vector3(1, 24, 10), matWall);
            Block(level, "SlamTower", new Vector3(28, 4, 27), new Vector3(8, 8, 8), matWall);
            Ramp(level, "TowerRamp", new Vector3(28, 0, 31f + 8f / Mathf.Tan(20f * Mathf.Deg2Rad)), 20f,
                 8f / Mathf.Sin(20f * Mathf.Deg2Rad), 6f, matBlock, yaw: 180f);
            Label(level, "Шахта: прыжки от стен (3 до земли)", new Vector3(28, 4f, 12f));
            Label(level, "Вышка 8 м: прыгни, Ctrl в воздухе — слэм, сразу Space — отскок", new Vector3(28, 10.5f, 27f));

            // 5. высокая полка рядом с вышкой — проще всего отскоком от слэма (обычный прыжок ≈ 2,8 м)
            Block(level, "BounceShelf", new Vector3(41, 3, 27), new Vector3(6, 6, 6), matBlock);
            Label(level, "Полка 6 м: слэм рядом → отскок", new Vector3(41, 8f, 27));

            // 6. низкий туннель (потолок 1,3 м) — только слайдом
            Block(level, "TunnelL", new Vector3(10.5f, 1, 26), new Vector3(1, 2, 16), matWall);
            Block(level, "TunnelR", new Vector3(15.5f, 1, 26), new Vector3(1, 2, 16), matWall);
            Block(level, "TunnelRoof", new Vector3(13, 1.65f, 26), new Vector3(6, 0.7f, 16), matWall);
            Label(level, "Туннель 1,3 м: только слайдом", new Vector3(13, 3.5f, 17));

            // 7. лес колонн для дэшей
            var rng = new System.Random(7);
            for (int i = 0; i < 18; i++)
            {
                float x = -20 + (float)rng.NextDouble() * 16f;
                float z = -45 + (float)rng.NextDouble() * 20f;
                Block(level, $"Pillar_{i}", new Vector3(x, 3, z), new Vector3(0.8f, 6, 0.8f), matInk);
            }
            Label(level, "Колонны: дэш (Shift), 3 заряда", new Vector3(-12, 7f, -35));

            // 8. ступеньки и бортики — проверка стыков
            for (int i = 0; i < 6; i++)
                Block(level, $"Step_{i}", new Vector3(-12, 0.15f + i * 0.3f, -6 + i * 0.6f), new Vector3(4, 0.3f + i * 0.6f, 0.6f), matBlock);
        }

        public static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            return go;
        }

        /// <summary>Наклонная плита: нижний край на земле в точке start, поднимается по +Z (с учётом yaw).</summary>
        static void Ramp(Transform parent, string name, Vector3 start, float angle, float length, float width, Material mat, float yaw = 0f)
        {
            var rot = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-angle, 0, 0);
            Vector3 center = start + rot * new Vector3(0, -0.5f, length / 2f);
            var go = Block(parent, name, center, new Vector3(width, 1, length), mat);
            go.transform.localRotation = rot;
        }

        public static void Label(Transform parent, string text, Vector3 pos)
        {
            var go = new GameObject("Label_" + text.Split(':')[0]);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.12f;
            tm.fontSize = 48;
            tm.color = Ink;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            go.AddComponent<FaceCamera>();
        }

        // ───────────── герой ─────────────
        public static GameObject CreatePlayer()
        {
            var go = new GameObject("Zhdan_FPS");
            go.tag = "Player";
            var cc = go.AddComponent<CharacterController>();
            MovementSelfTest.ConfigureController(cc);
            var input = go.AddComponent<ZhdanInput>();
            var move = go.AddComponent<ZhdanMovement>();
            move.input = input;
            var look = go.AddComponent<FirstPersonLook>();
            look.input = input;

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0, move.standingHeight - 0.18f, 0);
            look.cameraPivot = pivot;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 95f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Paper;
            camGo.AddComponent<AudioListener>();
            var fx = camGo.AddComponent<ZhdanCameraFx>();
            fx.movement = move;
            fx.eyePivot = pivot;

            var hud = go.AddComponent<MovementHud>();
            hud.movement = move;
            var respawn = go.AddComponent<TestRespawn>();
            respawn.movement = move;
            respawn.look = look;

            // вид от третьего лица, если в проекте есть модель Ждана
            ThirdPersonSetup.TryAdd(go);
            return go;
        }

        // ───────────── ассеты ─────────────
        static Texture2D MakeGridTexture(string path)
        {
            if (!File.Exists(path))
            {
                const int N = 256;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
                var px = new Color32[N * N];
                Color32 paper = Paper, ink = Ink, gray = LightGray;
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color32 c = paper;
                    if (x % 128 < 2 || y % 128 < 2) c = gray;      // полуклетка
                    if (x < 3 || y < 3) c = ink;                    // клетка 2 м
                    px[y * N + x] = c;
                }
                tex.SetPixels32(px);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.anisoLevel = 8;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material MakeMaterial(string path, Color color, Texture tex, Vector2 tiling)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            if (tex != null)
            {
                mat.mainTexture = tex;
                mat.mainTextureScale = tiling;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
