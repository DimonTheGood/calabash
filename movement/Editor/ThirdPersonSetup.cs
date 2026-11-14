using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Calabash.Movement.EditorTools
{
    /// <summary>
    /// Ставит герою тело (модель SM_Char_zhdan0 из characters/fbx), оранжевый контур и ZhdanCameraMode.
    /// Модель ищется в проекте по имени; если её нет, герой остаётся только от первого лица.
    /// </summary>
    public static class ThirdPersonSetup
    {
        public const string CharacterModelName = "SM_Char_zhdan0";

        /// <summary>Палитра style bible для материалов модели Ждана (имя материала из Blender → цвет).</summary>
        static readonly (string name, Color32 color)[] CharacterPalette =
        {
            ("M_Ink", new Color32(0x0B, 0x0B, 0x0B, 0xFF)),
            ("M_Black", new Color32(0x0B, 0x0B, 0x0B, 0xFF)),
            ("M_Dark", new Color32(0x2B, 0x2B, 0x2B, 0xFF)),
            ("M_Soot", new Color32(0x1E, 0x1E, 0x1E, 0xFF)),
            ("M_Wood", new Color32(0x5A, 0x5A, 0x5A, 0xFF)),
            ("M_Bread", new Color32(0xCF, 0xC8, 0xB8, 0xFF)),
            ("M_Skin", new Color32(0xD9, 0xD6, 0xCF, 0xFF)),
            ("M_Cloth", new Color32(0x9A, 0x98, 0x92, 0xFF)),
            ("M_Glow", new Color32(0xFF, 0x7A, 0x18, 0xFF)),
        };

        /// <summary>Путь к модели по имени файла (без расширения), или null.</summary>
        public static string FindModelPath(string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{fileName} t:Model"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == fileName) return p;
            }
            return null;
        }

        /// <summary>Подменить материалы модели на плоские материалы палитры (создаются в folder).</summary>
        public static void RemapMaterials(string modelPath, (string name, Color32 color)[] palette, string folder)
        {
            EnsureFolder(folder);
            var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var (name, color) in palette)
            {
                Material mat = MovementTestSceneBuilder.MakeMaterial($"{folder}/{name}.mat", color, null, Vector2.one);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            }
            imp.SaveAndReimport();
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static bool TryAdd(GameObject player)
        {
            string modelPath = FindModelPath(CharacterModelName);
            if (modelPath == null)
            {
                Debug.LogWarning($"[Movement] Модель {CharacterModelName}.fbx не найдена в проекте — вид от третьего лица без тела. " +
                                 "Скопируйте characters/fbx/SM_Char_zhdan0.fbx в Assets/Calabash/Art/.");
                AddCameraMode(player, null, null, null);
                return false;
            }

            string root = MovementTestSceneBuilder.RootFolder();
            RemapMaterials(modelPath, CharacterPalette, $"{root}/Materials/Character");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var move = player.GetComponent<ZhdanMovement>();

            var body = new GameObject("Body").transform;
            body.SetParent(player.transform, false);
            var pose = new GameObject("Pose").transform;
            pose.SetParent(body, false);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Model";
            model.transform.SetParent(pose, false);
            FitToFeet(model.transform, move != null ? move.standingHeight : 1.8f);

            // контур: та же модель, раздутая шейдером и вывернутая
            GameObject outline = null;
            Shader outlineShader = Shader.Find("Calabash/InkOutline");
            if (outlineShader != null)
            {
                var outlineMat = AssetDatabase.LoadAssetAtPath<Material>($"{root}/Materials/Character/M_HeroOutline.mat");
                if (outlineMat == null)
                {
                    outlineMat = new Material(outlineShader);
                    AssetDatabase.CreateAsset(outlineMat, $"{root}/Materials/Character/M_HeroOutline.mat");
                }
                outlineMat.SetColor("_Color", new Color32(0xFF, 0x7A, 0x18, 0xFF));
                outlineMat.SetFloat("_Width", 0.012f);
                outline = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                outline.name = "Outline";
                outline.transform.SetParent(pose, false);
                outline.transform.localPosition = model.transform.localPosition;
                outline.transform.localScale = model.transform.localScale;
                foreach (var r in outline.GetComponentsInChildren<Renderer>())
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = outlineMat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }

            var view = body.gameObject.AddComponent<ZhdanBodyView>();
            view.movement = move;
            view.pose = pose;

            var anchor = new GameObject("FistAnchor").transform;
            anchor.SetParent(body, false);
            anchor.localPosition = new Vector3(0f, 1.4f, 0.05f);

            AddCameraMode(player, model.GetComponentsInChildren<Renderer>(),
                          outline != null ? outline.GetComponentsInChildren<Renderer>() : new Renderer[0], anchor);
            return true;
        }

        static void AddCameraMode(GameObject player, Renderer[] body, Renderer[] outline, Transform anchor)
        {
            var mode = player.GetComponent<ZhdanCameraMode>() ?? player.AddComponent<ZhdanCameraMode>();
            mode.input = player.GetComponent<ZhdanInput>();
            var look = player.GetComponent<FirstPersonLook>();
            mode.cameraPivot = look != null ? look.cameraPivot : null;
            var cam = player.GetComponentInChildren<Camera>();
            mode.cameraFx = cam != null ? cam.GetComponent<ZhdanCameraFx>() : null;
            mode.bodyRenderers = body ?? new Renderer[0];
            mode.thirdPersonOnly = outline ?? new Renderer[0];
            mode.viewModelFirstPersonParent = cam != null ? cam.transform : null;
            mode.viewModelThirdPersonParent = anchor;
            foreach (var r in mode.bodyRenderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            foreach (var r in mode.thirdPersonOnly) r.enabled = false;
        }

        /// <summary>Поставить модель ногами в ноль, по центру, и подогнать рост, если он сильно отличается.</summary>
        static void FitToFeet(Transform model, float targetHeight)
        {
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y > 0.01f && Mathf.Abs(b.size.y - targetHeight) / targetHeight > 0.25f)
            {
                model.localScale *= targetHeight / b.size.y;
                b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
            }
            Vector3 parentPos = model.parent.position;
            model.localPosition += new Vector3(parentPos.x - b.center.x, parentPos.y - b.min.y, parentPos.z - b.center.z);
            Debug.Log($"[Movement] Модель Ждана: рост {b.size.y:0.00} м, размер {b.size}");
        }
    }
}
