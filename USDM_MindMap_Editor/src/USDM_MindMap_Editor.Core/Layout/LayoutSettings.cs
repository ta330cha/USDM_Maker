namespace USDM_MindMap_Editor.Core.Layout;

public sealed record LayoutSettings
{
    public const double DefaultNodeWidth = 280;
    public const double DefaultSiblingGap = 32;
    public const double DefaultParentChildVerticalGap = 96;
    public const double DefaultCanvasMargin = 96;
    public const double DefaultLineHeight = 22;
    public const double DefaultHorizontalPadding = 16;
    public const double DefaultVerticalPadding = 12;
    public const double DefaultIdAreaHeight = 24;
    public const double DefaultSectionGap = 8;
    public const double DefaultOperationAreaHeight = 32;
    public const double DefaultMinNodeHeight = 110;
    public const int DefaultEstimatedCharsPerLine = 18;
    public const double DefaultMeasuredDifferenceRatio = 0.10;
    public const double DefaultMinimumMeasuredDifference = 8;

    public double SiblingGap { get; init; } = DefaultSiblingGap;
    public double ParentChildVerticalGap { get; init; } = DefaultParentChildVerticalGap;
    public double CanvasMargin { get; init; } = DefaultCanvasMargin;
    // Initial visual tuning values; distances still adapt to the current rectangles.
    public double ConnectionBendRatio { get; init; } = 0.5;
    // Minimum drawing padding for the SVG stroke, independent of curve distances.
    public double ConnectionClearance { get; init; } = 8;
    public double NodeWidth { get; init; } = DefaultNodeWidth;
    public double LineHeight { get; init; } = DefaultLineHeight;
    public double HorizontalPadding { get; init; } = DefaultHorizontalPadding;
    public double VerticalPadding { get; init; } = DefaultVerticalPadding;
    public double IdAreaHeight { get; init; } = DefaultIdAreaHeight;
    public double SectionGap { get; init; } = DefaultSectionGap;
    public double OperationAreaHeight { get; init; } = DefaultOperationAreaHeight;
    public double MinNodeHeight { get; init; } = DefaultMinNodeHeight;
    public int EstimatedCharsPerLine { get; init; } = DefaultEstimatedCharsPerLine;
    public double MeasuredDifferenceRatio { get; init; } = DefaultMeasuredDifferenceRatio;
    public double MinimumMeasuredDifference { get; init; } = DefaultMinimumMeasuredDifference;
    public double MinZoom { get; init; } = 0.25;
    public double InitialZoom { get; init; } = 1.0;
    public double MaxZoom { get; init; } = 2.0;
    public double ZoomStep { get; init; } = 0.10;

    public double MeasuredDifferenceThreshold(double estimatedHeight) =>
        Math.Max(MinimumMeasuredDifference, estimatedHeight * MeasuredDifferenceRatio);
}
