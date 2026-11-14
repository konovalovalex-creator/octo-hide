using System.Collections.Generic;
using UnityEngine;

namespace OctoEvo
{
    public enum CreatureBody { Fish, Shrimp, Crab, Flatfish }

    /// <summary>
    /// Процедурные меши существ (генерируются один раз и кэшируются).
    /// Голова по +Z, длина тела 1. Цвет вершины R: 1 — тело, 0 — плавник/усик.
    /// </summary>
    public static class CreatureMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Mesh Get(CreatureBody body, float halfHeight, float halfWidth)
        {
            string key = body + "_" + halfHeight.ToString("0.000") + "_" + halfWidth.ToString("0.000");
            if (cache.TryGetValue(key, out Mesh m) && m != null) return m;
            switch (body)
            {
                case CreatureBody.Shrimp: m = Shrimp(halfHeight, halfWidth); break;
                case CreatureBody.Crab: m = Carapace(); break;
                case CreatureBody.Flatfish: m = Flatfish(halfHeight, halfWidth); break;
                default: m = Fish(halfHeight, halfWidth); break;
            }
            m.name = "Creature_" + key;
            cache[key] = m;
            return m;
        }

        static Mesh crabLeg, crabClaw;
        public static Mesh CrabLeg => crabLeg != null ? crabLeg : (crabLeg = Leg());
        public static Mesh CrabClaw => crabClaw != null ? crabClaw : (crabClaw = Ellipsoid(new Vector3(0.5f, 0.32f, 0.5f), 10, 7, "CrabClaw"));

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();
            public readonly List<int> finT = new List<int>();

            public int Add(Vector3 p, float body)
            {
                v.Add(p);
                c.Add(new Color(body, 0, 0, 1));
                return v.Count - 1;
            }

            public void Fin(params Vector3[] tri)
            {
                // отдельные вершины: у плавника своя (плоская) нормаль
                for (int i = 0; i < tri.Length; i++) finT.Add(Add(tri[i], 0f));
            }

            public Mesh Build()
            {
                var m = new Mesh();
                m.SetVertices(v);
                m.SetColors(c);
                var all = new List<int>(t);
                all.AddRange(finT);
                m.SetTriangles(all, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Тело-«веретено»: кольца вдоль Z, сечение — эллипс.</summary>
        static void Lathe(Builder b, float h, float w, int rings, int sides, System.Func<float, float> profile, System.Func<float, float> spineY, float zTail, float zHead)
        {
            int start = b.v.Count;
            int tail = b.Add(new Vector3(0f, spineY(0f), zTail), 1f);
            for (int r = 1; r < rings; r++)
            {
                float u = r / (float)rings;
                float z = Mathf.Lerp(zTail, zHead, u);
                float f = profile(u);
                float y0 = spineY(u);
                for (int s = 0; s < sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    float sy = Mathf.Sin(a);
                    // брюхо чуть площе спины
                    float yy = sy < 0f ? sy * 0.85f : sy;
                    b.Add(new Vector3(Mathf.Cos(a) * w * f, y0 + yy * h * f, z), 1f);
                }
            }
            int nose = b.Add(new Vector3(0f, spineY(1f), zHead), 1f);

            System.Func<int, int, int> Ring = (ri, si) => start + 1 + (ri - 1) * sides + ((si % sides) + sides) % sides;
            for (int s = 0; s < sides; s++)
            {
                b.t.Add(tail); b.t.Add(Ring(1, s)); b.t.Add(Ring(1, s + 1));
            }
            for (int r = 1; r < rings - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a0 = Ring(r, s), a1 = Ring(r, s + 1), b0 = Ring(r + 1, s), b1 = Ring(r + 1, s + 1);
                    b.t.Add(a0); b.t.Add(b0); b.t.Add(a1);
                    b.t.Add(a1); b.t.Add(b0); b.t.Add(b1);
                }
            for (int s = 0; s < sides; s++)
            {
                b.t.Add(Ring(rings - 1, s)); b.t.Add(nose); b.t.Add(Ring(rings - 1, s + 1));
            }
        }

        static Mesh Fish(float h, float w)
        {
            var b = new Builder();
            Lathe(b, h, w, 22, 14, u =>
            {
                if (u < 0.12f) return 0.14f + u * 0.5f; // хвостовой стебель
                float k = (u - 0.12f) / 0.88f;
                return Mathf.Max(0.05f, Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.68f)), 0.75f));
            }, u => 0f, -0.42f, 0.5f);

            // Хвостовой плавник с вырезом
            Vector3 bt = new Vector3(0f, h * 0.15f, -0.40f), bb = new Vector3(0f, -h * 0.15f, -0.40f);
            Vector3 tt = new Vector3(0f, h * 1.15f, -0.62f), notch = new Vector3(0f, 0f, -0.53f), tb = new Vector3(0f, -h * 1.05f, -0.61f);
            b.Fin(bt, tt, notch);
            b.Fin(bt, notch, bb);
            b.Fin(bb, notch, tb);
            // Спинной плавник
            b.Fin(new Vector3(0f, h * 0.9f, 0.18f), new Vector3(0f, h * 1.75f, -0.02f), new Vector3(0f, h * 0.75f, -0.18f));
            // Анальный плавник
            b.Fin(new Vector3(0f, -h * 0.7f, -0.08f), new Vector3(0f, -h * 1.25f, -0.2f), new Vector3(0f, -h * 0.45f, -0.28f));
            // Грудные плавники
            for (int sgn = -1; sgn <= 1; sgn += 2)
                b.Fin(new Vector3(w * 0.85f * sgn, -h * 0.2f, 0.2f), new Vector3(w * 1.9f * sgn, -h * 0.55f, 0.05f), new Vector3(w * 0.8f * sgn, -h * 0.35f, 0.08f));
            return b.Build();
        }

        /// <summary>Камбала: плоское овальное тело, кайма плавников по краям, хвост в плоскости тела.</summary>
        static Mesh Flatfish(float h, float w)
        {
            var b = new Builder();
            Lathe(b, h, w, 20, 12, u =>
            {
                if (u < 0.1f) return 0.18f + u;
                float k = (u - 0.1f) / 0.9f;
                return Mathf.Max(0.06f, Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.8f)), 0.6f));
            }, u => 0f, -0.4f, 0.5f);
            // хвост — горизонтальный веер
            b.Fin(new Vector3(-w * 0.2f, 0f, -0.38f), new Vector3(w * 0.2f, 0f, -0.38f), new Vector3(w * 0.55f, 0f, -0.6f));
            b.Fin(new Vector3(-w * 0.2f, 0f, -0.38f), new Vector3(w * 0.55f, 0f, -0.6f), new Vector3(-w * 0.55f, 0f, -0.6f));
            // кайма плавников вдоль боков
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int i = 0; i < 6; i++)
                {
                    float z0 = Mathf.Lerp(-0.3f, 0.3f, i / 6f), z1 = Mathf.Lerp(-0.3f, 0.3f, (i + 1) / 6f);
                    float e0 = Mathf.Sin(Mathf.PI * (i / 6f * 0.8f + 0.1f)), e1 = Mathf.Sin(Mathf.PI * ((i + 1) / 6f * 0.8f + 0.1f));
                    b.Fin(new Vector3(w * 0.85f * e0 * sgn, 0f, z0), new Vector3(w * 0.85f * e1 * sgn, 0f, z1), new Vector3(w * 1.3f * (e0 + e1) * 0.5f * sgn, 0f, (z0 + z1) * 0.5f));
                }
            return b.Build();
        }

        static Mesh Shrimp(float h, float w)
        {
            var b = new Builder();
            // изогнутая спина, сегменты — лёгкими «перетяжками»
            System.Func<float, float> spine = u => -0.18f * (u - 0.55f) * (u - 0.55f) + 0.02f;
            Lathe(b, h, w, 26, 10, u =>
            {
                float seg = 1f - 0.08f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 7f));
                float body = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 0.85f + 0.1f)), 0.6f);
                return Mathf.Max(0.12f, body * seg);
            }, spine, -0.45f, 0.45f);

            // хвостовой веер
            float ty = spine(0f);
            b.Fin(new Vector3(0f, ty, -0.43f), new Vector3(w * 1.6f, ty - h * 0.2f, -0.6f), new Vector3(-w * 1.6f, ty - h * 0.2f, -0.6f));
            // усики — длинные узкие треугольники
            float hy = spine(1f);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 root = new Vector3(w * 0.3f * sgn, hy + h * 0.3f, 0.42f);
                b.Fin(root, root + new Vector3(0.002f * sgn, 0.006f, 0f), new Vector3(w * 4f * sgn, hy + h * 1.5f, 1.1f));
            }
            // ножки
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(0.25f, -0.05f, i / 4f);
                float y = spine((z + 0.45f) / 0.9f) - h * 0.7f;
                b.Fin(new Vector3(-w * 0.4f, y, z), new Vector3(w * 0.4f, y, z), new Vector3(0f, y - h * 1.1f, z + 0.03f));
            }
            return b.Build();
        }

        /// <summary>Панцирь краба: ширина 0.5, глубина 0.38, высота 0.16.</summary>
        static Mesh Carapace()
        {
            var m = Ellipsoid(new Vector3(0.25f, 0.08f, 0.19f), 16, 9, "CrabBody");
            // Слегка расширяем вперёд, «плечи» — как у настоящего панциря
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                float front = Mathf.Clamp01(v[i].z / 0.19f * 0.5f + 0.5f);
                v[i].x *= 0.85f + 0.3f * front;
                if (v[i].y < 0f) v[i].y *= 0.6f;
            }
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh Ellipsoid(Vector3 radii, int seg, int rings, string name)
        {
            var b = new Builder();
            for (int r = 0; r <= rings; r++)
            {
                float th = r / (float)rings * Mathf.PI;
                for (int s = 0; s <= seg; s++)
                {
                    float ph = s / (float)seg * Mathf.PI * 2f;
                    b.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * radii.x, Mathf.Cos(th) * radii.y, Mathf.Sin(th) * Mathf.Sin(ph) * radii.z), 1f);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, c = a + seg + 1;
                    b.t.Add(a); b.t.Add(a + 1); b.t.Add(c);
                    b.t.Add(a + 1); b.t.Add(c + 1); b.t.Add(c);
                }
            var m = b.Build();
            m.name = name;
            return m;
        }

        /// <summary>Нога краба: два сегмента вдоль +X, длина 1, «колено» вверх.</summary>
        static Mesh Leg()
        {
            var b = new Builder();
            Vector3 a = Vector3.zero, k = new Vector3(0.5f, 0.25f, 0f), e = new Vector3(1f, -0.35f, 0f);
            Segment(b, a, k, 0.07f, 0.055f);
            Segment(b, k, e, 0.055f, 0.02f);
            var m = b.Build();
            m.name = "CrabLeg";
            return m;
        }

        static void Segment(Builder b, Vector3 p0, Vector3 p1, float r0, float r1)
        {
            const int sides = 6;
            Vector3 dir = (p1 - p0).normalized;
            Vector3 n = Vector3.Cross(dir, Vector3.forward).normalized;
            Vector3 bn = Vector3.Cross(dir, n);
            int s0 = b.v.Count;
            for (int i = 0; i < sides; i++)
            {
                float ang = i / (float)sides * Mathf.PI * 2f;
                Vector3 o = n * Mathf.Cos(ang) + bn * Mathf.Sin(ang);
                b.Add(p0 + o * r0, 1f);
                b.Add(p1 + o * r1, 1f);
            }
            for (int i = 0; i < sides; i++)
            {
                int a0 = s0 + i * 2, a1 = a0 + 1, c0 = s0 + ((i + 1) % sides) * 2, c1 = c0 + 1;
                b.t.Add(a0); b.t.Add(c0); b.t.Add(a1);
                b.t.Add(a1); b.t.Add(c0); b.t.Add(c1);
            }
            int tip = b.Add(p1 + dir * r1, 1f);
            for (int i = 0; i < sides; i++)
            {
                int a1 = s0 + i * 2 + 1, c1 = s0 + ((i + 1) % sides) * 2 + 1;
                b.t.Add(a1); b.t.Add(c1); b.t.Add(tip);
            }
        }
    }
}
