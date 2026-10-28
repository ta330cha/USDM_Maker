namespace USDM_MindMap_Editor.Core.Layout;

public interface ILayoutEngine
{
    LayoutResult Calculate(LayoutRequest request);
}
