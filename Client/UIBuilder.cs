using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MapLootEditorLite.Client
{
    public static class UIBuilder
    {
        private static Font _font;

        public static Font GetFont()
        {
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _font;
        }

        public static GameObject CreateCanvas(string name, Transform parent, int sortingOrder = 1000)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            var raycaster = go.AddComponent<GraphicRaycaster>();
            raycaster.ignoreReversedGraphics = true;
            return go;
        }

        public static EventSystem EnsureEventSystem(Transform parent)
        {
            var existing = parent.GetComponentsInChildren<EventSystem>(true).FirstOrDefault();
            if (existing != null)
                return existing;

            var go = new GameObject("MLE_EventSystem", typeof(EventSystem));
            go.transform.SetParent(parent, false);
            var eventSystem = go.GetComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            return eventSystem;
        }

        public static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;
            return rt;
        }

        public static RectTransform CreatePanel(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = CreatePanel(name, parent, color);
            rt.GetComponent<Image>().sprite = sprite;
            return rt;
        }

        public static Text CreateText(Transform parent, string text, int fontSize = 12, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = GetFont();
            t.text = Locale.Get(text);
            t.fontSize = fontSize;
            t.color = color ?? new Color(0.82f, 0.82f, 0.82f, 1f);
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 4;
            return t;
        }

        public static Text CreateLabel(Transform parent, string text, int fontSize = 12, int width = 0, int height = 20)
        {
            var t = CreateText(parent, text, fontSize, Color.white, FontStyle.Normal);
            t.alignment = TextAnchor.MiddleLeft;
            var rt = t.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);
            if (width > 0)
                AddLayoutElement(t.gameObject, null, height, width, height, 0, 0);
            else
                AddLayoutElement(t.gameObject, null, height, null, height, 1, 0);
            return t;
        }

        public static Button CreateButton(Transform parent, string label, UnityAction onClick, int width = 80, int height = 24, int fontSize = 12)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.24f, 0.24f, 0.24f, 1f);
            var button = go.AddComponent<Button>();
            var cb = button.colors;
            cb.normalColor = new Color(0.24f, 0.24f, 0.24f, 1f);
            cb.highlightedColor = new Color(0.34f, 0.34f, 0.34f, 1f);
            cb.pressedColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            cb.disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.1f;
            button.colors = cb;
            var text = CreateText(go.transform, label, fontSize, new Color(0.82f, 0.82f, 0.82f, 1f), FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var textRt = text.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            button.onClick.AddListener(onClick);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);
            AddLayoutElement(go, null, height, width, height, 0, 0);
            return button;
        }

        public static InputField CreateInputField(Transform parent, string placeholder, string text, UnityAction<string> onValueChanged, int width = 120, int height = 22)
        {
            var go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.07f, 0.07f, 0.07f, 1f);
            var input = go.AddComponent<InputField>();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(go.transform, false);
            var placeholderText = placeholderGo.AddComponent<Text>();
            placeholderText.font = GetFont();
            placeholderText.text = Locale.Get(placeholder);
            placeholderText.fontSize = 12;
            placeholderText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.raycastTarget = false;
            var placeholderRt = placeholderGo.GetComponent<RectTransform>();
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.offsetMin = new Vector2(4, 0);
            placeholderRt.offsetMax = new Vector2(-4, 0);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textText = textGo.AddComponent<Text>();
            textText.font = GetFont();
            textText.text = text;
            textText.fontSize = 12;
            textText.color = new Color(0.82f, 0.82f, 0.82f, 1f);
            textText.alignment = TextAnchor.MiddleLeft;
            textText.raycastTarget = false;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(4, 0);
            textRt.offsetMax = new Vector2(-4, 0);

            input.textComponent = textText;
            input.placeholder = placeholderText;
            input.text = text;
            input.contentType = InputField.ContentType.Standard;
            if (onValueChanged != null)
                input.onValueChanged.AddListener(onValueChanged);
            AddLayoutElement(go, null, height, width, height, 0, 0);
            return input;
        }

        public static Toggle CreateToggle(Transform parent, string label, bool isOn, UnityAction<bool> onValueChanged, int height = 20)
        {
            var go = new GameObject("Toggle", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var toggle = go.AddComponent<Toggle>();
            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(20, 20);

            var checkGo = new GameObject("Checkmark", typeof(RectTransform));
            checkGo.transform.SetParent(go.transform, false);
            var checkImage = checkGo.AddComponent<Image>();
            checkImage.color = new Color(0.25f, 0.55f, 0.85f, 1f);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(3, 3);
            checkRt.offsetMax = new Vector2(-3, -3);

            var hasLabel = !string.IsNullOrEmpty(label);
            var labelWidth = hasLabel ? 140 : 0;
            var totalWidth = height + 4 + labelWidth;
            if (hasLabel)
            {
                var labelText = CreateText(go.transform, label, 12, new Color(0.82f, 0.82f, 0.82f, 1f));
                labelText.raycastTarget = false;
                var labelRt = labelText.GetComponent<RectTransform>();
                labelRt.anchorMin = new Vector2(0, 0.5f);
                labelRt.anchorMax = new Vector2(0, 0.5f);
                labelRt.pivot = new Vector2(0, 0.5f);
                labelRt.anchoredPosition = new Vector2(height + 4, 0);
                labelRt.sizeDelta = new Vector2(labelWidth, height);
            }

            AddLayoutElement(go, null, height, totalWidth, height, 0, 0);

            toggle.graphic = checkImage;
            toggle.targetGraphic = image;
            toggle.isOn = isOn;
            if (onValueChanged != null)
                toggle.onValueChanged.AddListener(onValueChanged);
            return toggle;
        }

        public static ScrollRect CreateScrollView(Transform parent, out RectTransform content, out RectTransform viewport, int width, int height, int scrollbarWidth = 14)
        {
            var go = new GameObject("ScrollView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var scroll = go.AddComponent<ScrollRect>();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);

            viewport = CreatePanel("Viewport", go.transform, new Color(0.1f, 0.1f, 0.1f, 0.4f));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = new Vector2(1, 1);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-scrollbarWidth, 0);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            content = CreatePanel("Content", viewport, new Color(0, 0, 0, 0));
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1);
            content.sizeDelta = new Vector2(0, 0);
            AddContentSizeFitter(content, ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);

            var scrollbarGo = CreatePanel("Scrollbar", go.transform, new Color(0.18f, 0.18f, 0.18f, 1f));
            scrollbarGo.anchorMin = new Vector2(1, 0);
            scrollbarGo.anchorMax = Vector2.one;
            scrollbarGo.pivot = new Vector2(1, 0.5f);
            scrollbarGo.sizeDelta = new Vector2(scrollbarWidth, 0);
            scrollbarGo.anchoredPosition = Vector2.zero;
            var scrollbar = scrollbarGo.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handle = CreatePanel("Handle", scrollbarGo, new Color(0.4f, 0.4f, 0.4f, 1f));
            handle.anchorMin = new Vector2(0, 0);
            handle.anchorMax = new Vector2(1, 1);
            handle.offsetMin = new Vector2(2, 2);
            handle.offsetMax = new Vector2(-2, -2);
            scrollbar.handleRect = handle;
            scrollbar.size = 0.2f;
            scrollbar.numberOfSteps = 0;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.inertia = false;

            return scroll;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, int padding = 4, int spacing = 4, bool childControlWidth = true, bool childControlHeight = false)
        {
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(padding, padding, padding, padding);
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = childControlWidth;
            vlg.childControlHeight = childControlHeight;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return vlg;
        }

        public static VerticalLayoutGroup AddVerticalLayout(RectTransform rt, int padding = 4, int spacing = 4, bool childControlWidth = true, bool childControlHeight = false)
        {
            return AddVerticalLayout(rt.gameObject, padding, spacing, childControlWidth, childControlHeight);
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, int padding = 4, int spacing = 4, bool childControlWidth = false, bool childControlHeight = false)
        {
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(padding, padding, padding, padding);
            hlg.spacing = spacing;
            hlg.childAlignment = TextAnchor.UpperLeft;
            hlg.childControlWidth = childControlWidth;
            hlg.childControlHeight = childControlHeight;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            return hlg;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(RectTransform rt, int padding = 4, int spacing = 4, bool childControlWidth = false, bool childControlHeight = false)
        {
            return AddHorizontalLayout(rt.gameObject, padding, spacing, childControlWidth, childControlHeight);
        }

        public static LayoutElement AddLayoutElement(GameObject go, int? minWidth = null, int? minHeight = null, int? preferredWidth = null, int? preferredHeight = null, float? flexibleWidth = null, float? flexibleHeight = null)
        {
            var le = go.AddComponent<LayoutElement>();
            if (minWidth.HasValue) le.minWidth = minWidth.Value;
            if (minHeight.HasValue) le.minHeight = minHeight.Value;
            if (preferredWidth.HasValue) le.preferredWidth = preferredWidth.Value;
            if (preferredHeight.HasValue) le.preferredHeight = preferredHeight.Value;
            if (flexibleWidth.HasValue) le.flexibleWidth = flexibleWidth.Value;
            if (flexibleHeight.HasValue) le.flexibleHeight = flexibleHeight.Value;
            return le;
        }

        public static LayoutElement AddLayoutElement(RectTransform rt, int? minWidth = null, int? minHeight = null, int? preferredWidth = null, int? preferredHeight = null, float? flexibleWidth = null, float? flexibleHeight = null)
        {
            return AddLayoutElement(rt.gameObject, minWidth, minHeight, preferredWidth, preferredHeight, flexibleWidth, flexibleHeight);
        }

        public static ContentSizeFitter AddContentSizeFitter(GameObject go, ContentSizeFitter.FitMode horizontal, ContentSizeFitter.FitMode vertical)
        {
            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = horizontal;
            csf.verticalFit = vertical;
            return csf;
        }

        public static ContentSizeFitter AddContentSizeFitter(RectTransform rt, ContentSizeFitter.FitMode horizontal, ContentSizeFitter.FitMode vertical)
        {
            return AddContentSizeFitter(rt.gameObject, horizontal, vertical);
        }

        public static void SetAnchorsStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void SetAnchoredSize(RectTransform rt, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
        }
    }
}
