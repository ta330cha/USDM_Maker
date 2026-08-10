namespace USDM_MindMap_Editor.Core.Layout;

public sealed record LayoutSettings
{
    public const double DefaultNodeWidth = 240;
    public const double DefaultSiblingGap = 32;
    public const double DefaultLevelGap = 96;
    public const double DefaultCanvasMargin = 96;
    public const double DefaultLineHeight = 22;
    public const double DefaultHorizontalPadding = 24;
    public const double DefaultVerticalPadding = 20;
    public const int DefaultCharactersPerLine = 28;

    public double SiblingGap { get; init; } = DefaultSiblingGap;
    public double LevelGap { get; init; } = DefaultLevelGap;
    public double CanvasMargin { get; init; } = DefaultCanvasMargin;
    public double NodeWidth { get; init; } = DefaultNodeWidth;
    public double LineHeight { get; init; } = DefaultLineHeight;
    public double HorizontalPadding { get; init; } = DefaultHorizontalPadding;
    public double VerticalPadding { get; init; } = DefaultVerticalPadding;
    public int CharactersPerLine { get; init; } = DefaultCharactersPerLine;
    public double MinZoom { get; init; } = 0.25;
    public double InitialZoom { get; init; } = 1.0;
    public double MaxZoom { get; init; } = 2.0;
}
