using UnityEngine;

namespace OctoEvo
{
    public enum OctopusPose
    {
        Normal = 0,   // обычная
        Rock = 1,     // камень: щупальца поджаты
        Flat = 2,     // распластаться
        Reach = 3,    // вытянуться
        Coral = 4,    // коралл: щупальца вверх, как ветки
        Kelp = 5,     // водоросль: тело поднято на «стебле» и покачивается
        Starfish = 6, // морская звезда: плоско, щупальца лучами
        Snake = 7     // морская змея: щупальца собраны в «хвост»
    }

    /// <summary>
    /// Передвижение осьминога: ползание по любой поверхности (дно, стены, потолок) и свободное
    /// 3D-плавание в толще воды. Направление задаёт камера: смотришь вверх и жмёшь «вперёд» —
    /// отрываешься от дна и плывёшь; подплываешь к поверхности — снова ползёшь.
    /// Все дистанции и скорости масштабируются вместе с размером осьминога.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class OctopusMotor : MonoBehaviour
    {
        [Header("Ползание (при масштабе 1)")]
        public float crawlSpeed = 2.2f;
        public float acceleration = 10f;
        public float stickForce = 25f;
        public float surfaceProbe = 0.9f;
        public LayerMask surfaceMask = ~0;

        [Header("Плавание (при масштабе 1)")]
        public float swimSpeed = 3.2f;
        public float swimAcceleration = 6f;
        public float sinkAcceleration = 0.4f;
        public float waterDrag = 1.2f;
        public float jetImpulse = 7f;
        public float jetCooldown = 1.2f;
        [Tooltip("Насколько направление ввода должно смотреть «от поверхности», чтобы оторваться от неё")]
        public float detachThreshold = 0.5f;

        [Header("Поворот")]
        public float alignSpeed = 8f;

        [Header("Игрок")]
        [Tooltip("Управляется с этого устройства. У ботов и сетевых осьминогов — выключено.")]
        public bool isLocalPlayer = true;

        [Header("Ссылки")]
        public Transform cameraTransform;
        public Transform body;

        public bool IsGrounded { get; private set; }
        public bool IsSwimming => !IsGrounded;
        public Vector3 SurfaceNormal { get; private set; } = Vector3.up;
        public Collider SurfaceCollider { get; private set; }
        public Vector3 SurfacePoint { get; private set; }
        public OctopusPose Pose { get; private set; } = OctopusPose.Normal;
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public float Speed => Velocity.magnitude;
        /// <summary>Скорость относительно максимальной для текущего размера (0..1+).</summary>
        public float NormalizedSpeed => Mathf.Clamp01(Speed / Mathf.Max(0.01f, crawlSpeed * SpeedScale));
        public float JetCooldownLeft => Mathf.Max(0f, nextJetTime - Time.time);
        /// <summary>Текущий масштаб осьминога (1 = модель в исходном размере).</summary>
        public float Scale => transform.lossyScale.x;
        /// <summary>Скорости растут медленнее размера: большой плывёт быстрее, но «тяжелее».</summary>
        public float SpeedScale => Mathf.Sqrt(Mathf.Max(0.01f, Scale));

        /// <summary>Внешние системы (захват крупной добычи и т.п.) могут временно замедлить осьминога.</summary>
        public float externalSpeedMultiplier = 1f;

        public static readonly System.Collections.Generic.List<OctopusMotor> All = new System.Collections.Generic.List<OctopusMotor>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void SetPose(OctopusPose pose) => Pose = pose;

        /// <summary>Рывок по запросу других систем (чернила и т.п.).</summary>
        public void RequestJet(float multiplier = 1f, bool ignoreCooldown = false)
        {
            if (!ignoreCooldown && Time.time < nextJetTime) return;
            jetRequested = true;
            jetMultiplier = multiplier;
        }

        Rigidbody rb;
        SphereCollider sphere;
        Vector3 moveCrawl;   // ввод в плоскости поверхности
        Vector3 moveSwim;    // ввод в 3D по направлению камеры
        float inputForward;  // «вперёд» на стике: отрываемся от поверхности только плывя туда, куда смотрим
        float detachUntil;
        float nextJetTime;
        bool jetRequested;
        float jetMultiplier = 1f;
        Vector3 bodyBaseScale;
        Vector3 bodyBasePos;

        static readonly Vector3[] PoseScale =
        {
            new Vector3(1.00f, 1.00f, 1.00f), // Normal
            new Vector3(1.05f, 0.95f, 1.00f), // Rock
            new Vector3(1.35f, 0.45f, 1.35f), // Flat
            new Vector3(0.80f, 0.90f, 1.45f), // Reach
            new Vector3(1.10f, 0.70f, 1.10f), // Coral
            new Vector3(0.70f, 1.45f, 0.70f), // Kelp
            new Vector3(1.20f, 0.35f, 1.20f), // Starfish
            new Vector3(0.60f, 0.60f, 1.60f), // Snake
        };
        static readonly float[] PoseDrop = { 0f, -0.05f, -0.18f, 0f, -0.12f, 0.45f, -0.2f, -0.1f };
        static readonly float[] PoseSpeedMul = { 1f, 0.35f, 0.5f, 0.8f, 0.3f, 0.25f, 0.4f, 0.9f };
        static readonly OctopusPose[] TouchPoseCycle = { OctopusPose.Normal, OctopusPose.Rock, OctopusPose.Flat, OctopusPose.Starfish };

        /// <summary>На сколько тело сейчас поднято/опущено относительно обычного положения (в локальных единицах).</summary>
        public float BodyLift => body != null ? body.localPosition.y - bodyBasePos.y : 0f;

        float WaterSurfaceY => UnderwaterEnvironment.Instance != null ? UnderwaterEnvironment.Instance.surfaceY : float.MaxValue;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.None;
            rb.angularDamping = 10f;
            if (body != null)
            {
                bodyBaseScale = body.localScale;
                bodyBasePos = body.localPosition;
            }
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            if (isLocalPlayer)
            {
                Vector2 input = OctoInput.Move;
                ComputeMoveDirections(input);

                if (OctoInput.JetPressed && Time.time >= nextJetTime) jetRequested = true;

                int pose = OctoInput.PosePressed;
                if (pose >= 0) Pose = (OctopusPose)pose;
                if (OctoInput.PoseCyclePressed)
                {
                    int i = System.Array.IndexOf(TouchPoseCycle, Pose);
                    Pose = TouchPoseCycle[(i + 1) % TouchPoseCycle.Length];
                }
            }
            else
            {
                moveCrawl = Vector3.zero;
                moveSwim = Vector3.zero;
                inputForward = 0f;
            }

            AnimateBody();
        }

        void ComputeMoveDirections(Vector2 input)
        {
            inputForward = input.y;
            if (input.sqrMagnitude < 0.0001f)
            {
                moveCrawl = Vector3.zero;
                moveSwim = Vector3.zero;
                return;
            }
            Transform cam = cameraTransform != null ? cameraTransform : transform;

            // 3D: куда смотрит камера
            moveSwim = cam.forward * input.y + cam.right * input.x;
            moveSwim = Vector3.ClampMagnitude(moveSwim, 1f);

            // По поверхности: проекция на её плоскость
            Vector3 up = IsGrounded ? SurfaceNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, up);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(cam.up, up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);
            moveCrawl = Vector3.ClampMagnitude(fwd * input.y + right * input.x, 1f);
        }

        void FixedUpdate()
        {
            float s = Scale;
            float speedMul = PoseSpeedMul[(int)Pose] * externalSpeedMultiplier;

            // Оторваться от поверхности: ввод (по камере) смотрит прочь от неё
            if (IsGrounded && inputForward > 0.3f && moveSwim.sqrMagnitude > 0.04f && Vector3.Dot(moveSwim.normalized, SurfaceNormal) > detachThreshold)
            {
                IsGrounded = false;
                detachUntil = Time.time + 0.5f;
                rb.AddForce(SurfaceNormal * swimSpeed * SpeedScale * 0.4f, ForceMode.VelocityChange);
                if (Pose != OctopusPose.Normal) Pose = OctopusPose.Normal;
            }

            bool wasGrounded = IsGrounded;
            Vector3 vBefore = rb.linearVelocity;
            ProbeSurface(s);
            // Приземление после плавания — шлепок о дно
            if (!wasGrounded && IsGrounded && vBefore.magnitude > 0.8f * SpeedScale)
                AudioDirector.Play(Sfx.Land, transform.position, Mathf.Clamp01(vBefore.magnitude / (jetImpulse * SpeedScale)) * 0.8f);

            Vector3 v = rb.linearVelocity;

            if (jetRequested)
            {
                jetRequested = false;
                nextJetTime = Time.time + jetCooldown;
                detachUntil = Time.time + 0.4f;
                Transform cam = cameraTransform != null ? cameraTransform : transform;
                Vector3 dir = moveSwim.sqrMagnitude > 0.01f ? moveSwim.normalized : cam.forward;
                if (IsGrounded) dir = (dir + SurfaceNormal * 0.6f).normalized;
                rb.AddForce(dir * jetImpulse * jetMultiplier * SpeedScale, ForceMode.VelocityChange);
                AudioDirector.Play(Sfx.Jet, transform.position, Mathf.Clamp(jetMultiplier, 0.6f, 1.2f), 1.15f - Mathf.Clamp01(Scale) * 0.3f);
                jetMultiplier = 1f;
                IsGrounded = false;
                Pose = OctopusPose.Normal;
            }

            if (IsGrounded)
            {
                Vector3 n = SurfaceNormal;
                Vector3 tangentV = Vector3.ProjectOnPlane(v, n);
                Vector3 target = moveCrawl * crawlSpeed * SpeedScale * speedMul;
                tangentV = Vector3.MoveTowards(tangentV, target, acceleration * SpeedScale * Time.fixedDeltaTime);
                float normalV = Vector3.Dot(v, n);
                normalV = Mathf.MoveTowards(normalV, 0f, stickForce * Time.fixedDeltaTime);
                rb.linearVelocity = tangentV + n * normalV;
                rb.AddForce(-n * stickForce * Mathf.Max(0.3f, s), ForceMode.Acceleration);
            }
            else
            {
                // Свободное плавание: скорость тянется к направлению ввода, без ввода — медленно тонем
                Vector3 target = moveSwim * swimSpeed * SpeedScale * externalSpeedMultiplier;
                if (moveSwim.sqrMagnitude > 0.0001f)
                {
                    rb.linearVelocity = Vector3.MoveTowards(v, target, swimAcceleration * SpeedScale * Time.fixedDeltaTime);
                }
                else
                {
                    rb.AddForce(Vector3.down * sinkAcceleration * SpeedScale, ForceMode.Acceleration);
                    rb.linearVelocity *= 1f / (1f + waterDrag * Time.fixedDeltaTime);
                }
            }

            // Не выпрыгивать из воды
            float maxY = WaterSurfaceY - sphere.radius * s * 1.5f;
            if (rb.position.y > maxY)
            {
                rb.position = new Vector3(rb.position.x, maxY, rb.position.z);
                if (rb.linearVelocity.y > 0f) rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            }

            AlignRotation();
        }

        void ProbeSurface(float s)
        {
            if (Time.time < detachUntil)
            {
                IsGrounded = false;
                SurfaceCollider = null;
                return;
            }

            float r = sphere.radius * s;
            Vector3 origin = transform.position;
            bool found = false;
            RaycastHit best = default;

            if (IsGrounded)
            {
                // 1) Стена по ходу движения — переползаем на неё
                if (moveCrawl.sqrMagnitude > 0.01f &&
                    Physics.SphereCast(origin, r * 0.6f, moveCrawl.normalized, out RaycastHit wall, r + 0.25f * s, surfaceMask, QueryTriggerInteraction.Ignore)
                    && wall.collider.attachedRigidbody != rb)
                {
                    best = wall;
                    found = true;
                }

                // 2) Поверхность под собой
                if (!found)
                {
                    Vector3[] dirs = { -transform.up, -SurfaceNormal, Vector3.down };
                    foreach (var d in dirs)
                    {
                        if (Physics.SphereCast(origin, r * 0.8f, d, out RaycastHit hit, surfaceProbe * s, surfaceMask, QueryTriggerInteraction.Ignore)
                            && hit.collider.attachedRigidbody != rb)
                        {
                            best = hit;
                            found = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                // В плавании прилипаем только к близкой поверхности, если плывём к ней или стоим на месте
                Vector3 vel = rb.linearVelocity;
                Vector3 dir = vel.sqrMagnitude > 0.04f ? vel.normalized : Vector3.down;
                bool towards = moveSwim.sqrMagnitude < 0.01f || Vector3.Dot(moveSwim, dir) > 0.2f;
                if (towards &&
                    Physics.SphereCast(origin, r * 0.8f, dir, out RaycastHit hit, r * 0.7f + 0.1f * s, surfaceMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.attachedRigidbody != rb)
                {
                    best = hit;
                    found = true;
                }
                else if (moveSwim.sqrMagnitude < 0.01f &&
                         Physics.SphereCast(origin, r * 0.8f, Vector3.down, out RaycastHit down, r * 0.6f, surfaceMask, QueryTriggerInteraction.Ignore)
                         && down.collider.attachedRigidbody != rb)
                {
                    best = down;
                    found = true;
                }
            }

            IsGrounded = found;
            if (found)
            {
                SurfaceNormal = Vector3.Slerp(SurfaceNormal, best.normal, 0.35f).normalized;
                SurfaceCollider = best.collider;
                SurfacePoint = best.point;
            }
            else
            {
                SurfaceNormal = Vector3.Slerp(SurfaceNormal, Vector3.up, 0.05f).normalized;
                SurfaceCollider = null;
            }
        }

        void AlignRotation()
        {
            Vector3 up, fwd;
            Vector3 vel = rb.linearVelocity;
            if (IsGrounded)
            {
                up = SurfaceNormal;
                fwd = Vector3.ProjectOnPlane(transform.forward, up);
                Vector3 velT = Vector3.ProjectOnPlane(vel, up);
                if (moveCrawl.sqrMagnitude > 0.01f) fwd = Vector3.ProjectOnPlane(moveCrawl, up);
                else if (velT.sqrMagnitude > 0.25f * Scale) fwd = velT;
            }
            else
            {
                // В плавании голова по ходу, с лёгким наклоном по вертикали
                up = Vector3.up;
                Vector3 dir = moveSwim.sqrMagnitude > 0.01f ? moveSwim : (vel.sqrMagnitude > 0.1f * Scale ? vel : transform.forward);
                Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                float pitch = Mathf.Clamp(Vector3.SignedAngle(flat.normalized, dir.normalized, Vector3.Cross(Vector3.up, flat.normalized)), -50f, 50f);
                fwd = Quaternion.AngleAxis(pitch * 0.6f, Vector3.Cross(Vector3.up, flat.normalized)) * flat.normalized;
                up = Vector3.Cross(fwd, Vector3.Cross(Vector3.up, fwd)).normalized;
            }
            if (fwd.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(fwd.normalized, up);
            float k = 1f - Mathf.Exp(-(IsGrounded ? alignSpeed : alignSpeed * 0.5f) * Time.fixedDeltaTime);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, k));
            rb.angularVelocity = Vector3.zero;
        }

        void AnimateBody()
        {
            if (body == null) return;
            int p = (int)Pose;
            float breathe = 1f + Mathf.Sin(Time.time * 2.1f) * 0.02f * (Pose == OctopusPose.Rock ? 0.2f : 1f);
            Vector3 targetScale = Vector3.Scale(bodyBaseScale, PoseScale[p]) * breathe;
            // В плавании мантия вытягивается назад — «реактивная» поза
            if (IsSwimming && Speed > 0.5f * SpeedScale) targetScale = Vector3.Scale(targetScale, new Vector3(0.85f, 0.9f, 1.25f));
            Vector3 targetPos = bodyBasePos + Vector3.up * PoseDrop[p];
            float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
            body.localScale = Vector3.Lerp(body.localScale, targetScale, k);
            body.localPosition = Vector3.Lerp(body.localPosition, targetPos, k);

            Quaternion targetRot = Quaternion.identity;
            if (Pose == OctopusPose.Kelp)
                targetRot = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 7f, 0f, Mathf.Sin(Time.time * 0.9f + 1f) * 5f);
            body.localRotation = Quaternion.Slerp(body.localRotation, targetRot, k);
        }
    }
}
