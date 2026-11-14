#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OctoEvo.UI;

namespace OctoEvo.EditorTools
{
    /// <summary>
    /// Меню OctoEvo → Build Reef Scene: генерирует ассеты и тестовую сцену рифа с реалистичной
    /// подводной оптикой. Можно запускать повторно — всё пересоздаётся.
    /// </summary>
    public static class ReefSceneBuilder
    {
        const string ScenePath = "Assets/OctoEvo/Scenes/Reef.unity";
        const string ProfilePath = ProceduralAssets.GenRoot + "/UnderwaterPost.asset";

        [MenuItem("OctoEvo/Build Reef Scene", priority = 0)]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("OctoEvo", "Сначала остановите Play — сцену нельзя пересобрать во время игры.", "OK");
                return;
            }
            Shader env = Shader.Find("OctoEvo/EnvironmentLit");
            Shader skin = Shader.Find("OctoEvo/OctopusSkinLit");
            Shader ray = Shader.Find("OctoEvo/GodRay");
            Shader particle = Shader.Find("OctoEvo/SoftParticle");
            Shader surface = Shader.Find("OctoEvo/WaterSurfaceUnder");
            if (env == null || skin == null || ray == null || particle == null || surface == null)
            {
                EditorUtility.DisplayDialog("OctoEvo", "Не найдены шейдеры OctoEvo. Проверьте консоль на ошибки компиляции шейдеров.", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("OctoEvo", "Текстуры…", 0.1f);
                var sand = ProceduralAssets.Sand();
                var rockT = ProceduralAssets.Rock();
                var coralT = ProceduralAssets.Coral();
                var kelpT = ProceduralAssets.Kelp();
                var caustics = ProceduralAssets.Caustics();
                var dot = ProceduralAssets.SoftDot();
                var white = Texture2D.whiteTexture;

                EditorUtility.DisplayProgressBar("OctoEvo", "Меши…", 0.4f);
                var seabedMesh = ProceduralAssets.Seabed(100f, 140);
                var rocks = new Mesh[6];
                for (int i = 0; i < rocks.Length; i++) rocks[i] = ProceduralAssets.Rock("Rock_" + i, i + 1, i < 3 ? 1f : 1.4f);
                var coralHeads = new Mesh[3];
                for (int i = 0; i < coralHeads.Length; i++) coralHeads[i] = ProceduralAssets.Rock("CoralHead_" + i, 20 + i, 0.6f);

                EditorUtility.DisplayProgressBar("OctoEvo", "Материалы…", 0.55f);
                var mSand = EnvMat("Sand", env, sand.albedo, sand.normal, Color.white, 0.2f, 0.12f, 0.8f);
                var mRock = EnvMat("Rock", env, rockT.albedo, rockT.normal, Color.white, 0.35f, 0.2f, 1.2f);
                var mCoral = EnvMat("Coral", env, coralT.albedo, coralT.normal, Color.white, 0.9f, 0.25f, 1f);
                var mKelp = EnvMat("Kelp", env, kelpT.albedo, kelpT.normal, Color.white, 1.5f, 0.45f, 0.6f);
                var mPlain = EnvMat("Plain", env, white, null, Color.white, 1f, 0.5f, 0f);
                var mEye = EnvMat("Octo_Eye", env, white, null, new Color(0.78f, 0.62f, 0.28f), 1f, 0.9f, 0f);
                var mPupil = EnvMat("Octo_Pupil", env, white, null, new Color(0.02f, 0.02f, 0.02f), 1f, 0.95f, 0f);
                var mSkin = SaveMat(new Material(skin), "Octo_Skin");
                var mRay = SaveMat(new Material(ray), "GodRay");
                var mSnow = SaveMat(new Material(particle), "MarineSnow");
                mSnow.SetTexture("_BaseMap", dot);
                var mSurface = SaveMat(new Material(surface), "WaterSurface");

                EditorUtility.DisplayProgressBar("OctoEvo", "Сцена…", 0.7f);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var rnd = new System.Random(7);

                // ---------- Свет
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.42f, 0.62f, 0.72f);
                RenderSettings.ambientEquatorColor = new Color(0.2f, 0.36f, 0.42f);
                RenderSettings.ambientGroundColor = new Color(0.05f, 0.09f, 0.11f);
                RenderSettings.fog = false;

                var sunGo = new GameObject("Sun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.97f, 0.9f);
                sun.intensity = 3f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
                sunGo.transform.rotation = Quaternion.Euler(62f, -35f, 0f);

                // ---------- Камера
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 140f;
                cam.fieldOfView = 55f;
                camGo.AddComponent<AudioListener>();
                camGo.transform.position = new Vector3(0f, 3f, -5f);
                var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
                camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var orbit = camGo.AddComponent<OrbitCamera>();

                // ---------- Постобработка
                var volGo = new GameObject("Underwater Post");
                var volume = volGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = BuildPostProfile();

                // ---------- Вода и атмосфера
                var envGo = new GameObject("Underwater");
                var uw = envGo.AddComponent<UnderwaterEnvironment>();
                uw.causticsTexture = caustics;
                uw.targetCamera = cam;
                uw.postVolume = volume;
                var atmo = envGo.AddComponent<UnderwaterAtmosphere>();
                atmo.cameraTransform = camGo.transform;
                atmo.sun = sunGo.transform;
                atmo.godRayMaterial = mRay;
                atmo.snowMaterial = mSnow;
                atmo.surfaceMaterial = mSurface;

                // ---------- Дно
                var world = new GameObject("World").transform;
                var seabed = MeshObject("Seabed", seabedMesh, mSand, world, Vector3.zero, Quaternion.identity, Vector3.one);
                seabed.AddComponent<CamoSurface>().papillae = 0.1f;

                // ---------- Скалы по кругу
                var cliffs = new GameObject("Cliffs").transform;
                cliffs.SetParent(world);
                for (int i = 0; i < 26; i++)
                {
                    float a = i / 26f * Mathf.PI * 2f + Rand(rnd, -0.08f, 0.08f);
                    float r = Rand(rnd, 34f, 42f);
                    var pos = new Vector3(Mathf.Cos(a) * r, -1f, Mathf.Sin(a) * r);
                    var scale = new Vector3(Rand(rnd, 7f, 12f), Rand(rnd, 6f, 13f), Rand(rnd, 6f, 10f));
                    var go = MeshObject("Cliff", rocks[rnd.Next(rocks.Length)], mRock, cliffs, pos, Quaternion.Euler(Rand(rnd, -10, 10), Rand(rnd, 0, 360), Rand(rnd, -10, 10)), scale);
                    go.AddComponent<CamoSurface>().papillae = 0.75f;
                }

                // ---------- Валуны
                var boulders = new GameObject("Boulders").transform;
                boulders.SetParent(world);
                for (int i = 0; i < 40; i++)
                {
                    var p = new Vector3(Rand(rnd, -28f, 28f), 0f, Rand(rnd, -28f, 28f));
                    if (p.magnitude < 6f) continue;
                    float s = Mathf.Pow(Rand(rnd, 0.2f, 1f), 1.8f) * 3.2f + 0.3f;
                    p.y = GroundY(p) + s * 0.15f;
                    var go = MeshObject("Boulder", rocks[rnd.Next(rocks.Length)], mRock, boulders, p,
                        Quaternion.Euler(Rand(rnd, -15, 15), Rand(rnd, 0, 360), Rand(rnd, -15, 15)),
                        new Vector3(s * Rand(rnd, 0.9f, 1.4f), s * Rand(rnd, 0.6f, 1f), s * Rand(rnd, 0.9f, 1.4f)));
                    go.AddComponent<CamoSurface>().papillae = 0.75f;
                }

                // ---------- Арка
                var arch = new GameObject("Arch").transform;
                arch.SetParent(world);
                arch.position = new Vector3(15f, 0f, 7f);
                arch.rotation = Quaternion.Euler(0f, -25f, 0f);
                LocalRock("Pillar_L", rocks[1], mRock, arch, new Vector3(-2.6f, 1.8f, 0f), new Vector3(2.2f, 4.5f, 2.2f), new Vector3(0, 0, 6));
                LocalRock("Pillar_R", rocks[2], mRock, arch, new Vector3(2.6f, 1.8f, 0f), new Vector3(2.2f, 4.5f, 2.2f), new Vector3(0, 0, -6));
                LocalRock("Top", rocks[4], mRock, arch, new Vector3(0f, 4.6f, 0f), new Vector3(8f, 1.8f, 2.6f), new Vector3(0, 0, 3));

                // ---------- Пещера-туннель
                var cave = new GameObject("Cave").transform;
                cave.SetParent(world);
                cave.position = new Vector3(-15f, 0f, -9f);
                cave.rotation = Quaternion.Euler(0f, 20f, 0f);
                for (int i = 0; i < 4; i++)
                {
                    float z = -4.5f + i * 3f;
                    LocalRock("Wall_L", rocks[i % 6], mRock, cave, new Vector3(-2.3f, 1.2f, z), new Vector3(2.4f, 3f, 3.4f), new Vector3(0, i * 40, 0));
                    LocalRock("Wall_R", rocks[(i + 3) % 6], mRock, cave, new Vector3(2.3f, 1.2f, z), new Vector3(2.4f, 3f, 3.4f), new Vector3(0, i * 55, 0));
                }
                LocalRock("Roof", rocks[5], mRock, cave, new Vector3(0f, 3.1f, 0f), new Vector3(6.5f, 1.6f, 13f), new Vector3(0, 0, 2));

                // ---------- Навес
                var ledge = new GameObject("Overhang").transform;
                ledge.SetParent(world);
                ledge.position = new Vector3(-6f, 0f, 11f);
                LocalRock("Support", rocks[0], mRock, ledge, new Vector3(-2.5f, 0.6f, 0f), new Vector3(2.4f, 2f, 2.6f), Vector3.zero);
                LocalRock("Slab", rocks[3], mRock, ledge, new Vector3(0f, 1.9f, 0f), new Vector3(6.5f, 0.9f, 4.2f), new Vector3(-5, 10, 8));

                // ---------- Риф: коралловые головы и ветвистые кораллы
                var reef = new GameObject("Reef").transform;
                reef.SetParent(world);
                Color[] coralTints = { Color.white, new Color(1f, 0.85f, 0.75f), new Color(0.9f, 0.8f, 1f), new Color(1f, 0.95f, 0.7f) };
                for (int i = 0; i < 28; i++)
                {
                    var p = new Vector3(Rand(rnd, 5f, 22f), 0f, Rand(rnd, -20f, -4f));
                    float s = Rand(rnd, 0.6f, 2f);
                    p.y = GroundY(p) + s * 0.1f;
                    var go = MeshObject("CoralHead", coralHeads[rnd.Next(coralHeads.Length)], mCoral, reef, p,
                        Quaternion.Euler(0, Rand(rnd, 0, 360), 0), new Vector3(s, s * Rand(rnd, 0.5f, 0.8f), s));
                    go.AddComponent<CamoSurface>().papillae = 0.9f;
                    Tint(go, coralTints[rnd.Next(coralTints.Length)]);
                }
                for (int i = 0; i < 40; i++)
                {
                    var c = new Vector3(Rand(rnd, 6f, 21f), 0f, Rand(rnd, -19f, -5f));
                    float h = Rand(rnd, 0.4f, 1.3f);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "CoralBranch";
                    go.transform.SetParent(reef);
                    go.transform.position = c + Vector3.up * (GroundY(c) + h * 0.8f);
                    go.transform.localScale = new Vector3(0.14f, h, 0.14f);
                    go.transform.rotation = Quaternion.Euler(Rand(rnd, -30f, 30f), Rand(rnd, 0, 360), Rand(rnd, -30f, 30f));
                    go.GetComponent<Renderer>().sharedMaterial = mCoral;
                    go.AddComponent<CamoSurface>().papillae = 0.9f;
                    Tint(go, coralTints[rnd.Next(coralTints.Length)]);
                }

                // ---------- Водоросли
                var kelpRoot = new GameObject("Kelp").transform;
                kelpRoot.SetParent(world);
                Vector3[] kelpCenters = { new Vector3(-10f, 0f, 3f), new Vector3(3f, 0f, 14f), new Vector3(-20f, 0f, -20f) };
                foreach (var kc in kelpCenters)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var k = new GameObject("KelpStalk").AddComponent<KelpSway>();
                        k.transform.SetParent(kelpRoot);
                        var p = kc + new Vector3(Rand(rnd, -3f, 3f), 0f, Rand(rnd, -3f, 3f));
                        p.y = GroundY(p);
                        k.transform.position = p;
                        k.transform.rotation = Quaternion.Euler(0f, Rand(rnd, 0, 360), 0f);
                        k.material = mKelp;
                        k.tint = Color.white;
                        k.height = Rand(rnd, 3f, 7f);
                        k.width = Rand(rnd, 0.2f, 0.35f);
                        k.segments = 8;
                    }
                }

                // ---------- Мелкие камни
                var small = new GameObject("Pebbles").transform;
                small.SetParent(world);
                for (int i = 0; i < 60; i++)
                {
                    var p = new Vector3(Rand(rnd, -25f, 25f), 0f, Rand(rnd, -25f, 25f));
                    float s = Rand(rnd, 0.08f, 0.35f);
                    p.y = GroundY(p) + s * 0.1f;
                    var go = MeshObject("Pebble", rocks[rnd.Next(rocks.Length)], mRock, small, p, Quaternion.Euler(0, Rand(rnd, 0, 360), 0), new Vector3(s, s * 0.6f, s));
                    if (s < 0.25f) Object.DestroyImmediate(go.GetComponent<Collider>());
                }

                // ---------- Живность (временные модели — заменим на реалистичные на этапе 2)
                var fauna = new GameObject("Fauna").transform;
                AddSchool(fauna, new Vector3(4f, 3f, -8f), 16, new Color(0.85f, 0.8f, 0.5f), 1f, mPlain);
                AddSchool(fauna, new Vector3(-8f, 4f, 6f), 14, new Color(0.7f, 0.78f, 0.85f), 0.9f, mPlain);
                AddSchool(fauna, new Vector3(12f, 2.5f, -14f), 10, new Color(0.9f, 0.55f, 0.3f), 1.2f, mPlain);
                for (int i = 0; i < 8; i++)
                {
                    var p = new Vector3(Rand(rnd, -16f, 16f), 0.3f, Rand(rnd, -16f, 16f));
                    if (p.magnitude < 4f) continue;
                    var crab = new GameObject("Crab_" + i).AddComponent<Crab>();
                    crab.transform.SetParent(fauna);
                    crab.transform.position = p + Vector3.up * GroundY(p);
                    crab.transform.rotation = Quaternion.Euler(0f, Rand(rnd, 0, 360), 0f);
                    crab.shellMaterial = mPlain;
                    crab.size = Rand(rnd, 0.8f, 1.3f);
                    crab.shellColor = Color.Lerp(new Color(0.55f, 0.25f, 0.15f), new Color(0.6f, 0.45f, 0.3f), (float)rnd.NextDouble());
                }

                // ---------- Осьминог
                var octo = BuildOctopus(new Vector3(0f, 0.8f, 0f), mSkin, mEye, mPupil, camGo.transform);
                orbit.target = octo.transform;

                // ---------- HUD
                var hud = new GameObject("DebugHUD").AddComponent<DebugHUD>();
                hud.motor = octo.GetComponent<OctopusMotor>();
                hud.skin = octo.GetComponent<CamouflageSkin>();
                hud.meter = octo.GetComponent<ConspicuityMeter>();

                ProceduralAssets.EnsureFolder("Assets/OctoEvo/Scenes");
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                AssetDatabase.SaveAssets();
                Debug.Log("[OctoEvo] Сцена рифа собрана: " + ScenePath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ осьминог

        static GameObject BuildOctopus(Vector3 position, Material skin, Material eyeMat, Material pupilMat, Transform cam)
        {
            var root = new GameObject("Octopus");
            root.transform.position = position;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1f;
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.35f;

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0f, -0.12f, 0f);

            var head = Primitive(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 0.04f, 0.05f), new Vector3(0.62f, 0.52f, 0.62f), skin);
            var mantle = Primitive(PrimitiveType.Sphere, "Mantle", body, new Vector3(0f, 0.3f, -0.36f), new Vector3(0.68f, 0.72f, 1.05f), skin);
            mantle.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);

            // Глаза: золотистое глазное яблоко и горизонтальный щелевидный зрачок, как у настоящего осьминога
            var eyeList = new System.Collections.Generic.List<Transform>();
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = Primitive(PrimitiveType.Sphere, s < 0 ? "EyeL" : "EyeR", body, new Vector3(0.21f * s, 0.2f, 0.16f), Vector3.one * 0.15f, eyeMat);
                var pupil = Primitive(PrimitiveType.Cube, "Pupil", eye.transform, new Vector3(0.12f * s, 0f, 0.42f), new Vector3(0.55f, 0.13f, 0.2f), pupilMat);
                pupil.transform.localRotation = Quaternion.Euler(0f, 25f * s, 0f);
                eyeList.Add(eye.transform);
            }

            var tentGo = new GameObject("Tentacles", typeof(MeshFilter), typeof(MeshRenderer));
            tentGo.transform.SetParent(root.transform, false);
            tentGo.GetComponent<MeshRenderer>().sharedMaterial = skin;

            var motor = root.AddComponent<OctopusMotor>();
            motor.isLocalPlayer = true;
            motor.body = body;
            motor.cameraTransform = cam;

            var tent = tentGo.AddComponent<TentacleRig>();
            tent.controller = motor;

            var skinComp = root.AddComponent<CamouflageSkin>();
            skinComp.controller = motor;
            skinComp.skinMaterialTemplate = skin;
            skinComp.skinRenderers = new Renderer[] { head.GetComponent<Renderer>(), mantle.GetComponent<Renderer>(), tentGo.GetComponent<Renderer>() };

            var eyes = root.AddComponent<OctopusEyes>();
            eyes.skin = skinComp;
            eyes.eyes = eyeList.ToArray();

            var meter = root.AddComponent<ConspicuityMeter>();
            meter.controller = motor;
            meter.skin = skinComp;
            meter.eyes = eyes;
            return root;
        }

        // ------------------------------------------------------------------ постобработка

        static VolumeProfile BuildPostProfile()
        {
            ProceduralAssets.EnsureFolder(ProceduralAssets.GenRoot);
            AssetDatabase.DeleteAsset(ProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 0.6f;
            bloom.threshold.value = 0.95f;
            bloom.scatter.value = 0.7f;
            bloom.tint.value = new Color(0.8f, 0.95f, 1f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = 0.55f;
            color.contrast.value = 12f;
            color.saturation.value = 6f;

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.3f;
            vignette.smoothness.value = 0.5f;
            vignette.color.value = new Color(0f, 0.05f, 0.08f);

            var dof = profile.Add<DepthOfField>(true);
            dof.mode.value = DepthOfFieldMode.Gaussian;
            dof.gaussianStart.value = 12f;
            dof.gaussianEnd.value = 55f;
            dof.gaussianMaxRadius.value = 1f;

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ helpers

        static float GroundY(Vector3 p)
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(new Vector3(p.x, 50f, p.z), Vector3.down, out RaycastHit hit, 100f))
                return hit.point.y;
            return 0f;
        }

        static GameObject MeshObject(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        static GameObject LocalRock(string name, Mesh mesh, Material mat, Transform parent, Vector3 localPos, Vector3 scale, Vector3 euler)
        {
            var go = MeshObject(name, mesh, mat, parent, Vector3.zero, Quaternion.identity, scale);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<CamoSurface>().papillae = 0.75f;
            return go;
        }

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

        static void Tint(GameObject go, Color c)
        {
            if (c == Color.white) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }

        static void AddSchool(Transform parent, Vector3 pos, int count, Color tint, float size, Material mat)
        {
            var go = new GameObject("FishSchool");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var s = go.AddComponent<FishSchool>();
            s.count = count;
            s.tint = tint;
            s.size = size;
            s.material = mat;
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static Material EnvMat(string name, Shader env, Texture albedo, Texture normal, Color tint, float scale, float smoothness, float normalStrength)
        {
            var m = new Material(env);
            m.SetTexture("_BaseMap", albedo);
            if (normal != null) m.SetTexture("_NormalMap", normal);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Scale", scale);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_NormalStrength", normalStrength);
            m.enableInstancing = true;
            return SaveMat(m, name);
        }

        static Material SaveMat(Material m, string name)
        {
            ProceduralAssets.EnsureFolder(ProceduralAssets.MatDir);
            string path = $"{ProceduralAssets.MatDir}/{name}.mat";
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
    }
}
#endif
