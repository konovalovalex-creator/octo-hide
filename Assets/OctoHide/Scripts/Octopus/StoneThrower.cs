using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Бросок камня (F) по направлению камеры. Попадание в другого осьминога вызывает рябь
    /// на его коже, в рыб и крабов — пугает их.
    /// </summary>
    public class StoneThrower : MonoBehaviour
    {
        public OctopusController controller;
        public Transform cameraTransform;
        public Material stoneMaterial;

        public float throwSpeed = 11f;
        public float upwardBias = 0.12f;
        public float cooldown = 0.9f;
        public float stoneSize = 0.16f;

        public float CooldownLeft => Mathf.Max(0f, nextThrow - Time.time);
        public int Thrown { get; private set; }
        public int Hits { get; private set; }

        float nextThrow;
        Collider ownCollider;

        void Awake()
        {
            if (controller == null) controller = GetComponent<OctopusController>();
            ownCollider = GetComponent<Collider>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            if (controller != null && !controller.isLocalPlayer) return;
            if (OctoInput.ThrowPressed && Time.time >= nextThrow) Throw();
        }

        public void Throw()
        {
            nextThrow = Time.time + cooldown;
            Transform cam = cameraTransform != null ? cameraTransform : transform;
            Vector3 dir = (cam.forward + Vector3.up * upwardBias).normalized;
            Vector3 origin = transform.position + transform.up * 0.45f + dir * 0.5f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Stone";
            go.transform.position = origin;
            go.transform.localScale = new Vector3(stoneSize, stoneSize * 0.8f, stoneSize * 0.9f);
            go.transform.rotation = Random.rotation;
            var r = go.GetComponent<Renderer>();
            if (stoneMaterial != null) r.sharedMaterial = stoneMaterial;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.linearDamping = 0.5f; // вода тормозит
            rb.angularDamping = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = dir * throwSpeed + (controller != null ? controller.GetComponent<Rigidbody>().linearVelocity * 0.5f : Vector3.zero);

            var col = go.GetComponent<Collider>();
            if (ownCollider != null) Physics.IgnoreCollision(col, ownCollider);

            var stone = go.AddComponent<Stone>();
            stone.owner = this;
            Thrown++;
        }

        internal void ReportHit() => Hits++;
    }
}
