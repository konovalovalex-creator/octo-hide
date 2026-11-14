using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace OctoEvo.UI
{
    /// <summary>
    /// Экранное управление для телефона. Строит себя само при старте:
    /// левая половина — плавающий джойстик (появляется там, где коснулся палец),
    /// правая половина — свайп для обзора, справа внизу — кнопки действий.
    /// На ПК скрыто; в редакторе включается по F2, чтобы проверить раскладку.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        [Tooltip("Показывать на ПК с самого начала (для проверки в редакторе)")]
        public bool showInEditor = false;
        [Tooltip("Градусов поворота камеры за свайп на всю высоту экрана (до умножения на чувствительность камеры)")]
        public float lookSpeed = 70f;
        public Color tint = new Color(1f, 1f, 1f, 0.35f);

        Canvas canvas;
        static Sprite circle, ring;

        void Awake()
        {
            EnsureEventSystem();
            Build();
            SetVisible(Application.isMobilePlatform || (Application.isEditor && showInEditor));
        }

        void OnDisable() => TouchInputState.Reset();

        void Update()
        {
            if (OctoInput.ToggleTouchUiPressed) SetVisible(!TouchInputState.Active);
        }

        public void SetVisible(bool visible)
        {
            TouchInputState.Reset();
            TouchInputState.Active = visible;
            if (canvas != null) canvas.gameObject.SetActive(visible);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        void Build()
        {
            if (circle == null) circle = MakeCircleSprite(128, false);
            if (ring == null) ring = MakeCircleSprite(128, true);

            var cgo = new GameObject("TouchCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            RectTransform root = (RectTransform)cgo.transform;

            // Зона обзора — правая половина (создаётся первой, чтобы кнопки были поверх)
            var look = Panel("LookZone", root, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
            look.gameObject.AddComponent<TouchLookZone>().owner = this;

            // Зона джойстика — левая половина
            var moveZone = Panel("MoveZone", root, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            var stickBase = Circle("StickBase", moveZone, ring, 280f, new Vector2(260f, 260f));
            var knob = Circle("StickKnob", stickBase, circle, 120f, Vector2.zero);
            knob.anchorMin = knob.anchorMax = new Vector2(0.5f, 0.5f);
            var joy = moveZone.gameObject.AddComponent<TouchJoystick>();
            joy.stickBase = stickBase;
            joy.knob = knob;

            // Кнопки
            Button(root, "Рывок", new Vector2(-170f, 170f), 190f, TouchInputState.PressJet);
            Button(root, "Схватить", new Vector2(-400f, 120f), 150f, TouchInputState.PressAttack);
            Button(root, "Чернила", new Vector2(-150f, 400f), 140f, TouchInputState.PressInk);
            Button(root, "Маскировка", new Vector2(-370f, 330f), 140f, TouchInputState.PressCamo);
            Button(root, "Поза", new Vector2(-560f, 90f), 120f, TouchInputState.PressPose);
        }

        static RectTransform Panel(string name, RectTransform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // невидимая, но ловит касания
            return rt;
        }

        RectTransform Circle(string name, RectTransform parent, Sprite sprite, float size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = tint;
            img.raycastTarget = false;
            return rt;
        }

        void Button(RectTransform root, string label, Vector2 fromBottomRight, float size, Action onPress)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = fromBottomRight;
            var img = go.GetComponent<Image>();
            img.sprite = circle;
            img.color = tint;
            var btn = go.AddComponent<TouchButton>();
            btn.onPress = onPress;
            btn.image = img;
            btn.baseColor = tint;

            var tgo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var trt = (RectTransform)tgo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = tgo.GetComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = Mathf.RoundToInt(size * 0.17f);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 1f, 1f, 0.9f);
            text.raycastTarget = false;
        }

        internal void AddLook(Vector2 screenDelta)
        {
            TouchInputState.AddLook(screenDelta / Mathf.Max(1f, Screen.height) * lookSpeed);
        }

        static Sprite MakeCircleSprite(int size, bool ringOnly)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = ringOnly ? "TouchRing" : "TouchCircle" };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01((1f - d) * c * 0.5f); // мягкий край
                    if (ringOnly) a *= Mathf.Clamp01((d - 0.86f) * c * 0.5f) + 0.12f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>Плавающий джойстик: основание появляется под пальцем, смещение ручки — направление.</summary>
    public class TouchJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform stickBase;
        public RectTransform knob;
        int pointer = int.MinValue;
        Vector2 restPos;

        void Start() => restPos = stickBase.anchoredPosition;

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            pointer = e.pointerId;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out Vector2 local);
            RectTransform zone = (RectTransform)transform;
            stickBase.anchoredPosition = local - zone.rect.min; // якорь основания — левый нижний угол зоны
            UpdateKnob(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == pointer) UpdateKnob(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            knob.anchoredPosition = Vector2.zero;
            stickBase.anchoredPosition = restPos;
            TouchInputState.Move = Vector2.zero;
        }

        void UpdateKnob(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(stickBase, e.position, e.pressEventCamera, out Vector2 local);
            float radius = stickBase.rect.width * 0.5f;
            Vector2 offset = Vector2.ClampMagnitude(local, radius);
            knob.anchoredPosition = offset;
            Vector2 v = offset / radius;
            // Мёртвая зона, чтобы осьминог не полз от дрожания пальца
            float m = v.magnitude;
            TouchInputState.Move = m < 0.12f ? Vector2.zero : v * Mathf.InverseLerp(0.12f, 1f, m) / m;
        }
    }

    /// <summary>Свайп по правой половине экрана поворачивает камеру.</summary>
    public class TouchLookZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public TouchControls owner;
        int pointer = int.MinValue;

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer == int.MinValue) pointer = e.pointerId;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == pointer && owner != null) owner.AddLook(e.delta);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointer) pointer = int.MinValue;
        }
    }

    /// <summary>Круглая кнопка: срабатывает на касание (не на отпускание) — так отзывчивее в бою.</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action onPress;
        public Image image;
        public Color baseColor;

        public void OnPointerDown(PointerEventData e)
        {
            onPress?.Invoke();
            AudioDirector.Play(Sfx.Bubble, Vector3.zero, 0.35f, 1.3f);
            if (image != null) image.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Min(1f, baseColor.a * 2f));
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (image != null) image.color = baseColor;
        }
    }
}
