using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Calabash.Style.EditorTools
{
    /// <summary>
    /// Подключает «мрак»: материал M_MangaGloom и полноэкранный проход URP во все Universal Renderer Data проекта,
    /// и вешает MangaGloom на камеру. Меню: Calabash → Мрак.
    /// </summary>
    public static class MangaGloomSetup
    {
        public const string ShaderName = "Calabash/MangaGloom";
        const string FeatureName = "MangaGloom";

        [MenuItem("Calabash/Мрак/Подключить к URP")]
        static void SetupFromMenu()
        {
            int added = EnsureRendererFeature();
            EditorUtility.DisplayDialog("Мрак",
                added >= 0
                    ? $"Проход MangaGloom есть во всех рендерерах URP (добавлен в {added}). Повесьте компонент MangaGloom на камеру или выберите камеру и Calabash → Мрак → Добавить на выбранную камеру."
                    : "Не найден шейдер Calabash/MangaGloom или рендереры URP.",
                "OK");
        }

        [MenuItem("Calabash/Мрак/Добавить на выбранную камеру")]
        static void AddToSelected()
        {
            EnsureRendererFeature();
            var go = Selection.activeGameObject;
            var cam = go != null ? go.GetComponentInChildren<Camera>() : Camera.main;
            if (cam == null)
            {
                EditorUtility.DisplayDialog("Мрак", "Выберите объект с камерой.", "OK");
                return;
            }
            AddTo(cam);
            Selection.activeGameObject = cam.gameObject;
        }

        /// <summary>Повесить MangaGloom на камеру (и подключить проход URP, если его ещё нет).</summary>
        public static MangaGloom AddTo(Camera cam)
        {
            EnsureRendererFeature();
            var g = cam.GetComponent<MangaGloom>();
            if (g == null) g = Undo.AddComponent<MangaGloom>(cam.gameObject);
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.requiresDepthOption = CameraOverrideOption.On;
            return g;
        }

        /// <summary>Материал фильтра рядом с шейдером: Style/Materials/M_MangaGloom.mat.</summary>
        public static Material EnsureMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) return null;
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            string root = Path.GetDirectoryName(Path.GetDirectoryName(shaderPath)).Replace('\\', '/');
            string folder = $"{root}/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(root, "Materials");
            string matPath = $"{folder}/M_MangaGloom.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_MangaGloom" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            return mat;
        }

        /// <summary>Добавить проход во все UniversalRendererData. Возвращает, во сколько добавлен, или −1 при ошибке.</summary>
        public static int EnsureRendererFeature()
        {
            Material mat = EnsureMaterial();
            if (mat == null)
            {
                Debug.LogError($"[Мрак] Шейдер {ShaderName} не найден");
                return -1;
            }
            int added = 0, total = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;      // рендереры внутри пакетов не трогаем
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;
                total++;
                bool has = false;
                foreach (var f in data.rendererFeatures)
                    if (f is FullScreenPassRendererFeature fs && fs.passMaterial != null && fs.passMaterial.shader == mat.shader)
                    {
                        has = true;
                        Configure(fs, mat);
                        EditorUtility.SetDirty(fs);
                    }
                if (has) continue;

                var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                Configure(feature, mat);
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedProperties();
                data.SetDirty();
                EditorUtility.SetDirty(data);
                added++;
            }
            if (total == 0)
            {
                Debug.LogError("[Мрак] В проекте нет Universal Renderer Data");
                return -1;
            }
            AssetDatabase.SaveAssets();
            if (added > 0) Debug.Log($"[Мрак] Проход MangaGloom добавлен в {added} рендерер(ов) URP");
            return added;
        }

        static void Configure(FullScreenPassRendererFeature f, Material mat)
        {
            f.passMaterial = mat;
            f.passIndex = 0;
            f.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            f.fetchColorBuffer = true;
            f.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color;
            f.bindDepthStencilAttachment = false;
        }
    }
}
