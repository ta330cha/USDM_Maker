using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class AutoLayoutServiceTests
{
    [TestMethod]
    public void PlacesTreeFromLeftToRightWithConfiguredSpacing()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest { Document = TestDocumentFactory.Create() });
        var background = result.Find(LayoutNodeKind.Background, "001");
        var upper = result.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");

        Assert.IsLessThan(lower.Bounds.X, background.Bounds.X);
        Assert.IsLessThan(specification.Bounds.X, lower.Bounds.X);
        Assert.IsLessThan(upper.Bounds.X, background.Bounds.X);
        Assert.AreEqual(96, upper.Bounds.X - background.Bounds.Right, 0.001);
        Assert.AreEqual(96, lower.Bounds.X - upper.Bounds.Right, 0.001);
        Assert.AreEqual(96, specification.Bounds.X - lower.Bounds.Right, 0.001);
    }

    [TestMethod]
    public void UsesMeasuredSizeInPreferenceToPrediction()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest
        {
            Document = TestDocumentFactory.Create(),
            MeasuredSizes = new Dictionary<string, NodeSize> { ["001-001"] = new(320, 300) }
        });

        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");
        Assert.AreEqual(320, lower.Size.Width);
        Assert.AreEqual(300, lower.Size.Height);
        Assert.AreEqual(96, specification.Bounds.X - lower.Bounds.Right, 0.001);
    }

    [TestMethod]
    public void ProducesNonOverlappingNodeRectanglesAndParentEdges()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest { Document = TestDocumentFactory.Create() });
        for (var i = 0; i < result.Nodes.Count; i++)
        for (var j = i + 1; j < result.Nodes.Count; j++)
            Assert.IsFalse(result.Nodes[i].Bounds.Intersects(result.Nodes[j].Bounds));

        var background = result.Find(LayoutNodeKind.Background, "001");
        var upper = result.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");
        Assert.IsTrue(result.Edges.Any(x => x.ParentId == background.Key && x.ChildId == upper.Key));
        var edge = result.Edges.Single(x => x.ParentId == lower.Key && x.ChildId == specification.Key);
        Assert.AreEqual(lower.Bounds.RightCenter, edge.Start);
        Assert.AreEqual(specification.Bounds.LeftCenter, edge.End);
    }

    [TestMethod]
    public void UsesCurrentDesignPredictionSettings()
    {
        var settings = new LayoutSettings();
        var size = new NodeSizePredictor(settings).Predict(
            LayoutNodeKind.TopLevelRequirement,
            "短い要求",
            null,
            reasonExpanded: false);

        Assert.AreEqual(280, size.Width);
        Assert.AreEqual(110, size.Height);
        Assert.AreEqual(8, settings.MeasuredDifferenceThreshold(80));
        Assert.AreEqual(12, settings.MeasuredDifferenceThreshold(120));
    }

    [TestMethod]
    public void SortsSiblingIdsByNumericSegments()
    {
        var document = new UsdmDocument
        {
            Backgrounds =
            [
                new() { Id = "1000", Text = "大きいID" },
                new() { Id = "999", Text = "小さいID" }
            ],
            Requirements =
            [
                new() { Id = "1000", Request = "要求", ParentBackgroundId = "1000" },
                new() { Id = "999", Request = "要求", ParentBackgroundId = "999" }
            ]
        };

        var backgrounds = new AutoLayoutService().Calculate(new LayoutRequest { Document = document })
            .Nodes.Where(x => x.Kind == LayoutNodeKind.Background).Select(x => x.Id).ToArray();
        CollectionAssert.AreEqual(new[] { "999", "1000" }, backgrounds);
    }
}
