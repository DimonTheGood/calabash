using System.Collections.Generic;
using System.IO;
using System.Linq;
using Calabash.Combat;
using Calabash.Movement;
using Calabash.Movement.EditorTools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace Calabash.Animation.EditorTools
{
    /// <summary>
    /// Подключает анимированных персонажей из animation/fbx:
    /// импорт клипов (Generic, циклы), Animator Controller'ы, герой с ригом и руками в кадре, NPC.
    /// </summary>
    public static class CharacterAnimSetup
    {
        public const string HeroModel = "SK_Char_hero";
        public const string ArmsModel = "SM_Zhdan_FPArms";
        const string ImportTag = "calabash-anim-1";
        /// <summary>Скинованные модели после импорта смотрят в −Z — разворачиваем к +Z (вперёд героя и NPC).</summary>
        public const float ModelYaw = 180f;

        /// <summary>Клипы, которые зациклены. Остальные (удары, Hit, Death, GetUp, Jump) — разовые.</summary>
        public static readonly string[] Looping = { "Idle", "Walk", "Run", "Fall", "Slide", "Dash", "Slam", "Talk", "Pray", "Cower" };
        public static readonly string[] HeroAttacks = { "Jab_L", "Jab_R", "Hook_R", "Heavy", "DashStrike", "Uppercut" };

        /// <summary>Материалы персонажей (имена из build_characters.py) → плоские цвета палитры style bible.</summary>
        static readonly (string name, Color32 color)[] Palette =
        {
            ("M_Ink", new Color32(0x0B, 0x0B, 0x0B, 0xFF)), ("M_Black", new Color32(0x1A, 0x1A, 0x1A, 0xFF)),
            ("M_Char", new Color32(0x24, 0x24, 0x24, 0xFF)), ("M_Dark", new Color32(0x2B, 0x2B, 0x2B, 0xFF)),
            ("M_Soot", new Color32(0x1E, 0x1E, 0x1E, 0xFF)), ("M_Skin", new Color32(0xB8, 0xB5, 0xAE, 0xFF)),
            ("M_Cloth", new Color32(0x9A, 0x98, 0x92, 0xFF)), ("M_Wood", new Color32(0x5A, 0x5A, 0x5A, 0xFF)),
            ("M_Steel", new Color32(0x9C, 0x9C, 0x9C, 0xFF)), ("M_Iron", new Color32(0x1C, 0x1C, 0x1C, 0xFF)),
            ("M_Linen", new Color32(0xB4, 0xB2, 0xAC, 0xFF)), ("M_Bread", new Color32(0xCF, 0xC8, 0xB8, 0xFF)),
            ("M_Ash", new Color32(0xC4, 0xC2, 0xBC, 0xFF)), ("M_Bone", new Color32(0xD2, 0xD0, 0xC9, 0xFF)),
            ("M_Pale", new Color32(0xE8, 0xE6, 0xE0, 0xFF)), ("M_Crown", new Color32(0x0D, 0x0D, 0x0D, 0xFF)),
            ("M_Void", new Color32(0x05, 0x05, 0x05, 0xFF)), ("M_Stone", new Color32(0x6E, 0x6E, 0x6E, 0xFF)),
            ("M_Plank", new Color32(0x50, 0x50, 0x50, 0xFF)), ("M_Timber", new Color32(0x1E, 0x1E, 0x1E, 0xFF)),
            ("M_Snow", new Color32(0xF7, 0xF6, 0xF2, 0xFF)),
            ("M_Water", new Color32(0x7F, 0xB2, 0xC4, 0xFF)), ("M_WaterDeep", new Color32(0x3F, 0x6E, 0x7E, 0xFF)),
            ("M_Blood", new Color32(0xA3, 0x12, 0x1C, 0xFF)), ("M_BloodDark", new Color32(0x5E, 0x0A, 0x10, 0xFF)),
            ("M_Glow", new Color32(0xFF, 0xFF, 0xFF, 0xFF)), ("M_Ember", new Color32(0xFF, 0x7A, 0x18, 0xFF)),
            ("M_EmberHot", new Color32(0xFF, 0xD1, 0x66, 0xFF)), ("M_EmberDeep", new Color32(0xC2, 0x4E, 0x00, 0xFF)),
        };

        static readonly Dictionary<string, AnimatorController> controllers = new Dictionary<string, AnimatorController>();

        /// <summary>Есть ли в проекте модели из animation/fbx.</summary>
        public static bool Available => ThirdPersonSetup.FindModelPath(HeroModel) != null;

        /// <summary>Папка модуля (Assets/Calabash/Animation).</summary>
        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("CharacterAnimSetup t:Script"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == "CharacterAnimSetup")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Animation";
        }

        /// <summary>Начало сборки сцены: контроллеры пересоздаются один раз за сборку.</summary>
        public static void BeginBuild() => controllers.Clear();

        // ------------------------------------------------------------------ импорт
        /// <summary>Настроить импорт модели: Generic-риг, клипы по именам действий Blender, циклы, палитра.</summary>
        public static string PrepareModel(string name, bool animated)
        {
            string path = ThirdPersonSetup.FindModelPath(name);
            if (path == null) return null;
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool clipsOk = !animated || (imp.clipAnimations.Length > 0 && imp.clipAnimations.Length == imp.defaultClipAnimations.Length);
            if (imp.userData == ImportTag && clipsOk) return path;

            if (animated)
            {
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = true;
                imp.bakeAxisConversion = true;
                imp.SaveAndReimport();
                var clips = imp.defaultClipAnimations;
                foreach (var c in clips)
                {
                    string take = c.takeName;
                    int i = take.LastIndexOf('|');
                    c.name = i >= 0 ? take.Substring(i + 1) : take;
                    c.loopTime = Looping.Contains(c.name);
                    c.loopPose = false;
                    c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = false;
                }
                imp.clipAnimations = clips;
            }
            imp.userData = ImportTag;
            ThirdPersonSetup.RemapMaterials(path, Palette, $"{RootFolder()}/Materials/Character");   // сохраняет и переимпортирует
            return path;
        }

        public static Dictionary<string, AnimationClip> LoadClips(string path)
        {
            var map = new Dictionary<string, AnimationClip>();
            foreach (var c in AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview__")) map[c.name] = c;
            return map;
        }

        static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string name)
        {
            if (clips.TryGetValue(name, out var c)) return c;
            Debug.LogWarning($"[Анимация] Нет клипа {name}");
            return null;
        }

        // ------------------------------------------------------------------ контроллеры
        /// <summary>Контроллер героя: нижний слой — движение, верхний (маска от поясницы) — удары, их перематывает ZhdanAnimator.</summary>
        public static AnimatorController HeroController(string modelPath)
        {
            if (controllers.TryGetValue(HeroModel, out var cached)) return cached;
            string folder = $"{RootFolder()}/Controllers";
            ThirdPersonSetup.EnsureFolder(folder);
            string cp = $"{folder}/AC_Hero.controller";
            AssetDatabase.DeleteAsset(cp);
            var clips = LoadClips(modelPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cp);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("VSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "LocoRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var sm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController(ZhdanAnimator.Locomotion, out BlendTree lt, 0);
            lt.blendParameter = "Speed"; lt.useAutomaticThresholds = false;
            lt.AddChild(Clip(clips, "Idle"), 0f);
            lt.AddChild(Clip(clips, "Walk"), 2.5f);
            lt.AddChild(Clip(clips, "Run"), 9f);
            loco.speedParameter = "LocoRate"; loco.speedParameterActive = true;
            var air = ctrl.CreateBlendTreeInController(ZhdanAnimator.Air, out BlendTree at, 0);
            at.blendParameter = "VSpeed"; at.useAutomaticThresholds = false;
            at.AddChild(Clip(clips, "Fall"), -6f);
            at.AddChild(Clip(clips, "Jump"), 4f);
            foreach (string n in new[] { ZhdanAnimator.Slide, ZhdanAnimator.Dash, ZhdanAnimator.Slam })
                sm.AddState(n).motion = Clip(clips, n);
            sm.defaultState = loco;

            ctrl.AddLayer("Upper");
            var layers = ctrl.layers;
            layers[1].avatarMask = UpperBodyMask(modelPath, folder);
            layers[1].defaultWeight = 0f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var usm = ctrl.layers[1].stateMachine;
            foreach (string n in HeroAttacks)
            {
                var st = usm.AddState(n);
                st.motion = Clip(clips, n);
                st.speed = 0f;                                   // время задаёт ZhdanAnimator
            }
            EditorUtility.SetDirty(ctrl);
            controllers[HeroModel] = ctrl;
            return ctrl;
        }

        /// <summary>Маска верхнего слоя: Spine1 и всё выше (руки, голова). Ноги и таз остаются у нижнего слоя.</summary>
        static AvatarMask UpperBodyMask(string modelPath, string folder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var mask = new AvatarMask { name = "AM_UpperBody" };
            var paths = model.GetComponentsInChildren<Transform>(true)
                             .Where(t => t != model.transform)
                             .Select(t => AnimationUtility.CalculateTransformPath(t, model.transform)).ToList();
            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, paths[i].Contains("/Spine1"));
            }
            string mp = $"{folder}/AM_UpperBody.mask";
            AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(mask, mp);
            return mask;
        }

        /// <summary>Контроллер NPC: по состоянию на каждый клип, переключает VillagerNpc.</summary>
        public static AnimatorController NpcController(string cid, string modelPath)
        {
            if (controllers.TryGetValue(cid, out var cached)) return cached;
            string folder = $"{RootFolder()}/Controllers";
            ThirdPersonSetup.EnsureFolder(folder);
            string cp = $"{folder}/AC_{cid}.controller";
            AssetDatabase.DeleteAsset(cp);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cp);
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState idle = null;
            foreach (var kv in LoadClips(modelPath).OrderBy(k => k.Key))
            {
                var st = sm.AddState(kv.Key);
                st.motion = kv.Value;
                if (kv.Key == "Idle") idle = st;
            }
            if (idle != null) sm.defaultState = idle;
            EditorUtility.SetDirty(ctrl);
            controllers[cid] = ctrl;
            return ctrl;
        }

        // ------------------------------------------------------------------ герой
        /// <summary>
        /// Заменить статичную модель героя на SK_Char_hero с Animator, контуром и ZhdanAnimator,
        /// а кубы-кулаки — на руки Ждана с IK (ZhdanArmsView). Без моделей ничего не меняет.
        /// </summary>
        public static bool SetupHero(GameObject player)
        {
            string path = PrepareModel(HeroModel, true);
            if (path == null)
            {
                Debug.LogWarning($"[Анимация] {HeroModel}.fbx не найден — герой остаётся статичным. Скопируйте animation/ в Assets/Calabash/Animation.");
                return false;
            }
            var ctrl = HeroController(path);
            var move = player.GetComponent<ZhdanMovement>();
            var mode = GetOrAdd<ZhdanCameraMode>(player);

            Transform body = player.transform.Find("Body");
            if (body == null)
            {
                body = new GameObject("Body").transform;
                body.SetParent(player.transform, false);
            }
            Transform pose = body.Find("Pose");
            if (pose == null)
            {
                pose = new GameObject("Pose").transform;
                pose.SetParent(body, false);
            }
            for (int i = pose.childCount - 1; i >= 0; i--) Object.DestroyImmediate(pose.GetChild(i).gameObject);
            var view = GetOrAdd<ZhdanBodyView>(body.gameObject);
            view.movement = move; view.pose = pose; view.animated = true;
            view.modelYawOffset = ModelYaw;
            pose.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            if (body.Find("FistAnchor") == null)
            {
                var anchor = new GameObject("FistAnchor").transform;
                anchor.SetParent(body, false);
                anchor.localPosition = new Vector3(0f, 1.4f, 0.05f);
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            model.name = "Model";
            model.transform.SetParent(pose, false);
            var animator = GetOrAdd<Animator>(model);
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;     // в первом лице тело только отбрасывает тень

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.updateWhenOffscreen = true;
            Debug.Log($"[Анимация] Ждан: рост {smr.bounds.size.y:0.00} м, {ctrl.animationClips.Length} клипов");

            // оранжевый контур: второй SkinnedMeshRenderer на тех же костях
            var outline = new GameObject("Outline");
            outline.transform.SetParent(smr.transform.parent, false);
            outline.transform.localPosition = smr.transform.localPosition;
            outline.transform.localRotation = smr.transform.localRotation;
            outline.transform.localScale = smr.transform.localScale;
            var osmr = outline.AddComponent<SkinnedMeshRenderer>();
            osmr.sharedMesh = smr.sharedMesh;
            osmr.rootBone = smr.rootBone;
            osmr.bones = smr.bones;
            osmr.updateWhenOffscreen = true;
            var outlineMat = OutlineMaterial();
            osmr.sharedMaterials = Enumerable.Repeat(outlineMat, smr.sharedMaterials.Length).ToArray();
            osmr.shadowCastingMode = ShadowCastingMode.Off;
            osmr.receiveShadows = false;

            var za = GetOrAdd<ZhdanAnimator>(player);
            za.movement = move;
            za.combat = player.GetComponent<ZhdanCombat>();
            za.animator = animator;

            mode.input = player.GetComponent<ZhdanInput>();
            mode.bodyRenderers = new Renderer[] { smr };
            mode.thirdPersonOnly = new Renderer[] { osmr };
            smr.shadowCastingMode = mode.thirdPerson ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            osmr.enabled = mode.thirdPerson;

            SetupArms(player, mode);
            return true;
        }

        static Material OutlineMaterial()
        {
            string p = $"{MovementTestSceneBuilder.RootFolder()}/Materials/Character/M_HeroOutline.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (mat != null) return mat;
            ThirdPersonSetup.EnsureFolder(Path.GetDirectoryName(p).Replace('\\', '/'));
            mat = new Material(Shader.Find("Calabash/InkOutline"));
            mat.SetColor("_Color", new Color32(0xFF, 0x7A, 0x18, 0xFF));
            mat.SetFloat("_Width", 0.012f);
            AssetDatabase.CreateAsset(mat, p);
            return mat;
        }

        /// <summary>Руки в кадре: куски SM_Zhdan_FPArms под камерой + ZhdanArmsView, кубы-кулаки убираются.</summary>
        static void SetupArms(GameObject player, ZhdanCameraMode mode)
        {
            var fists = player.GetComponentInChildren<ZhdanFistsView>(true);
            string path = PrepareModel(ArmsModel, false);
            if (fists == null || path == null) return;
            var cam = player.GetComponentInChildren<Camera>();
            if (cam == null) return;

            var root = new GameObject("FPArms").transform;
            root.SetParent(cam.transform, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Pieces";
            inst.transform.SetParent(root, false);

            var arms = root.gameObject.AddComponent<ZhdanArmsView>();
            arms.leftTarget = fists.leftFist;
            arms.rightTarget = fists.rightFist;
            arms.upperLeft = FindDeep(inst.transform, "FP_UpperArm.L");
            arms.forearmLeft = FindDeep(inst.transform, "FP_Forearm.L");
            arms.fistLeft = FindDeep(inst.transform, "FP_Fist.L");
            arms.upperRight = FindDeep(inst.transform, "FP_UpperArm.R");
            arms.forearmRight = FindDeep(inst.transform, "FP_Forearm.R");
            arms.fistRight = FindDeep(inst.transform, "FP_Fist.R");

            foreach (var t in new[] { fists.leftFist, fists.rightFist })        // кубы больше не нужны, остаются цели
                if (t != null)
                    for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);

            var rends = inst.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.enabled = !mode.thirdPerson;
            }
            mode.viewModel = null;                                // в третьем лице руки — у тела
            mode.firstPersonOnly = rends;
        }

        // ------------------------------------------------------------------ NPC
        /// <summary>Житель с анимациями и поведением VillagerNpc. null — если модели нет в проекте.</summary>
        public static GameObject CreateNpc(string cid, Transform parent, Vector3 pos, Vector3 facing,
                                           VillagerNpc.Role role, Vector3[] waypoints, float hp)
        {
            string path = PrepareModel($"SK_Char_{cid}", true);
            if (path == null) return null;
            var ctrl = NpcController(cid, path);

            var go = new GameObject($"NPC_{cid}");
            if (parent != null) go.transform.SetParent(parent, false);
            facing.y = 0f;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing.normalized : Vector3.forward));

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var animator = GetOrAdd<Animator>(model);
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            Bounds b = smr != null ? smr.bounds : new Bounds(pos + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));

            var col = GetOrAdd<CapsuleCollider>(go);
            col.height = Mathf.Clamp(b.size.y, 1.2f, 2.4f);
            col.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.45f, 0.28f, 0.5f);
            col.center = new Vector3(0f, col.height * 0.5f, 0f);

            var npc = go.AddComponent<VillagerNpc>();
            npc.role = role;
            npc.animator = animator;
            npc.waypoints = waypoints ?? new Vector3[0];
            npc.maxHealth = hp;
            bool guard = role == VillagerNpc.Role.Guard || role == VillagerNpc.Role.Patrol;
            npc.mass = guard ? 1.6f : 1f;
            return go;
        }

        /// <summary>В редакторе GetComponent возвращает «фальшивый null», и ?? его не ловит.</summary>
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
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
