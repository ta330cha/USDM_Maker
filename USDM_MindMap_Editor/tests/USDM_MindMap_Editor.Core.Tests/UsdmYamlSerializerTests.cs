using USDM_MindMap_Editor.Core.Serialization;

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
    public void RejectsUnsupportedVersion()
    {
        var yaml = _serializer.Serialize(TestDocumentFactory.Create()).Replace("version: \"0.1.0\"", "version: \"9.0.0\"", StringComparison.Ordinal);
        Assert.ThrowsExactly<UsdmValidationException>(() => _serializer.Deserialize(yaml));
    }
}
