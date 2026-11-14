using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Процедурные щупальца: 8 цепочек точек (verlet) с «шагающими» кончиками,
    /// которые прилипают к поверхности, и общий трубчатый меш, перестраиваемый каждый кадр.
    /// Ничего из этого не синхронизируется по сети — каждый клиент считает щупальца
    /// сам из позиции, позы и скорости осьминога.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class OctopusTentacles : MonoBehaviour
    {
        public OctopusController controller;

        [Header("Форма")]
        [Range(4, 12)] public int tentacleCount = 8;
        [Range(6, 24)] public int segments = 14;
        [Range(4, 16)] public int radialSides = 8;
        public float length = 1.1f;
        public float rootRadius = 0.11f;
        public float tipRadius = 0.015f;
        public float rootRing = 0.28f;
        public float rootHeight = -0.24f;

        [Header("Шаги")]
        public float reach = 0.85f;
        public float stepDistance = 0.45f;
        public float stepDuration = 0.18f;
        public float stepHeight = 0.18f;

        [Header("Физика")]
        public float gravity = 1.5f;
        public float damping = 0.94f;
        public float shapeStiffness = 9f;
        [Range(1, 8)] public int iterations = 4;
        public LayerMask surfaceMask = ~0;

        class Tentacle
        {
            public Vector3[] pos;
            public Vector3[] prev;
            public Vector3 foot;
            public Vector3 footFrom;
            public Vector3 footTo;
            public float stepT = 1f;
            public bool planted;
            public float phase;
        }

        Tentacle[] tentacles;
        Mesh mesh;
        Vector3[] verts;
        Vector3[] normals;
        int[] tris;
        float segLen;

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<OctopusController>();
            mesh = new Mesh { name = "OctopusTentacles" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            Build();
        }

        void OnValidate()
        {
            if (Application.isPlaying && mesh != null) Build();
        }

        void Build()
        {
            segLen = length / (segments - 1);
            tentacles = new Tentacle[tentacleCount];
            Transform root = controller != null ? controller.transform : transform;
            for (int t = 0; t < tentacleCount; t++)
            {
                var tc = new Tentacle
                {
                    pos = new Vector3[segments],
                    prev = new Vector3[segments],
                    phase = (t % 2 == 0) ? 0f : 0.5f
                };
                Vector3 rootPos = RootWorld(root, t);
                Vector3 outDir = OutWorld(root, t);
                for (int i = 0; i < segments; i++)
                {
                    tc.pos[i] = tc.prev[i] = rootPos + outDir * segLen * i;
                }
                tc.foot = tc.footFrom = tc.footTo = tc.pos[segments - 1];
                tentacles[t] = tc;
            }

            int ringVerts = radialSides + 1;
            int vPerTentacle = segments * ringVerts + 1; // +1 — кончик
            verts = new Vector3[vPerTentacle * tentacleCount];
            normals = new Vector3[verts.Length];
            int trisPerTentacle = (segments - 1) * radialSides * 6 + radialSides * 3;
            tris = new int[trisPerTentacle * tentacleCount];

            int ti = 0;
            for (int t = 0; t < tentacleCount; t++)
            {
                int vb = t * vPerTentacle;
                for (int i = 0; i < segments - 1; i++)
                {
                    for (int s = 0; s < radialSides; s++)
                    {
                        int a = vb + i * ringVerts + s;
                        int b = a + 1;
                        int c = a + ringVerts;
                        int d = c + 1;
                        tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                        tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                    }
                }
                int tip = vb + segments * ringVerts;
                int last = vb + (segments - 1) * ringVerts;
                for (int s = 0; s < radialSides; s++)
                {
                    tris[ti++] = last + s; tris[ti++] = tip; tris[ti++] = last + s + 1;
                }
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris;
        }

        Vector3 RootLocal(int t)
        {
            float a = (t + 0.5f) / tentacleCount * Mathf.PI * 2f;
            return new Vector3(Mathf.Sin(a) * rootRing, rootHeight, Mathf.Cos(a) * rootRing);
        }

        Vector3 RootWorld(Transform root, int t) => root.TransformPoint(RootLocal(t));

        Vector3 OutWorld(Transform root, int t)
        {
            Vector3 l = RootLocal(t);
            l.y = 0f;
            return root.TransformDirection(l.normalized);
        }

        void LateUpdate()
        {
            if (controller == null || tentacles == null) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            Transform root = controller.transform;
            OctopusPose pose = controller.Pose;
            bool grounded = controller.IsGrounded;
            Vector3 up = root.up;

            float poseReach = reach;
            float curl = 0f;
            switch (pose)
            {
                case OctopusPose.Rock: poseReach = 0.25f; curl = 1f; break;
                case OctopusPose.Flat: poseReach = reach * 1.25f; break;
                case OctopusPose.Reach: poseReach = reach * 1.1f; break;
            }

            // Сколько щупалец сейчас в шаге — шагаем «волнами», не всеми сразу
            int stepping = 0;
            foreach (var tc in tentacles) if (tc.stepT < 1f) stepping++;

            for (int t = 0; t < tentacleCount; t++)
            {
                var tc = tentacles[t];
                Vector3 rootPos = RootWorld(root, t);
                Vector3 outDir = OutWorld(root, t);

                // ---- Желаемое положение кончика
                bool wantsPlant = grounded && curl < 0.5f;
                Vector3 desired;
                if (wantsPlant)
                {
                    Vector3 guess = rootPos + outDir * poseReach + root.forward * controller.NormalizedSpeed * 0.25f;
                    Vector3 castFrom = guess + up * 0.6f;
                    if (Physics.Raycast(castFrom, -up, out RaycastHit hit, 1.6f, surfaceMask, QueryTriggerInteraction.Ignore)
                        && hit.collider.attachedRigidbody == null)
                        desired = hit.point + hit.normal * tipRadius;
                    else
                        desired = guess - up * 0.3f;
                }
                else if (curl > 0.5f)
                {
                    // «Камень»: щупальца поджаты под тело
                    desired = rootPos - up * 0.15f - outDir * 0.05f;
                }
                else
                {
                    // В толще воды щупальца волочатся за телом
                    Vector3 trail = -root.forward * 0.5f - up * 0.6f;
                    desired = rootPos + (outDir * 0.4f + trail).normalized * length * 0.9f;
                }

                // ---- Шаги
                if (wantsPlant)
                {
                    if (!tc.planted)
                    {
                        tc.planted = true;
                        tc.foot = tc.footFrom = tc.footTo = desired;
                        tc.stepT = 1f;
                    }
                    else if (tc.stepT >= 1f)
                    {
                        float dist = Vector3.Distance(tc.foot, desired);
                        float maxDist = Vector3.Distance(tc.foot, rootPos);
                        bool tooFar = dist > stepDistance || maxDist > length * 0.98f;
                        bool myTurn = (Mathf.Repeat(Time.time * 2.5f, 1f) > 0.5f) == (tc.phase > 0.25f);
                        if (tooFar && stepping < tentacleCount / 2 && (myTurn || maxDist > length * 0.98f))
                        {
                            tc.footFrom = tc.foot;
                            tc.footTo = desired;
                            tc.stepT = 0f;
                            stepping++;
                        }
                    }
                    if (tc.stepT < 1f)
                    {
                        tc.stepT = Mathf.Min(1f, tc.stepT + dt / stepDuration);
                        float e = tc.stepT * tc.stepT * (3f - 2f * tc.stepT);
                        tc.foot = Vector3.Lerp(tc.footFrom, tc.footTo, e) + up * Mathf.Sin(e * Mathf.PI) * stepHeight;
                    }
                }
                else
                {
                    tc.planted = false;
                    tc.stepT = 1f;
                    tc.foot = Vector3.Lerp(tc.foot, desired, 1f - Mathf.Exp(-6f * dt));
                }

                Simulate(tc, rootPos, outDir, up, curl, dt);
            }

            RebuildMesh(root);
        }

        void Simulate(Tentacle tc, Vector3 rootPos, Vector3 outDir, Vector3 up, float curl, float dt)
        {
            int n = segments;
            float wave = Time.time * 3f + tc.phase * 6.28f;

            // Целевая форма: дуга от корня к кончику, приподнятая посередине
            Vector3 tip = tc.foot;
            Vector3 mid = (rootPos + tip) * 0.5f + up * (0.15f + curl * 0.05f);

            for (int i = 1; i < n; i++)
            {
                Vector3 p = tc.pos[i];
                Vector3 vel = (p - tc.prev[i]) * damping;
                tc.prev[i] = p;

                float u = i / (float)(n - 1);
                Vector3 shape = Bezier(rootPos, mid, tip, u);
                if (curl > 0.5f)
                {
                    // спираль под телом
                    float ang = u * Mathf.PI * 1.6f;
                    Vector3 side = Vector3.Cross(up, outDir);
                    shape = rootPos + (outDir * Mathf.Cos(ang) * 0.12f + side * Mathf.Sin(ang) * 0.06f - up * u * 0.12f) * (1f + u);
                }
                // лёгкое шевеление
                shape += Vector3.Cross(up, outDir) * Mathf.Sin(wave + u * 5f) * 0.03f * u;

                Vector3 next = p + vel + Vector3.down * gravity * dt * dt;
                tc.pos[i] = Vector3.Lerp(next, shape, 1f - Mathf.Exp(-shapeStiffness * dt));
            }

            tc.pos[0] = rootPos;
            tc.prev[0] = rootPos;

            for (int it = 0; it < iterations; it++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 a = tc.pos[i];
                    Vector3 b = tc.pos[i + 1];
                    Vector3 d = b - a;
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    float diff = (len - segLen) / len;
                    if (i == 0)
                    {
                        tc.pos[i + 1] -= d * diff;
                    }
                    else
                    {
                        tc.pos[i] += d * diff * 0.5f;
                        tc.pos[i + 1] -= d * diff * 0.5f;
                    }
                }
                // Кончик тянется к точке опоры
                float pin = tc.planted ? 0.6f : 0.15f;
                tc.pos[n - 1] = Vector3.Lerp(tc.pos[n - 1], tc.foot, pin);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float it = 1f - t;
            return it * it * a + 2f * it * t * b + t * t * c;
        }

        void RebuildMesh(Transform root)
        {
            // Меш строится в локальном пространстве этого объекта
            Transform space = transform;
            int ringVerts = radialSides + 1;
            int vPerTentacle = segments * ringVerts + 1;

            for (int t = 0; t < tentacleCount; t++)
            {
                var tc = tentacles[t];
                int vb = t * vPerTentacle;
                Vector3 prevNormal = root.up;

                for (int i = 0; i < segments; i++)
                {
                    Vector3 p = tc.pos[i];
                    Vector3 tangent = i < segments - 1 ? tc.pos[i + 1] - p : p - tc.pos[i - 1];
                    if (tangent.sqrMagnitude < 1e-8f) tangent = root.forward;
                    tangent.Normalize();

                    // параллельный перенос рамки — без перекручивания трубы
                    Vector3 nrm = Vector3.ProjectOnPlane(prevNormal, tangent);
                    if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.ProjectOnPlane(root.forward, tangent);
                    nrm.Normalize();
                    prevNormal = nrm;
                    Vector3 bin = Vector3.Cross(tangent, nrm);

                    float u = i / (float)(segments - 1);
                    float radius = Mathf.Lerp(rootRadius, tipRadius, Mathf.Pow(u, 0.8f));

                    for (int s = 0; s <= radialSides; s++)
                    {
                        float a = s / (float)radialSides * Mathf.PI * 2f;
                        Vector3 dir = nrm * Mathf.Cos(a) + bin * Mathf.Sin(a);
                        // присоски снизу делают трубу чуть приплюснутой
                        float flat = 1f - 0.18f * Mathf.Max(0f, -Mathf.Cos(a));
                        Vector3 wp = p + dir * radius * flat;
                        verts[vb + i * ringVerts + s] = space.InverseTransformPoint(wp);
                        normals[vb + i * ringVerts + s] = space.InverseTransformDirection(dir);
                    }
                }

                Vector3 last = tc.pos[segments - 1];
                Vector3 tipDir = (last - tc.pos[segments - 2]).normalized;
                verts[vb + segments * ringVerts] = space.InverseTransformPoint(last + tipDir * tipRadius);
                normals[vb + segments * ringVerts] = space.InverseTransformDirection(tipDir);
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }
    }
}
