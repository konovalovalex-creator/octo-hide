using UnityEngine;

namespace OctoEvo.UI
{
    /// <summary>Временный HUD (IMGUI): FPS, размер и рост, заметность, сообщения, подсказки.</summary>
    public class DebugHUD : MonoBehaviour
    {
        public OctopusMotor motor;
        public CamouflageSkin skin;
        public ConspicuityMeter meter;
        public GrowthSystem growth;
        public GrabSystem grab;
        public InkAbility ink;
        public SpawnDirector spawner;
        public bool showHelp = true;

        static readonly string[] PoseNames =
            { "Обычная", "Камень", "Распластаться", "Вытянуться", "Коралл", "Водоросль", "Морская звезда", "Морская змея" };

        float fps, fpsTimer;
        int frames;
        GUIStyle label, small, center;
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
                center = new GUIStyle(label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                white = Texture2D.whiteTexture;
            }

            float s = Screen.height / 1080f;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s, h = Screen.height / s;

            string tier = UnderwaterEnvironment.IsLowQuality ? "Mobile" : "PC";
            string pop = spawner != null ? $" · существ {spawner.ActiveCount}" : "";
            GUI.Label(new Rect(20, 16, 700, 26), $"{fps:0} FPS · качество {tier}{pop}", label);

            if (growth != null)
            {
                GUI.Label(new Rect(20, 44, 700, 26), $"Длина {GrowthSystem.FormatLength(growth.Length)} · биомасса {growth.Biomass:0.##} · съедено {growth.Eaten}", label);
                if (growth.NextMilestone(out CreatureDefinition next, out float progress))
                {
                    Bar(new Rect(20, 76, 260, 12), progress, new Color(0.95f, 0.75f, 0.3f));
                    GUI.Label(new Rect(290, 68, 500, 26), $"до «{next.displayName}»", label);
                }
            }
            if (grab != null && grab.Current == GrabSystem.Phase.Holding)
                Bar(new Rect(w * 0.5f - 100, h * 0.62f, 200, 10), grab.EatProgress, new Color(0.9f, 0.5f, 0.4f));

            ShowLatestMessage(w);

            if (motor != null && skin != null)
            {
                string inkCd = ink != null && ink.CooldownLeft > 0f ? $"   Чернила: {ink.CooldownLeft:0} с" : "";
                string mode = motor.IsSwimming ? "плывёт" : "ползёт";
                GUI.Label(new Rect(20, h - 70, 900, 26), $"Поза: {PoseNames[(int)motor.Pose]} ({mode})   Пигмент: {skin.Energy:0}{inkCd}", label);
                if (meter != null)
                {
                    GUI.color = new Color(0, 0, 0, 0.4f);
                    GUI.DrawTexture(new Rect(20, h - 38, 260, 14), white);
                    GUI.color = Color.Lerp(new Color(0.3f, 0.85f, 0.5f), new Color(0.95f, 0.3f, 0.25f), meter.Value);
                    GUI.DrawTexture(new Rect(22, h - 36, 256 * meter.Value, 10), white);
                    GUI.color = Color.white;
                }
            }

            if (showHelp && !TouchInputState.Active)
            {
                GUI.Label(new Rect(w - 640, h - 110, 620, 100),
                    "WASD — двигаться (смотри вверх и жми W, чтобы всплыть)   Space — рывок   Мышь — камера\n" +
                    "ЛКМ — схватить   F — чернила   E — маскировка   1–8 — позы   Q — глаза\n" +
                    "F1 — скрыть подсказки   F2 — сенсорное управление   Esc — курсор", small);
            }
            GUI.matrix = old;
        }

        void Bar(Rect r, float v, Color c)
        {
            GUI.color = new Color(0, 0, 0, 0.4f);
            GUI.DrawTexture(r, white);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(v), r.height - 4), white);
            GUI.color = Color.white;
        }

        static void Take(string m, float time, ref string msg, ref float t)
        {
            if (!string.IsNullOrEmpty(m) && time > t) { msg = m; t = time; }
        }

        void ShowLatestMessage(float w)
        {
            string msg = null;
            float t = -10f;
            if (skin != null) Take(skin.LastMessage, skin.LastMessageTime, ref msg, ref t);
            if (growth != null) Take(growth.LastMessage, growth.LastMessageTime, ref msg, ref t);
            if (grab != null) Take(grab.LastMessage, grab.LastMessageTime, ref msg, ref t);
            if (ink != null) Take(ink.LastMessage, ink.LastMessageTime, ref msg, ref t);
            float age = Time.time - t;
            if (msg == null || age > 3f) return;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(3f - age));
            GUI.Label(new Rect(w * 0.5f - 500, 120, 1000, 40), msg, center);
            GUI.color = Color.white;
        }
    }
}
