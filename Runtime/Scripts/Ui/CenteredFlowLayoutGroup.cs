using UnityEngine;
using UnityEngine.UI;

public class CenteredFlowLayoutGroup : LayoutGroup
{
    public Vector2 spacing;
    public Vector2 cellSize = new Vector2(100, 100);

    public override void CalculateLayoutInputHorizontal() => base.CalculateLayoutInputHorizontal();
    public override void CalculateLayoutInputVertical() => CalculateLayout();
    public override void SetLayoutHorizontal() => CalculateLayout();
    public override void SetLayoutVertical() => CalculateLayout();

    private void CalculateLayout()
    {
        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;

        float workingWidth = width - padding.left - padding.right;
        float xOffset = padding.left;
        float yOffset = padding.top;

        int itemsPerRow = Mathf.FloorToInt((workingWidth + spacing.x) / (cellSize.x + spacing.x));
        itemsPerRow = Mathf.Max(1, itemsPerRow);

        for (int i = 0; i < rectChildren.Count; i += itemsPerRow)
        {
            int rowCount = Mathf.Min(itemsPerRow, rectChildren.Count - i);

            float rowWidth = (rowCount * cellSize.x) + ((rowCount - 1) * spacing.x);
            float centerOffset = (workingWidth - rowWidth) / 2f;

            for (int j = 0; j < rowCount; j++)
            {
                var child = rectChildren[i + j];
                float xPos = padding.left + centerOffset + (j * (cellSize.x + spacing.x));
                float yPos = yOffset;

                SetChildAlongAxis(child, 0, xPos, cellSize.x);
                SetChildAlongAxis(child, 1, yPos, cellSize.y);
            }

            yOffset += cellSize.y + spacing.y;
        }
    }
}