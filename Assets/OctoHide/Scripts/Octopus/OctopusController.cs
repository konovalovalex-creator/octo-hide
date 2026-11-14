using UnityEngine;

namespace OctoHide
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
    /// Передвижение осьминога: ползание по любой поверхности (дно, стены, потолок),
    /// реактивный рывок и медленное погружение в толще воды.
    /// Физическая модель намеренно простая (сфера + Rigidbody) — так её легко сделать
    /// авторитарной на сервере. Щупальца — чисто визуальные (см. OctopusTentacles).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class OctopusController : MonoBehaviour
    {
        [Header("Ползание")]
        public float crawlSpeed = 2.2f;
        public float acceleration = 10f;
        public float stickForce = 25f;
        public float surfaceProbe = 0.9f;
        public LayerMask surfaceMask = ~0;

        [Header("Плавание")]
        public float sinkAcceleration = 2.5f;
        public float swimSteer = 3f;
        public float waterDrag = 1.5f;
        public float jetImpulse = 7f;
        public float jetCooldown = 1.2f;

        [Header("Поворот")]
        public float alignSpeed = 8f;
        public float turnSpeed = 10f;

        [Header("Игрок")]
        [Tooltip("Управляется с клавиатуры этого компьютера. У манекенов и (позже) у чужих осьминогов — выключено.")]
        public bool isLocalPlayer = true;

        [Header("Ссылки")]
        public Transform cameraTransform;
        public Transform body;

        public bool IsGrounded { get; private set; }
        public Vector3 SurfaceNormal { get; private set; } = Vector3.up;
        public Collider SurfaceCollider { get; private set; }
        public Vector3 SurfacePoint { get; private set; }
        public OctopusPose Pose { get; private set; } = OctopusPose.Normal;
        public float Speed => rb != null ? rb.linearVelocity.magnitude : 0f;
        public float NormalizedSpeed => Mathf.Clamp01(Speed / crawlSpeed);
        public float JetCooldownLeft => Mathf.Max(0f, nextJetTime - Time.time);

        Rigidbody rb;
        SphereCollider sphere;
        Vector3 moveInputWorld;
        float detachUntil;
        float nextJetTime;
        bool jetRequested;
        Vector3 bodyBaseScale;
        Vector3 bodyBasePos;

        // Форма тела для каждой позы (множитель масштаба и смещение вниз)
        static readonly Vector3[] PoseScale =
        {
            new Vector3(1.00f, 1.00f, 1.00f), // Normal
            new Vector3(1.05f, 0.95f, 1.00f), // Rock — компактный «камень»
            new Vector3(1.35f, 0.45f, 1.35f), // Flat — распластаться
            new Vector3(0.80f, 0.90f, 1.45f), // Reach — вытянуться
            new Vector3(1.10f, 0.70f, 1.10f), // Coral
            new Vector3(0.70f, 1.45f, 0.70f), // Kelp
            new Vector3(1.20f, 0.35f, 1.20f), // Starfish
            new Vector3(0.60f, 0.60f, 1.60f), // Snake
        };
        static readonly float[] PoseDrop = { 0f, -0.05f, -0.18f, 0f, -0.12f, 0.45f, -0.2f, -0.1f };
        static readonly float[] PoseSpeedMul = { 1f, 0.35f, 0.5f, 0.8f, 0.3f, 0.25f, 0.4f, 0.9f };

        /// <summary>На сколько тело сейчас поднято/опущено относительно обычного положения (м).</summary>
        public float BodyLift => body != null ? body.localPosition.y - bodyBasePos.y : 0f;

        /// <summary>Все осьминоги на сцене — для рыб, крабов, камней и (позже) ботов.</summary>
        public static readonly System.Collections.Generic.List<OctopusController> All = new System.Collections.Generic.List<OctopusController>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void SetPose(OctopusPose pose) => Pose = pose;

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
                moveInputWorld = CameraRelative(input);

                if (OctoInput.JetPressed && Time.time >= nextJetTime) jetRequested = true;

                int pose = OctoInput.PosePressed;
                if (pose >= 0) Pose = (OctopusPose)pose;
            }
            else moveInputWorld = Vector3.zero;

            AnimateBody();
        }

        Vector3 CameraRelative(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;
            Transform cam = cameraTransform != null ? cameraTransform : transform;
            Vector3 up = IsGrounded ? SurfaceNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, up);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(cam.up, up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);
            return (fwd * input.y + right * input.x);
        }

        void FixedUpdate()
        {
            ProbeSurface();

            float speedMul = PoseSpeedMul[(int)Pose];
            Vector3 v = rb.linearVelocity;

            if (jetRequested)
            {
                jetRequested = false;
                nextJetTime = Time.time + jetCooldown;
                detachUntil = Time.time + 0.35f;
                Transform cam = cameraTransform != null ? cameraTransform : transform;
                Vector3 dir = moveInputWorld.sqrMagnitude > 0.01f ? moveInputWorld.normalized : cam.forward;
                dir = (dir + (IsGrounded ? SurfaceNormal * 0.6f : Vector3.zero)).normalized;
                rb.AddForce(dir * jetImpulse, ForceMode.VelocityChange);
                IsGrounded = false;
                Pose = OctopusPose.Normal;
            }

            if (IsGrounded)
            {
                Vector3 n = SurfaceNormal;
                Vector3 tangentV = Vector3.ProjectOnPlane(v, n);
                Vector3 target = moveInputWorld * crawlSpeed * speedMul;
                tangentV = Vector3.MoveTowards(tangentV, target, acceleration * Time.fixedDeltaTime);
                float normalV = Vector3.Dot(v, n);
                normalV = Mathf.MoveTowards(normalV, 0f, stickForce * Time.fixedDeltaTime);
                rb.linearVelocity = tangentV + n * normalV;
                rb.AddForce(-n * stickForce, ForceMode.Acceleration);
            }
            else
            {
                // В толще воды: медленное погружение, лёгкое руление и сопротивление воды
                rb.AddForce(Vector3.down * sinkAcceleration, ForceMode.Acceleration);
                rb.AddForce(moveInputWorld * swimSteer, ForceMode.Acceleration);
                rb.linearVelocity *= 1f / (1f + waterDrag * Time.fixedDeltaTime);
            }

            AlignRotation();
        }

        void ProbeSurface()
        {
            if (Time.time < detachUntil)
            {
                IsGrounded = false;
                SurfaceCollider = null;
                return;
            }

            float r = sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            Vector3 origin = transform.position;
            bool found = false;
            RaycastHit best = default;

            // 1) Стена или препятствие по ходу движения — переползаем на неё
            if (moveInputWorld.sqrMagnitude > 0.01f &&
                Physics.SphereCast(origin, r * 0.6f, moveInputWorld.normalized, out RaycastHit wall, r + 0.25f, surfaceMask, QueryTriggerInteraction.Ignore)
                && wall.collider.attachedRigidbody != rb)
            {
                best = wall;
                found = true;
            }

            // 2) Поверхность «под ногами» (вдоль локального низа), потом мировой низ
            if (!found)
            {
                Vector3[] dirs = { -transform.up, -SurfaceNormal, Vector3.down };
                foreach (var d in dirs)
                {
                    if (Physics.SphereCast(origin, r * 0.8f, d, out RaycastHit hit, surfaceProbe, surfaceMask, QueryTriggerInteraction.Ignore)
                        && hit.collider.attachedRigidbody != rb)
                    {
                        best = hit;
                        found = true;
                        break;
                    }
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
            Vector3 up = IsGrounded ? SurfaceNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            Vector3 vel = Vector3.ProjectOnPlane(rb.linearVelocity, up);
            if (moveInputWorld.sqrMagnitude > 0.01f) fwd = Vector3.ProjectOnPlane(moveInputWorld, up);
            else if (vel.sqrMagnitude > 0.5f) fwd = vel;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(transform.up * -1f, up);
            if (fwd.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(fwd.normalized, up);
            float k = 1f - Mathf.Exp(-(IsGrounded ? alignSpeed : alignSpeed * 0.4f) * Time.fixedDeltaTime);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, k));
            rb.angularVelocity = Vector3.zero;
        }

        void AnimateBody()
        {
            if (body == null) return;
            int p = (int)Pose;
            float breathe = 1f + Mathf.Sin(Time.time * 2.1f) * 0.02f * (Pose == OctopusPose.Rock ? 0.2f : 1f);
            Vector3 targetScale = Vector3.Scale(bodyBaseScale, PoseScale[p]) * breathe;
            Vector3 targetPos = bodyBasePos + Vector3.up * PoseDrop[p];
            float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
            body.localScale = Vector3.Lerp(body.localScale, targetScale, k);
            body.localPosition = Vector3.Lerp(body.localPosition, targetPos, k);

            // «Водоросль» покачивается на течении
            Quaternion targetRot = Quaternion.identity;
            if (Pose == OctopusPose.Kelp)
                targetRot = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 7f, 0f, Mathf.Sin(Time.time * 0.9f + 1f) * 5f);
            body.localRotation = Quaternion.Slerp(body.localRotation, targetRot, k);
        }
    }
}
