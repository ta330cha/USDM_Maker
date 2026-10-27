using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

/// <summary>Shared connection scenarios for geometric tests and the browser verification harness.</summary>
public static class ConnectionScenarioFactory
{
    public static IReadOnlyList<string> Names { get; } =
        ["single", "multiple", "upward", "four-level", "multiple-backgrounds", "long-text", "long-expanded"];

    public static UsdmDocument Create(string name)
    {
        if (!Names.Contains(name)) throw new ArgumentException($"Unknown scenario: {name}", nameof(name));
        var document = new UsdmDocument { Title = name };
        var backgroundCount = name == "multiple-backgrounds" ? 3 : 1;
        var childCount = name is "multiple" or "upward" ? 7 : name == "multiple-backgrounds" ? 2 : 1;
        var fourLevels = name is "four-level" or "multiple-backgrounds" or "long-text" or "long-expanded";
        var longText = name is "long-text" or "long-expanded";
        var upperNumber = 0;
        for (var backgroundNumber = 1; backgroundNumber <= backgroundCount; backgroundNumber++)
        {
            var backgroundId = backgroundNumber.ToString("D3");
            document.Backgrounds.Add(new()
            {
                Id = backgroundId,
                Text = longText ? Text("背景", 9) : $"背景 {backgroundNumber}：操作を分かりやすくする"
            });
            for (var childNumber = 1; childNumber <= childCount; childNumber++)
            {
                var upperId = (++upperNumber).ToString("D3");
                document.Requirements.Add(new()
                {
                    Id = upperId,
                    Request = longText ? Text("上位要求", 12) : $"上位要求 {upperId}：目的に合わせて操作する",
                    Reason = longText ? Text("理由", 18) : "利用者が手順を理解できるため",
                    ParentBackgroundId = backgroundId
                });
                if (!fourLevels) continue;
                var lowerCount = name == "multiple-backgrounds" ? 2 : 1;
                for (var lowerNumber = 1; lowerNumber <= lowerCount; lowerNumber++)
                {
                    var lowerId = $"{upperId}-{lowerNumber:D3}";
                    document.Requirements.Add(new()
                    {
                        Id = lowerId,
                        Request = longText ? Text("下位要求", 5) : $"下位要求 {lowerId}：入力内容を確認する",
                        Reason = longText ? Text("理由", 11) : "誤った操作を防ぐため",
                        ParentRequirementId = upperId
                    });
                    document.Specifications.Add(new()
                    {
                        Id = $"{lowerId}-001",
                        Specification = longText ? Text("仕様", 8) : "確認結果を画面に表示する",
                        ParentRequirementId = lowerId
                    });
                }
            }
        }
        if (name == "upward")
        {
            document.Requirements[0].Request = Text("上側の長い要求", 5);
            document.Requirements[^1].Request = Text("下側の要求", 3);
        }
        if (name == "long-expanded")
            foreach (var requirement in document.Requirements)
                document.View.Nodes[$"requirement:{requirement.Id}"] = new() { ReasonExpanded = true };
        return document;
    }

    public static LayoutRequest CreateRequest(string name)
    {
        var document = Create(name);
        return new LayoutRequest
        {
            Document = document,
            ExpandedReasonNodeIds = document.View.Nodes.Where(x => x.Value.ReasonExpanded)
                .Select(x => x.Key).ToHashSet(StringComparer.Ordinal)
        };
    }

    private static string Text(string prefix, int lines) => string.Join('\n',
        Enumerable.Range(1, lines).Select(index => $"{prefix} {index}：長文の折り返しと接続位置が一致することを確認する。"));
}
