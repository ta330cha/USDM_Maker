namespace USDM_MindMap_Editor.Web.State;

// Screen-space interaction settings; never serialized into a document.
public sealed record WebUiSettings
{
    public double ChildAddHitHeight { get; init; } = 10;
    public double ChildAddDragTolerance { get; init; } = 4;
    public double ChildAddWidthRatio { get; init; } = .5;
    public double TreeDragTolerance { get; init; } = 4;
}
