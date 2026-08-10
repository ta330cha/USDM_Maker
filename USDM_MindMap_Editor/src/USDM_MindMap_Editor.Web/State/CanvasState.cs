using USDM_MindMap_Editor.Core.Layout;

namespace USDM_MindMap_Editor.Web.State;

public sealed class CanvasState
{
    public double Zoom { get; private set; } = 1.0;
    public double PanX { get; private set; }
    public double PanY { get; private set; }

    public void Reset(LayoutSettings settings)
    {
        Zoom = settings.InitialZoom;
        PanX = 0;
        PanY = 0;
    }

    public void ZoomAt(double pointerX, double pointerY, double factor, LayoutSettings settings)
    {
        var nextZoom = Math.Clamp(Zoom * factor, settings.MinZoom, settings.MaxZoom);
        var canvasX = (pointerX - PanX) / Zoom;
        var canvasY = (pointerY - PanY) / Zoom;
        Zoom = nextZoom;
        PanX = pointerX - canvasX * Zoom;
        PanY = pointerY - canvasY * Zoom;
    }

    public void Synchronize(double zoom, double panX, double panY, LayoutSettings settings)
    {
        Zoom = Math.Clamp(zoom, settings.MinZoom, settings.MaxZoom);
        PanX = panX;
        PanY = panY;
    }
}
