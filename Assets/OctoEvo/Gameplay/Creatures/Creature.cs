using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OctoEvo
{
    /// <summary>
    /// Живое существо рифа. Простая машина состояний:
    /// гуляет → замечает хищника (зависит от маскировки игрока) → удирает → успокаивается.
    /// Схваченное — бьётся, пока его не съедят. В чернильном облаке теряет ориентацию.
    /// Существа не имеют коллайдеров: осьминог ищет их через общий список <see cref="All"/>.
    /// </summary>
    public class Creature : MonoBehaviour
    {
        public enum State { Wander, Flee, Confused, Grabbed }

        public static readonly List<Creature> All = new List<Creature>();

        public CreatureDefinition Def { get; private set; }
        public float Length { get; private set; }
        /// <summary>Масса в «единицах осьминога 10 см»: (L / 0.1)³.</summary>
        public float Mass => Mathf.Pow(Length / 0.1f, 3f);
        public float Radius => Length * 0.45f;
        public State Current { get; private set; }
        public Vector3 Velocity { get; private set; }
        public bool IsAvailable => isActiveAndEnabled && Current != State.Grabbed;
        public SpawnDirector Director { get; private set; }

        Transform visual;
        Renderer[] renderers;
        Transform[] legs;
        float[] legYaw;
        Transform[] claws;
        Material mat; // экземпляр материала этой особи
        Color back, belly, pattern;
        float swimPhase;
        float stateUntil;
        float nextDecision;
        float nextGroundProbe;
        float groundY = float.MinValue;
        Vector3 groundNormal = Vector3.up;
        Vector3 home;
        Vector3 wanderTarget;
        Vector3 fleeFrom;
        float struggle;
        float nextWhoosh;
        float restUntil;

        static readonly int IdBase = Shader.PropertyToID("_BaseColor");
        static readonly int IdBelly = Shader.PropertyToID("_BellyColor");
        static readonly int IdPattern = Shader.PropertyToID("_PatternColor");
        static readonly int IdPatternType = Shader.PropertyToID("_PatternType");
        static readonly int IdPatternScale = Shader.PropertyToID("_PatternScale");
        static readonly int IdHalfHeight = Shader.PropertyToID("_HalfHeight");
        static readonly int IdEyePos = Shader.PropertyToID("_EyePos");
        static readonly int IdEyeRadius = Shader.PropertyToID("_EyeRadius");
        static readonly int IdSmooth = Shader.PropertyToID("_Smoothness");
        static readonly int IdTrans = Shader.PropertyToID("_Translucency");
        static readonly int IdPhase = Shader.PropertyToID("_SwimPhase");
        static readonly int IdAmp = Shader.PropertyToID("_SwimAmp");
        static readonly int IdTint = Shader.PropertyToID("_Tint");
        bool textured;
        Color tint = Color.white;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Init(CreatureDefinition def, float length, Material material, SpawnDirector director, Vector3 position, Quaternion rotation)
        {
            Director = director;
            Length = length;
            transform.SetPositionAndRotation(position, rotation);
            home = position;
            Velocity = rotation * Vector3.forward * def.cruiseSpeed * length * 0.5f;
            Current = State.Wander;
            struggle = 0f;
            restUntil = 0f;
            groundY = float.MinValue;
            nextGroundProbe = 0f;
            nextDecision = 0f;
            swimPhase = Random.value * 10f;

            if (Def != def || visual == null) BuildVisual(def, material);
            Def = def;

            float vis;
            if (textured)
            {
                vis = length * def.modelScale;
                visual.localPosition = Vector3.zero;
            }
            else
            {
                vis = def.body == CreatureBody.Crab ? length * 2f : length;
                visual.localPosition = def.body == CreatureBody.Crab ? new Vector3(0f, vis * 0.1f, 0f)
                    : (def.habitat == CreatureHabitat.Bottom ? new Vector3(0f, vis * def.halfHeight * 0.8f, 0f) : Vector3.zero);
            }
            visual.localScale = Vector3.one * vis;

            // У каждой особи окрас немного свой
            float jitter = Random.Range(-0.06f, 0.06f);
            back = Shift(def.backColor, jitter);
            belly = Shift(def.bellyColor, jitter * 0.5f);
            pattern = Shift(def.patternColor, jitter);
            float tj = Random.Range(-0.08f, 0.08f);
            tint = new Color(1f + tj, 1f + tj * 0.5f, 1f - tj * 0.3f, 1f);
            ApplyProperties(0f);

            foreach (var r in renderers) r.shadowCastingMode = length > 0.12f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            PickWanderTarget();
        }

        static Color Shift(Color c, float j) => new Color(Mathf.Clamp01(c.r + j), Mathf.Clamp01(c.g + j * 0.7f), Mathf.Clamp01(c.b - j * 0.5f), 1f);

        void BuildVisual(CreatureDefinition def, Material material)
        {
            if (visual != null) Destroy(visual.gameObject);
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            var list = new List<Renderer>();
            legs = null;
            claws = null;
            textured = def.modelMesh != null && def.modelMaterial != null;
            if (textured)
            {
                var model = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer));
                model.transform.SetParent(visual, false);
                model.GetComponent<MeshFilter>().sharedMesh = def.modelMesh;
                var mr = model.GetComponent<MeshRenderer>();
                mr.sharedMaterial = def.modelMaterial;
                list.Add(mr);
                renderers = list.ToArray();
                MakeMaterial(def.modelMaterial);
                return;
            }

            var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(visual, false);
            body.GetComponent<MeshFilter>().sharedMesh = CreatureMeshes.Get(def.body, def.halfHeight, def.halfWidth);
            var br = body.GetComponent<MeshRenderer>();
            br.sharedMaterial = material;
            list.Add(br);

            legs = null;
            claws = null;
            if (def.body == CreatureBody.Crab)
            {
                legs = new Transform[8];
                legYaw = new float[8];
                for (int i = 0; i < 8; i++)
                {
                    int side = i < 4 ? -1 : 1;
                    float z = Mathf.Lerp(0.08f, -0.12f, (i % 4) / 3f);
                    var pivot = new GameObject("Leg" + i).transform;
                    pivot.SetParent(visual, false);
                    pivot.localPosition = new Vector3(0.2f * side, 0f, z);
                    legYaw[i] = side > 0 ? -15f * (i % 4 - 1.5f) : 180f + 15f * (i % 4 - 1.5f);
                    pivot.localRotation = Quaternion.Euler(0f, legYaw[i], 0f);
                    var leg = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    leg.transform.SetParent(pivot, false);
                    leg.transform.localScale = Vector3.one * 0.34f;
                    leg.GetComponent<MeshFilter>().sharedMesh = CreatureMeshes.CrabLeg;
                    var lr = leg.GetComponent<MeshRenderer>();
                    lr.sharedMaterial = material;
                    list.Add(lr);
                    legs[i] = pivot;
                }
                claws = new Transform[2];
                for (int i = 0; i < 2; i++)
                {
                    int side = i == 0 ? -1 : 1;
                    var pivot = new GameObject("Claw" + i).transform;
                    pivot.SetParent(visual, false);
                    pivot.localPosition = new Vector3(0.16f * side, 0.02f, 0.2f);
                    var claw = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    claw.transform.SetParent(pivot, false);
                    claw.transform.localPosition = new Vector3(0.03f * side, 0f, 0.07f);
                    claw.transform.localScale = new Vector3(0.12f, 0.12f, 0.2f);
                    claw.GetComponent<MeshFilter>().sharedMesh = CreatureMeshes.CrabClaw;
                    var cr = claw.GetComponent<MeshRenderer>();
                    cr.sharedMaterial = material;
                    list.Add(cr);
                    claws[i] = pivot;
                }
            }
            renderers = list.ToArray();
            MakeMaterial(material);
        }

        /// <summary>
        /// У каждой особи свой экземпляр материала (а не MaterialPropertyBlock): так объект остаётся
        /// в SRP Batcher и получает от URP данные о главном источнике света. С блоком свойств существа были чёрными.
        /// </summary>
        void MakeMaterial(Material source)
        {
            if (mat != null) Destroy(mat);
            mat = new Material(source) { name = source.name + " (creature)" };
            foreach (var r in renderers) r.sharedMaterial = mat;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        void ApplyProperties(float swimAmp)
        {
            if (mat == null) return;
            var def = Def;
            mat.SetColor(IdBase, back);
            mat.SetColor(IdBelly, belly);
            mat.SetColor(IdPattern, pattern);
            mat.SetFloat(IdPatternType, def.pattern);
            mat.SetFloat(IdPatternScale, def.patternScale);
            mat.SetFloat(IdSmooth, def.smoothness);
            mat.SetFloat(IdTrans, def.translucency);
            mat.SetFloat(IdPhase, swimPhase);
            if (textured)
            {
                mat.SetColor(IdTint, tint);
                mat.SetFloat(IdAmp, swimAmp * def.swimWave / 0.05f);
                return;
            }
            if (def.body == CreatureBody.Crab)
            {
                mat.SetFloat(IdHalfHeight, 0.08f);
                mat.SetVector(IdEyePos, new Vector4(0.045f, 0.075f, 0.16f, 0f));
                mat.SetFloat(IdEyeRadius, 0.018f);
                mat.SetFloat(IdAmp, 0f);
            }
            else
            {
                mat.SetFloat(IdHalfHeight, def.halfHeight);
                float eyeZ = def.body == CreatureBody.Shrimp ? 0.36f : 0.37f;
                if (def.body == CreatureBody.Flatfish)
                {
                    // оба глаза сверху
                    mat.SetVector(IdEyePos, new Vector4(def.halfWidth * 0.22f, def.halfHeight * 0.75f, 0.33f, 0f));
                    mat.SetFloat(IdEyeRadius, 0.022f);
                    mat.SetFloat(IdAmp, 0f);
                }
                else
                {
                    mat.SetVector(IdEyePos, new Vector4(def.halfWidth * 0.75f, def.halfHeight * 0.25f, eyeZ, 0f));
                    mat.SetFloat(IdEyeRadius, def.halfHeight * 0.22f);
                    mat.SetFloat(IdAmp, swimAmp);
                }
            }
        }

        // ---------------- Внешнее управление

        public void SetGrabbed(bool grabbed)
        {
            if (grabbed)
            {
                Current = State.Grabbed;
                Velocity = Vector3.zero;
            }
            else
            {
                Current = State.Flee;
                stateUntil = Time.time + 3f;
            }
        }

        /// <summary>Чернила: существо теряет цель и мечется.</summary>
        public void Confuse(float seconds)
        {
            if (Current == State.Grabbed) return;
            Current = State.Confused;
            stateUntil = Time.time + seconds;
            nextDecision = 0f;
        }

        /// <summary>Пока схвачено — бьётся (видно по хвосту и дрожанию).</summary>
        public void Struggle(float amount) => struggle = amount;

        // ---------------- Поведение

        void Update()
        {
            if (Def == null) return;
            float dt = Time.deltaTime;

            if (Current == State.Grabbed)
            {
                swimPhase += dt * (14f + struggle * 10f);
                AnimateLegs(1f + struggle, dt);
                ApplyProperties(0.12f * (0.5f + struggle));
                return;
            }

            ProbeGround();
            Perceive();

            Vector3 desired = DesiredDirection();
            float speed = TargetSpeed();
            Vector3 targetVel = desired * speed;

            float accel = Mathf.Max(0.5f, Def.turnRate * speed);
            Velocity = Vector3.MoveTowards(Velocity, targetVel, accel * dt);
            Velocity = Avoid(Velocity);

            Vector3 pos = transform.position + Velocity * dt;
            pos = ClampToHabitat(pos);
            transform.position = pos;
            Orient(dt);

            float spd = Velocity.magnitude;
            swimPhase += dt * (3f + spd / Mathf.Max(0.01f, Length) * 1.6f);
            AnimateLegs(spd / Mathf.Max(0.01f, Length), dt);
            ApplyProperties(Def.body == CreatureBody.Shrimp ? 0.015f + Mathf.Clamp01(spd / (Def.fleeSpeed * Length)) * 0.04f
                                                           : 0.045f + Mathf.Clamp01(spd / (Def.fleeSpeed * Length)) * 0.05f);
        }

        void Perceive()
        {
            if (Current == State.Confused)
            {
                if (Time.time > stateUntil) Current = State.Wander;
                return;
            }
            if (Director == null || Director.Player == null) return;

            Vector3 playerPos = Director.Player.transform.position;
            // Крупная рыба проплывает рядом — низкое «вуум»
            if (Length > 0.6f && Time.time > nextWhoosh && (transform.position - playerPos).sqrMagnitude < Length * Length * 9f && Velocity.sqrMagnitude > 0.25f)
            {
                nextWhoosh = Time.time + 10f;
                AudioDirector.Play(Sfx.BigFish, transform.position, Mathf.Clamp01(Length / 2f) * 0.6f + 0.4f, 1.2f - Mathf.Clamp01(Length / 3f) * 0.4f);
            }
            float playerLen = Director.PlayerLength;
            float ratio = Length / Mathf.Max(0.001f, playerLen);
            bool threatened = ratio <= 0.85f;

            if (threatened && !InkCloud.Contains(playerPos))
            {
                // Дистанция испуга — в длинах осьминога: на виду ~8 длин, замаскированного и неподвижного
                // подпускают почти вплотную (ближе броска). Беспечные (улитки) почти не реагируют.
                float c = Director.PlayerConspicuity;
                float notice = playerLen * Mathf.Lerp(0.7f, 8f, c) * Mathf.Lerp(0.15f, 1.4f, Def.wariness);
                notice = Mathf.Min(notice, Def.noticeDistance * 4f);
                float d = Vector3.Distance(transform.position, playerPos);
                if (d < notice)
                {
                    if (Current != State.Flee)
                    {
                        nextDecision = 0f;
                        if (Def.body == CreatureBody.Crab) AudioDirector.Play(Sfx.Crab, transform.position, 0.6f);
                        else AudioDirector.Play(Sfx.FishScare, transform.position, Def.schooling ? 0.8f : 0.5f);
                    }
                    Current = State.Flee;
                    fleeFrom = playerPos;
                    stateUntil = Time.time + Random.Range(2f, 3.5f);
                    return;
                }
            }

            if (Current == State.Flee && Time.time > stateUntil)
            {
                Current = State.Wander;
                home = transform.position; // успокоилось там, куда убежало
                PickWanderTarget();
            }
        }

        float TargetSpeed()
        {
            float bl = Length;
            if (Current == State.Wander && Time.time < restUntil) return 0f;
            switch (Current)
            {
                case State.Flee: return Mathf.Min(Def.fleeSpeed * bl, Def.maxSpeed);
                case State.Confused: return Mathf.Min(Def.cruiseSpeed * bl * 1.5f, Def.maxSpeed * 0.6f);
                default: return Mathf.Min(Def.cruiseSpeed * bl, Def.maxSpeed * 0.5f);
            }
        }

        Vector3 DesiredDirection()
        {
            Vector3 pos = transform.position;
            Vector3 dir;

            if (Current == State.Flee)
            {
                dir = pos - fleeFrom;
                if (Def.habitat == CreatureHabitat.Bottom) dir = Vector3.ProjectOnPlane(dir, groundNormal);
                else dir += Vector3.up * 0.3f * Mathf.Sin(GetInstanceHash() + Time.time);
                if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
                dir.Normalize();
            }
            else
            {
                if (Time.time > nextDecision || (wanderTarget - pos).sqrMagnitude < Length * Length * 4f)
                    PickWanderTarget();
                dir = (wanderTarget - pos).normalized;
            }

            if (Def.schooling && Current != State.Grabbed) dir = (dir + School(pos) * 0.8f).normalized;
            return dir;
        }

        int hash;
        float GetInstanceHash()
        {
            if (hash == 0) hash = Random.Range(1, 9999);
            return hash * 0.137f;
        }

        Vector3 School(Vector3 pos)
        {
            Vector3 center = Vector3.zero, align = Vector3.zero, separate = Vector3.zero;
            int n = 0;
            float radius = Length * 8f;
            float sep = Length * 1.6f;
            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i];
                if (o == this || o.Def != Def || o.Current == State.Grabbed) continue;
                Vector3 d = o.transform.position - pos;
                float m = d.sqrMagnitude;
                if (m > radius * radius) continue;
                center += o.transform.position;
                align += o.Velocity;
                if (m < sep * sep && m > 1e-8f) separate -= d / m * Length;
                n++;
            }
            if (n == 0) return Vector3.zero;
            center = (center / n - pos).normalized;
            align = align.sqrMagnitude > 1e-6f ? align.normalized : Vector3.zero;
            // Убегающая стая держится плотнее
            float cohesion = Current == State.Flee ? 0.4f : 0.6f;
            return center * cohesion + align * 0.8f + separate * 1.5f;
        }

        void PickWanderTarget()
        {
            float range = Def.habitat == CreatureHabitat.Bottom ? 2.5f : (Def.habitat == CreatureHabitat.NearBottom ? 4f : 7f);
            range *= Mathf.Clamp(Mathf.Sqrt(Length / 0.1f), 0.6f, 3f);
            Vector2 c = Random.insideUnitCircle * range;
            Vector3 t = home + new Vector3(c.x, 0f, c.y);
            if (Director != null) t = Director.ClampToArea(t);

            float gy = SampleGround(t);
            float top = SurfaceY - 0.5f;
            switch (Def.habitat)
            {
                case CreatureHabitat.Bottom: t.y = gy; break;
                case CreatureHabitat.NearBottom: t.y = Mathf.Min(top, gy + Random.Range(Def.hoverHeight.x, Def.hoverHeight.y) * Mathf.Max(1f, Length / 0.2f)); break;
                default: t.y = Mathf.Lerp(Mathf.Min(top, gy + 0.4f), top, Random.value * Random.value); break;
            }
            t.y = Mathf.Min(t.y, SurfaceY - Def.minDepth);
            wanderTarget = t;
            nextDecision = Time.time + Random.Range(3f, 8f);
            if (Def.restful > 0f && Random.value < Def.restful)
            {
                restUntil = Time.time + Random.Range(4f, 14f);
                nextDecision = restUntil + Random.Range(2f, 5f);
            }
        }

        static float SurfaceY => UnderwaterEnvironment.Instance != null ? UnderwaterEnvironment.Instance.surfaceY : 10f;

        static float SampleGround(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, SurfaceY, p.z), Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return p.y - 2f;
        }

        void ProbeGround()
        {
            if (Def.habitat == CreatureHabitat.Bottom)
            {
                // Краб ходит по дну каждый кадр
                Vector3 from = transform.position + groundNormal * Length * 1.5f;
                if (Physics.Raycast(from, -groundNormal, out RaycastHit hit, Length * 4f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null)
                {
                    groundY = hit.point.y;
                    groundNormal = Vector3.Slerp(groundNormal, hit.normal, 0.3f).normalized;
                    transform.position = hit.point;
                }
                else if (Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                {
                    groundY = hit.point.y;
                    groundNormal = hit.normal;
                    transform.position = hit.point;
                }
                return;
            }

            if (Time.time < nextGroundProbe) return;
            nextGroundProbe = Time.time + 0.3f;
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit h, 50f, ~0, QueryTriggerInteraction.Ignore))
                groundY = h.point.y;
        }

        Vector3 Avoid(Vector3 v)
        {
            float spd = v.magnitude;
            if (spd < 1e-4f) return v;
            Vector3 dir = v / spd;
            float dist = spd * 0.6f + Length;
            Vector3 origin = transform.position;
            if (Def.habitat == CreatureHabitat.Bottom) origin += groundNormal * Length * 0.4f;
            if (Physics.SphereCast(origin, Length * 0.25f, dir, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody == null)
            {
                Vector3 slide = Vector3.ProjectOnPlane(dir, hit.normal);
                if (slide.sqrMagnitude < 0.01f) slide = Vector3.Cross(hit.normal, Vector3.up);
                Vector3 nd = (slide.normalized + hit.normal * 0.6f).normalized;
                if (Def.habitat == CreatureHabitat.Bottom) nd = Vector3.ProjectOnPlane(nd, groundNormal).normalized;
                // при лобовом столкновении — новая цель
                if (Vector3.Dot(dir, hit.normal) < -0.8f) nextDecision = 0f;
                return nd * spd;
            }
            return v;
        }

        Vector3 ClampToHabitat(Vector3 p)
        {
            if (Def.habitat == CreatureHabitat.Bottom) return p;
            float top = SurfaceY - Mathf.Max(0.3f, Def.minDepth);
            p.y = Mathf.Min(p.y, top);
            if (groundY > float.MinValue) p.y = Mathf.Max(p.y, groundY + Length * 0.5f);
            return p;
        }

        void Orient(float dt)
        {
            Vector3 v = Velocity;
            if (v.sqrMagnitude < 1e-6f) return;
            Quaternion target;
            if (Def.habitat == CreatureHabitat.Bottom)
            {
                // Краб ходит боком
                Vector3 move = Vector3.ProjectOnPlane(v, groundNormal);
                if (move.sqrMagnitude < 1e-6f) return;
                Vector3 fwd;
                if (Def.body == CreatureBody.Crab)
                {
                    fwd = Vector3.Cross(move.normalized, groundNormal);
                    if (Current == State.Flee && Vector3.Dot(fwd, fleeFrom - transform.position) < 0f) fwd = -fwd; // клешнями к угрозе
                }
                else fwd = move.normalized; // улитки и камбалы ползут головой вперёд
                target = Quaternion.LookRotation(fwd, groundNormal);
            }
            else
            {
                Vector3 look = v;
                // Креветка удирает хвостом вперёд
                if (Def.body == CreatureBody.Shrimp && Current == State.Flee) look = -v;
                // рыбы держатся почти горизонтально
                look.y *= 0.6f;
                target = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-Def.turnRate * 2f * dt));
        }

        void AnimateLegs(float bodyLengthsPerSec, float dt)
        {
            if (legs == null) return;
            float t = swimPhase * 1.5f;
            float amp = Mathf.Clamp(bodyLengthsPerSec * 6f, 0f, 25f);
            for (int i = 0; i < legs.Length; i++)
            {
                float ph = t + (i % 2 == 0 ? 0f : Mathf.PI) + i * 0.4f;
                legs[i].localRotation = Quaternion.Euler(0f, legYaw[i], Mathf.Sin(ph) * amp - 10f);
            }
            if (claws != null)
            {
                float open = Current == State.Flee || Current == State.Grabbed ? 25f : 5f;
                for (int i = 0; i < claws.Length; i++)
                    claws[i].localRotation = Quaternion.Euler(-open + Mathf.Sin(Time.time * 3f + i) * 4f, (i == 0 ? 1 : -1) * 15f, 0f);
            }
        }
    }
}
