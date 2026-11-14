#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OctoHide.EditorTools
{
    /// <summary>
    /// Меню OctoHide → Build Prototype Scene: генерирует текстуры дна, материалы и тестовую сцену
    /// с осьминогом, камерой и HUD. Можно запускать повторно — всё пересоздаётся.
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        const string Root = "Assets/OctoHide";
        const string GenTex = Root + "/Generated/Textures";
        const string GenMat = Root + "/Generated/Materials";
        const string ScenePath = Root + "/Scenes/Prototype.unity";
        const int TexSize = 256;

        static readonly Color FogColor = new Color(0.10f, 0.36f, 0.50f);

        [MenuItem("OctoHide/Build Prototype Scene")]
        public static void Build()
        {
            Shader env = Shader.Find("OctoHide/EnvTriplanar");
            Shader skin = Shader.Find("OctoHide/OctopusSkin");
            if (env == null || skin == null)
            {
                EditorUtility.DisplayDialog("OctoHide", "Не найдены шейдеры OctoHide/EnvTriplanar и OctoHide/OctopusSkin. Проверьте, что проект на URP и шейдеры скомпилировались без ошибок.", "OK");
                return;
            }

            EnsureFolder(GenTex);
            EnsureFolder(GenMat);
            EnsureFolder(Root + "/Scenes");

            // ---------- Текстуры
            var sand = SaveTexture("Sand", GenSand());
            var pebbles = SaveTexture("Pebbles", GenPebbles());
            var rock = SaveTexture("Rock", GenRock());
            var kelp = SaveTexture("KelpStripes", GenStripes());
            var coral = SaveTexture("CoralDots", GenCoral());
            var rust = SaveTexture("Rust", GenRust());
            var white = Texture2D.whiteTexture;

            // ---------- Материалы
            var mSand = EnvMat("Env_Sand", env, sand, Color.white, 0.45f);
            var mPebbles = EnvMat("Env_Pebbles", env, pebbles, Color.white, 0.35f);
            var mRock = EnvMat("Env_Rock", env, rock, Color.white, 0.3f);
            var mKelp = EnvMat("Env_Kelp", env, kelp, Color.white, 0.4f);
            var mCoral = EnvMat("Env_Coral", env, coral, Color.white, 0.5f);
            var mRust = EnvMat("Env_Rust", env, rust, Color.white, 0.4f);
            var mEyeWhite = EnvMat("Octo_EyeWhite", env, white, new Color(0.95f, 0.93f, 0.85f), 1f);
            var mPupil = EnvMat("Octo_Pupil", env, white, new Color(0.05f, 0.05f, 0.07f), 1f);
            var mSkin = SaveMaterial(new Material(skin), "Octo_Skin");

            // ---------- Сцена
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.70f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.45f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.18f, 0.22f);
            RenderSettings.skybox = null;

            var lightGo = new GameObject("Sun (сквозь воду)");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.85f, 0.95f, 1f);
            light.intensity = 1.4f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(58f, -35f, 0f);

            // Дно из 6 участков с разными материалами
            var floorRoot = new GameObject("Seabed").transform;
            Material[] tiles = { mSand, mPebbles, mRock, mKelp, mCoral, mRust };
            float[] tilePap = { 0.1f, 0.6f, 0.75f, 0.3f, 0.9f, 0.5f };
            const float tileSize = 12f;
            for (int i = 0; i < tiles.Length; i++)
            {
                int gx = i % 3 - 1, gz = i / 3;
                var tile = Box($"Floor_{tiles[i].name}", new Vector3(gx * tileSize, -0.5f, (gz - 0.5f) * tileSize),
                    new Vector3(tileSize, 1f, tileSize), tiles[i], floorRoot);
                tile.AddComponent<CamoSurface>().papillae = tilePap[i];
            }

            // Валуны, стена, обломки
            var props = new GameObject("Props").transform;
            var rnd = new System.Random(42);
            Material[] boulderMats = { mRock, mCoral, mPebbles, mRust };
            float[] boulderPap = { 0.75f, 0.9f, 0.6f, 0.5f };
            for (int i = 0; i < 22; i++)
            {
                int m = rnd.Next(boulderMats.Length);
                float sc = 0.8f + (float)rnd.NextDouble() * 2.2f;
                var pos = new Vector3(Rand(rnd, -16f, 16f), sc * 0.25f, Rand(rnd, -11f, 11f));
                if (pos.magnitude < 3f) continue;
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                b.name = $"Boulder_{i}";
                b.transform.SetParent(props);
                b.transform.position = pos;
                b.transform.localScale = new Vector3(sc * Rand(rnd, 0.9f, 1.4f), sc * Rand(rnd, 0.5f, 0.9f), sc * Rand(rnd, 0.9f, 1.4f));
                b.transform.rotation = Quaternion.Euler(0, Rand(rnd, 0, 360), 0);
                b.GetComponent<Renderer>().sharedMaterial = boulderMats[m];
                b.AddComponent<CamoSurface>().papillae = boulderPap[m];
            }

            var wall = Box("Wall_Kelp", new Vector3(0f, 2.5f, 12.5f), new Vector3(20f, 6f, 1f), mKelp, props);
            wall.AddComponent<CamoSurface>().papillae = 0.3f;
            var wreck = Box("Wreck_Hull", new Vector3(-12f, 1.2f, 6f), new Vector3(6f, 2.4f, 3f), mRust, props);
            wreck.transform.rotation = Quaternion.Euler(0f, 25f, 12f);
            wreck.AddComponent<CamoSurface>().papillae = 0.5f;
            for (int i = 0; i < 4; i++)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = $"CoralPillar_{i}";
                pillar.transform.SetParent(props);
                pillar.transform.position = new Vector3(8f + i * 2.2f, 1.5f, -6f + (i % 2) * 2f);
                pillar.transform.localScale = new Vector3(0.9f, 1.5f + i * 0.3f, 0.9f);
                pillar.GetComponent<Renderer>().sharedMaterial = mCoral;
                pillar.AddComponent<CamoSurface>().papillae = 0.9f;
            }

            // ---------- Камера
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 3f, -5f);
            var orbit = camGo.AddComponent<OrbitCamera>();

            // ---------- Осьминог
            var octo = BuildOctopus(mSkin, mEyeWhite, mPupil, camGo.transform);
            orbit.target = octo.transform;

            // ---------- HUD
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<PrototypeHUD>();
            hud.controller = octo.GetComponent<OctopusController>();
            hud.skin = octo.GetComponent<OctopusSkin>();
            hud.meter = octo.GetComponent<ConspicuityMeter>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[OctoHide] Сцена прототипа собрана: " + ScenePath + ". Нажмите Play.");
        }

        static GameObject BuildOctopus(Material skin, Material eyeWhite, Material pupil, Transform cam)
        {
            var root = new GameObject("Octopus");
            root.transform.position = new Vector3(0f, 0.6f, 0f);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1f;
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.35f;

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0f, -0.12f, 0f);

            var head = Primitive(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 0.05f, 0.05f), new Vector3(0.72f, 0.6f, 0.72f), skin);
            var mantle = Primitive(PrimitiveType.Sphere, "Mantle", body, new Vector3(0f, 0.32f, -0.32f), new Vector3(0.75f, 0.8f, 1.0f), skin);
            mantle.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);

            for (int s = -1; s <= 1; s += 2)
            {
                var eye = Primitive(PrimitiveType.Sphere, s < 0 ? "EyeL" : "EyeR", body, new Vector3(0.2f * s, 0.22f, 0.24f), Vector3.one * 0.2f, eyeWhite);
                Primitive(PrimitiveType.Sphere, "Pupil", eye.transform, new Vector3(0f, 0f, 0.38f), new Vector3(0.6f, 0.35f, 0.4f), pupil);
            }

            var tentGo = new GameObject("Tentacles", typeof(MeshFilter), typeof(MeshRenderer));
            tentGo.transform.SetParent(root.transform, false);
            tentGo.GetComponent<MeshRenderer>().sharedMaterial = skin;

            var ctrl = root.AddComponent<OctopusController>();
            ctrl.body = body;
            ctrl.cameraTransform = cam;

            var tent = tentGo.AddComponent<OctopusTentacles>();
            tent.controller = ctrl;

            var skinComp = root.AddComponent<OctopusSkin>();
            skinComp.controller = ctrl;
            skinComp.skinMaterialTemplate = skin;
            skinComp.skinRenderers = new Renderer[]
            {
                head.GetComponent<Renderer>(), mantle.GetComponent<Renderer>(), tentGo.GetComponent<Renderer>()
            };

            var meter = root.AddComponent<ConspicuityMeter>();
            meter.controller = ctrl;
            meter.skin = skinComp;
            return root;
        }

        // ---------------------------------------------------------------- helpers

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static void EnsureFolder(string path)
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

        static Material EnvMat(string name, Shader env, Texture tex, Color tint, float scale)
        {
            var m = new Material(env);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Scale", scale);
            return SaveMaterial(m, name);
        }

        static Material SaveMaterial(Material m, string name)
        {
            string path = $"{GenMat}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D SaveTexture(string name, Color[] px)
        {
            var t = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            t.SetPixels(px);
            t.Apply();
            string path = $"{GenTex}/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.isReadable = true;            // нужно CamoSurface для среднего цвета
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void AddToBuildSettings(string path)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------------------------------------------------------------- процедурные узоры (бесшовные)

        delegate Color PixelFn(float u, float v);

        static Color[] Gen(PixelFn f)
        {
            var px = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
                px[y * TexSize + x] = f(x / (float)TexSize, y / (float)TexSize);
            return px;
        }

        static float N(float u, float v, int f) => CamoUtil.TileableNoise(u, v, f);

        static Color[] GenSand() => Gen((u, v) =>
        {
            float ripple = Mathf.Sin((v + N(u, v, 3) * 0.15f) * Mathf.PI * 2f * 6f) * 0.5f + 0.5f;
            float speck = N(u, v, 48) > 0.68f ? 0.82f : 1f;
            float g = Mathf.Lerp(0.88f, 1f, ripple) * Mathf.Lerp(0.9f, 1.05f, N(u, v, 12)) * speck;
            return new Color(0.86f, 0.77f, 0.58f) * g;
        });

        static Color[] GenPebbles()
        {
            const int cells = 7;
            var rnd = new System.Random(7);
            var pts = new Vector2[cells * cells];
            var cols = new Color[cells * cells];
            Color[] palette = { new Color(0.55f, 0.52f, 0.48f), new Color(0.42f, 0.40f, 0.38f), new Color(0.65f, 0.58f, 0.48f), new Color(0.35f, 0.33f, 0.36f) };
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                cols[i] = palette[rnd.Next(palette.Length)] * Rand(rnd, 0.85f, 1.15f);
            }
            return Gen((u, v) =>
            {
                float px = u * cells, py = v * cells;
                int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
                float d1 = 9f, d2 = 9f; Color c = Color.black;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int gx = cx + ox, gy = cy + oy;
                    int wx = (gx % cells + cells) % cells, wy = (gy % cells + cells) % cells;
                    int idx = wy * cells + wx;
                    Vector2 p = new Vector2(gx + pts[idx].x, gy + pts[idx].y);
                    float d = Vector2.Distance(p, new Vector2(px, py));
                    if (d < d1) { d2 = d1; d1 = d; c = cols[idx]; }
                    else if (d < d2) d2 = d;
                }
                float gap = Mathf.SmoothStep(0.03f, 0.12f, d2 - d1);
                return Color.Lerp(new Color(0.2f, 0.18f, 0.16f), c * Mathf.Lerp(0.9f, 1.1f, N(u, v, 20)), gap);
            });
        }

        static Color[] GenRock() => Gen((u, v) =>
        {
            float n = N(u, v, 4) * 0.5f + N(u, v, 9) * 0.3f + N(u, v, 22) * 0.2f;
            float crack = Mathf.Abs(N(u, v, 6) - 0.5f) < 0.02f ? 0.55f : 1f;
            Color a = new Color(0.38f, 0.40f, 0.42f), b = new Color(0.62f, 0.62f, 0.58f);
            Color moss = new Color(0.35f, 0.45f, 0.30f);
            Color c = Color.Lerp(a, b, n);
            c = Color.Lerp(c, moss, Mathf.SmoothStep(0.6f, 0.75f, N(u + 0.3f, v, 5)) * 0.6f);
            return c * crack;
        });

        static Color[] GenStripes() => Gen((u, v) =>
        {
            float wob = N(u, v, 3) * 0.25f;
            float s = Mathf.Sin((u + wob) * Mathf.PI * 2f * 5f);
            float stripe = Mathf.SmoothStep(-0.2f, 0.2f, s);
            Color dark = new Color(0.18f, 0.35f, 0.16f), light = new Color(0.45f, 0.62f, 0.25f);
            return Color.Lerp(dark, light, stripe) * Mathf.Lerp(0.85f, 1.1f, N(u, v, 16));
        });

        static Color[] GenCoral() => Gen((u, v) =>
        {
            Color baseC = new Color(0.92f, 0.48f, 0.55f) * Mathf.Lerp(0.85f, 1.05f, N(u, v, 6));
            float dots = N(u, v, 24);
            float big = N(u + 0.5f, v + 0.2f, 8);
            Color c = baseC;
            c = Color.Lerp(c, new Color(1f, 0.92f, 0.85f), Mathf.SmoothStep(0.66f, 0.7f, dots));
            c = Color.Lerp(c, new Color(0.98f, 0.62f, 0.25f), Mathf.SmoothStep(0.62f, 0.66f, big) * 0.8f);
            return c;
        });

        static Color[] GenRust() => Gen((u, v) =>
        {
            float n = N(u, v, 5) * 0.6f + N(u, v, 17) * 0.4f;
            Color a = new Color(0.45f, 0.22f, 0.12f), b = new Color(0.78f, 0.45f, 0.2f);
            Color c = Color.Lerp(a, b, n);
            float pits = N(u + 0.7f, v + 0.1f, 30) > 0.7f ? 0.6f : 1f;
            float plates = (Mathf.Repeat(v * 4f, 1f) < 0.02f || Mathf.Repeat(u * 2f, 1f) < 0.01f) ? 0.5f : 1f;
            return c * pits * plates;
        });
    }
}
#endif
