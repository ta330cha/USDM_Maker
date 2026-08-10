using USDM_MindMap_Editor.Core.Serialization;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class UsdmYamlSerializerTests
{
    private readonly UsdmYamlSerializer _serializer = new();

    [TestMethod]
    public void RoundTripPreservesDocument()
    {
        var yaml = _serializer.Serialize(TestDocumentFactory.Create());
        var restored = _serializer.Deserialize(yaml);
        Assert.AreEqual("0.1.0", restored.Version);
        Assert.AreEqual(DocumentFormat.Current, restored.FormatVersion);
        Assert.HasCount(3, restored.Requirements);
        CollectionAssert.AreEqual(new[] { "001-002" }, restored.Specifications[0].RelatedRequirementIds);
    }

    [TestMethod]
    public void OutputIsDeterministicAndUsesStableOrder()
    {
        var document = TestDocumentFactory.Create();
        document.Requirements.Reverse();
        var first = _serializer.Serialize(document);
        var second = _serializer.Serialize(document);
        Assert.AreEqual(first, second);
        Assert.IsLessThan(
            first.IndexOf("id: \"001-001\"\n", StringComparison.Ordinal),
            first.IndexOf("id: \"001\"\n", StringComparison.Ordinal));
        Assert.IsFalse(first.Contains("\r", StringComparison.Ordinal));
        StringAssert.Contains(first, "version: \"0.1.0\"");
        StringAssert.Contains(first, "format_version: 2");
        StringAssert.Contains(first, "related_requirement_ids:\n  - \"001-002\"");
    }

    [TestMethod]
    public void OmitsEmptyOptionalValues()
    {
        var document = TestDocumentFactory.Create();
        document.Requirements[0].Reason = "";
        document.Specifications[0].RelatedRequirementIds.Clear();
        var yaml = _serializer.Serialize(document);
        Assert.IsFalse(yaml.Contains("reason:", StringComparison.Ordinal));
        Assert.IsFalse(yaml.Contains("related_requirement_ids:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AcceptsDocumentFromOlderApplicationVersionWhenFormatIsSupported()
    {
        var yaml = _serializer.Serialize(TestDocumentFactory.Create()).Replace("version: \"0.1.0\"", "version: \"9.0.0\"", StringComparison.Ordinal);
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
    }

    [TestMethod]
    public void RoundTripPreservesViewAndOnlyPersistsExpandedNodes()
    {
        var document = TestDocumentFactory.Create();
        document.View.Zoom = 1.2;
        document.View.Pan = new PanState { X = -10, Y = 24 };
        document.View.Nodes["requirement:001"] = new NodeViewState { ReasonExpanded = true };
        document.View.Nodes["requirement:001-001"] = new NodeViewState { ReasonExpanded = false };

        var yaml = _serializer.Serialize(document);
        StringAssert.Contains(yaml, "format_version: 2");
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
        var yaml = _serializer.Serialize(TestDocumentFactory.Create()).Replace("format_version: 2", "format_version: 9", StringComparison.Ordinal);
        var exception = Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Deserialize(yaml));
        Assert.IsTrue(exception.Errors.Any(error => error.Code == "format_version.unsupported"));
    }

    [TestMethod]
    public void QuotesFiveDigitIdentifiers()
    {
        var document = TestDocumentFactory.Create();
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

        var yaml = _serializer.Serialize(document);
        StringAssert.Contains(yaml, "id: \"1000\"");
        StringAssert.Contains(yaml, "id: \"1000-1000-1000\"");
        StringAssert.Contains(yaml, "parent_requirement_id: \"1000-1000\"");
        Assert.HasCount(1, _serializer.Deserialize(yaml).Specifications);
    }
}
