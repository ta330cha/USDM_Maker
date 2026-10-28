using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.VisualTests;

// Input documents only. All toolbar, viewport and document operations belong to Home.
public static class ToolbarScenarioFactory
{
    public static IReadOnlyList<int> ZoomPercents { get; } = [25, 100, 200];
    public static IReadOnlyList<string> ViewModes { get; } = ["plain", "saved-pan"];
    public static IReadOnlyList<string> WidthModes { get; } = ["wide", "narrow"];

    public static UsdmDocument Create(int zoomPercent, string viewMode)
    {
        if (!ZoomPercents.Contains(zoomPercent)) throw new ArgumentOutOfRangeException(nameof(zoomPercent));
        if (!ViewModes.Contains(viewMode)) throw new ArgumentException("不明な表示条件です。", nameof(viewMode));
        var document = new UsdmDocument { Title = "上部メニュー折り畳み確認：3背景・各4階層" };
        for (var number = 1; number <= 3; number++)
        {
            var id = $"{number:D3}";
            var lowerId = $"{id}-001";
            document.Backgrounds.Add(new() { Id = id, Text = $"背景 {number}：配置を維持" });
            document.Requirements.Add(new()
            {
                Id = id, ParentBackgroundId = id, Request = "上部メニューだけを切り替える",
                Reason = "ノードの論理座標と接続線を維持する。\nキャンバス内のパンと倍率も変えない。"
            });
            document.Requirements.Add(new()
            {
                Id = lowerId, ParentRequirementId = id, Request = "切り替え後も通常操作を続ける",
                Reason = "新しい画面原点で理由操作と子追加を確認する。"
            });
            document.Specifications.Add(new()
            {
                Id = $"{lowerId}-001", ParentRequirementId = lowerId,
                Specification = "最新の表示領域に従って操作する"
            });
        }
        document.View.BackgroundPositions = new(StringComparer.Ordinal)
        {
            ["001"] = new() { X = 96, Y = 96 },
            ["002"] = new() { X = 1400, Y = 96 },
            ["003"] = new() { X = 96, Y = 1300 }
        };
        document.View.Zoom = zoomPercent / 100d;
        if (viewMode == "saved-pan") document.View.Pan = new() { X = 83.25, Y = 41.5 };
        return document;
    }
}
