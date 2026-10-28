using System.Text.Json;
using USDM_MindMap_Editor.Core.Editing;
using USDM_MindMap_Editor.Core.Ids;
using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Serialization;
using USDM_MindMap_Editor.Core.Validation;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class NodeCreationServiceTests
{
    private readonly NodeCreationService _service = new();

    [TestMethod]
    public void AddsEachKindWithSingleParentAndPreservesConfirmedText()
    {
        var document = new UsdmDocument();
        const string backgroundText = "  背景\n改行を維持  ";
        const string requestText = "  要求\n改行を維持  ";
        const string reasonText = "  理由\n改行を維持  ";
        const string specificationText = "  仕様\n改行を維持  ";
        var backgroundId = _service.AddBackground(document, backgroundText);
        var topId = _service.AddTopLevelRequirement(document, backgroundId, requestText, reasonText);
        var lowerId = _service.AddLowerLevelRequirement(document, topId, requestText, null);
        var specificationId = _service.AddSpecification(document, lowerId, specificationText);

        Assert.AreEqual("001", backgroundId);
        Assert.AreEqual("001", topId);
        Assert.AreEqual("001-001", lowerId);
        Assert.AreEqual("001-001-001", specificationId);
        Assert.AreEqual(backgroundText, document.Backgrounds.Single().Text);
        Assert.AreEqual(requestText, document.Requirements[0].Request);
        Assert.AreEqual(reasonText, document.Requirements[0].Reason);
        Assert.AreEqual(backgroundId, document.Requirements[0].ParentBackgroundId);
        Assert.IsNull(document.Requirements[0].ParentRequirementId);
        Assert.AreEqual(topId, document.Requirements[1].ParentRequirementId);
        Assert.IsNull(document.Requirements[1].ParentBackgroundId);
        Assert.IsNull(document.Requirements[1].Reason);
        Assert.AreEqual(lowerId, document.Specifications.Single().ParentRequirementId);
        Assert.AreEqual(specificationText, document.Specifications.Single().Specification);
        Assert.AreEqual(DocumentFormat.Current, document.FormatVersion);
        Assert.IsTrue(new UsdmDocumentValidator().Validate(document).IsValid);
    }

    [TestMethod]
    public void TopLevelIdsAreGlobalAcrossBackgroundsAndLowerIdsArePerParent()
    {
        var document = new UsdmDocument();
        var firstBackground = _service.AddBackground(document, "背景1");
        var secondBackground = _service.AddBackground(document, "背景2");
        var firstTop = _service.AddTopLevelRequirement(document, firstBackground, "上位1", null);
        var secondTop = _service.AddTopLevelRequirement(document, secondBackground, "上位2", "");
        Assert.AreEqual("001", firstTop);
        Assert.AreEqual("002", secondTop);
        Assert.AreEqual("003", _service.AddTopLevelRequirement(document, firstBackground, "上位3", null));
        Assert.AreEqual("001-001", _service.AddLowerLevelRequirement(document, firstTop, "下位1", null));
        Assert.AreEqual("002-001", _service.AddLowerLevelRequirement(document, secondTop, "下位2", null));
        Assert.AreEqual("001-002", _service.AddLowerLevelRequirement(document, firstTop, "下位3", null));
        Assert.AreEqual("001-001-001", _service.AddSpecification(document, "001-001", "仕様1"));
        Assert.AreEqual("002-001-001", _service.AddSpecification(document, "002-001", "仕様2"));
    }

    [TestMethod]
    [DataRow("background")]
    [DataRow("top")]
    [DataRow("lower")]
    [DataRow("specification")]
    public void EmptyInputsDoNotChangeDocumentOrConsumeAnId(string kind)
    {
        var document = TestDocumentFactory.Create();
        document.View.Zoom = 1.4;
        document.View.Pan.X = 42;
        document.View.Nodes["requirement:001"] = new() { ReasonExpanded = true };
        foreach (var text in new[] { "", " ", "\r\n\t" })
            AssertUnchanged<ArgumentException>(document, () => Add(document, kind, text));
        var id = Add(document, kind, "有効な本文");
        Assert.AreEqual(kind switch
        {
            "background" => "002",
            "top" => "002",
            "lower" => "001-003",
            _ => "001-001-002"
        }, id);
        Assert.AreEqual(1.4, document.View.Zoom);
        Assert.AreEqual(42, document.View.Pan.X);
        Assert.IsTrue(document.View.Nodes["requirement:001"].ReasonExpanded);
    }

    [TestMethod]
    [DataRow("top", "999")]
    [DataRow("top", "001-001")]
    [DataRow("lower", "999")]
    [DataRow("lower", "001-001")]
    [DataRow("specification", "999-001")]
    [DataRow("specification", "001")]
    [DataRow("specification", "001-001-001")]
    public void MissingAndWrongLevelParentsDoNotChangeDocument(string kind, string parentId)
    {
        var document = TestDocumentFactory.Create();
        AssertUnchanged<ArgumentException>(document, () =>
        {
            _ = kind switch
            {
                "top" => _service.AddTopLevelRequirement(document, parentId, "要求", null),
                "lower" => _service.AddLowerLevelRequirement(document, parentId, "要求", null),
                _ => _service.AddSpecification(document, parentId, "仕様")
            };
        });
    }

    [TestMethod]
    public void BackgroundIdCannotBeUsedAsAnUpperRequirementUnlessThatRequirementExists()
    {
        var document = new UsdmDocument { Backgrounds = [new() { Id = "001", Text = "背景" }] };
        AssertUnchanged<ArgumentException>(document, () => _service.AddLowerLevelRequirement(document, "001", "要求", null));
    }

    [TestMethod]
    public void SpecificationCopiesValidRelatedLowerRequirementIds()
    {
        var document = TestDocumentFactory.Create();
        var selected = new List<string> { "001-002" };
        var id = _service.AddSpecification(document, "001-001", "関連付き仕様", selected);
        selected.Clear();
        var added = document.Specifications.Single(x => x.Id == id);
        CollectionAssert.AreEqual(new[] { "001-002" }, added.RelatedRequirementIds);
        Assert.AreEqual("001-001", added.ParentRequirementId);
        Assert.IsTrue(new UsdmDocumentValidator().Validate(document).IsValid);
    }

    [TestMethod]
    [DataRow("001-001")]
    [DataRow("001")]
    [DataRow("999-001")]
    [DataRow("001-001-001")]
    public void InvalidRelatedRequirementDoesNotChangeDocumentOrConsumeAnId(string relatedId)
    {
        var document = TestDocumentFactory.Create();
        AssertUnchanged<ArgumentException>(document, () => _service.AddSpecification(document, "001-001", "仕様", [relatedId]));
        Assert.AreEqual("001-001-002", _service.AddSpecification(document, "001-001", "仕様", ["001-002"]));
    }

    [TestMethod]
    public void DuplicateRelatedRequirementsAreRejectedAtomically()
    {
        var document = TestDocumentFactory.Create();
        AssertUnchanged<ArgumentException>(document, () => _service.AddSpecification(document, "001-001", "仕様", ["001-002", "001-002"]));
    }

    [TestMethod]
    [DataRow("background")]
    [DataRow("top")]
    [DataRow("lower")]
    [DataRow("specification")]
    public void ExhaustedIdsDoNotChangeDocument(string kind)
    {
        var document = TestDocumentFactory.Create();
        for (var number = 1; number <= UsdmIdRules.MaximumSegment; number++)
        {
            var segment = number.ToString("D3");
            switch (kind)
            {
                case "background" when number > 1:
                    document.Backgrounds.Add(new() { Id = segment, Text = "背景" });
                    break;
                case "top" when number > 1:
                    document.Requirements.Add(new() { Id = segment, Request = "要求", ParentBackgroundId = "001" });
                    break;
                case "lower" when number > 2:
                    document.Requirements.Add(new() { Id = $"001-{segment}", Request = "要求", ParentRequirementId = "001" });
                    break;
                case "specification" when number > 1:
                    document.Specifications.Add(new() { Id = $"001-001-{segment}", Specification = "仕様", ParentRequirementId = "001-001" });
                    break;
            }
        }
        AssertUnchanged<InvalidOperationException>(document, () => Add(document, kind, "本文"));
    }

    [TestMethod]
    public void AddedNodesRoundTripWithBackgroundPositionsButWithoutDerivedGeometryOrRelatedEdges()
    {
        var document = TestDocumentFactory.Create();
        var background = _service.AddBackground(document, "追加背景");
        var top = _service.AddTopLevelRequirement(document, background, "追加上位", null);
        var lower = _service.AddLowerLevelRequirement(document, top, "追加下位", "追加理由");
        var specification = _service.AddSpecification(document, lower, "追加仕様", ["001-002"]);
        var serializer = new UsdmYamlSerializer();
        var initialLayout = new AutoLayoutService().Calculate(new LayoutRequest { Document = document });
        document.View.BackgroundPositions = initialLayout.Nodes.Where(x => x.Kind == LayoutNodeKind.Background)
            .ToDictionary(x => x.Id, x => new CanvasPosition { X = x.Bounds.X, Y = x.Bounds.Y }, StringComparer.Ordinal);
        var yaml = serializer.Serialize(document);
        var restored = serializer.Deserialize(yaml);
        var layout = new AutoLayoutService().Calculate(new LayoutRequest { Document = restored });
        Assert.AreEqual(DocumentFormat.Current, restored.FormatVersion);
        Assert.AreEqual(yaml, serializer.Serialize(restored));
        Assert.HasCount(restored.Backgrounds.Count + restored.Requirements.Count + restored.Specifications.Count, layout.Nodes);
        Assert.HasCount(layout.Nodes.Count - restored.Backgrounds.Count, layout.Edges);
        Assert.IsTrue(layout.Edges.Any(x => x.ParentId == $"requirement:{lower}" && x.ChildId == $"specification:{specification}"));
        Assert.IsFalse(layout.Edges.Any(x => x.ParentId == "requirement:001-002" && x.ChildId == $"specification:{specification}"));
        Assert.IsFalse(yaml.Contains("control_point", StringComparison.Ordinal));
        Assert.IsFalse(yaml.Contains("bounds:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AddingChildPreservesEarlierPositionsAndMovesOnlyFollowingSubtrees()
    {
        var document = ConnectionScenarioFactory.Create("multiple-backgrounds");
        var engine = new AutoLayoutService();
        var before = engine.Calculate(new LayoutRequest { Document = document });
        var parent = before.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var existingChild = before.Find(LayoutNodeKind.Specification, "001-001-001");
        var id = _service.AddSpecification(document, "001-001", "追加仕様");
        var after = engine.Calculate(new LayoutRequest { Document = document });
        var added = after.Find(LayoutNodeKind.Specification, id);
        var shift = added.Size.Height + new LayoutSettings().SiblingGap;

        Assert.AreEqual(parent.Bounds, after.Find(LayoutNodeKind.LowerLevelRequirement, "001-001").Bounds);
        Assert.AreEqual(existingChild.Bounds, after.Find(LayoutNodeKind.Specification, existingChild.Id).Bounds);
        foreach (var node in before.Nodes)
        {
            var changed = after.Nodes.Single(x => x.Key == node.Key);
            Assert.AreEqual(node.Bounds.X, changed.Bounds.X, 0.00001);
            Assert.AreEqual(node.Bounds.Y + (node.Bounds.Y > existingChild.Bounds.Y ? shift : 0), changed.Bounds.Y, 0.00001);
        }
        for (var i = 0; i < after.Nodes.Count; i++)
        for (var j = i + 1; j < after.Nodes.Count; j++)
            Assert.IsFalse(after.Nodes[i].Bounds.Intersects(after.Nodes[j].Bounds));
        var edge = after.Edges.Single(x => x.ChildId == added.Key);
        Assert.AreEqual(parent.Bounds.BottomCenter, edge.Start);
        Assert.AreEqual(added.Bounds.LeftCenter, edge.End);
    }

    private string Add(UsdmDocument document, string kind, string text) => kind switch
    {
        "background" => _service.AddBackground(document, text),
        "top" => _service.AddTopLevelRequirement(document, "001", text, null),
        "lower" => _service.AddLowerLevelRequirement(document, "001", text, null),
        _ => _service.AddSpecification(document, "001-001", text)
    };

    private static void AssertUnchanged<TException>(UsdmDocument document, Action action) where TException : Exception
    {
        var before = JsonSerializer.Serialize(document);
        Assert.ThrowsExactly<TException>(action);
        Assert.AreEqual(before, JsonSerializer.Serialize(document));
    }
}
