using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.VisualTests;

// Initial input only. Production Home/DocumentEditingSession own every operation.
public static class DirectionalScenarioFactory
{
    public static IReadOnlyList<string> Names { get; } = ["right", "down"];
    public static IReadOnlyList<int> ZoomPercents { get; } = [25, 100, 200];
    public static IReadOnlyList<string> ViewModes { get; } = ["plain", "saved-pan", "negative-pan"];

    public static UsdmDocument Create(string scenario, int zoomPercent, string viewMode)
    {
        if (!Names.Contains(scenario)) throw new ArgumentException("不明な方向別押し出しケースです。", nameof(scenario));
        if (!ZoomPercents.Contains(zoomPercent)) throw new ArgumentOutOfRangeException(nameof(zoomPercent));
        if (!ViewModes.Contains(viewMode)) throw new ArgumentException("不明な表示条件です。", nameof(viewMode));

        var settings = new LayoutSettings();
        var document = scenario == "right" ? CreateRight(settings) : CreateDown(settings);
        document.Title = $"方向別押し出し確認（2026-10-28）: {scenario}/{zoomPercent}/{viewMode}";
        document.View.Zoom = zoomPercent / 100d;
        if (viewMode == "saved-pan") document.View.Pan = new PanState { X = 83.25, Y = 41.5 };
        if (viewMode == "negative-pan")
        {
            var origin = document.View.BackgroundPositions["001"];
            var dx = -360.5 - origin.X;
            var dy = -220.25 - origin.Y;
            foreach (var position in document.View.BackgroundPositions.Values)
            {
                position.X += dx;
                position.Y += dy;
            }
            document.View.Pan = new PanState
            {
                X = 96 + 360.5 * document.View.Zoom,
                Y = 96 + 220.25 * document.View.Zoom
            };
        }
        return document;
    }

    private static UsdmDocument CreateRight(LayoutSettings settings)
    {
        var document = new UsdmDocument
        {
            Backgrounds =
            [
                new() { Id = "001", Text = "右へ拡大する背景" },
                new() { Id = "002", Text = "右側の背景2" },
                new() { Id = "003", Text = "右側の背景3" },
                new() { Id = "004", Text = "非衝突の背景4" }
            ]
        };
        var stride = settings.NodeWidth + settings.SiblingGap;
        for (var index = 0; index < 3; index++)
            document.View.BackgroundPositions[$"{index + 1:000}"] = new CanvasPosition
            {
                X = settings.CanvasMargin + index * stride,
                Y = settings.CanvasMargin
            };
        document.View.BackgroundPositions["004"] = new CanvasPosition
        {
            X = settings.CanvasMargin,
            Y = settings.CanvasMargin + 4 * (settings.MinNodeHeight + settings.ParentChildVerticalGap)
        };
        return document;
    }

    private static UsdmDocument CreateDown(LayoutSettings settings)
    {
        var document = new UsdmDocument
        {
            Backgrounds =
            [
                new() { Id = "001", Text = "下へ拡大する背景" },
                new() { Id = "002", Text = "下側の背景2" },
                new() { Id = "003", Text = "下側の背景3" },
                new() { Id = "004", Text = "非衝突の背景4" }
            ],
            Requirements =
            [
                new()
                {
                    Id = "001", Request = "理由を展開する", ParentBackgroundId = "001",
                    Reason = string.Join('\n', Enumerable.Range(1, 12).Select(index => $"下方向の拡大確認 {index} 行目"))
                },
                new() { Id = "001-001", Request = "既存の下位要求", ParentRequirementId = "001" }
            ],
            Specifications = [new() { Id = "001-001-001", Specification = "既存の仕様", ParentRequirementId = "001-001" }]
        };
        // Derive the initial downward chain from production layout, not duplicated
        // height guesses. No expansion or relocation is performed by this fixture.
        var initial = new AutoLayoutService().Calculate(new LayoutRequest { Document = document, Settings = settings });
        document.View.BackgroundPositions = initial.BackgroundPositions.ToDictionary(entry => entry.Key,
            entry => new CanvasPosition { X = entry.Value.X, Y = entry.Value.Y }, StringComparer.Ordinal);
        var source = initial.Trees.Single(tree => tree.BackgroundId == "001");
        document.View.BackgroundPositions["004"] = new CanvasPosition
        {
            X = source.Bounds.Right + 4 * settings.NodeWidth,
            Y = document.View.BackgroundPositions["001"].Y
        };
        return document;
    }
}
