using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Краб: бродит боком возле «дома», замирает, удирает от осьминогов — даже замаскированных.
    /// Убегающий краб — подсказка ищущему, что рядом кто-то прячется.
    /// </summary>
    public class Crab : MonoBehaviour, IScareable
    {
        public Material shellMaterial;
        public Color shellColor = new Color(0.85f, 0.35f, 0.2f);
        public float size = 1f;
        public float walkSpeed = 0.9f;
        public float fleeSpeed = 3.2f;
        public float homeRadius = 4f;
        public float noticeRadius = 1.3f;   // замечает любого осьминога ближе этого
        public float noticeMoving = 3.5f;   // и движущегося — ближе этого

        enum State { Idle, Walk, Flee }
        State state = State.Idle;
        float stateUntil, nextLook;
        Vector3 home, moveDir;
        Transform[] legs;
        Transform[] claws;
        float legPhase;

        void Start()
        {
            home = transform.position;
            BuildVisual();
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.25f * size;
            Snap(1f);
            SetIdle();
        }

        void BuildVisual()
        {
            float s = size;
            Part(PrimitiveType.Sphere, transform, new Vector3(0f, 0.09f, 0f) * s, new Vector3(0.36f, 0.14f, 0.26f) * s);
            // глаза на стебельках
            for (int e = -1; e <= 1; e += 2)
            {
                Part(PrimitiveType.Cylinder, transform, new Vector3(0.04f * e, 0.17f, 0.1f) * s, new Vector3(0.015f, 0.04f, 0.015f) * s);
                Part(PrimitiveType.Sphere, transform, new Vector3(0.04f * e, 0.22f, 0.1f) * s, Vector3.one * 0.03f * s, new Color(0.05f, 0.05f, 0.05f));
            }

            legs = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                int side = i < 3 ? -1 : 1;
                float z = (i % 3 - 1) * 0.08f;
                var pivot = new GameObject("Leg").transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(0.15f * side, 0.08f, z) * s;
                Part(PrimitiveType.Cube, pivot, new Vector3(0.09f * side, -0.03f, 0f) * s, new Vector3(0.18f, 0.025f, 0.025f) * s);
                legs[i] = pivot;
            }

            claws = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var pivot = new GameObject("Claw").transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(0.12f * side, 0.1f, 0.13f) * s;
                Part(PrimitiveType.Sphere, pivot, new Vector3(0.03f * side, 0f, 0.07f) * s, new Vector3(0.08f, 0.05f, 0.11f) * s);
                claws[i] = pivot;
            }
        }

        GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale)
        {
            return Part(type, parent, pos, scale, shellColor);
        }

        GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (shellMaterial != null) r.sharedMaterial = shellMaterial;
            var b = new MaterialPropertyBlock();
            b.SetColor("_BaseColor", color);
            r.SetPropertyBlock(b);
            return go;
        }

        public void Scare(Vector3 from)
        {
            Vector3 away = transform.position - from;
            away.y = 0f;
            Flee(away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere);
        }

        void Flee(Vector3 away)
        {
            away = Vector3.ProjectOnPlane(away, transform.up).normalized;
            state = State.Flee;
            stateUntil = Time.time + Random.Range(1.2f, 2f);
            moveDir = away;
        }

        void SetIdle()
        {
            state = State.Idle;
            stateUntil = Time.time + Random.Range(1f, 3.5f);
        }

        void SetWalk()
        {
            state = State.Walk;
            stateUntil = Time.time + Random.Range(1f, 2.5f);
            Vector3 toHome = home - transform.position;
            toHome.y = 0f;
            Vector3 side = transform.right * (Random.value < 0.5f ? -1f : 1f);
            moveDir = toHome.magnitude > homeRadius ? toHome.normalized : side;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (state != State.Flee && Time.time >= nextLook)
            {
                nextLook = Time.time + 0.2f;
                foreach (var o in OctopusController.All)
                {
                    Vector3 d = transform.position - o.transform.position;
                    float m = d.magnitude;
                    if (m < noticeRadius || (m < noticeMoving && o.Speed > 1f)) { Flee(d); break; }
                }
            }

            if (Time.time >= stateUntil)
            {
                if (state == State.Idle) SetWalk(); else SetIdle();
            }

            bool moving = state != State.Idle;
            if (moving)
            {
                float speed = state == State.Flee ? fleeSpeed : walkSpeed;
                Vector3 step = moveDir * speed * dt;
                if (Physics.Raycast(transform.position + transform.up * 0.1f, moveDir, out RaycastHit wall, 0.35f * size, ~0, QueryTriggerInteraction.Ignore)
                    && wall.normal.y < 0.6f)
                {
                    moveDir = Vector3.Reflect(moveDir, wall.normal);
                    moveDir.y = 0f;
                    moveDir.Normalize();
                }
                else transform.position += step;

                // Крабы ходят боком: поворачиваемся так, чтобы бок смотрел по ходу
                Vector3 fwd = Vector3.Cross(moveDir, transform.up);
                if (fwd.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd, transform.up), dt * 5f);
                legPhase += dt * speed * 18f;
            }
            Snap(dt * 10f);

            // ноги и клешни
            for (int i = 0; i < legs.Length; i++)
            {
                float a = moving ? Mathf.Sin(legPhase + i * 1.3f) * 25f : 0f;
                int side = i < 3 ? -1 : 1;
                legs[i].localRotation = Quaternion.Euler(0f, a * 0.5f, a * side);
            }
            for (int i = 0; i < claws.Length; i++)
                claws[i].localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 2f + i) * 10f, 0f, 0f);
        }

        void Snap(float k)
        {
            Vector3 from = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null)
            {
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(p.y, hit.point.y, Mathf.Clamp01(k));
                transform.position = p;
                Quaternion align = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
                transform.rotation = Quaternion.Slerp(transform.rotation, align, Mathf.Clamp01(k * 0.5f));
            }
        }
    }
}
