#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OctoEvo.EditorTools
{
    /// <summary>
    /// Настройки импорта для Assets/OctoEvo/Art: модели из Tripo (OBJ) и текстуры-сканы.
    /// Срабатывает автоматически при первом импорте (после git pull).
    /// </summary>
    public class OctoArtPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/OctoEvo/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            if (file.EndsWith("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (file.EndsWith("_Roughness"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
            }
            ti.mipmapEnabled = true;
            ti.anisoLevel = assetPath.Contains("/Textures/") ? 4 : 1;
            ti.wrapMode = assetPath.Contains("/Textures/") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.maxTextureSize = assetPath.Contains("/Octopus/") ? 4096 : 2048;

            // На телефоне — вдвое меньше
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = assetPath.Contains("/Octopus/") ? 2048 : 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.optimizeMeshVertices = true;
            mi.optimizeMeshPolygons = true;
            // Осьминога риггуем кодом — нужен доступ к вершинам
            mi.isReadable = assetPath.Contains("/Octopus/");
            mi.indexFormat = ModelImporterIndexFormat.Auto;
        }
    }

    /// <summary>Доступ к арту из Assets/OctoEvo/Art и сборка наших материалов поверх него.</summary>
    public static class ArtLibrary
    {
        public const string ModelsDir = "Assets/OctoEvo/Art/Models";
        public const string TexturesDir = "Assets/OctoEvo/Art/Textures";

        public static bool HasModel(string name) => File.Exists($"{ModelsDir}/{name}/{name}.obj");

        public static Mesh Mesh(string name)
        {
            string path = $"{ModelsDir}/{name}/{name}.obj";
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Mesh m) return m;
            return null;
        }

        public static Texture2D ModelTex(string name, string suffix) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelsDir}/{name}/{name}_{suffix}.jpg");

        public static Texture2D GroundTex(string name, string suffix) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{name}/{name}_{suffix}.jpg");

        /// <summary>Материал для предмета рифа (кораллы, водоросли…).</summary>
        public static Material PropMaterial(string name, float sway = 0f, float swaySpeed = 1f, float translucency = 0f, float smoothness = 0.8f)
        {
            var shader = Shader.Find("OctoEvo/PropLit");
            var m = new Material(shader);
            m.SetTexture("_BaseMap", ModelTex(name, "BaseColor"));
            var n = ModelTex(name, "Normal");
            if (n != null) m.SetTexture("_NormalMap", n);
            var r = ModelTex(name, "Roughness");
            if (r != null) m.SetTexture("_RoughnessMap", r);
            m.SetFloat("_SwayAmount", sway);
            m.SetFloat("_SwaySpeed", swaySpeed);
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_SmoothnessScale", smoothness);
            m.enableInstancing = true;
            return Save(m, "Prop_" + name);
        }

        /// <summary>Текстурный вариант шейдера существ.</summary>
        public static Material CreatureMaterial(string name)
        {
            var m = new Material(Shader.Find("OctoEvo/CreatureLit"));
            m.SetFloat("_Textured", 1f);
            m.EnableKeyword("_TEXTURED");
            m.SetTexture("_BaseMap", ModelTex(name, "BaseColor"));
            var n = ModelTex(name, "Normal");
            if (n != null) m.SetTexture("_NormalMap", n);
            var r = ModelTex(name, "Roughness");
            if (r != null) m.SetTexture("_RoughnessMap", r);
            m.enableInstancing = true;
            return Save(m, "Creature_" + name);
        }

        /// <summary>Материал окружения из сканов: основной слой + пятна второго.</summary>
        public static Material GroundMaterial(string matName, string baseTex, float scale, string layer2 = null, float scale2 = 0.5f,
                                              float coverage = 0.4f, float preferUp = 0f, float patchSize = 0.15f, float normalStrength = 1f, float smoothness = 0.2f)
        {
            var m = new Material(Shader.Find("OctoEvo/EnvironmentLit"));
            m.SetTexture("_BaseMap", GroundTex(baseTex, "Color"));
            m.SetTexture("_NormalMap", GroundTex(baseTex, "Normal"));
            m.SetFloat("_Scale", scale);
            m.SetFloat("_NormalStrength", normalStrength);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Occlusion", 0.35f);
            if (!string.IsNullOrEmpty(layer2) && GroundTex(layer2, "Color") != null)
            {
                m.SetFloat("_Layer2", 1f);
                m.EnableKeyword("_LAYER2");
                m.SetTexture("_Base2Map", GroundTex(layer2, "Color"));
                m.SetTexture("_Normal2Map", GroundTex(layer2, "Normal"));
                m.SetFloat("_Scale2", scale2);
                m.SetFloat("_BlendAmount", coverage);
                m.SetFloat("_BlendUp", preferUp);
                m.SetFloat("_BlendNoiseScale", patchSize);
            }
            m.enableInstancing = true;
            return Save(m, matName);
        }

        public static bool HasGround(string name) => File.Exists($"{TexturesDir}/{name}/{name}_Color.jpg");

        /// <summary>Средний цвет картинки (читаем файл напрямую — в игре текстуры нечитаемы).</summary>
        public static Color AverageColor(Texture tex)
        {
            if (tex == null) return new Color(0, 0, 0, 0);
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Color(0, 0, 0, 0);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            t.LoadImage(File.ReadAllBytes(path));
            t.Apply(true);
            Color[] px = t.GetPixels(Mathf.Max(0, t.mipmapCount - 3));
            Color sum = Color.black;
            foreach (var c in px) sum += c;
            Object.DestroyImmediate(t);
            Color avg = sum / Mathf.Max(1, px.Length);
            // В шейдере текстура sRGB → в среднем цвете работаем так же, как CamoUtil (гамма-значения)
            avg.a = 1f;
            return avg;
        }

        static Material Save(Material m, string name)
        {
            ProceduralAssets.EnsureFolder(ProceduralAssets.MatDir);
            string path = $"{ProceduralAssets.MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.enableInstancing = m.enableInstancing;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
#endif
