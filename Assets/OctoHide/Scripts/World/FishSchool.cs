using System.Collections.Generic;
using UnityEngine;

namespace OctoHide
{
    /// <summary>Что-то, что можно напугать (камнем, осьминогом).</summary>
    public interface IScareable
    {
        void Scare(Vector3 from);
    }

    /// <summary>
    /// Стайка рыб: держатся вместе, огибают скалы, шарахаются от осьминогов и камней.
    /// Внешний вид собирается из примитивов при старте — без внешних ассетов.
    /// </summary>
    public class FishSchool : MonoBehaviour
    {
        public int count = 12;
        public Material material;
        public Color tint = new Color(1f, 0.85f, 0.3f);
        public float size = 1f;
        public Vector3 wanderRadius = new Vector3(8f, 1.2f, 6f);
        public float minHeight = 0.8f;
        public float maxHeight = 5f;
        public float cruiseSpeed = 1.6f;
        public float fleeSpeed = 5f;
        public float fleeRadius = 2.8f;

        static readonly List<FishSchool> Schools = new List<FishSchool>();

        class Fish
        {
            public Transform t, tail;
            public Vector3 vel, avoid, scare;
            public float phase, groundY;
            public int probeFrame;
        }

        readonly List<Fish> fish = new List<Fish>();
        Vector3 home;
        float seed;
        MaterialPropertyBlock mpb;

        void OnEnable() => Schools.Add(this);
        void OnDisable() => Schools.Remove(this);

        public static void ScareAll(Vector3 point, float radius)
        {
            foreach (var s in Schools) s.Scare(point, radius);
        }

        void Start()
        {
            home = transform.position;
            seed = Random.value * 100f;
            mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", tint);
            for (int i = 0; i < count; i++) fish.Add(Spawn(i));
        }

        Fish Spawn(int i)
        {
            var root = new GameObject("Fish_" + i).transform;
            root.SetParent(transform, false);
            root.position = home + Random.insideUnitSphere * 1.5f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root, false);
            body.transform.localScale = new Vector3(0.07f, 0.13f, 0.3f) * size;
            Paint(body);

            var tailPivot = new GameObject("TailPivot").transform;
            tailPivot.SetParent(root, false);
            tailPivot.localPosition = new Vector3(0f, 0f, -0.13f * size);
            var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(tail.GetComponent<Collider>());
            tail.transform.SetParent(tailPivot, false);
            tail.transform.localPosition = new Vector3(0f, 0f, -0.05f * size);
            tail.transform.localScale = new Vector3(0.01f, 0.12f, 0.1f) * size;
            Paint(tail);

            return new Fish
            {
                t = root,
                tail = tailPivot,
                vel = Random.onUnitSphere * cruiseSpeed,
                phase = Random.value * 10f,
                groundY = 0f,
                probeFrame = Random.Range(0, 6)
            };
        }

        void Paint(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (material != null) r.sharedMaterial = material;
            r.SetPropertyBlock(mpb);
        }

        public void Scare(Vector3 point, float radius)
        {
            foreach (var f in fish)
            {
                Vector3 d = f.t.position - point;
                if (d.magnitude < radius) f.scare = d.normalized * fleeSpeed;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float t = Time.time * 0.15f + seed;
            Vector3 center = home + new Vector3(Mathf.Sin(t * 1.1f) * wanderRadius.x, Mathf.Sin(t * 1.7f) * wanderRadius.y, Mathf.Cos(t * 0.8f) * wanderRadius.z);

            Vector3 avgVel = Vector3.zero;
            foreach (var f in fish) avgVel += f.vel;
            avgVel /= Mathf.Max(1, fish.Count);

            for (int i = 0; i < fish.Count; i++)
            {
                var f = fish[i];
                Vector3 p = f.t.position;
                Vector3 steer = (center - p) * 0.6f + (avgVel - f.vel) * 0.5f;

                // разойтись с соседями
                for (int j = 0; j < fish.Count; j++)
                {
                    if (i == j) continue;
                    Vector3 d = p - fish[j].t.position;
                    float m = d.magnitude;
                    if (m < 0.45f * size && m > 1e-4f) steer += d / (m * m) * 0.05f;
                }

                // бежать от осьминогов
                bool fleeing = f.scare.sqrMagnitude > 0.5f;
                foreach (var o in OctopusController.All)
                {
                    Vector3 d = p - o.transform.position;
                    float m = d.magnitude;
                    if (m < fleeRadius)
                    {
                        steer += d.normalized * (fleeRadius - m) * 4f;
                        fleeing = true;
                    }
                }
                steer += f.scare * 2f;
                f.scare = Vector3.MoveTowards(f.scare, Vector3.zero, dt * 3f);

                // препятствия и высота — проверяем раз в несколько кадров
                if ((Time.frameCount + f.probeFrame) % 6 == 0)
                {
                    f.avoid = Vector3.zero;
                    Vector3 dir = f.vel.sqrMagnitude > 0.01f ? f.vel.normalized : f.t.forward;
                    if (Physics.SphereCast(p, 0.15f, dir, out RaycastHit hit, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                        f.avoid = hit.normal * 6f;
                    if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit g, 20f, ~0, QueryTriggerInteraction.Ignore))
                        f.groundY = g.point.y;
                }
                steer += f.avoid;
                float h = p.y - f.groundY;
                if (h < minHeight) steer += Vector3.up * (minHeight - h) * 6f;
                if (h > maxHeight) steer += Vector3.down * (h - maxHeight) * 2f;

                f.vel += steer * dt;
                float maxSpeed = fleeing ? fleeSpeed : cruiseSpeed;
                float speed = Mathf.Clamp(f.vel.magnitude, cruiseSpeed * 0.5f, maxSpeed);
                f.vel = f.vel.normalized * speed;

                f.t.position = p + f.vel * dt;
                f.t.rotation = Quaternion.Slerp(f.t.rotation, Quaternion.LookRotation(f.vel, Vector3.up), dt * 6f);
                f.phase += dt * speed * 6f;
                f.tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(f.phase) * 30f, 0f);
            }
        }
    }
}
