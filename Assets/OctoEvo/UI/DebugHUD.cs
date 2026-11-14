using UnityEngine;

namespace OctoEvo.UI
{
    /// <summary>Временный отладочный HUD этапа 0: FPS, уровень качества, заметность, подсказки.</summary>
    public class DebugHUD : MonoBehaviour
    {
        public OctopusMotor motor;
        public CamouflageSkin skin;
        public ConspicuityMeter meter;
        public bool showHelp = true;

        static readonly string[] PoseNames =
            { "Обычная", "Камень", "Распластаться", "Вытянуться", "Коралл", "Водоросль", "Морская звезда", "Морская змея" };

        float fps, fpsTimer;
        int frames;
        GUIStyle label, small;
        Texture2D white;

        void Update()
        {
            frames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fps = frames / fpsTimer;
                frames = 0;
                fpsTimer = 0f;
            }
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F1) showHelp = !showHelp;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                label.normal.textColor = Color.white;
                small = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Normal, alignment = TextAnchor.LowerRight };
                white = Texture2D.whiteTexture;
            }

            float s = Screen.height / 1080f;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s, h = Screen.height / s;

            string tier = UnderwaterEnvironment.IsLowQuality ? "Mobile" : "PC";
            GUI.Label(new Rect(20, 16, 500, 26), $"{fps:0} FPS · качество {tier}", label);

            if (motor != null && skin != null)
            {
                GUI.Label(new Rect(20, h - 70, 700, 26), $"Поза: {PoseNames[(int)motor.Pose]}   Пигмент: {skin.Energy:0}", label);
                if (meter != null)
                {
                    GUI.color = new Color(0, 0, 0, 0.4f);
                    GUI.DrawTexture(new Rect(20, h - 38, 260, 14), white);
                    GUI.color = Color.Lerp(new Color(0.3f, 0.85f, 0.5f), new Color(0.95f, 0.3f, 0.25f), meter.Value);
                    GUI.DrawTexture(new Rect(22, h - 36, 256 * meter.Value, 10), white);
                    GUI.color = Color.white;
                }
            }

            if (showHelp)
            {
                GUI.Label(new Rect(w - 640, h - 110, 620, 100),
                    "WASD — двигаться   Space — рывок   Мышь — камера\n" +
                    "E — маскировка под поверхность   1–8 — позы   Q — глаза\n" +
                    "F1 — скрыть подсказки   Esc — курсор", small);
            }
            GUI.matrix = old;
        }
    }
}
