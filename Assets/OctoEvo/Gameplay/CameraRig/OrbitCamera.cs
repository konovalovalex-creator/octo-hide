using UnityEngine;

namespace OctoEvo
{
    /// <summary>Камера от третьего лица: вращение мышью/стиком, зум колесом, без проваливания в стены.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 0.4f, 0f);
        public float distance = 4f;
        public float minDistance = 1.2f;
        public float maxDistance = 9f;
        public float sensitivity = 2.2f;
        public float minPitch = -30f;
        public float maxPitch = 80f;
        public float followSharpness = 12f;
        public float collisionRadius = 0.2f;
        public LayerMask collisionMask = ~0;

        float yaw, pitch = 20f;
        Vector3 smoothTarget;
        float currentDistance;

        void Start()
        {
            if (target != null) smoothTarget = target.position + targetOffset;
            currentDistance = distance;
            yaw = transform.eulerAngles.y;
            LockCursor(true);
        }

        void LateUpdate()
        {
            if (target == null) return;

            if (OctoInput.UnlockCursorPressed) LockCursor(false);
            if (OctoInput.ClickPressed && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = OctoInput.Look;
                yaw += look.x * sensitivity;
                pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);
            }
            distance = Mathf.Clamp(distance - OctoInput.Zoom * 0.8f, minDistance, maxDistance);

            Vector3 focus = target.position + targetOffset;
            smoothTarget = Vector3.Lerp(smoothTarget, focus, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.back;

            float wanted = distance;
            if (Physics.SphereCast(smoothTarget, collisionRadius, dir, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody == null)
                wanted = Mathf.Max(minDistance * 0.5f, hit.distance - 0.05f);
            currentDistance = wanted < currentDistance ? wanted : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-4f * Time.deltaTime));

            transform.SetPositionAndRotation(smoothTarget + dir * currentDistance, rot);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
