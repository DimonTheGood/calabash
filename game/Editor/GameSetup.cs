using System.IO;
using Calabash.Animation;
using Calabash.Animation.EditorTools;
using Calabash.Combat.EditorTools;
using Calabash.Movement;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Calabash.Game.EditorTools
{
    /// <summary>
    /// Игра целиком: в деревню — GameFlow (меню, комикс, пауза, улучшения, смерть → лимб),
    /// сцена Afterlife — лимб, рай и ад. Обе сцены попадают в Build Settings (деревня первой).
    /// Вызывается сборщиком деревни; отдельно — меню Calabash → Game → Собрать сцену лимба.
    /// </summary>
    public static class GameSetup
    {
        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("GameSetup t:Script"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == "GameSetup")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Game";
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{RootFolder()}/Textures/T_Story_{name}.png");

        /// <summary>Комикс-вступление: за что Ждана хотели сжечь.</summary>
        static readonly (string img, string text)[] Intro =
        {
            ("gavrila", "Зима. Голодный год.\nУ пекаря Гаврилы пропал каравай."),
            ("zhdan0", "Каравай украл Ждан.\nЕго поймали у реки с хлебом за пазухой."),
            ("nikodim", "Поп Никодим сказал: вор и бесово отродье.\nНа площади все кивали."),
            ("king", "Царь даже не поднял глаз.\n«Сжечь»."),
            ("korney", "Палач Корней сложил хворост\nи поднёс факел."),
            ("zhdan4", "Ждан сгорел. Но огонь не ушёл из него.\n\nТеперь горит деревня."),
        };

        /// <summary>Добавить в собранную сцену деревни ход игры.</summary>
        public static GameFlow SetupVillage()
        {
            FixTextures();
            // отладочный HUD движения (скорость, клавиши) в игре скрыт; F1 показывает его снова
            foreach (var mh in Object.FindObjectsByType<MovementHud>(FindObjectsInactive.Include)) mh.visible = false;
            // темп и сложность игры: Ждан тяжелее, огонь прогорает быстрее
            foreach (var mv in Object.FindObjectsByType<ZhdanMovement>(FindObjectsInactive.Include)) GameBalance.Apply(mv.gameObject);
            var flow = new GameObject("Game").AddComponent<GameFlow>();
            foreach (var (img, text) in Intro)
                flow.intro.Add(new GameFlow.Slide { image = Tex(img), text = text });
            return flow;
        }

        static void FixTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("T_Story_ t:Texture2D", new[] { $"{RootFolder()}/Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null || (imp.textureType == TextureImporterType.GUI && !imp.mipmapEnabled)) continue;
                imp.textureType = TextureImporterType.GUI;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
        }

        [MenuItem("Calabash/Game/Собрать сцену лимба")]
        public static void BuildAfterlifeFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            // контроллеры анимаций пересоздаются при сборке, поэтому деревня и лимб собираются вместе
            if (VillageSceneBuilder.Build() == null) return;
            EditorSceneManager.OpenScene($"{RootFolder()}/Scenes/Afterlife.unity");
        }

        /// <summary>
        /// Сцена Afterlife: белая пустота, Ждан, Лимб в 14 м, огонь ада (выключен до концовки).
        /// Вызывать сразу после сборки деревни: контроллеры анимаций берутся из той же сборки.
        /// </summary>
        public static string BuildAfterlife()
        {
            FixTextures();
            string root = RootFolder();
            ThirdPersonSetup.EnsureFolder($"{root}/Scenes");
            ThirdPersonSetup.EnsureFolder($"{root}/Materials");

            MovementTestSceneBuilder.CreateEnvironmentScene();            // материалы полигона (тушь, огонь)
            Material ink = MovementTestSceneBuilder.InkMaterial;
            Material fire = MovementTestSceneBuilder.FireMaterial;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Color white = new Color32(0xF7, 0xF6, 0xF2, 0xFF);
            MovementTestSceneBuilder.SetupAtmosphere(white, 20f, 90f);
            RenderSettings.ambientLight = new Color(0.85f, 0.85f, 0.83f);

            // земля: белая плоскость до горизонта
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>($"{root}/Materials/M_LimboGround.mat");
            if (groundMat == null)
            {
                groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                AssetDatabase.CreateAsset(groundMat, $"{root}/Materials/M_LimboGround.mat");
            }
            groundMat.SetColor("_BaseColor", white);
            groundMat.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(groundMat);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            // герой
            GameObject player = MovementTestSceneBuilder.CreatePlayer();
            player.name = "Zhdan_Limbo";
            CombatTestSceneBuilder.AddCombat(player, ink, fire);
            CharacterAnimSetup.SetupHero(player);
            Calabash.Vfx.EditorTools.ZhdanFireSetup.Setup(player);
            Calabash.Audio.EditorTools.CalabashAudioSetup.Setup(player, "limbo");
            GameBalance.Apply(player);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var hud = player.GetComponent<Calabash.Combat.CombatHud>();
            if (hud != null) hud.visible = false;
            var mhud = player.GetComponent<MovementHud>();
            if (mhud != null) mhud.enabled = false;
            var cam = player.GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = white;
                cam.farClipPlane = 300f;
                Calabash.Style.EditorTools.MangaGloomSetup.AddTo(cam);
            }

            // Лимб
            GameObject limbo = CharacterAnimSetup.Available
                ? CharacterAnimSetup.CreateNpc("limbo", null, new Vector3(0f, 0f, 14f), Vector3.back, VillagerNpc.Role.Stander, null, 9999f)
                : null;
            if (limbo != null)
            {
                Object.DestroyImmediate(limbo.GetComponent<VillagerNpc>());   // не ходит и не пугается — стоит и ждёт
                limbo.name = "Limbo";
            }

            // огонь ада: поле языков пламени вокруг, выключено до концовки
            Material flame = null;
            foreach (string guid in AssetDatabase.FindAssets("M_FX_Flame t:Material"))
                flame = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            var hellFire = HellFire(flame);
            hellFire.gameObject.SetActive(false);

            var after = new GameObject("Afterlife").AddComponent<Afterlife>();
            after.limbo = limbo != null ? limbo.transform : null;
            after.ground = ground.GetComponent<Renderer>();
            after.cam = cam;
            after.hellFire = hellFire;
            after.sun = Object.FindAnyObjectByType<Light>();
            after.limboImage = Tex("limbo");
            after.heavenImage = Tex("zhdan0");
            after.hellImage = Tex("zhdan4");

            string path = $"{root}/Scenes/Afterlife.unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            AddToBuild(path, 1);
            return path;
        }

        static ParticleSystem HellFire(Material flame)
        {
            var go = new GameObject("HellFire");
            go.transform.position = new Vector3(0f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0xFF, 0xD1, 0x66, 0xFF), new Color32(0xC2, 0x4E, 0x00, 0xFF));
            main.gravityModifier = -0.3f;
            main.maxParticles = 1500;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 500f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Donut;
            sh.radius = 18f; sh.donutRadius = 10f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true; tsa.numTilesX = 16; tsa.numTilesY = 4;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = flame;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>Поставить сцену в Build Settings на место index.</summary>
        public static void AddToBuild(string scenePath, int index)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(x => x.path == scenePath);
            list.Insert(Mathf.Clamp(index, 0, list.Count), new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
