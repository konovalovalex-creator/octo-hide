using UnityEngine;

namespace OctoEvo
{
    public enum CreatureHabitat
    {
        MidWater,   // в толще воды
        NearBottom, // у дна, в 0.2–1.5 м над ним
        Bottom      // ходит по дну
    }

    /// <summary>
    /// Вид существа: размер, поведение, окрас. Каждый вид — отдельный ассет,
    /// так новых существ добавляют без кода.
    /// </summary>
    [CreateAssetMenu(menuName = "OctoEvo/Creature Definition", fileName = "Creature")]
    public class CreatureDefinition : ScriptableObject
    {
        [HideInInspector] public int presetVersion;
        public string displayName = "Рыбка";
        public CreatureBody body = CreatureBody.Fish;
        public CreatureHabitat habitat = CreatureHabitat.MidWater;

        [Header("Размер (полная длина, м)")]
        public float minLength = 0.06f;
        public float maxLength = 0.1f;

        [Header("Модель (если задана — вместо процедурной)")]
        [Tooltip("Меш нормализован: голова по +Z, длина 1 (у краба — размах 1, низ на y = 0)")]
        public Mesh modelMesh;
        public Material modelMaterial;
        [Tooltip("Во сколько раз модель крупнее «длины» вида (краб: размах ног больше ширины панциря)")]
        public float modelScale = 1f;
        [Tooltip("Амплитуда плавательной волны (в долях длины). 0 — не изгибать (крабы, креветки)")]
        public float swimWave = 0.05f;

        [Header("Форма (в долях длины)")]
        public float halfHeight = 0.12f;
        public float halfWidth = 0.05f;

        [Header("Окрас")]
        public Color backColor = new Color(0.35f, 0.42f, 0.45f);
        public Color bellyColor = new Color(0.85f, 0.85f, 0.8f);
        public Color patternColor = new Color(0.1f, 0.1f, 0.12f);
        [Tooltip("0 — нет, 1 — поперечные полосы, 2 — пятна, 3 — продольная полоса, 4 — крап")]
        public int pattern = 1;
        public float patternScale = 6f;
        [Range(0, 1)] public float smoothness = 0.55f;
        [Range(0, 1)] public float translucency = 0f;

        [Header("Движение (в длинах тела в секунду)")]
        public float cruiseSpeed = 1.5f;
        public float fleeSpeed = 6f;
        [Tooltip("Ограничение скорости в м/с, чтобы крупные не носились как ракеты")]
        public float maxSpeed = 3f;
        public float turnRate = 3f;

        [Tooltip("Высота над дном для NearBottom (мин, макс), м")]
        public Vector2 hoverHeight = new Vector2(0.2f, 1.2f);
        [Tooltip("Доля времени, когда существо просто лежит/сидит на месте (камбала, улитка)")]
        [Range(0, 1)] public float restful = 0f;

        [Header("Стая")]
        public bool schooling = false;
        public Vector2Int groupSize = new Vector2Int(1, 1);

        [Header("Восприятие")]
        [Tooltip("С какого расстояния (м) замечает совсем незамаскированного хищника")]
        public float noticeDistance = 1.5f;
        [Tooltip("Насколько осторожен: 0 — беспечный, 1 — пугливый")]
        [Range(0, 1)] public float wariness = 0.5f;

        [Header("Появление")]
        public float spawnWeight = 1f;
        [Tooltip("Минимальная глубина под поверхностью, м")]
        public float minDepth = 0f;

        public float RandomLength() => Random.Range(minLength, maxLength);
        public float AverageLength => (minLength + maxLength) * 0.5f;
    }
}
