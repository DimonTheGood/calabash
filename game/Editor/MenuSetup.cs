using Calabash.Animation.EditorTools;
using Calabash.Combat.EditorTools;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Calabash.Game.EditorTools
{
    /// <summary>
    /// Сцена MainMenu — главное меню отдельно от игры. Та же ночная деревня: у колодца догорает костёр,
    /// на котором Ждана хотели сжечь, рядом стоит сам Ждан и горит. Камера медленно ходит вокруг него,
    /// слева — название и кнопки (MainMenu). Первая сцена в билде.
    /// </summary>
    public static class MenuSetup
    {
        public const string SceneName = "MainMenu";

        public static string BuildMenu()
        {
            string mapPath = ThirdPersonSetup.FindModelPath(VillageSceneBuilder.MapModelName);
            if (mapPath == null || !CharacterAnimSetup.Available) return null;
            string root = GameSetup.RootFolder();
            ThirdPersonSetup.EnsureFolder($"{root}/Scenes");
            ThirdPersonSetup.EnsureFolder($"{root}/Materials");

            MovementTestSceneBuilder.CreateEnvironmentScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MovementTestSceneBuilder.SetupAtmosphere(VillageSceneBuilder.NightFog, 4f, 38f);
            VillageSceneBuilder.SetupNight();
            VillageSceneBuilder.PlaceMap(mapPath, out Vector3 spawn, out Vector3 along, out _, out _, out _, out _);

            // то же место, что в катсцене казни: столб в 9 м от колодца
            Vector3 P = CutsceneSetup.Free(CutsceneSetup.Ground(spawn + along * 9f), 4f);
            Vector3 F = -along;
            Vector3 R = Vector3.Cross(Vector3.up, F).normalized;

            Material burnt = CutsceneSetup.Mat($"{root}/Materials/M_BurntWood.mat", new Color32(0x14, 0x11, 0x0F, 0xFF));
            Material dark = CutsceneSetup.Mat($"{root}/Materials/M_TorchWood.mat", new Color32(0x1A, 0x16, 0x12, 0xFF));
            Material flame = CutsceneSetup.FindMat("M_FX_Flame"), small = CutsceneSetup.FindMat("M_FX_SmallFlame");
            Material sparks = CutsceneSetup.FindMat("M_FX_Sparks"), smoke = CutsceneSetup.FindMat("M_FX_Smoke");

            var stage = new GameObject("Menu").transform;
            CutsceneSetup.BuildPyre(stage, P, burnt);

            // костёр догорает: низкий огонь, угли, густой дым
            var fire = CutsceneSetup.Flames(stage, P, flame);
            var fm = fire.main;
            fm.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            fm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            fm.prewarm = true;
            var fe = fire.emission; fe.rateOverTime = 45f;
            var fs = fire.shape; fs.radius = 1.1f;
            var emb = CutsceneSetup.Embers(stage, P, sparks);
            var em = emb.main; em.prewarm = true;
            var ee = emb.emission; ee.rateOverTime = 22f;
            var smk = CutsceneSetup.Smoke(stage, P, smoke);
            var sm = smk.main; sm.prewarm = true;
            var se = smk.emission; se.rateOverTime = 9f;
            var pyreLight = new GameObject("PyreLight").AddComponent<Light>();
            pyreLight.transform.SetParent(stage, false);
            pyreLight.transform.position = P + Vector3.up * 1.2f;
            pyreLight.type = LightType.Point;
            pyreLight.color = new Color(1f, 0.55f, 0.28f);
            pyreLight.range = 11f;
            pyreLight.intensity = 9f;
            pyreLight.shadows = LightShadows.None;
            pyreLight.gameObject.AddComponent<TorchFlicker>().intensity = 9f;

            // Ждан сошёл с костра и стоит рядом, горит
            Vector3 zp = CutsceneSetup.Free(CutsceneSetup.Ground(P + F * 2.4f + R * 0.6f), 2f);
            Animator zhdan = CutsceneSetup.Hero(stage, zp, F);
            if (zhdan != null)
            {
                zhdan.transform.parent.name = "Zhdan";
                var (bodyFire, lamp) = CutsceneSetup.BodyFire(zhdan.transform.parent, flame);
                var bm = bodyFire.main;                      // тлеет, а не полыхает: фигура должна читаться
                bm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
                bm.prewarm = true;
                var be = bodyFire.emission; be.rateOverTime = 55f;
                lamp.gameObject.AddComponent<TorchFlicker>().intensity = lamp.intensity;
            }
            CutsceneSetup.Brazier(stage, CutsceneSetup.Free(CutsceneSetup.Ground(P + F * 6f - R * 3.5f), 2f), dark, small);

            // камера
            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 160f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = VillageSceneBuilder.NightFog;
            camGo.AddComponent<AudioListener>();
            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.transform.SetParent(camGo.transform, false);
            fill.transform.localPosition = new Vector3(0.4f, 0.6f, 0f);
            fill.type = LightType.Point;
            fill.color = new Color(0.75f, 0.8f, 1f);
            fill.range = 12f;
            fill.intensity = 4f;
            fill.shadows = LightShadows.None;
            VillageSceneBuilder.NightGloom(Calabash.Style.EditorTools.MangaGloomSetup.AddTo(cam));
            VillageSceneBuilder.AddSnow(camGo.transform, CombatTestSceneBuilder.RootFolder());

            Calabash.Audio.EditorTools.CalabashAudioSetup.Setup(null, "menu");

            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.cam = cam;
            menu.focus = zhdan != null ? zhdan.transform.parent : stage;
            menu.pyre = P;
            menu.front = F;
            menu.Place(0f);

            string path = $"{root}/Scenes/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            GameSetup.AddToBuild(path, 0);
            Debug.Log($"[Меню] сцена {path}, Ждан {(zhdan != null)}");
            return path;
        }
    }
}
