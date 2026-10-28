using USDM_MindMap_Editor.Core.Serialization;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Layout;
using System.Text.Json;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class UsdmYamlSerializerTests
{
    private readonly UsdmYamlSerializer _serializer = new();

    [TestMethod]
    public void RoundTripPreservesDocument()
    {
        var yaml = _serializer.Serialize(CreatePositionedDocument());
        var restored = _serializer.Deserialize(yaml);
        Assert.AreEqual("0.1.0", restored.Version);
        Assert.AreEqual(DocumentFormat.Current, restored.FormatVersion);
        Assert.HasCount(3, restored.Requirements);
        CollectionAssert.AreEqual(new[] { "001-002" }, restored.Specifications[0].RelatedRequirementIds);
    }

    [TestMethod]
    public void OutputIsDeterministicAndUsesStableOrder()
    {
        var document = CreatePositionedDocument();
        document.Requirements.Reverse();
        var first = _serializer.Serialize(document);
        var second = _serializer.Serialize(document);
        Assert.AreEqual(first, second);
        Assert.IsLessThan(
            first.IndexOf("id: \"001-001\"\n", StringComparison.Ordinal),
            first.IndexOf("id: \"001\"\n", StringComparison.Ordinal));
        Assert.IsFalse(first.Contains("\r", StringComparison.Ordinal));
        StringAssert.Contains(first, "version: \"0.1.0\"");
        StringAssert.Contains(first, "format_version: 3");
        StringAssert.Contains(first, "related_requirement_ids:\n  - \"001-002\"");
    }

    [TestMethod]
    public void OmitsEmptyOptionalValues()
    {
        var document = CreatePositionedDocument();
        document.Requirements[0].Reason = "";
        document.Specifications[0].RelatedRequirementIds.Clear();
        var yaml = _serializer.Serialize(document);
        Assert.IsFalse(yaml.Contains("reason:", StringComparison.Ordinal));
        Assert.IsFalse(yaml.Contains("related_requirement_ids:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AcceptsDocumentFromOlderApplicationVersionWhenFormatIsSupported()
    {
        var yaml = _serializer.Serialize(CreatePositionedDocument()).Replace("version: \"0.1.0\"", "version: \"9.0.0\"", StringComparison.Ordinal);
        Assert.AreEqual("9.0.0", _serializer.Deserialize(yaml).Version);
    }

    [TestMethod]
    public void LoadsLegacyYamlWithDefaultView()
    {
        const string yaml = """
            version: "0.1.0"
            title: Legacy
            backgrounds:
              - id: "001"
                text: 背景
            requirements:
              - id: "001"
                request: 要求
                parent_background_id: "001"
            specifications: []
            """;

        var restored = _serializer.Deserialize(yaml);
        Assert.AreEqual(DocumentFormat.Legacy, restored.FormatVersion);
        Assert.AreEqual(1.0, restored.View.Zoom);
        Assert.AreEqual(0, restored.View.Pan.X);
        Assert.IsEmpty(restored.View.Nodes);
        Assert.IsEmpty(restored.View.BackgroundPositions);
    }

    [TestMethod]
    public void RoundTripPreservesViewAndOnlyPersistsExpandedNodes()
    {
        var document = CreatePositionedDocument();
        document.View.Zoom = 1.2;
        document.View.Pan = new PanState { X = -10, Y = 24 };
        document.View.Nodes["requirement:001"] = new NodeViewState { ReasonExpanded = true };
        document.View.Nodes["requirement:001-001"] = new NodeViewState { ReasonExpanded = false };

        var yaml = _serializer.Serialize(document);
        StringAssert.Contains(yaml, "format_version: 3");
        StringAssert.Contains(yaml, "requirement:001");
        Assert.IsFalse(yaml.Contains("001-001\":\n      reason_expanded: false", StringComparison.Ordinal));
        var restored = _serializer.Deserialize(yaml);
        Assert.AreEqual(1.2, restored.View.Zoom, 0.0001);
        Assert.AreEqual(-10, restored.View.Pan.X, 0.0001);
        Assert.IsTrue(restored.View.Nodes["requirement:001"].ReasonExpanded);
    }

    [TestMethod]
    public void RejectsUnsupportedFormatVersion()
    {
        var yaml = _serializer.Serialize(CreatePositionedDocument()).Replace("format_version: 3", "format_version: 9", StringComparison.Ordinal);
        var exception = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Deserialize(yaml));
        Assert.IsTrue(exception.Errors.Any(error => error.Code == "format_version.unsupported"));
    }

    [TestMethod]
    public void QuotesFiveDigitIdentifiers()
    {
        var document = CreatePositionedDocument();
        document.Backgrounds[0].Id = "1000";
        document.Requirements[0].Id = "1000";
        document.Requirements[0].ParentBackgroundId = "1000";
        document.Requirements[1].Id = "1000-1000";
        document.Requirements[1].ParentRequirementId = "1000";
        document.Requirements[2].Id = "1000-1001";
        document.Requirements[2].ParentRequirementId = "1000";
        document.Specifications[0].Id = "1000-1000-1000";
        document.Specifications[0].ParentRequirementId = "1000-1000";
        document.Specifications[0].RelatedRequirementIds = ["1000-1001"];
        document.View.BackgroundPositions["1000"] = document.View.BackgroundPositions["001"];
        document.View.BackgroundPositions.Remove("001");

        var yaml = _serializer.Serialize(document);
        StringAssert.Contains(yaml, "id: \"1000\"");
        StringAssert.Contains(yaml, "id: \"1000-1000-1000\"");
        StringAssert.Contains(yaml, "parent_requirement_id: \"1000-1000\"");
        Assert.HasCount(1, _serializer.Deserialize(yaml).Specifications);
    }

    [TestMethod]
    public void RoundTripPreservesNegativeFractionalAndZeroBackgroundPositions()
    {
        var document = CreatePositionedDocument();
        document.Backgrounds.Add(new() { Id = "002", Text = "独立した背景" });
        document.View.BackgroundPositions["001"] = new() { X = -1234.56789, Y = -0.125 };
        document.View.BackgroundPositions["002"] = new() { X = 0, Y = 456.75 };
        document.View.Zoom = 0.25;
        document.View.Pan = new() { X = 123, Y = -456 };
        document.View.Nodes["requirement:001"] = new() { ReasonExpanded = true };

        var yaml = _serializer.Serialize(document);
        var restored = _serializer.Deserialize(yaml);
        Assert.AreEqual(-1234.56789, restored.View.BackgroundPositions["001"].X);
        Assert.AreEqual(-0.125, restored.View.BackgroundPositions["001"].Y);
        Assert.AreEqual(0, restored.View.BackgroundPositions["002"].X);
        Assert.AreEqual(456.75, restored.View.BackgroundPositions["002"].Y);
        Assert.AreEqual(0.25, restored.View.Zoom);
        Assert.AreEqual(123, restored.View.Pan.X);
        Assert.AreEqual(-456, restored.View.Pan.Y);
        Assert.IsTrue(restored.View.Nodes["requirement:001"].ReasonExpanded);
        Assert.AreEqual(yaml, _serializer.Serialize(restored));
    }

    [TestMethod]
    public void WritesFixedKeyOrderAndBackgroundPositionsInNumericIdOrder()
    {
        var document = CreatePositionedDocument();
        document.View.Nodes["requirement:001"] = new() { ReasonExpanded = true };
        foreach (var id in new[] { "65535", "1000", "999", "010", "002" })
        {
            document.Backgrounds.Add(new() { Id = id, Text = id });
            document.View.BackgroundPositions[id] = new() { X = -10.5, Y = 20.25 };
        }
        document.View.BackgroundPositions = document.View.BackgroundPositions.Reverse().ToDictionary(x => x.Key, x => x.Value);
        var yaml = _serializer.Serialize(document);
        AssertOrdered(yaml, "version:", "format_version:", "title:", "backgrounds:", "requirements:", "specifications:", "view:");
        var view = yaml[yaml.IndexOf("view:", StringComparison.Ordinal)..];
        AssertOrdered(view, "zoom:", "pan:", "nodes:", "background_positions:");
        var positions = view[view.IndexOf("background_positions:", StringComparison.Ordinal)..];
        AssertOrdered(positions, "\"001\":", "\"002\":", "\"010\":", "\"999\":", "\"1000\":", "\"65535\":");
        AssertOrdered(positions, "x:", "y:");
        Assert.IsFalse(yaml.Contains("\r", StringComparison.Ordinal));
        Assert.AreEqual(yaml, _serializer.Serialize(_serializer.Deserialize(yaml)));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void OldFormatsCanBeInitializedByLayoutAndResavedAsFormatThree(int format)
    {
        var yaml = MinimalYaml(format, """
              zoom: 1.5
              pan: { x: -20.25, y: 30.5 }
              nodes:
                "requirement:001": { reason_expanded: true }
            """);
        var document = _serializer.Deserialize(yaml);
        Assert.AreEqual(format, document.FormatVersion);
        Assert.IsEmpty(document.View.BackgroundPositions);
        Assert.AreEqual(format == 1 ? 1 : 1.5, document.View.Zoom);
        Assert.AreEqual(format == 1 ? 0 : -20.25, document.View.Pan.X);
        Assert.AreEqual(format == 2, document.View.Nodes.ContainsKey("requirement:001"));
        Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Serialize(document));
        CommitInitialPositions(document);
        var saved = _serializer.Serialize(document);
        StringAssert.Contains(saved, "format_version: 3");
        var restored = _serializer.Deserialize(saved);
        Assert.HasCount(document.Backgrounds.Count, restored.View.BackgroundPositions);
        Assert.AreEqual(document.View.Zoom, restored.View.Zoom);
        Assert.AreEqual(document.View.Pan.X, restored.View.Pan.X);
        Assert.HasCount(document.View.Nodes.Count, restored.View.Nodes);
    }

    [TestMethod]
    public void FormatThreeAllowsMissingPositionsUntilLayoutInitialization()
    {
        var document = _serializer.Deserialize(MinimalYaml(3, """
              zoom: 1
              pan: { x: 0, y: 0 }
            """));
        Assert.IsEmpty(document.View.BackgroundPositions);
        document.Backgrounds.Add(new() { Id = "002", Text = "第二背景" });
        document.View.BackgroundPositions["001"] = new() { X = -500.25, Y = 99.5 };
        CommitInitialPositions(document);
        var restored = _serializer.Deserialize(_serializer.Serialize(document));
        Assert.HasCount(2, restored.View.BackgroundPositions);
        Assert.AreEqual(-500.25, restored.View.BackgroundPositions["001"].X);
        Assert.AreEqual(99.5, restored.View.BackgroundPositions["001"].Y);
    }

    [TestMethod]
    public void PartialPositionMapLoadsWithoutInventingOrChangingAnyCoordinates()
    {
        const string yaml = """
            version: "0.1.0"
            format_version: 3
            backgrounds:
              - id: "001"
                text: 保存位置あり
              - id: "002"
                text: 保存位置なし
            requirements: []
            specifications: []
            view:
              zoom: 1
              pan: { x: 0, y: 0 }
              background_positions:
                "001": { x: -200.5, y: 20.25 }
            """;
        var document = _serializer.Deserialize(yaml);
        Assert.HasCount(1, document.View.BackgroundPositions);
        Assert.AreEqual(-200.5, document.View.BackgroundPositions["001"].X);
        Assert.AreEqual(20.25, document.View.BackgroundPositions["001"].Y);
        var exception = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Serialize(document));
        Assert.IsTrue(exception.Errors.Any(x => x.Code == "view.background_position.required" && x.NodeId == "002"));
    }

    [TestMethod]
    [DataRow("\"001\": { x: 1, y: 2 }\n    \"001\": { x: 3, y: 4 }")]
    [DataRow("\"001\": { x: 1, x: 2, y: 3 }")]
    public void RejectsDuplicatePositionOrCoordinateKeys(string entries)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => _serializer.Deserialize(PositionYaml(entries)));
    }

    [TestMethod]
    [DataRow("\"001\": { x: 1 }")]
    [DataRow("\"001\": { y: 2 }")]
    [DataRow("\"001\": { x: null, y: 2 }")]
    [DataRow("\"001\": null")]
    public void RejectsPositionsWithoutBothCoordinates(string entries)
    {
        var exception = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Deserialize(PositionYaml(entries)));
        Assert.IsTrue(exception.Errors.Any(x => x.Code == "view.background_position.invalid"));
    }

    [TestMethod]
    [DataRow(".nan", "0")]
    [DataRow(".inf", "0")]
    [DataRow("-.inf", "0")]
    [DataRow("0", ".nan")]
    [DataRow("0", ".inf")]
    public void RejectsNonFiniteCoordinates(string x, string y)
    {
        var exception = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Deserialize(PositionYaml($"\"001\": {{ x: {x}, y: {y} }}")));
        Assert.IsTrue(exception.Errors.Any(error => error.Code == "view.background_position.invalid"));
    }

    [TestMethod]
    public void RejectsUnknownBackgroundPositionReferences()
    {
        var exception = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Deserialize(PositionYaml("\"002\": { x: 1, y: 2 }")));
        Assert.IsTrue(exception.Errors.Any(x => x.Code == "view.background_position.missing" && x.NodeId == "002"));
    }

    [TestMethod]
    public void SerializationRequiresConfirmedPositionsAndNeverMutatesItsInput()
    {
        var document = TestDocumentFactory.Create();
        document.Version = "0.0.1";
        document.FormatVersion = DocumentFormat.Legacy;
        var before = JsonSerializer.Serialize(document);
        var missing = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Serialize(document));
        Assert.IsTrue(missing.Errors.Any(x => x.Code == "view.background_position.required"));
        Assert.AreEqual(before, JsonSerializer.Serialize(document));

        document.View.BackgroundPositions["001"] = new() { X = -12.5, Y = 5.25 };
        before = JsonSerializer.Serialize(document);
        var saved = _serializer.Serialize(document);
        Assert.AreEqual(before, JsonSerializer.Serialize(document));
        StringAssert.Contains(saved, "version: \"0.1.0\"");
        StringAssert.Contains(saved, "format_version: 3");
        document.View.BackgroundPositions["001"].X = double.NaN;
        Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Serialize(document));
        Assert.IsTrue(double.IsNaN(document.View.BackgroundPositions["001"].X));
        Assert.AreEqual("0.0.1", document.Version);
        Assert.AreEqual(DocumentFormat.Legacy, document.FormatVersion);
    }

    [TestMethod]
    public void RemovedBackgroundPositionDoesNotPersistAndEmptyDocumentsStillWriteTheMap()
    {
        var document = CreatePositionedDocument();
        document.Backgrounds.Add(new() { Id = "002", Text = "削除対象" });
        document.View.BackgroundPositions["002"] = new() { X = 800, Y = 800 };
        document.Backgrounds.RemoveAt(1);
        document.View.BackgroundPositions.Remove("002");
        Assert.HasCount(1, _serializer.Deserialize(_serializer.Serialize(document)).View.BackgroundPositions);
        var empty = _serializer.Serialize(new UsdmDocument());
        StringAssert.Contains(empty, "background_positions: {}");
        Assert.IsEmpty(_serializer.Deserialize(empty).View.BackgroundPositions);
    }

    private static UsdmDocument CreatePositionedDocument()
    {
        var document = TestDocumentFactory.Create();
        document.View.BackgroundPositions["001"] = new() { X = 96, Y = 96 };
        return document;
    }

    private static void CommitInitialPositions(UsdmDocument document)
    {
        var expanded = document.View.Nodes.Where(x => x.Value.ReasonExpanded).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var layout = new AutoLayoutService().Calculate(new LayoutRequest { Document = document, ExpandedReasonNodeIds = expanded });
        document.View.BackgroundPositions = layout.Nodes.Where(x => x.Kind == LayoutNodeKind.Background)
            .ToDictionary(x => x.Id, x => new CanvasPosition { X = x.Bounds.X, Y = x.Bounds.Y }, StringComparer.Ordinal);
    }

    private static void AssertOrdered(string text, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsGreaterThan(previous, index, $"Expected {marker} after the preceding key.");
            previous = index;
        }
    }

    private static string MinimalYaml(int format, string view) => $$"""
        version: "0.1.0"
        format_version: {{format}}
        backgrounds:
          - id: "001"
            text: 背景
        requirements:
          - id: "001"
            request: 要求
            parent_background_id: "001"
        specifications: []
        view:
        {{view}}
        """;

    private static string PositionYaml(string entries) => MinimalYaml(3,
        "  zoom: 1\n  pan: { x: 0, y: 0 }\n  background_positions:\n    " + entries);
}
