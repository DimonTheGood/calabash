using System.Collections.Generic;
using System.IO;
using System.Linq;
using Calabash.Combat;
using Calabash.Movement;
using UnityEditor;
using UnityEngine;

namespace Calabash.Vfx.EditorTools
{
    /// <summary>
    /// Подключение огня к Ждану: настраивает импорт текстур vfx/Textures, создаёт материалы частиц (URP Particles/Unlit,
    /// прозрачные, двусторонние) и вешает ZhdanFireVfx на героя с кулаками и костями модели.
    /// Вызывается сборщиками сцен (арена, деревня) после настройки анимаций.
    /// </summary>
    public static class ZhdanFireSetup
    {
        /// <summary>Папка модуля (Assets/Calabash/Vfx).</summary>
        public static string RootFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("ZhdanFireSetup t:Script"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == "ZhdanFireSetup")
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets/Calabash/Vfx";
        }

        public static bool Available => AssetDatabase.LoadAssetAtPath<Texture2D>($"{RootFolder()}/Textures/T_Flame03_16x4.png") != null;

        /// <summary>Повесить огонь на героя. Возвращает компонент или null, если текстур нет.</summary>
        public static ZhdanFireVfx Setup(GameObject player)
        {
            if (!Available)
            {
                Debug.LogWarning("[Огонь] vfx/Textures не найдены — Ждан без эффектов огня. Скопируйте vfx/ в Assets/Calabash/Vfx.");
                return null;
            }
            var fx = player.GetComponent<ZhdanFireVfx>();
            if (fx == null) fx = player.AddComponent<ZhdanFireVfx>();
            fx.movement = player.GetComponent<ZhdanMovement>();
            fx.combat = player.GetComponent<ZhdanCombat>();
            fx.fuel = player.GetComponent<ZhdanFuel>();
            fx.cameraMode = player.GetComponent<ZhdanCameraMode>();
            // огонь светит: ночью Ждан — главный источник света
            var lamp = player.GetComponent<ZhdanFireLight>();
            if (lamp == null) lamp = player.AddComponent<ZhdanFireLight>();
            lamp.fuel = fx.fuel;
            lamp.fx = fx;

            fx.flameFlipbook = Mat("M_FX_Flame", "T_Flame03_16x4");
            fx.smallFlameFlipbook = Mat("M_FX_SmallFlame", "T_SmallFlame01_16x4");
            fx.tongues = Mat("M_FX_Tongues", "T_Flames_3x2");
            fx.sparks = Mat("M_FX_Sparks", "T_Sparks_4x2");
            fx.smoke = Mat("M_FX_Smoke", "T_WispySmoke01_8x8");
            fx.burst = Mat("M_FX_Burst", "T_Burst_3x2");
            fx.trace = Mat("M_FX_Trail", "T_Trail", clamp: false);
            fx.rings = Mat("M_FX_Rings", "T_Rings_2x2", soft: false);
            fx.scorch = Mat("M_FX_Scorch", "T_Scorch_2x2", soft: false);

            // кулаки от первого лица: куски рук из animation/ или кубы ZhdanFistsView
            var arms = player.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m.GetType().Name == "ZhdanArmsView");
            if (arms != null)
            {
                var so = new SerializedObject(arms);
                fx.fpFistLeft = so.FindProperty("fistLeft").objectReferenceValue as Transform;
                fx.fpFistRight = so.FindProperty("fistRight").objectReferenceValue as Transform;
                fx.fpFistCenter = 0.09f;
            }
            if (fx.fpFistLeft == null || fx.fpFistRight == null)
            {
                var fists = player.GetComponentInChildren<ZhdanFistsView>(true);
                if (fists != null) { fx.fpFistLeft = fists.leftFist; fx.fpFistRight = fists.rightFist; }
                fx.fpFistCenter = 0f;
            }

            // кости модели для третьего лица
            var animator = player.GetComponentInChildren<Animator>(true);
            Transform rig = animator != null ? animator.transform : null;
            fx.handLeft = Bone(rig, "Hand.L");
            fx.handRight = Bone(rig, "Hand.R");
            fx.head = Bone(rig, "Head");
            var pts = new List<Transform>();
            var weights = new List<float>();
            void Add(string bone, float w)
            {
                Transform t = Bone(rig, bone);
                if (t == null) return;
                pts.Add(t);
                weights.Add(w);
            }
            Add("Chest", 3f);              // грудная клетка — топка
            Add("Spine1", 1.5f);
            Add("Shoulder.L", 1.5f);
            Add("Shoulder.R", 1.5f);
            Add("UpperArm.L", 1f);
            Add("UpperArm.R", 1f);
            Add("Forearm.L", 1.2f);
            Add("Forearm.R", 1.2f);
            Add("Neck", 1f);
            Add("Head", 0.8f);
            if (pts.Count == 0)            // статичная модель без рига: огонь от корпуса
            {
                Transform body = player.transform.Find("Body");
                if (body == null) body = player.transform;
                pts.Add(body);
                weights.Add(1f);
            }
            fx.bodyPoints = pts.ToArray();
            fx.bodyWeights = weights.ToArray();
            EditorUtility.SetDirty(fx);
            return fx;
        }

        static Transform Bone(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Material Mat(string name, string texture, bool clamp = true, bool soft = true)
        {
            string root = RootFolder();
            string texPath = $"{root}/Textures/{texture}.png";
            var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (imp != null && (imp.wrapMode != (clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat) || !imp.alphaIsTransparency || imp.sRGBTexture != true))
            {
                imp.textureType = TextureImporterType.Default;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = true;
                imp.sRGBTexture = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string dir = $"{root}/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(root, "Materials");
            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            // прозрачный, обычное смешивание: на белом снегу аддитивный огонь не виден
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_ColorMode", 0f);
            // мягкие частицы: пламя гаснет, входя в тело и землю, без прямых срезов квадрата
            mat.SetFloat("_SoftParticlesEnabled", soft ? 1f : 0f);
            mat.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            mat.SetFloat("_SoftParticlesFarFadeDistance", 0.35f);
            mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 0.35f, 0f, 0f));
            if (soft) mat.EnableKeyword("_SOFTPARTICLES_ON"); else mat.DisableKeyword("_SOFTPARTICLES_ON");
            // у самой камеры частицы тают, а не перекрывают кадр квадратом
            mat.SetFloat("_CameraFadingEnabled", 1f);
            mat.SetFloat("_CameraNearFadeDistance", 0.05f);
            mat.SetFloat("_CameraFarFadeDistance", 0.2f);
            mat.SetVector("_CameraFadeParams", new Vector4(0.05f, 1f / 0.15f, 0f, 0f));
            mat.EnableKeyword("_FADING_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
