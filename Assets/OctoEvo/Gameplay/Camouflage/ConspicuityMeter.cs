using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Индекс заметности 0..1. Используется для HUD, очков и (позже) для ботов-искателей.
    /// НЕ решает за живого игрока, видит он осьминога или нет.
    /// </summary>
    public class ConspicuityMeter : MonoBehaviour
    {
        public OctopusMotor controller;
        public CamouflageSkin skin;
        public OctopusEyes eyes;

        [Header("Веса")]
        public float colorWeight = 0.45f;
        public float alignWeight = 0.25f;
        public float motionWeight = 0.2f;
        public float papillaeWeight = 0.1f;
        [Tooltip("Добавка к заметности, пока глаза открыты")]
        public float openEyesPenalty = 0.12f;

        public float Value { get; private set; }
        public float ColorPart { get; private set; }
        public float AlignPart { get; private set; }
        public float MotionPart { get; private set; }
        public float PapillaePart { get; private set; }
        public float EyesPart { get; private set; }
        public float RipplePart { get; private set; }

        void Awake()
        {
            if (controller == null) controller = GetComponent<OctopusMotor>();
            if (skin == null) skin = GetComponent<CamouflageSkin>();
            if (eyes == null) eyes = GetComponent<OctopusEyes>();
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
                    AlignPart = Mathf.Clamp01(shift / (0.6f * Mathf.Max(0.01f, transform.lossyScale.x)) + angle / 45f);
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

            EyesPart = eyes != null ? 1f - eyes.Closed : 1f;
            float target = Mathf.Clamp01(ColorPart * colorWeight + AlignPart * alignWeight + MotionPart * motionWeight
                                         + PapillaePart * papillaeWeight + EyesPart * openEyesPenalty);
            // Рябь от попадания выдаёт сразу и сильно
            RipplePart = skin.RippleAmount;
            target = Mathf.Max(target, RipplePart);
            Value = Mathf.MoveTowards(Value, target, Time.deltaTime * 2f);
        }
    }
}
