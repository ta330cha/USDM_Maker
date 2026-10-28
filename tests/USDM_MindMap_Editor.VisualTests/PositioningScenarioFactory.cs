using System.Globalization;
using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Serialization;

namespace USDM_MindMap_Editor.VisualTests;

// Input documents only: Home owns editing, initial correction, and all gestures.
public static class PositioningScenarioFactory
{
    public static IReadOnlyList<string> Names { get; } = ["chain", "negative", "overlap", "legacy2"];
    public static IReadOnlyList<int> ZoomPercents { get; } = [25, 100, 200];

    public static UsdmDocument Create(string scenario, int zoomPercent)
    {
        if (!Names.Contains(scenario)) throw new ArgumentException("不明な背景配置確認ケースです。", nameof(scenario));
        if (!ZoomPercents.Contains(zoomPercent)) throw new ArgumentOutOfRangeException(nameof(zoomPercent));
        var zoom = zoomPercent / 100d;
        if (scenario == "legacy2") return ReadLegacyDocument(zoom);

        var document = CreateChain();
        document.Title = $"背景配置確認: {scenario}";
        document.View.Zoom = zoom;
        if (scenario == "negative")
        {
            var origin = document.View.BackgroundPositions["001"];
            var dx = -360.5 - origin.X;
            var dy = -220.25 - origin.Y;
            foreach (var position in document.View.BackgroundPositions.Values)
            {
                position.X += dx;
                position.Y += dy;
            }
            // Keep the negative root visible without normalizing its saved coordinates.
            document.View.Pan = new PanState { X = 96 + 360.5 * zoom, Y = 96 + 220.25 * zoom };
        }
        else if (scenario == "overlap")
        {
            var origin = document.View.BackgroundPositions["001"];
            document.View.BackgroundPositions["002"] = new() { X = origin.X, Y = origin.Y };
            document.View.BackgroundPositions["003"] = new() { X = origin.X + 40, Y = origin.Y + 40 };
            // Deliberately do not run layout again: Home must detect and report correction.
        }
        return document;
    }

    private static UsdmDocument CreateChain()
    {
        var document = new UsdmDocument
        {
            Backgrounds =
            [
                new() { Id = "001", Text = "拡大元の背景" },
                new() { Id = "002", Text = "近接する背景2" },
                new() { Id = "003", Text = "近接する背景3" },
                new() { Id = "004", Text = "横に離れた背景4" }
            ],
            Requirements =
            [
                new()
                {
                    Id = "001", Request = "理由を展開して拡大", ParentBackgroundId = "001",
                    Reason = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"理由の展開確認 {i} 行目"))
                },
                new() { Id = "001-001", Request = "仕様を追加して拡大", ParentRequirementId = "001" }
            ],
            Specifications = [new() { Id = "001-001-001", Specification = "既存の仕様", ParentRequirementId = "001-001" }]
        };
        var initial = new AutoLayoutService().Calculate(new LayoutRequest { Document = document });
        document.View.BackgroundPositions = initial.BackgroundPositions.ToDictionary(x => x.Key,
            x => new CanvasPosition { X = x.Value.X, Y = x.Value.Y }, StringComparer.Ordinal);
        var source = initial.Trees.Single(x => x.BackgroundId == "001");
        document.View.BackgroundPositions["004"] = new CanvasPosition
        {
            X = source.Bounds.Right + 480,
            Y = document.View.BackgroundPositions["001"].Y
        };
        return document;
    }

    private static UsdmDocument ReadLegacyDocument(double zoom)
    {
        // The selected zoom is already part of the legacy input, not overwritten
        // after deserialization. Pan and expanded reason state must survive as well.
        var yaml = $$"""
            version: "0.1.0"
            format_version: 2
            title: 形式2からの背景配置補完
            backgrounds:
              - id: "001"
                text: 旧形式の背景1
              - id: "002"
                text: 旧形式の背景2
            requirements:
              - id: "001"
                request: 旧ビュー状態を維持
                reason: |
                  読み込み時から理由を展開する
                  ズームとパンを引き継ぐ
                parent_background_id: "001"
            specifications: []
            view:
              zoom: {{zoom.ToString(CultureInfo.InvariantCulture)}}
              pan:
                x: 83.25
                y: -31.5
              nodes:
                "requirement:001":
                  reason_expanded: true
            """;
        return new UsdmYamlSerializer().Deserialize(yaml);
    }
}
