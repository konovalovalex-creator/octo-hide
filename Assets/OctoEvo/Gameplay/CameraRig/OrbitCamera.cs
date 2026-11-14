using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Камера от третьего лица: мышь / стик / палец, зум колесом, без проваливания в стены.
    /// Дистанция и высота масштабируются с размером осьминога. Если игрок давно не крутил камеру
    /// и плывёт — камера сама плавно заходит за спину (важно на телефоне).
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 0.4f, 0f);
        [Tooltip("Дистанции указаны для осьминога масштаба 1")]
        public float distance = 4f;
        public float minDistance = 1.2f;
        public float maxDistance = 9f;
        public float sensitivity = 2.2f;
        public float minPitch = -60f;
        public float maxPitch = 80f;
        public float followSharpness = 12f;
        public float collisionRadius = 0.2f;
        public LayerMask collisionMask = ~0;

        [Header("Автоследование")]
        public bool autoFollow = true;
        public float autoFollowDelay = 1.5f;
        public float autoFollowSpeed = 1.5f;

        float yaw, pitch = 20f;
        Vector3 smoothTarget;
        float currentDistance;
        float lastManualLook = -10f;
        float smoothScale = 1f;
        OctopusMotor motor;
        Camera cam;

        bool UseCursorLock => !TouchInputState.Active && !Application.isMobilePlatform;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (target != null)
            {
                smoothTarget = target.position + targetOffset;
                motor = target.GetComponent<OctopusMotor>();
                smoothScale = TargetScale;
            }
            currentDistance = distance * smoothScale;
            yaw = transform.eulerAngles.y;
            if (UseCursorLock) LockCursor(true);
        }

        float TargetScale => target != null ? Mathf.Max(0.01f, target.lossyScale.x) : 1f;

        void LateUpdate()
        {
            if (target == null) return;

            if (UseCursorLock)
            {
                if (OctoInput.UnlockCursorPressed) LockCursor(false);
                if (OctoInput.ClickPressed && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
            }
            else if (Cursor.lockState == CursorLockMode.Locked) LockCursor(false);

            Vector2 look = OctoInput.StickAndTouchLook;
            if (Cursor.lockState == CursorLockMode.Locked) look += OctoInput.MouseLook;
            if (look.sqrMagnitude > 0.0001f) lastManualLook = Time.time;
            yaw += look.x * sensitivity;
            pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);

            distance = Mathf.Clamp(distance - OctoInput.Zoom * 0.8f, minDistance, maxDistance);

            AutoFollow();

            smoothScale = Mathf.Lerp(smoothScale, TargetScale, 1f - Mathf.Exp(-2f * Time.deltaTime));
            float s = smoothScale;
            // Маленькому осьминогу нужна близкая плоскость отсечения, иначе камера «режет» его и камни рядом
            if (cam != null) cam.nearClipPlane = Mathf.Clamp(0.05f * s, 0.008f, 0.05f);

            Vector3 focus = target.position + targetOffset * s;
            smoothTarget = Vector3.Lerp(smoothTarget, focus, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.back;

            float dist = distance * s;
            float wanted = dist;
            if (Physics.SphereCast(smoothTarget, collisionRadius * s, dir, out RaycastHit hit, dist, collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody == null)
                wanted = Mathf.Max(minDistance * 0.5f * s, hit.distance - 0.05f * s);
            currentDistance = wanted < currentDistance ? wanted : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-4f * Time.deltaTime));

            Vector3 pos = smoothTarget + dir * currentDistance;
            // Не высовываться из воды
            if (UnderwaterEnvironment.Instance != null)
                pos.y = Mathf.Min(pos.y, UnderwaterEnvironment.Instance.surfaceY - 0.15f);

            transform.SetPositionAndRotation(pos, rot);
        }

        void AutoFollow()
        {
            if (!autoFollow || motor == null) return;
            if (Time.time - lastManualLook < autoFollowDelay) return;
            Vector3 v = motor.Velocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (flat.magnitude < 0.4f * motor.SpeedScale) return;

            float wantYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float k = 1f - Mathf.Exp(-autoFollowSpeed * Time.deltaTime);
            yaw = Mathf.LerpAngle(yaw, wantYaw, k);
            if (motor.IsSwimming)
            {
                float wantPitch = Mathf.Clamp(-Mathf.Atan2(v.y, flat.magnitude) * Mathf.Rad2Deg * 0.5f + 12f, minPitch, maxPitch);
                pitch = Mathf.Lerp(pitch, wantPitch, k * 0.5f);
            }
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
