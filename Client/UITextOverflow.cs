using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MapLootEditorLite.Client
{
    // Keeps a single-line legacy Text inside its rect by truncating with an ellipsis. The full string is kept
    // and shown in a hover tooltip only while truncated. Re-evaluates when the rect width or the text changes.
    [RequireComponent(typeof(Text))]
    public sealed class EllipsisText : MonoBehaviour
    {
        private const string Ellipsis = "\u2026";

        private Text _text;
        private string _full = "";
        private string _applied = "";
        private float _lastWidth = -1f;
        private UITooltipTarget _tooltip;

        public GameObject HoverTarget;

        private void Awake()
        {
            _text = GetComponent<Text>();
            _full = _text.text ?? "";
            _applied = _full;
        }

        private void OnEnable()
        {
            _lastWidth = -1f;
        }

        private void LateUpdate()
        {
            if (_text == null)
                return;

            var current = _text.text ?? "";
            if (current != _applied)
            {
                _full = current;
                _applied = current;
                _lastWidth = -1f;
            }

            var width = _text.rectTransform.rect.width;
            if (width <= 1f || Mathf.Approximately(width, _lastWidth))
                return;
            _lastWidth = width;
            Apply(width);
        }

        private void Apply(float width)
        {
            var truncated = false;
            var result = _full;
            if (_full.Length > 0 && UIBuilder.MeasureTextWidth(_text, _full) > width)
            {
                truncated = true;
                var lo = 0;
                var hi = _full.Length;
                while (lo < hi)
                {
                    var mid = (lo + hi + 1) / 2;
                    if (UIBuilder.MeasureTextWidth(_text, _full.Substring(0, mid) + Ellipsis) <= width)
                        lo = mid;
                    else
                        hi = mid - 1;
                }
                result = lo > 0 ? _full.Substring(0, lo).TrimEnd() + Ellipsis : Ellipsis;
            }

            if (result != _applied)
            {
                _text.text = result;
                _applied = result;
            }

            var host = HoverTarget != null ? HoverTarget : gameObject;
            if (host == gameObject)
                _text.raycastTarget = truncated;
            if (truncated)
            {
                if (_tooltip == null)
                    _tooltip = host.GetComponent<UITooltipTarget>() ?? host.AddComponent<UITooltipTarget>();
                _tooltip.Content = _full;
                _tooltip.enabled = true;
            }
            else if (_tooltip != null)
            {
                _tooltip.Content = null;
                _tooltip.enabled = false;
            }
        }
    }

    // Hover target that shows UITooltip after a short delay. Disabled while it has no content.
    public sealed class UITooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Content;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (enabled && !string.IsNullOrEmpty(Content))
                UITooltip.Show(this, Content);
        }

        public void OnPointerExit(PointerEventData eventData) => UITooltip.Hide(this);
        private void OnDisable() => UITooltip.Hide(this);
        private void OnDestroy() => UITooltip.Hide(this);
    }

    // One shared tooltip panel per canvas. It never blocks raycasts and is kept inside the screen.
    public static class UITooltip
    {
        private const float ShowDelay = 0.35f;
        private const float MaxWidth = 380f;

        private static RectTransform _panel;
        private static Text _label;
        private static LayoutElement _labelLayout;
        private static UITooltipTarget _owner;
        private static float _showAt;
        private static Canvas _canvas;

        public static void Show(UITooltipTarget owner, string content)
        {
            var canvas = owner.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;
            canvas = canvas.rootCanvas;
            EnsurePanel(canvas);

            _owner = owner;
            _canvas = canvas;
            _showAt = Time.unscaledTime + ShowDelay;
            _label.text = content;
            var width = Mathf.Min(UIBuilder.MeasureTextWidth(_label, content) + 2f, MaxWidth);
            _labelLayout.preferredWidth = Mathf.Max(20f, width);
            _panel.gameObject.SetActive(false);
        }

        public static void Hide(UITooltipTarget owner)
        {
            if (_owner != owner)
                return;
            _owner = null;
            if (_panel != null)
                _panel.gameObject.SetActive(false);
        }

        private static void EnsurePanel(Canvas canvas)
        {
            if (_panel != null && _panel.transform.parent == canvas.transform)
                return;
            if (_panel != null)
                Object.Destroy(_panel.gameObject);

            _panel = UIBuilder.CreatePanel("UITooltip", canvas.transform, new Color(0.05f, 0.05f, 0.05f, 0.96f));
            _panel.GetComponent<Image>().raycastTarget = false;
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0f, 1f);
            UIBuilder.AddVerticalLayout(_panel, 4, 0, true, true);
            UIBuilder.AddContentSizeFitter(_panel, ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize);

            _label = UIBuilder.CreateText(_panel, "", 11, Color.white);
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.alignment = TextAnchor.UpperLeft;
            _labelLayout = _label.GetComponent<LayoutElement>();
            _labelLayout.preferredHeight = -1f;

            // The panel is inactive when hidden, so a driver on the always-active canvas positions it.
            if (canvas.GetComponent<UITooltipDriver>() == null)
                canvas.gameObject.AddComponent<UITooltipDriver>();
            _panel.gameObject.SetActive(false);
        }

        private static void Place()
        {
            var scale = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
            var screen = new Vector2(Screen.width, Screen.height) / scale;
            var mouse = (Vector2)Input.mousePosition / scale;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            var size = _panel.rect.size;
            var x = Mathf.Clamp(mouse.x + 12f, 2f, Mathf.Max(2f, screen.x - size.x - 2f));
            var y = mouse.y - 16f;
            if (y - size.y < 2f)
                y = mouse.y + 14f + size.y;
            y = Mathf.Clamp(y, size.y + 2f, Mathf.Max(size.y + 2f, screen.y - 2f));
            _panel.anchoredPosition = new Vector2(x, y);
        }

        internal static void Tick()
        {
            if (_owner == null || _panel == null)
                return;
            if (!_owner.isActiveAndEnabled)
            {
                _owner = null;
                _panel.gameObject.SetActive(false);
                return;
            }
            if (Time.unscaledTime < _showAt)
                return;

            if (!_panel.gameObject.activeSelf)
            {
                _panel.gameObject.SetActive(true);
                _panel.SetAsLastSibling();
            }
            Place();
        }
    }

    public sealed class UITooltipDriver : MonoBehaviour
    {
        private void LateUpdate() => UITooltip.Tick();
    }
}
