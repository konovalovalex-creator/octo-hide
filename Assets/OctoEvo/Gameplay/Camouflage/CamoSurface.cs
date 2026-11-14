using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Описывает поверхность, с которой осьминог может скопировать окрас.
    /// Узор, цвет и масштаб читаются из материала (шейдер OctoEvo/EnvironmentLit).
    /// На объектах без этого компонента осьминог копирует хотя бы цвет материала.
    /// </summary>
    [DisallowMultipleComponent]
    public class CamoSurface : MonoBehaviour
    {
        [Tooltip("Рельеф поверхности: 0 — гладкая, 1 — бугристая. Осьминог подстроит папиллы.")]
        [Range(0, 1)] public float papillae = 0.2f;

        [Tooltip("Если не задан — берётся первый Renderer на объекте или в дочерних.")]
        public Renderer targetRenderer;

        public Texture Pattern { get; private set; }
        public Color Tint { get; private set; } = Color.white;
        public float Scale { get; private set; } = 1f;
        public Color Average { get; private set; } = Color.gray;
        public int Id { get; private set; }

        static int nextId = 1;
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");

        void Awake()
        {
            Id = nextId++;
            Refresh();
        }

        public void Refresh()
        {
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
            if (targetRenderer == null) return;
            ReadMaterial(targetRenderer.sharedMaterial, out Texture tex, out Color tint, out float scale);
            Pattern = tex;
            Tint = tint;
            Scale = scale;
            Average = AverageOf(tex) * tint;
            Average = new Color(Average.r, Average.g, Average.b, 1f);
        }

        public CamoState ToState(Matrix4x4 capturePose)
        {
            return new CamoState
            {
                pattern = Pattern != null ? Pattern : Texture2D.whiteTexture,
                tint = Tint,
                scale = Scale,
                papillae = papillae,
                average = Average,
                captureLocalToWorld = capturePose,
                sourceSurfaceId = Id
            };
        }

        /// <summary>Состояние маскировки для любого коллайдера — с CamoSurface или без.</summary>
        public static bool TryGetState(Collider col, Matrix4x4 capturePose, out CamoState state)
        {
            state = default;
            if (col == null) return false;
            var surf = col.GetComponentInParent<CamoSurface>();
            if (surf != null)
            {
                state = surf.ToState(capturePose);
                return true;
            }
            var r = col.GetComponentInParent<Renderer>();
            if (r == null) r = col.GetComponentInChildren<Renderer>();
            if (r == null) return false;
            ReadMaterial(r.sharedMaterial, out Texture tex, out Color tint, out float scale);
            Color avg = AverageOf(tex) * tint;
            avg.a = 1f;
            state = new CamoState
            {
                // Чужие материалы обычно с UV-развёрткой — узор не совпадёт, поэтому берём только цвет
                pattern = CamoUtil.MottleTexture,
                tint = avg / CamoUtil.MottleAverage,
                scale = 2f,
                papillae = 0.2f,
                average = avg,
                captureLocalToWorld = capturePose,
                sourceSurfaceId = -1 - (r.GetHashCode() & 0x7FFFFFFE) // отрицательные ID — поверхности без CamoSurface
            };
            return true;
        }

        static void ReadMaterial(Material m, out Texture tex, out Color tint, out float scale)
        {
            tex = null; tint = Color.white; scale = 1f;
            if (m == null) return;
            if (m.HasProperty(BaseMapId)) tex = m.GetTexture(BaseMapId);
            else if (m.HasProperty(MainTexId)) tex = m.GetTexture(MainTexId);
            if (m.HasProperty(BaseColorId)) tint = m.GetColor(BaseColorId);
            else if (m.HasProperty(ColorId)) tint = m.GetColor(ColorId);
            if (m.HasProperty(ScaleId)) scale = m.GetFloat(ScaleId);
        }

        static Color AverageOf(Texture tex)
        {
            if (tex is Texture2D t2 && t2.isReadable)
            {
                int mip = Mathf.Max(0, t2.mipmapCount - 1);
                Color[] px = t2.GetPixels(mip);
                if (px.Length > 0)
                {
                    Color sum = Color.black;
                    foreach (var c in px) sum += c;
                    return sum / px.Length;
                }
            }
            return Color.white;
        }
    }
}
