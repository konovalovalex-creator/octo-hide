using UnityEngine;

namespace OctoHide
{
    /// <summary>Временный HUD прототипа на IMGUI. В вертикальном срезе заменим на UI Toolkit.</summary>
    public class PrototypeHUD : MonoBehaviour
    {
        public OctopusController controller;
        public OctopusSkin skin;
        public ConspicuityMeter meter;
        public bool showHelp = true;
        public bool showDebug = false;

        static readonly string[] PoseNames = { "Обычная", "Камень", "Распластаться", "Вытянуться" };

        GUIStyle label, big, small;
        Texture2D white;

        void OnGUI()
        {
            if (controller == null || skin == null || meter == null) return;
            EnsureStyles();

            float s = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s, h = Screen.height / s;

            // --- Заметность («глаз»)
            float v = meter.Value;
            Color eye = Color.Lerp(new Color(0.3f, 0.85f, 0.5f), new Color(0.95f, 0.3f, 0.25f), v);
            DrawBar(new Rect(w / 2 - 200, 30, 400, 22), v, eye);
            GUI.Label(new Rect(w / 2 - 200, 54, 400, 30), v < 0.25f ? "Невидим" : v < 0.5f ? "Почти незаметен" : v < 0.75f ? "Заметен" : "Как на ладони", big);

            // --- Пигмент
            GUI.Label(new Rect(30, h - 120, 300, 26), "Пигмент", label);
            DrawBar(new Rect(30, h - 92, 260, 18), skin.Energy / skin.maxEnergy, new Color(0.95f, 0.6f, 0.2f));
            GUI.Label(new Rect(30, h - 66, 400, 26), $"Поза: {PoseNames[(int)controller.Pose]}   Папиллы: {skin.Papillae:0.00}", label);
            if (controller.JetCooldownLeft > 0f)
                GUI.Label(new Rect(30, h - 40, 400, 26), $"Рывок через {controller.JetCooldownLeft:0.0} с", label);

            // --- Сообщение
            if (Time.time - skin.LastMessageTime < 1.6f)
                GUI.Label(new Rect(w / 2 - 300, h * 0.62f, 600, 40), skin.LastMessage, big);

            // --- Подсказки
            if (showHelp)
            {
                string help =
                    "WASD — ползти   Space — рывок   Мышь — камера   Колесо — зум\n" +
                    "E — скопировать окрас поверхности   C — палитра   R — сброс\n" +
                    "Z / X — папиллы (гладкая / бугристая кожа)\n" +
                    "1–4 — позы   Esc — освободить курсор   F1 — скрыть подсказки";
                GUI.Label(new Rect(w - 620, h - 120, 600, 110), help, small);
            }

            if (showDebug)
            {
                GUI.Label(new Rect(30, 30, 500, 140),
                    $"цвет {meter.ColorPart:0.00}  узор {meter.AlignPart:0.00}\nдвижение {meter.MotionPart:0.00}  папиллы {meter.PapillaePart:0.00}\n" +
                    $"на поверхности: {controller.IsGrounded} ({(controller.SurfaceCollider ? controller.SurfaceCollider.name : "—")})", small);
            }

            GUI.matrix = old;

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.F1) showHelp = !showHelp;
                if (Event.current.keyCode == KeyCode.F3) showDebug = !showDebug;
            }
        }

        void DrawBar(Rect r, float t, Color c)
        {
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(r, white);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(t), r.height - 4), white);
            GUI.color = Color.white;
        }

        void EnsureStyles()
        {
            if (white == null) white = Texture2D.whiteTexture;
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            label.normal.textColor = Color.white;
            big = new GUIStyle(label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(label) { fontSize = 16, fontStyle = FontStyle.Normal, alignment = TextAnchor.LowerRight, wordWrap = true };
        }
    }
}
