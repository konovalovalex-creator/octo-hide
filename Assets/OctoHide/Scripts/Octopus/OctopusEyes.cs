using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Глаза осьминога: веки из той же кожи (значит, тоже маскируются), моргание и закрытие по Q.
    /// Открытые глаза выдают осьминога; закрытые — прячут, но игрок почти ничего не видит.
    /// </summary>
    public class OctopusEyes : MonoBehaviour
    {
        public OctopusSkin skin;
        public Transform[] eyes;

        [Header("Веки")]
        public float closeSpeed = 10f;
        public float lidOpenScale = 0.9f;   // веко спрятано внутри глаза
        public float lidClosedScale = 1.12f; // веко полностью накрывает глаз

        [Header("Моргание")]
        public Vector2 blinkInterval = new Vector2(2.5f, 6f);
        public float blinkDuration = 0.12f;

        /// <summary>0 — открыты, 1 — закрыты.</summary>
        public float Closed { get; private set; }
        public bool WantsClosed { get; private set; }
        public bool EyesClosed => Closed > 0.9f;

        public void SetClosed(bool closed) => WantsClosed = closed;

        bool isLocal = true;
        Transform[] lids;
        Renderer[] lidRenderers;
        float nextBlink;
        float blinkUntil;

        void Start()
        {
            if (skin == null) skin = GetComponent<OctopusSkin>();
            var ctrl = GetComponent<OctopusController>();
            isLocal = ctrl == null || ctrl.isLocalPlayer;
            if (eyes == null || eyes.Length == 0) return;

            lids = new Transform[eyes.Length];
            lidRenderers = new Renderer[eyes.Length];
            for (int i = 0; i < eyes.Length; i++)
            {
                if (eyes[i] == null) continue;
                var lid = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lid.name = "Eyelid";
                Destroy(lid.GetComponent<Collider>());
                lid.transform.SetParent(eyes[i], false);
                lid.transform.localScale = Vector3.one * lidOpenScale;
                var r = lid.GetComponent<Renderer>();
                if (skin != null && skin.SkinMaterial != null) r.sharedMaterial = skin.SkinMaterial;
                r.enabled = false;
                lids[i] = lid.transform;
                lidRenderers[i] = r;
            }
            ScheduleBlink();
        }

        void ScheduleBlink() => nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);

        void Update()
        {
            if (lids == null) return;

            if (isLocal && OctoInput.EyesTogglePressed) WantsClosed = !WantsClosed;

            if (!WantsClosed && Time.time >= nextBlink)
            {
                blinkUntil = Time.time + blinkDuration;
                ScheduleBlink();
            }

            float target = (WantsClosed || Time.time < blinkUntil) ? 1f : 0f;
            Closed = Mathf.MoveTowards(Closed, target, closeSpeed * Time.deltaTime);

            float s = Mathf.Lerp(lidOpenScale, lidClosedScale, Closed);
            for (int i = 0; i < lids.Length; i++)
            {
                if (lids[i] == null) continue;
                // Веко «наезжает» сверху: сначала сплюснуто, к закрытию — круглое
                lids[i].localScale = new Vector3(s, s * Mathf.Lerp(0.6f, 1f, Closed), s);
                lids[i].localPosition = new Vector3(0f, Mathf.Lerp(0.12f, 0f, Closed), 0f);
                lidRenderers[i].enabled = Closed > 0.02f;
            }
        }
    }
}
