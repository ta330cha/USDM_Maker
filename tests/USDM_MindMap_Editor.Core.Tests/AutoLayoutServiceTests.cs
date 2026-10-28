using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class AutoLayoutServiceTests
{
    private const double Tolerance = 0.00001;

    [TestMethod]
    [DataRow(240.0)]
    [DataRow(280.0)]
    [DataRow(320.0)]
    public void PlacesChildrenBelowParentsAndIndentsTheirLeftEdgesByThreeQuartersOfParentWidth(double width)
    {
        var settings = new LayoutSettings { NodeWidth = width };
        var result = new AutoLayoutService().Calculate(new LayoutRequest { Document = TestDocumentFactory.Create(), Settings = settings });
        var byKey = result.Nodes.ToDictionary(x => x.Key);
        var background = result.Find(LayoutNodeKind.Background, "001");
        Assert.AreEqual(settings.CanvasMargin, background.Bounds.X, Tolerance);
        Assert.AreEqual(settings.CanvasMargin, background.Bounds.Y, Tolerance);
        foreach (var edge in result.Edges)
        {
            var parent = byKey[edge.ParentId];
            var child = byKey[edge.ChildId];
            Assert.AreEqual(parent.Bounds.X + width * 0.75, child.Bounds.X, Tolerance);
            Assert.IsGreaterThanOrEqualTo(parent.Bounds.Bottom + settings.ParentChildVerticalGap - Tolerance, child.Bounds.Y);
            Assert.IsLessThan(parent.Bounds.Right, child.Bounds.X, "Horizontal projections should overlap with the new indent.");
        }
        foreach (var group in result.Edges.GroupBy(x => x.ParentId))
        {
            var firstChild = group.Select(x => byKey[x.ChildId]).MinBy(x => x.Bounds.Y)!;
            Assert.AreEqual(byKey[group.Key].Bounds.Bottom + settings.ParentChildVerticalGap, firstChild.Bounds.Y, Tolerance);
        }
    }

    [TestMethod]
    public void UsesMeasuredSizeForAnchorsVerticalSpacingAndWidthDerivedIndent()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest
        {
            Document = TestDocumentFactory.Create(),
            MeasuredSizes = new Dictionary<string, NodeSize> { ["requirement:001-001"] = new(320, 300) }
        });

        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");
        Assert.AreEqual(320, lower.Size.Width);
        Assert.AreEqual(300, lower.Size.Height);
        Assert.AreEqual(lower.Bounds.X + 240, specification.Bounds.X, Tolerance);
        Assert.AreEqual(96, specification.Bounds.Y - lower.Bounds.Bottom, Tolerance);
        var edge = result.Edges.Single(x => x.ChildId == specification.Key);
        Assert.AreEqual(lower.Bounds.BottomCenter, edge.Start);
        Assert.AreEqual(specification.Bounds.LeftCenter, edge.End);
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
        Assert.AreEqual(lower.Bounds.BottomCenter, edge.Start);
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
    public void SortsBackgroundsAndAllSiblingKindsNumericallyFromTopToBottom()
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
                new() { Id = "1000", Request = "要求", ParentBackgroundId = "999" },
                new() { Id = "999", Request = "要求", ParentBackgroundId = "999" },
                new() { Id = "999-1000", Request = "要求", ParentRequirementId = "999" },
                new() { Id = "999-999", Request = "要求", ParentRequirementId = "999" }
            ],
            Specifications =
            [
                new() { Id = "999-999-1000", Specification = "仕様", ParentRequirementId = "999-999" },
                new() { Id = "999-999-999", Specification = "仕様", ParentRequirementId = "999-999" }
            ]
        };

        var result = new AutoLayoutService().Calculate(new LayoutRequest { Document = document });
        foreach (var kind in Enum.GetValues<LayoutNodeKind>())
        {
            var siblings = result.Nodes.Where(x => x.Kind == kind).OrderBy(x => x.Bounds.Y).Select(x => x.Id).ToArray();
            var expected = kind switch
            {
                LayoutNodeKind.LowerLevelRequirement => new[] { "999-999", "999-1000" },
                LayoutNodeKind.Specification => ["999-999-999", "999-999-1000"],
                _ => ["999", "1000"]
            };
            CollectionAssert.AreEqual(expected, siblings);
        }
    }

    [TestMethod]
    public void PlacesFollowingSiblingBelowCompleteDeepPrecedingSubtree()
    {
        var settings = new LayoutSettings();
        var result = new AutoLayoutService().Calculate(ConnectionScenarioFactory.CreateRequest("deep-first-subtree"));
        var first = result.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var second = result.Find(LayoutNodeKind.TopLevelRequirement, "002");
        var third = result.Find(LayoutNodeKind.TopLevelRequirement, "003");
        var subtree = DescendantsIncludingSelf(result, first.Key);
        Assert.HasCount(7, subtree);
        var subtreeBottom = OccupiedBottom(result, subtree);
        Assert.AreEqual(subtreeBottom + settings.SiblingGap, second.Bounds.Y, Tolerance);
        Assert.AreEqual(OccupiedBottom(result, new HashSet<string> { second.Key }) + settings.SiblingGap, third.Bounds.Y, Tolerance);
        Assert.AreEqual(first.Bounds.X, second.Bounds.X, Tolerance);
        Assert.AreEqual(first.Bounds.X, third.Bounds.X, Tolerance);
    }

    [TestMethod]
    public void AppliesCustomVerticalGapsAndMarginsAcrossMultipleBackgrounds()
    {
        var settings = new LayoutSettings
        {
            ParentChildVerticalGap = 37,
            SiblingGap = 19,
            CanvasMargin = 23,
            NodeWidth = 320
        };
        var result = new AutoLayoutService().Calculate(new LayoutRequest
        {
            Document = ConnectionScenarioFactory.Create("multiple-backgrounds"),
            Settings = settings
        });
        var byKey = result.Nodes.ToDictionary(x => x.Key);
        foreach (var group in result.Edges.GroupBy(x => x.ParentId))
        {
            var parent = byKey[group.Key];
            var siblings = group.Select(x => byKey[x.ChildId]).OrderBy(x => x.Bounds.Y).ToArray();
            Assert.AreEqual(parent.Bounds.Bottom + 37, siblings[0].Bounds.Y, Tolerance);
            foreach (var child in siblings) Assert.AreEqual(parent.Bounds.X + 240, child.Bounds.X, Tolerance);
            for (var index = 1; index < siblings.Length; index++)
                Assert.AreEqual(OccupiedBottom(result, DescendantsIncludingSelf(result, siblings[index - 1].Key)) + 19,
                    siblings[index].Bounds.Y, Tolerance);
        }
        var backgrounds = result.Nodes.Where(x => x.Kind == LayoutNodeKind.Background).OrderBy(x => x.Bounds.Y).ToArray();
        Assert.AreEqual(23, backgrounds[0].Bounds.Y, Tolerance);
        foreach (var background in backgrounds) Assert.AreEqual(23, background.Bounds.X, Tolerance);
        for (var index = 1; index < backgrounds.Length; index++)
            Assert.AreEqual(OccupiedBottom(result, DescendantsIncludingSelf(result, backgrounds[index - 1].Key)) + 19,
                backgrounds[index].Bounds.Y, Tolerance);
        Assert.AreEqual(result.Nodes.Max(x => x.Bounds.Right) + 23, result.CanvasBounds.Right, Tolerance);
        Assert.AreEqual(result.Nodes.Max(x => x.Bounds.Bottom) + 23, result.CanvasBounds.Bottom, Tolerance);
    }

    [TestMethod]
    public void HeightChangesMoveFollowingSubtreesWithoutChangingIndent()
    {
        var document = ConnectionScenarioFactory.Create("deep-first-subtree");
        var service = new AutoLayoutService();
        var before = service.Calculate(new LayoutRequest { Document = document });
        var original = before.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var after = service.Calculate(new LayoutRequest
        {
            Document = document,
            MeasuredSizes = new Dictionary<string, NodeSize> { [original.Key] = new(original.Size.Width, original.Size.Height + 300) }
        });
        foreach (var node in before.Nodes)
            Assert.AreEqual(node.Bounds.X, after.Nodes.Single(x => x.Key == node.Key).Bounds.X, Tolerance);
        Assert.AreEqual(before.Find(LayoutNodeKind.TopLevelRequirement, "002").Bounds.Y + 300,
            after.Find(LayoutNodeKind.TopLevelRequirement, "002").Bounds.Y, Tolerance);
        Assert.AreEqual(before.Find(LayoutNodeKind.TopLevelRequirement, "001").Bounds.Y,
            after.Find(LayoutNodeKind.TopLevelRequirement, "001").Bounds.Y, Tolerance);
    }

    private static HashSet<string> DescendantsIncludingSelf(LayoutResult result, string rootKey)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { rootKey };
        var pending = new Queue<string>();
        pending.Enqueue(rootKey);
        while (pending.TryDequeue(out var key))
            foreach (var child in result.Edges.Where(x => x.ParentId == key))
                if (keys.Add(child.ChildId)) pending.Enqueue(child.ChildId);
        return keys;
    }

    private static double OccupiedBottom(LayoutResult result, HashSet<string> keys) =>
        result.Nodes.Where(x => keys.Contains(x.Key)).Select(x => x.Bounds.Bottom)
            .Concat(result.Edges.Where(x => keys.Contains(x.ChildId)).Select(x => x.Bounds.Bottom)).Max();
}
