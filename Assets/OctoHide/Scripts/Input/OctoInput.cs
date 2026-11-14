using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OctoHide
{
    /// <summary>
    /// Тонкая обёртка над вводом: работает и с новым Input System, и со старым Input Manager.
    /// В прототипе намеренно без Input Actions — позже заменим на ассет действий с ребиндом.
    /// </summary>
    public static class OctoInput
    {
        public static Vector2 Move
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current;
                if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                var gp = Gamepad.current;
                Vector2 v = new Vector2(x, y);
                if (gp != null) v += gp.leftStick.ReadValue();
                return Vector2.ClampMagnitude(v, 1f);
#else
                return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#endif
            }
        }

        public static Vector2 Look
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Vector2 v = Vector2.zero;
                if (Mouse.current != null) v += Mouse.current.delta.ReadValue() * 0.1f;
                if (Gamepad.current != null) v += Gamepad.current.rightStick.ReadValue() * 3f;
                return v;
#else
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
            }
        }

        public static float Zoom
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

        public static bool JetPressed => Down(KeyCode.Space);
        public static bool SamplePressed => Down(KeyCode.E);
        public static bool ResetSkinPressed => Down(KeyCode.R);
        public static bool CyclePalettePressed => Down(KeyCode.C);
        public static bool PapillaeUp => Held(KeyCode.X);
        public static bool PapillaeDown => Held(KeyCode.Z);
        public static bool UnlockCursorPressed => Down(KeyCode.Escape);
        public static bool EyesTogglePressed => Down(KeyCode.Q);
        public static bool ThrowPressed => Down(KeyCode.F);
        public static bool ClickPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
                return Input.GetMouseButtonDown(0);
#endif
            }
        }

        static readonly KeyCode[] PoseKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8
        };

        /// <summary>Возвращает индекс позы 0..7, если в этом кадре нажата 1..8, иначе -1.</summary>
        public static int PosePressed
        {
            get
            {
                for (int i = 0; i < PoseKeys.Length; i++)
                    if (Down(PoseKeys[i])) return i;
                return -1;
            }
        }

        static bool Down(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            var key = ToKey(code);
            return key != null && key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(code);
#endif
        }

        static bool Held(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            var key = ToKey(code);
            return key != null && key.isPressed;
#else
            return Input.GetKey(code);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl ToKey(KeyCode code)
        {
            var k = Keyboard.current;
            switch (code)
            {
                case KeyCode.Space: return k.spaceKey;
                case KeyCode.E: return k.eKey;
                case KeyCode.R: return k.rKey;
                case KeyCode.C: return k.cKey;
                case KeyCode.X: return k.xKey;
                case KeyCode.Z: return k.zKey;
                case KeyCode.Escape: return k.escapeKey;
                case KeyCode.Q: return k.qKey;
                case KeyCode.F: return k.fKey;
                case KeyCode.Alpha1: return k.digit1Key;
                case KeyCode.Alpha2: return k.digit2Key;
                case KeyCode.Alpha3: return k.digit3Key;
                case KeyCode.Alpha4: return k.digit4Key;
                case KeyCode.Alpha5: return k.digit5Key;
                case KeyCode.Alpha6: return k.digit6Key;
                case KeyCode.Alpha7: return k.digit7Key;
                case KeyCode.Alpha8: return k.digit8Key;
                default: return null;
            }
        }
#endif
    }
}
