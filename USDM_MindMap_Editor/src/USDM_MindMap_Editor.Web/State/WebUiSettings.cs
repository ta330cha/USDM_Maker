namespace USDM_MindMap_Editor.Web.State;

// Screen-space interaction settings; never serialized into a document.
public sealed record WebUiSettings
{
    public double ChildAddHitHeight { get; init; } = 10;
    public double ChildAddDragTolerance { get; init; } = 4;
    public double ChildAddWidthRatio { get; init; } = .5;
    public double TreeDragTolerance { get; init; } = 4;
    // Provisional screen-space dimensions; evaluated separately from canvas zoom.
    public double ToolbarToggleDiameter { get; init; } = 36;
    public double ToolbarToggleIconSize { get; init; } = 20;
    public double ToolbarToggleInset { get; init; } = 8;
}
