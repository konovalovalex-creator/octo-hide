using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Индекс заметности 0..1. Используется для HUD, очков и (позже) для ботов-искателей.
    /// НЕ решает за живого игрока, видит он осьминога или нет.
    /// </summary>
    public class ConspicuityMeter : MonoBehaviour
    {
        public OctopusController controller;
        public OctopusSkin skin;

        [Header("Веса")]
        public float colorWeight = 0.45f;
        public float alignWeight = 0.25f;
        public float motionWeight = 0.2f;
        public float papillaeWeight = 0.1f;

        public float Value { get; private set; }
        public float ColorPart { get; private set; }
        public float AlignPart { get; private set; }
        public float MotionPart { get; private set; }
        public float PapillaePart { get; private set; }

        void Awake()
        {
            if (controller == null) controller = GetComponent<OctopusController>();
            if (skin == null) skin = GetComponent<OctopusSkin>();
        }

        void Update()
        {
            if (controller == null || skin == null) return;

            MotionPart = controller.NormalizedSpeed;

            if (controller.SurfaceCollider != null &&
                CamoSurface.TryGetState(controller.SurfaceCollider, transform.localToWorldMatrix, out CamoState surface))
            {
                ColorPart = Mathf.Clamp01(CamoUtil.DeltaE(skin.EffectiveAverage, surface.average) / 40f);

                CamoState cur = skin.Current;
                if (cur.IsFromSurface && cur.sourceSurfaceId == surface.sourceSurfaceId && !skin.InTransition)
                {
                    // Насколько узор «съехал» с поверхности с момента копирования
                    Matrix4x4 d = skin.DeltaMatrix(cur);
                    Vector3 p = transform.position;
                    float shift = (d.MultiplyPoint3x4(p) - p).magnitude;
                    float angle = Quaternion.Angle(Quaternion.identity, d.rotation);
                    AlignPart = Mathf.Clamp01(shift / 0.6f + angle / 45f);
                }
                else AlignPart = 1f;

                PapillaePart = Mathf.Clamp01(Mathf.Abs(skin.Papillae - surface.papillae) * 2f);
            }
            else
            {
                // В толще воды на виду у всех
                ColorPart = 1f;
                AlignPart = 1f;
                PapillaePart = 0.5f;
            }

            float target = Mathf.Clamp01(ColorPart * colorWeight + AlignPart * alignWeight + MotionPart * motionWeight + PapillaePart * papillaeWeight);
            Value = Mathf.MoveTowards(Value, target, Time.deltaTime * 2f);
        }
    }
}
