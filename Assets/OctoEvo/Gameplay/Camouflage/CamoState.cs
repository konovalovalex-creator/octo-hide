using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Одно состояние маскировки. Для сети позже передаём не текстуру, а ID поверхности/паттерна
    /// + цвет + масштаб + матрицу (позицию и поворот на момент копирования) — всё компактно.
    /// </summary>
    [System.Serializable]
    public struct CamoState
    {
        public Texture pattern;
        public Color tint;
        public float scale;
        public float papillae;
        public Color average;          // средний цвет узора (с учётом tint) — для смаза в движении и оценки заметности
        public Matrix4x4 captureLocalToWorld; // поза осьминога в момент копирования
        public int sourceSurfaceId;    // 0 — ручная палитра
        public bool natural;           // естественный окрас модели (текстура из Tripo)
        public Texture pattern2;       // второй слой поверхности (мох, гравий) — пятнами, как на самой поверхности
        public float scale2;
        public Vector4 layer;          // x — размер пятен (1/м), y — смещение покрытия, z — резкость, w — слой включён

        public bool IsFromSurface => sourceSurfaceId != 0;
    }

    public static class CamoUtil
    {
        static Texture2D mottle;

        /// <summary>Пятнистая серая текстура для ручной палитры и начального окраса.</summary>
        public static Texture2D MottleTexture
        {
            get
            {
                if (mottle != null) return mottle;
                const int size = 128;
                mottle = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "OctoMottle", wrapMode = TextureWrapMode.Repeat };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = TileableNoise(u, v, 6) * 0.65f + TileableNoise(u, v, 14) * 0.35f;
                    float spots = Mathf.SmoothStep(0.45f, 0.62f, TileableNoise(u + 0.37f, v + 0.11f, 9));
                    float g = Mathf.Lerp(0.6f, 1f, n) * Mathf.Lerp(1f, 0.7f, spots);
                    px[y * size + x] = new Color(g, g, g, 1f);
                }
                mottle.SetPixels(px);
                mottle.Apply(true, false);
                return mottle;
            }
        }

        public const float MottleAverage = 0.74f;

        /// <summary>Шум Перлина, бесшовно повторяющийся с периодом 1.</summary>
        public static float TileableNoise(float u, float v, int freq)
        {
            float x = u * freq, y = v * freq;
            float a = Mathf.PerlinNoise(x, y);
            float b = Mathf.PerlinNoise(x - freq, y);
            float c = Mathf.PerlinNoise(x, y - freq);
            float d = Mathf.PerlinNoise(x - freq, y - freq);
            float fu = u - Mathf.Floor(u), fv = v - Mathf.Floor(v);
            return Mathf.Lerp(Mathf.Lerp(a, b, fu), Mathf.Lerp(c, d, fu), fv);
        }

        /// <summary>Цветовая разница ΔE (CIE76) — грубо, но для оценки заметности хватает.</summary>
        public static float DeltaE(Color a, Color b)
        {
            Vector3 la = ToLab(a), lb = ToLab(b);
            return Vector3.Distance(la, lb);
        }

        static Vector3 ToLab(Color c)
        {
            Color lin = c.linear;
            float x = lin.r * 0.4124f + lin.g * 0.3576f + lin.b * 0.1805f;
            float y = lin.r * 0.2126f + lin.g * 0.7152f + lin.b * 0.0722f;
            float z = lin.r * 0.0193f + lin.g * 0.1192f + lin.b * 0.9505f;
            x /= 0.95047f; z /= 1.08883f;
            float fx = F(x), fy = F(y), fz = F(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : (7.787f * t + 16f / 116f);
    }
}
