#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OctoEvo.EditorTools
{
    /// <summary>
    /// Генерация временных реалистичных ассетов: PBR-текстуры (цвет + нормали) для песка, скал,
    /// кораллов и водорослей, текстура каустики, меш дна и меши валунов.
    /// Позже текстуры заменяются сканами (Poly Haven / ambientCG) — материалы и сцена при этом не меняются.
    /// </summary>
    public static class ProceduralAssets
    {
        public const string GenRoot = "Assets/OctoEvo/Generated";
        public const string TexDir = GenRoot + "/Textures";
        public const string MeshDir = GenRoot + "/Meshes";
        public const string MatDir = GenRoot + "/Materials";

        // ------------------------------------------------------------------ шум

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static float N(float u, float v, int f) => CamoUtil.TileableNoise(u, v, f);

        static float Fbm(float u, float v, int baseFreq, int octaves)
        {
            float s = 0, a = 0.5f, norm = 0;
            int f = baseFreq;
            for (int i = 0; i < octaves; i++) { s += N(u + i * 0.37f, v + i * 0.71f, f) * a; norm += a; a *= 0.5f; f *= 2; }
            return s / norm;
        }

        static float Ridged(float u, float v, int baseFreq, int octaves)
        {
            float s = 0, a = 0.5f, norm = 0;
            int f = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(N(u + i * 0.21f, v + i * 0.53f, f) * 2f - 1f);
                s += n * n * a; norm += a; a *= 0.5f; f *= 2;
            }
            return s / norm;
        }

        /// <summary>Бесшовный Вороной: возвращает (F1, F2) в долях ячейки.</summary>
        static Vector2 Voronoi(float u, float v, int cells, int seed)
        {
            float px = u * cells, py = v * cells;
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
            float f1 = 9f, f2 = 9f;
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                float fx = gx + Hash(wx, wy, seed), fy = gy + Hash(wx, wy, seed + 17);
                float d = Mathf.Sqrt((fx - px) * (fx - px) + (fy - py) * (fy - py));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
            return new Vector2(f1, f2);
        }

        // ------------------------------------------------------------------ PBR-наборы

        public delegate float HeightFn(float u, float v);
        public delegate Color AlbedoFn(float u, float v, float h, int x, int y);

        public struct TexSet { public Texture2D albedo; public Texture2D normal; }

        public static TexSet MakeSet(string name, int size, HeightFn height, AlbedoFn albedo, float normalStrength)
        {
            var hgt = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                hgt[y * size + x] = height(x / (float)size, y / (float)size);

            var col = new Color[size * size];
            var nrm = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float h = hgt[y * size + x];
                col[y * size + x] = albedo(x / (float)size, y / (float)size, h, x, y);
                float hl = hgt[y * size + (x - 1 + size) % size], hr = hgt[y * size + (x + 1) % size];
                float hd = hgt[((y - 1 + size) % size) * size + x], hu = hgt[((y + 1) % size) * size + x];
                var n = new Vector3((hl - hr) * normalStrength * size / 64f, (hd - hu) * normalStrength * size / 64f, 1f).normalized;
                nrm[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }

            return new TexSet
            {
                albedo = SaveTexture($"{name}_Albedo", size, col, true, false),
                normal = SaveTexture($"{name}_Normal", size, nrm, false, true)
            };
        }

        public static TexSet Sand() => MakeSet("Sand", 512,
            (u, v) =>
            {
                float warp = Fbm(u, v, 3, 3) * 0.9f;
                float ripple = Mathf.Sin((v * 9f + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
                ripple = Mathf.Pow(ripple, 1.6f);
                return ripple * 0.55f + Fbm(u, v, 16, 3) * 0.3f + Fbm(u, v, 64, 2) * 0.15f;
            },
            (u, v, h, x, y) =>
            {
                Color baseC = Color.Lerp(new Color(0.62f, 0.55f, 0.42f), new Color(0.82f, 0.76f, 0.62f), h);
                baseC *= Mathf.Lerp(0.9f, 1.06f, Fbm(u + 0.3f, v, 6, 3));
                float g = Hash(x, y, 7);
                if (g > 0.985f) baseC = Color.Lerp(baseC, new Color(0.3f, 0.27f, 0.24f), 0.7f);       // тёмные песчинки
                else if (g < 0.01f) baseC = Color.Lerp(baseC, new Color(0.96f, 0.93f, 0.86f), 0.8f);  // обломки ракушек
                baseC.a = 1f;
                return baseC;
            }, 2.2f);

        public static TexSet Rock() => MakeSet("Rock", 512,
            (u, v) => Ridged(u, v, 4, 5) * 0.75f + Fbm(u, v, 24, 3) * 0.25f,
            (u, v, h, x, y) =>
            {
                Color c = Color.Lerp(new Color(0.27f, 0.26f, 0.24f), new Color(0.58f, 0.55f, 0.49f), Mathf.SmoothStep(0.15f, 0.9f, h));
                c *= Mathf.Lerp(0.85f, 1.1f, Fbm(u + 0.5f, v + 0.2f, 5, 3));
                // водоросли во впадинах
                float algae = Mathf.SmoothStep(0.45f, 0.2f, h) * Mathf.SmoothStep(0.4f, 0.65f, Fbm(u, v + 0.4f, 6, 3));
                c = Color.Lerp(c, new Color(0.2f, 0.27f, 0.15f), algae * 0.8f);
                // корковые кораллиновые водоросли (розоватые пятна)
                float crust = Mathf.SmoothStep(0.62f, 0.7f, Fbm(u + 0.8f, v + 0.1f, 4, 4));
                c = Color.Lerp(c, new Color(0.62f, 0.42f, 0.44f), crust * 0.6f);
                c.a = 1f;
                return c;
            }, 3.5f);

        public static TexSet Coral() => MakeSet("Coral", 512,
            (u, v) =>
            {
                Vector2 f = Voronoi(u, v, 22, 3);
                float pits = Mathf.SmoothStep(0.0f, 0.45f, f.x);
                return pits * 0.7f + Fbm(u, v, 8, 3) * 0.3f;
            },
            (u, v, h, x, y) =>
            {
                float hue = Fbm(u + 0.2f, v + 0.6f, 3, 3);
                Color a = new Color(0.78f, 0.5f, 0.36f), b = new Color(0.72f, 0.62f, 0.42f), cc = new Color(0.6f, 0.35f, 0.4f);
                Color c = hue < 0.5f ? Color.Lerp(a, b, hue * 2f) : Color.Lerp(b, cc, hue * 2f - 1f);
                c *= Mathf.Lerp(0.55f, 1.05f, h);  // полипы в ячейках темнее
                c.a = 1f;
                return c;
            }, 3f);

        public static TexSet Kelp() => MakeSet("Kelp", 256,
            (u, v) => (Mathf.Sin(u * Mathf.PI * 2f * 6f + Fbm(u, v, 4, 2) * 2f) * 0.5f + 0.5f) * 0.4f + Fbm(u, v, 16, 3) * 0.6f,
            (u, v, h, x, y) =>
            {
                Color c = Color.Lerp(new Color(0.3f, 0.28f, 0.1f), new Color(0.52f, 0.47f, 0.2f), h);
                c.a = 1f;
                return c;
            }, 1.5f);

        public static Texture2D Caustics()
        {
            const int size = 512;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                // слегка искривлённая сетка Вороного: яркие тонкие линии на стыках ячеек
                float wu = u + (N(u, v, 4) - 0.5f) * 0.06f, wv = v + (N(u + 0.5f, v, 4) - 0.5f) * 0.06f;
                Vector2 f = Voronoi(wu, wv, 9, 11);
                float edge = f.y - f.x;
                float c = Mathf.Pow(Mathf.Clamp01(1f - edge * 4.5f), 5f);
                c += Mathf.Pow(Mathf.Clamp01(1f - edge * 2.2f), 3f) * 0.25f;
                px[y * size + x] = new Color(c, c, c, 1f);
            }
            return SaveTexture("Caustics", size, px, false, false);
        }

        public static Texture2D SoftDot()
        {
            const int size = 64;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            return SaveTexture("SoftDot", size, px, true, false, TextureWrapMode.Clamp);
        }

        static Texture2D SaveTexture(string name, int size, Color[] px, bool srgb, bool normalMap, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            EnsureFolder(TexDir);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
            t.SetPixels(px);
            t.Apply();
            string path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.isReadable = srgb && !normalMap; // средний цвет нужен CamoSurface
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.textureCompression = imp.isReadable ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
            imp.alphaIsTransparency = name == "SoftDot";
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ меши

        static float Noise3(Vector3 p)
        {
            return (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) + Mathf.PerlinNoise(p.z + 17.3f, p.x + 5.1f)) / 3f;
        }

        static float Fbm3(Vector3 p, int oct)
        {
            float s = 0, a = 0.5f, n = 0;
            for (int i = 0; i < oct; i++) { s += Noise3(p) * a; n += a; a *= 0.5f; p *= 2.03f; }
            return s / n;
        }

        /// <summary>Валун: икосфера, искажённая шумом, с приплюснутым низом.</summary>
        public static Mesh Rock(string name, int seed, float roughness = 1f)
        {
            var mesh = Icosphere(3);
            var verts = mesh.vertices;
            Vector3 off = new Vector3(seed * 13.1f, seed * 7.7f, seed * 3.3f);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 d = verts[i].normalized;
                float r = 0.75f + Fbm3(d * 1.4f + off, 3) * 0.55f * roughness;
                float ridge = 1f - Mathf.Abs(Noise3(d * 3.7f + off * 0.5f) * 2f - 1f);
                r += ridge * ridge * 0.12f * roughness;
                Vector3 p = d * r;
                if (p.y < -0.25f) p.y = -0.25f + (p.y + 0.25f) * 0.3f;
                verts[i] = p;
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.name = name;
            return SaveMesh(mesh, name);
        }

        /// <summary>Дно: сетка с дюнами, ровной площадкой в центре и подъёмом к краям.</summary>
        public static Mesh Seabed(float size, int res)
        {
            var verts = new Vector3[(res + 1) * (res + 1)];
            var tris = new int[res * res * 6];
            for (int z = 0; z <= res; z++)
            for (int x = 0; x <= res; x++)
            {
                float px = (x / (float)res - 0.5f) * size, pz = (z / (float)res - 0.5f) * size;
                float h = (Mathf.PerlinNoise(px * 0.03f + 10f, pz * 0.03f + 10f) - 0.5f) * 1.6f
                        + (Mathf.PerlinNoise(px * 0.11f + 3f, pz * 0.11f) - 0.5f) * 0.5f;
                float r = Mathf.Sqrt(px * px + pz * pz);
                h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 12f, r)); // ровная площадка в центре
                h += Mathf.Pow(Mathf.Clamp01((r - size * 0.38f) / (size * 0.12f)), 2f) * 6f; // склоны у краёв
                verts[z * (res + 1) + x] = new Vector3(px, h, pz);
            }
            int t = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int i = z * (res + 1) + x;
                tris[t++] = i; tris[t++] = i + res + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + res + 1; tris[t++] = i + res + 2;
            }
            var mesh = new Mesh { name = "Seabed", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveMesh(mesh, "Seabed");
        }

        static Mesh Icosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var nf = new List<int>();
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(v, cache, a, b), bc = Mid(v, cache, b, c), ca = Mid(v, cache, c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(f, 0);
            return mesh;
        }

        static int Mid(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = v.Count - 1;
            return v.Count - 1;
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            EnsureFolder(MeshDir);
            string path = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
