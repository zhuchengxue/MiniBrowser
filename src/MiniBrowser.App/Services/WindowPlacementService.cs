namespace MiniBrowser.App.Services;

public static class WindowPlacementService
{
    public static PopupBounds Calculate(
        double workLeft,
        double workTop,
        double workRight,
        double workBottom,
        double width,
        double height,
        string popupPosition,
        double margin = 8)
    {
        var availableWidth = Math.Max(1, workRight - workLeft - (margin * 2));
        var availableHeight = Math.Max(1, workBottom - workTop - (margin * 2));
        var safeWidth = Math.Clamp(width, 1, availableWidth);
        var safeHeight = Math.Clamp(height, 1, availableHeight);
        var minLeft = workLeft + margin;
        var maxLeft = workRight - safeWidth - margin;
        var targetLeft = popupPosition switch
        {
            "BottomLeft" => minLeft,
            "BottomCenter" => workLeft + ((workRight - workLeft) - safeWidth) / 2,
            _ => maxLeft
        };

        return new PopupBounds(
            Math.Clamp(targetLeft, minLeft, maxLeft),
            workBottom - safeHeight - margin,
            safeWidth,
            safeHeight);
    }
}

public readonly record struct PopupBounds(double Left, double Top, double Width, double Height);
