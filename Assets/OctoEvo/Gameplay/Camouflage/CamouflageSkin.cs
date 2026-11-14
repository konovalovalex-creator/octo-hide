using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Управление окрасом: копирование узора с поверхности (E), ручная палитра (C),
    /// сброс (R), папиллы (Z/X). Расходует «энергию пигмента».
    /// </summary>
    public class CamouflageSkin : MonoBehaviour
    {
        public OctopusMotor controller;
        [Tooltip("Body, щупальца — всё, что красится кожей. Получат один общий экземпляр материала.")]
        public Renderer[] skinRenderers;
        public Material skinMaterialTemplate;

        [Header("Смена окраса")]
        public float transitionTime = 1.1f;
        public float sampleRange = 1.6f;

        [Header("Энергия пигмента")]
        public float maxEnergy = 100f;
        public float sampleCost = 30f;
        public float paletteCost = 15f;
        public float regenStill = 12f;
        public float regenMoving = 4f;

        [Header("Папиллы")]
        public float papillaeSpeed = 0.8f;

        [Header("Палитра (C)")]
        public Color defaultColor = new Color(0.58f, 0.3f, 0.24f);
        public Color[] palette =
        {
            new Color(0.58f, 0.3f, 0.24f),  // обычный красно-бурый
            new Color(0.78f, 0.7f, 0.55f),  // песок
            new Color(0.42f, 0.4f, 0.37f),  // камень
            new Color(0.33f, 0.4f, 0.25f),  // водоросли
            new Color(0.85f, 0.85f, 0.8f),  // бледный (испуг)
            new Color(0.2f, 0.15f, 0.12f),  // тёмный
        };

        public Material SkinMaterial => mat;
        public CamoState Current => stateB;
        public CamoState Previous => stateA;
        public float Blend => blend;
        public bool InTransition => blend < 1f;
        public float Energy { get; private set; }
        public float Papillae => papillae;
        public string LastMessage { get; private set; } = "";
        public float LastMessageTime { get; private set; } = -10f;

        [Header("Рябь от попадания")]
        public float rippleDuration = 2.5f;

        /// <summary>0..1 — насколько сейчас видна рябь от попадания камнем.</summary>
        public float RippleAmount => Mathf.Clamp01(1f - (Time.time - rippleStart) / rippleDuration) * rippleStrength;
        public int TimesHit { get; private set; }

        Vector3 rippleLocal;
        float rippleStart = -100f;
        float rippleStrength;

        /// <summary>Попадание (камнем и т.п.): по коже расходится рябь, маскировка сбивается.</summary>
        public void Disturb(Vector3 worldPoint, float strength)
        {
            rippleLocal = transform.InverseTransformPoint(worldPoint);
            rippleStart = Time.time;
            rippleStrength = Mathf.Clamp01(strength);
            TimesHit++;
            papillae = Mathf.Min(1f, papillae + 0.3f); // кожа «вздрагивает»
            Say("В тебя попали!");
        }

        public void SetPapillae(float value) => papillaeTarget = Mathf.Clamp01(value);

        Material mat;
        CamoState stateA, stateB;
        float blend = 1f;
        float papillae, papillaeTarget;
        int paletteIndex;

        static readonly int TexA = Shader.PropertyToID("_TexA"), TintA = Shader.PropertyToID("_TintA"),
            ScaleA = Shader.PropertyToID("_ScaleA"), AvgA = Shader.PropertyToID("_AvgA"), CapA = Shader.PropertyToID("_CapA");
        static readonly int TexB = Shader.PropertyToID("_TexB"), TintB = Shader.PropertyToID("_TintB"),
            ScaleB = Shader.PropertyToID("_ScaleB"), AvgB = Shader.PropertyToID("_AvgB"), CapB = Shader.PropertyToID("_CapB");
        static readonly int BlendId = Shader.PropertyToID("_Blend"), PapId = Shader.PropertyToID("_Papillae"),
            SmearId = Shader.PropertyToID("_Smear");
        static readonly int RippleCenterId = Shader.PropertyToID("_RippleCenter"), RippleTimeId = Shader.PropertyToID("_RippleTime"),
            RippleStrengthId = Shader.PropertyToID("_RippleStrength");

        void Awake()
        {
            if (controller == null) controller = GetComponent<OctopusMotor>();
            Material src = skinMaterialTemplate != null ? skinMaterialTemplate
                : (skinRenderers != null && skinRenderers.Length > 0 ? skinRenderers[0].sharedMaterial : null);
            if (src == null)
            {
                Debug.LogError("[CamouflageSkin] Не задан материал кожи (OctoEvo/OctopusSkinLit).", this);
                enabled = false;
                return;
            }
            mat = new Material(src) { name = src.name + " (Instance)" };
            basePapFreq = mat.HasProperty(PapFreqId) ? mat.GetFloat(PapFreqId) : 14f;
            basePapHeight = mat.HasProperty(PapHeightId) ? mat.GetFloat(PapHeightId) : 0.035f;
            foreach (var r in skinRenderers) if (r != null) r.sharedMaterial = mat;

            stateB = PaletteState(defaultColor);
            stateA = stateB;
            blend = 1f;
            papillae = papillaeTarget = 0.25f;
            Energy = maxEnergy;
            Push();
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }

        void Update()
        {
            if (mat == null) return;

            if (controller == null || controller.isLocalPlayer)
            {
                if (OctoInput.SamplePressed) TrySampleSurface();
                if (OctoInput.CyclePalettePressed) TryPalette();
                if (OctoInput.ResetSkinPressed) StartTransition(PaletteState(defaultColor), 0f, "Обычный окрас");

                if (OctoInput.PapillaeUp) papillaeTarget = Mathf.Clamp01(papillaeTarget + papillaeSpeed * Time.deltaTime);
                if (OctoInput.PapillaeDown) papillaeTarget = Mathf.Clamp01(papillaeTarget - papillaeSpeed * Time.deltaTime);
            }
            papillae = Mathf.MoveTowards(papillae, papillaeTarget, papillaeSpeed * 1.5f * Time.deltaTime);

            if (blend < 1f)
            {
                blend = Mathf.Min(1f, blend + Time.deltaTime / transitionTime);
                if (blend >= 1f) stateA = stateB;
            }

            float move = controller != null ? controller.NormalizedSpeed : 0f;
            Energy = Mathf.Min(maxEnergy, Energy + Mathf.Lerp(regenStill, regenMoving, move) * Time.deltaTime);

            Push();
        }

        public bool TrySampleSurface()
        {
            if (InTransition) { Say("Кожа ещё меняется…"); return false; }
            if (Energy < sampleCost) { Say("Мало пигмента"); return false; }

            Collider col = controller != null ? controller.SurfaceCollider : null;
            if (col == null)
            {
                // В толще воды — ищем поверхность под собой
                if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, sampleRange * transform.lossyScale.x, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.attachedRigidbody == null)
                    col = hit.collider;
            }
            if (col == null || !CamoSurface.TryGetState(col, transform.localToWorldMatrix, out CamoState s))
            {
                Say("Нечего копировать");
                return false;
            }
            papillaeTarget = s.papillae;
            StartTransition(s, sampleCost, "Окрас скопирован");
            return true;
        }

        void TryPalette()
        {
            if (InTransition) { Say("Кожа ещё меняется…"); return; }
            if (Energy < paletteCost) { Say("Мало пигмента"); return; }
            paletteIndex = (paletteIndex + 1) % palette.Length;
            StartTransition(PaletteState(palette[paletteIndex]), paletteCost, "Палитра");
        }

        void StartTransition(CamoState target, float cost, string message)
        {
            if (InTransition) return;
            Energy -= cost;
            stateA = stateB;
            stateB = target;
            blend = 0f;
            Say(message);
        }

        CamoState PaletteState(Color c)
        {
            Color avg = c * CamoUtil.MottleAverage;
            avg.a = 1f;
            return new CamoState
            {
                pattern = CamoUtil.MottleTexture,
                tint = c,
                scale = 2.2f,
                papillae = 0.25f,
                average = avg,
                captureLocalToWorld = transform.localToWorldMatrix,
                sourceSurfaceId = 0
            };
        }

        /// <summary>Матрица «текущий мир → мир на момент копирования».</summary>
        public Matrix4x4 DeltaMatrix(in CamoState s) => s.captureLocalToWorld * transform.worldToLocalMatrix;

        /// <summary>Средний цвет кожи прямо сейчас (с учётом перехода и смаза) — для оценки заметности.</summary>
        public Color EffectiveAverage => Color.Lerp(stateA.average, stateB.average, blend);

        void Push()
        {
            float smear = controller != null ? Mathf.Clamp01(controller.NormalizedSpeed * 0.7f) : 0f;

            mat.SetTexture(TexA, stateA.pattern);
            mat.SetColor(TintA, stateA.tint);
            mat.SetFloat(ScaleA, stateA.scale);
            mat.SetColor(AvgA, stateA.average);
            mat.SetMatrix(CapA, DeltaMatrix(stateA));

            mat.SetTexture(TexB, stateB.pattern);
            mat.SetColor(TintB, stateB.tint);
            mat.SetFloat(ScaleB, stateB.scale);
            mat.SetColor(AvgB, stateB.average);
            mat.SetMatrix(CapB, DeltaMatrix(stateB));

            mat.SetFloat(BlendId, blend);
            mat.SetFloat(PapId, papillae);
            // Пупырышки растут вместе с осьминогом, а не остаются «метровыми»
            float sc = Mathf.Max(0.01f, transform.lossyScale.x);
            mat.SetFloat(PapFreqId, basePapFreq / sc);
            mat.SetFloat(PapHeightId, basePapHeight * sc);
            mat.SetFloat(SmearId, Mathf.Max(smear, RippleAmount * 0.5f));
            mat.SetVector(RippleCenterId, transform.TransformPoint(rippleLocal));
            mat.SetFloat(RippleTimeId, Time.time - rippleStart);
            mat.SetFloat(RippleStrengthId, RippleAmount > 0f ? rippleStrength : 0f);
        }

        static readonly int PapFreqId = Shader.PropertyToID("_PapillaeFreq");
        static readonly int PapHeightId = Shader.PropertyToID("_PapillaeHeight");
        float basePapFreq = 14f, basePapHeight = 0.035f;

        void Say(string msg)
        {
            LastMessage = msg;
            LastMessageTime = Time.time;
        }
    }
}
