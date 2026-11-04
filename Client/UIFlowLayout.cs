using UnityEngine;
using UnityEngine.UI;

namespace MapLootEditorLite.Client
{
    // Left-to-right layout that wraps children onto new lines when the row is too narrow, so button/shape rows
    // stay inside a narrow panel instead of being clipped. Children keep their own preferred sizes.
    public sealed class FlowLayoutGroup : LayoutGroup
    {
        public float spacing = 2f;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            var widest = 0f;
            for (var i = 0; i < rectChildren.Count; i++)
                widest = Mathf.Max(widest, LayoutUtility.GetPreferredWidth(rectChildren[i]));
            var min = widest + padding.horizontal;
            SetLayoutInputForAxis(min, min, 1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            var height = Arrange(rectTransform.rect.width, false);
            SetLayoutInputForAxis(height, height, -1f, 1);
        }

        public override void SetLayoutHorizontal()
        {
        }

        public override void SetLayoutVertical()
        {
            Arrange(rectTransform.rect.width, true);
        }

        private float Arrange(float totalWidth, bool apply)
        {
            var available = Mathf.Max(1f, totalWidth - padding.horizontal);
            float x = 0f, y = 0f, rowHeight = 0f;
            for (var i = 0; i < rectChildren.Count; i++)
            {
                var child = rectChildren[i];
                var w = Mathf.Min(LayoutUtility.GetPreferredWidth(child), available);
                var h = LayoutUtility.GetPreferredHeight(child);
                if (x > 0f && x + w > available)
                {
                    x = 0f;
                    y += rowHeight + spacing;
                    rowHeight = 0f;
                }
                if (apply)
                {
                    SetChildAlongAxis(child, 0, padding.left + x, w);
                    SetChildAlongAxis(child, 1, padding.top + y, h);
                }
                x += w + spacing;
                rowHeight = Mathf.Max(rowHeight, h);
            }
            return padding.top + y + rowHeight;
        }
    }
}
