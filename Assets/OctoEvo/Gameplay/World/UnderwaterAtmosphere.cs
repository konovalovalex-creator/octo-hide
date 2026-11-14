using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Атмосфера вокруг камеры: лучи солнца сквозь воду, «морской снег» (взвесь) и поверхность воды над головой.
    /// Всё следует за камерой, поэтому работает на карте любого размера.
    /// </summary>
    public class UnderwaterAtmosphere : MonoBehaviour
    {
        public Transform cameraTransform;
        public Transform sun;

        [Header("Лучи")]
        public Material godRayMaterial;
        public int godRaysHigh = 14;
        public int godRaysLow = 6;
        public float godRayRadius = 22f;
        public Vector2 godRayWidth = new Vector2(1.5f, 4f);

        [Header("Взвесь")]
        public Material snowMaterial;
        public int snowHigh = 900;
        public int snowLow = 300;
        public Vector3 snowBox = new Vector3(24f, 14f, 24f);

        [Header("Поверхность")]
        public Material surfaceMaterial;
        public float surfaceSize = 160f;

        Transform[] rays;
        Material[] rayMats;
        ParticleSystem snow;
        Transform surface;

        void Start()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            bool low = UnderwaterEnvironment.IsLowQuality;
            BuildRays(low ? godRaysLow : godRaysHigh);
            BuildSnow(low ? snowLow : snowHigh);
            BuildSurface();
        }

        float SurfaceY => UnderwaterEnvironment.Instance != null ? UnderwaterEnvironment.Instance.surfaceY : 14f;

        void BuildRays(int count)
        {
            if (godRayMaterial == null) return;
            rays = new Transform[count];
            rayMats = new Material[count];
            Mesh quad = BuildRayMesh();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("GodRay_" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = quad;
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                rayMats[i] = new Material(godRayMaterial);
                rayMats[i].SetFloat("_Seed", Random.value * 100f);
                rayMats[i].SetFloat("_Intensity", Random.Range(0.15f, 0.4f));
                r.sharedMaterial = rayMats[i];
                float w = Random.Range(godRayWidth.x, godRayWidth.y);
                go.transform.localScale = new Vector3(w, 1f, 1f);
                rays[i] = go.transform;
                Place(i, true);
            }
        }

        static Mesh BuildRayMesh()
        {
            // Плоскость: низ (uv.y=0) в точке объекта, верх уходит вверх на 1 (масштаб задаём по высоте)
            var m = new Mesh { name = "GodRayQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2, 2, 2) * 50f);
            return m;
        }

        void Place(int i, bool anywhere)
        {
            Vector3 c = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            Vector2 offset = Random.insideUnitCircle * godRayRadius;
            if (!anywhere) offset = offset.normalized * godRayRadius * Random.Range(0.6f, 1f);
            float groundY = 0f;
            Vector3 top = new Vector3(c.x + offset.x, SurfaceY, c.z + offset.y);
            if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) groundY = hit.point.y;
            rays[i].position = new Vector3(top.x, groundY, top.z);
            float height = Mathf.Max(2f, SurfaceY - groundY);
            Vector3 s = rays[i].localScale;
            rays[i].localScale = new Vector3(s.x, height, 1f);
        }

        void BuildSnow(int max)
        {
            if (snowMaterial == null) return;
            var go = new GameObject("MarineSnow");
            go.transform.SetParent(transform, false);
            snow = go.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = snow.main;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = 12f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.55f);
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.002f;

            var emission = snow.emission;
            emission.rateOverTime = max / 12f;

            var shape = snow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = snowBox;

            var vel = snow.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.04f, 0.01f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var noise = snow.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.3f;

            var col = snow.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = snowMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            snow.Play();
        }

        void BuildSurface()
        {
            if (surfaceMaterial == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "WaterSurface";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * (surfaceSize / 10f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = surfaceMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            surface = go.transform;
        }

        void LateUpdate()
        {
            if (cameraTransform == null) return;
            Vector3 c = cameraTransform.position;

            if (snow != null) snow.transform.position = c;
            if (surface != null) surface.position = new Vector3(c.x, SurfaceY, c.z);

            if (rays != null)
            {
                // Лучи наклонены по направлению солнца и развёрнуты лицом к камере вокруг своей оси
                Vector3 axis = sun != null ? -sun.forward : Vector3.up;
                if (axis.y < 0.2f) axis = Vector3.up;
                axis.Normalize();
                for (int i = 0; i < rays.Length; i++)
                {
                    Vector3 p = rays[i].position;
                    Vector2 flat = new Vector2(p.x - c.x, p.z - c.z);
                    if (flat.magnitude > godRayRadius * 1.15f) Place(i, false);
                    Vector3 toCam = Vector3.ProjectOnPlane(c - rays[i].position, axis);
                    if (toCam.sqrMagnitude < 0.01f) continue;
                    rays[i].rotation = Quaternion.LookRotation(-toCam.normalized, axis);
                }
            }
        }

        void OnDestroy()
        {
            if (rayMats == null) return;
            foreach (var m in rayMats) if (m != null) Destroy(m);
        }
    }
}
