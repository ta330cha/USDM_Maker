using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Layout;

public sealed class NodeSizePredictor
{
    private readonly LayoutSettings _settings;

    public NodeSizePredictor(LayoutSettings? settings = null) => _settings = settings ?? new();

    public NodeSize Predict(LayoutNodeKind kind, string text, string? reason, bool reasonExpanded)
    {
        var textLines = CountLines(text);
        var reasonLines = reasonExpanded && !string.IsNullOrWhiteSpace(reason) ? CountLines(reason) : 0;
        var reasonHeight = reasonLines == 0
            ? 0
            : _settings.SectionGap + reasonLines * _settings.LineHeight;
        var height = (_settings.VerticalPadding * 2)
            + _settings.IdAreaHeight
            + _settings.SectionGap
            + textLines * _settings.LineHeight
            + reasonHeight
            + _settings.OperationAreaHeight;
        height = Math.Max(height, _settings.MinNodeHeight);
        return new NodeSize(_settings.NodeWidth, height);
    }

    private int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        return text.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)_settings.EstimatedCharsPerLine)));
    }
}
