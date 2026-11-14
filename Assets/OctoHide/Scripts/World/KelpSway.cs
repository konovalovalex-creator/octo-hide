using UnityEngine;

namespace OctoHide
{
    /// <summary>Стебель водоросли из нескольких сегментов, покачивается на течении. Строится при старте.</summary>
    public class KelpSway : MonoBehaviour
    {
        public Material material;
        public Color tint = new Color(0.35f, 0.55f, 0.25f);
        public int segments = 6;
        public float height = 3f;
        public float width = 0.22f;
        public float swayAmount = 9f;
        public float swaySpeed = 0.9f;

        Transform[] joints;
        float[] yaw;
        float phase;

        void Start()
        {
            phase = Random.value * 10f;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", tint);
            float seg = height / segments;
            joints = new Transform[segments];
            yaw = new float[segments];
            Transform parent = transform;
            for (int i = 0; i < segments; i++)
            {
                var j = new GameObject("Joint" + i).transform;
                j.SetParent(parent, false);
                j.localPosition = i == 0 ? Vector3.zero : Vector3.up * seg;
                yaw[i] = Random.Range(-25f, 25f);
                j.localRotation = Quaternion.Euler(0f, yaw[i], 0f);
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(blade.GetComponent<Collider>()); // сквозь водоросли можно проплыть
                blade.transform.SetParent(j, false);
                blade.transform.localPosition = Vector3.up * seg * 0.5f;
                float w = width * Mathf.Lerp(1f, 0.55f, i / (float)segments);
                blade.transform.localScale = new Vector3(w, seg * 1.05f, 0.03f);
                var r = blade.GetComponent<Renderer>();
                if (material != null) r.sharedMaterial = material;
                r.SetPropertyBlock(mpb);
                joints[i] = j;
                parent = j;
            }
        }

        void Update()
        {
            if (joints == null) return;
            float t = Time.time * swaySpeed + phase;
            for (int i = 0; i < joints.Length; i++)
            {
                float k = (i + 1f) / joints.Length;
                joints[i].localRotation = Quaternion.Euler(Mathf.Sin(t + i * 0.6f) * swayAmount * k, yaw[i], Mathf.Cos(t * 0.7f + i * 0.4f) * swayAmount * 0.6f * k);
            }
        }
    }
}
