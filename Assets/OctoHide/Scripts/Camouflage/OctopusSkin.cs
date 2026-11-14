using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Управление окрасом: копирование узора с поверхности (E), ручная палитра (C),
    /// сброс (R), папиллы (Z/X). Расходует «энергию пигмента».
    /// </summary>
    public class OctopusSkin : MonoBehaviour
    {
        public OctopusController controller;
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
        public Color defaultColor = new Color(0.92f, 0.38f, 0.32f);
        public Color[] palette =
        {
            new Color(0.92f, 0.38f, 0.32f),
            new Color(0.85f, 0.78f, 0.58f),
            new Color(0.45f, 0.45f, 0.42f),
            new Color(0.35f, 0.55f, 0.3f),
            new Color(0.95f, 0.55f, 0.7f),
            new Color(0.25f, 0.2f, 0.18f),
        };

        public CamoState Current => stateB;
        public CamoState Previous => stateA;
        public float Blend => blend;
        public bool InTransition => blend < 1f;
        public float Energy { get; private set; }
        public float Papillae => papillae;
        public string LastMessage { get; private set; } = "";
        public float LastMessageTime { get; private set; } = -10f;

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

        void Awake()
        {
            if (controller == null) controller = GetComponent<OctopusController>();
            Material src = skinMaterialTemplate != null ? skinMaterialTemplate
                : (skinRenderers != null && skinRenderers.Length > 0 ? skinRenderers[0].sharedMaterial : null);
            if (src == null)
            {
                Debug.LogError("[OctopusSkin] Не задан материал кожи (OctoHide/OctopusSkin).", this);
                enabled = false;
                return;
            }
            mat = new Material(src) { name = src.name + " (Instance)" };
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

            if (OctoInput.SamplePressed) TrySampleSurface();
            if (OctoInput.CyclePalettePressed) TryPalette();
            if (OctoInput.ResetSkinPressed) StartTransition(PaletteState(defaultColor), 0f, "Обычный окрас");

            if (OctoInput.PapillaeUp) papillaeTarget = Mathf.Clamp01(papillaeTarget + papillaeSpeed * Time.deltaTime);
            if (OctoInput.PapillaeDown) papillaeTarget = Mathf.Clamp01(papillaeTarget - papillaeSpeed * Time.deltaTime);
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
                if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, sampleRange, ~0, QueryTriggerInteraction.Ignore)
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
            mat.SetFloat(SmearId, smear);
        }

        void Say(string msg)
        {
            LastMessage = msg;
            LastMessageTime = Time.time;
        }
    }
}
