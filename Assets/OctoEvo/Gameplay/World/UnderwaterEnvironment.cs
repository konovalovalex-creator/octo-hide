using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OctoEvo
{
    /// <summary>
    /// Параметры подводной оптики для всех наших шейдеров (глобальные переменные _UW_*),
    /// цвет фона камеры и уровень качества (телефон / ПК).
    /// </summary>
    [ExecuteAlways]
    public class UnderwaterEnvironment : MonoBehaviour
    {
        public static UnderwaterEnvironment Instance { get; private set; }

        [Header("Вода")]
        [Tooltip("Цвет рассеянного света в воде у поверхности")]
        public Color waterColor = new Color(0.1f, 0.45f, 0.55f);
        [Tooltip("Поглощение на метр по каналам: красный гаснет первым")]
        public Vector3 absorption = new Vector3(0.07f, 0.028f, 0.018f);
        [Tooltip("Мутность: рассеяние на метр")]
        public float scatter = 0.025f;
        public float surfaceY = 10f;

        [Header("Каустика")]
        public Texture2D causticsTexture;
        public float causticsStrength = 2.2f;
        public float causticsScale = 0.18f;

        [Header("Ссылки")]
        public Camera targetCamera;
        public Volume postVolume;

        public static bool IsLowQuality => Application.isMobilePlatform || QualitySettings.GetQualityLevel() == 0;

        static readonly int WaterColorId = Shader.PropertyToID("_UW_WaterColor");
        static readonly int AbsorptionId = Shader.PropertyToID("_UW_Absorption");
        static readonly int ScatterId = Shader.PropertyToID("_UW_Scatter");
        static readonly int SurfaceYId = Shader.PropertyToID("_UW_SurfaceY");
        static readonly int CausticsTexId = Shader.PropertyToID("_UW_CausticsTex");
        static readonly int CausticsStrengthId = Shader.PropertyToID("_UW_CausticsStrength");
        static readonly int CausticsScaleId = Shader.PropertyToID("_UW_CausticsScale");

        void OnEnable()
        {
            Instance = this;
            Push();
            if (Application.isPlaying) ApplyQualityTier();
        }

        void OnValidate() => Push();

        void Update()
        {
            Push();
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null)
            {
                // Фон — это вода на глубине камеры
                float depth = Mathf.Max(0f, surfaceY - targetCamera.transform.position.y);
                Color c = waterColor;
                c.r *= Mathf.Exp(-absorption.x * 0.6f * depth);
                c.g *= Mathf.Exp(-absorption.y * 0.6f * depth);
                c.b *= Mathf.Exp(-absorption.z * 0.6f * depth);
                targetCamera.backgroundColor = c;
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
            }
        }

        void Push()
        {
            Shader.SetGlobalColor(WaterColorId, waterColor.linear);
            Shader.SetGlobalVector(AbsorptionId, absorption);
            Shader.SetGlobalFloat(ScatterId, scatter);
            Shader.SetGlobalFloat(SurfaceYId, surfaceY);
            Shader.SetGlobalFloat(CausticsStrengthId, causticsTexture != null ? causticsStrength : 0f);
            Shader.SetGlobalFloat(CausticsScaleId, causticsScale);
            if (causticsTexture != null) Shader.SetGlobalTexture(CausticsTexId, causticsTexture);
            RenderSettings.fog = false; // туман делаем сами, с поглощением по каналам
        }

        /// <summary>На телефоне выключаем дорогие эффекты постобработки.</summary>
        void ApplyQualityTier()
        {
            if (!IsLowQuality || postVolume == null || postVolume.profile == null) return;
            if (postVolume.profile.TryGet(out DepthOfField dof)) dof.active = false;
            if (postVolume.profile.TryGet(out Bloom bloom)) bloom.highQualityFiltering.value = false;
        }
    }
}
