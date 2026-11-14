using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Состояние сенсорного управления. Экранные контролы (джойстик, кнопки, зона обзора)
    /// записывают сюда, а OctoInput читает — так геймплей не знает, откуда пришёл ввод.
    /// </summary>
    public static class TouchInputState
    {
        /// <summary>Сенсорное управление показано и используется (на телефоне или включено в редакторе по F2).</summary>
        public static bool Active;

        public static Vector2 Move;
        static Vector2 look;

        static int jet, camo, ink, pose, attack;
        public static bool CamoHeld;

        public static void AddLook(Vector2 delta) => look += delta;

        /// <summary>Отдаёт накопленный сдвиг обзора и обнуляет его (читается один раз за кадр).</summary>
        public static Vector2 ConsumeLook()
        {
            Vector2 v = look;
            look = Vector2.zero;
            return v;
        }

        public static void PressJet() => jet++;
        public static void PressCamo() => camo++;
        public static void PressInk() => ink++;
        public static void PressPose() => pose++;
        public static void PressAttack() => attack++;

        public static bool ConsumeJet() => Consume(ref jet);
        public static bool ConsumeCamo() => Consume(ref camo);
        public static bool ConsumeInk() => Consume(ref ink);
        public static bool ConsumePose() => Consume(ref pose);
        public static bool ConsumeAttack() => Consume(ref attack);

        static bool Consume(ref int counter)
        {
            if (counter <= 0) return false;
            counter = 0;
            return true;
        }

        public static void Reset()
        {
            Move = Vector2.zero;
            look = Vector2.zero;
            jet = camo = ink = pose = attack = 0;
            CamoHeld = false;
        }
    }
}
