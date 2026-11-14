using UnityEngine;

namespace OctoHide
{
    /// <summary>Брошенный камень. Засчитывает только первый удар.</summary>
    public class Stone : MonoBehaviour
    {
        public StoneThrower owner;
        public float hitRadius = 0.55f;
        public float lifetime = 8f;

        bool spent;
        Rigidbody rb;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            Destroy(gameObject, lifetime);
        }

        void FixedUpdate()
        {
            // Под водой камни тонут медленнее, чем в воздухе
            rb.AddForce(Vector3.down * 4f, ForceMode.Acceleration);
        }

        void OnCollisionEnter(Collision c)
        {
            if (spent) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed < 1.5f) return;
            spent = true;

            Vector3 p = c.GetContact(0).point;
            bool hitOcto = false;

            // Ищем осьминогов рядом с точкой удара: щупальца не имеют коллайдеров, поэтому — по радиусу
            foreach (var octo in OctopusController.All)
            {
                if (owner != null && octo.gameObject == owner.gameObject) continue;
                if (Vector3.Distance(octo.transform.position, p) > hitRadius + 0.35f) continue;
                var skin = octo.GetComponent<OctopusSkin>();
                if (skin != null)
                {
                    skin.Disturb(p, Mathf.Clamp01(speed / 8f) * 0.6f + 0.4f);
                    hitOcto = true;
                }
            }
            if (hitOcto && owner != null) owner.ReportHit();

            // Пугаем живность поблизости
            foreach (var h in Physics.OverlapSphere(p, 2.5f, ~0, QueryTriggerInteraction.Collide))
            {
                var scare = h.GetComponentInParent<IScareable>();
                scare?.Scare(p);
            }
            FishSchool.ScareAll(p, 4f);
        }
    }
}
