using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class BackgroundPlacementTests
{
    private const double Tolerance = 0.000001;
    private readonly AutoLayoutService _layout = new();
    private readonly BackgroundPlacementService _placement = new();

    [TestMethod]
    public void SavedPositionsIncludingNegativeCoordinatesOverrideIdOrderAndKeepLocalGeometry()
    {
        var document = ConnectionScenarioFactory.Create("multiple-backgrounds");
        document.View.BackgroundPositions = new()
        {
            ["001"] = new() { X = 1800.25, Y = 600.5 },
            ["002"] = new() { X = -900.5, Y = -600.25 },
            ["003"] = new() { X = 500, Y = 100 }
        };
        var result = _layout.Calculate(new() { Document = document });
        foreach (var tree in result.Trees)
        {
            Assert.AreEqual(document.View.BackgroundPositions[tree.BackgroundId].X, tree.Position.X);
            Assert.AreEqual(document.View.BackgroundPositions[tree.BackgroundId].Y, tree.Position.Y);
            Assert.AreEqual(tree.Position.X, tree.Nodes[0].Bounds.X);
            Assert.AreEqual(tree.Position.Y, tree.Nodes[0].Bounds.Y);
            AssertTreeAnchors(tree);
        }
        Assert.IsLessThan(-900.5, result.CanvasBounds.X);
        Assert.IsLessThan(-600.25, result.CanvasBounds.Y);
        Assert.IsEmpty(result.MovedBackgroundIds);
        Assert.IsEmpty(result.InitializedBackgroundIds);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void MissingPositionsFollowSavedTreesAndNumericIdOrderWithoutMutatingTheDocument()
    {
        var document = Backgrounds("1000", "001", "999");
        document.View.BackgroundPositions["001"] = new() { X = 700, Y = 800 };
        var result = _layout.Calculate(new() { Document = document });
        Assert.AreEqual(new LayoutPoint(700, 800), result.BackgroundPositions["001"]);
        Assert.AreEqual(new LayoutPoint(96, 942), result.BackgroundPositions["999"]);
        Assert.AreEqual(new LayoutPoint(96, 1084), result.BackgroundPositions["1000"]);
        CollectionAssert.AreEqual(new[] { "999", "1000" }, result.InitializedBackgroundIds.ToArray());
        Assert.HasCount(1, document.View.BackgroundPositions);
    }

    [TestMethod]
    public void ExplicitPositionsAreAPureCompleteOverride()
    {
        var document = Backgrounds("001");
        document.View.BackgroundPositions["001"] = new() { X = 999, Y = 888 };
        var positions = new Dictionary<string, LayoutPoint> { ["001"] = new(-40.25, 12.75) };
        var result = _layout.Calculate(new() { Document = document, BackgroundPositions = positions });
        Assert.AreEqual(positions["001"], result.Trees[0].Position);
        Assert.AreEqual(999, document.View.BackgroundPositions["001"].X);
        Assert.AreEqual(new LayoutPoint(-40.25, 12.75), positions["001"]);
    }

    [TestMethod]
    public void ReloadOverlapResolutionPrioritizesCurrentTopThenNumericId()
    {
        var document = Backgrounds("1000", "999", "001");
        var positions = new Dictionary<string, LayoutPoint>
        {
            ["1000"] = new(0, 0), ["999"] = new(0, 0), ["001"] = new(0, 40)
        };
        var result = _layout.Calculate(new() { Document = document, BackgroundPositions = positions });
        Assert.AreEqual(new LayoutPoint(0, 0), result.BackgroundPositions["999"]);
        Assert.AreEqual(new LayoutPoint(0, 142), result.BackgroundPositions["1000"]);
        Assert.AreEqual(new LayoutPoint(0, 284), result.BackgroundPositions["001"]);
        CollectionAssert.AreEqual(new[] { "001", "1000" }, result.MovedBackgroundIds.ToArray());
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void ExpansionKeepsSourceFixedAndPushesOnlyCollidingTreesDownInAChain()
    {
        var document = Backgrounds("001", "002", "003", "004");
        var positions = new Dictionary<string, LayoutPoint>
        {
            ["001"] = new(100, 0), ["002"] = new(100, 150),
            ["003"] = new(100, 300), ["004"] = new(1000, 20)
        };
        var before = _layout.Calculate(new() { Document = document, BackgroundPositions = positions });
        var after = _layout.Calculate(new()
        {
            Document = document, BackgroundPositions = before.BackgroundPositions,
            AnchoredBackgroundId = "001", PreviousLayout = before,
            ChangedBackgroundIds = new HashSet<string> { "001" },
            MeasuredSizes = new Dictionary<string, NodeSize> { ["background:001"] = new(280, 250) }
        });
        Assert.AreEqual(new LayoutPoint(100, 0), after.BackgroundPositions["001"]);
        Assert.AreEqual(new LayoutPoint(100, 282), after.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(100, 424), after.BackgroundPositions["003"]);
        Assert.AreEqual(positions["004"], after.BackgroundPositions["004"]);
        Assert.AreSame(before.Trees.Single(tree => tree.BackgroundId == "004"), after.Trees.Single(tree => tree.BackgroundId == "004"));
        CollectionAssert.AreEqual(new[] { "002", "003" }, after.MovedBackgroundIds.ToArray());
        AssertNonOverlapping(after);
    }

    [TestMethod]
    public void AnchoredSourceWinsEvenWhenItHasALaterYAndId()
    {
        var document = Backgrounds("001", "002");
        var result = _layout.Calculate(new()
        {
            Document = document, AnchoredBackgroundId = "002",
            BackgroundPositions = new Dictionary<string, LayoutPoint> { ["001"] = new(0, 0), ["002"] = new(0, 50) }
        });
        Assert.AreEqual(new LayoutPoint(0, 50), result.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(0, 192), result.BackgroundPositions["001"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void ShrinkingAnAnchoredTreeDoesNotCompactOtherPositions()
    {
        var document = Backgrounds("001", "002");
        var before = _layout.Calculate(new()
        {
            Document = document,
            BackgroundPositions = new Dictionary<string, LayoutPoint> { ["001"] = new(0, 0), ["002"] = new(0, 500) },
            MeasuredSizes = new Dictionary<string, NodeSize> { ["background:001"] = new(280, 400) }
        });
        var after = _layout.Calculate(new()
        {
            Document = document, BackgroundPositions = before.BackgroundPositions, AnchoredBackgroundId = "001",
            PreviousLayout = before, ChangedBackgroundIds = new HashSet<string> { "001" }
        });
        Assert.AreEqual(before.BackgroundPositions["002"], after.BackgroundPositions["002"]);
        Assert.AreSame(before.Trees[1], after.Trees[1]);
        Assert.IsEmpty(after.MovedBackgroundIds);
    }

    [TestMethod]
    public void ValidMoveTranslatesTheWholeFourLevelTreeAndAllBezierControlsOnlyOnce()
    {
        var document = ConnectionScenarioFactory.Create("multiple-backgrounds");
        var before = _layout.Calculate(new() { Document = document });
        var original = before.Trees[0];
        var candidate = new LayoutPoint(original.Position.X - 2400.5, original.Position.Y - 320.25);
        var moved = _placement.TryMove(before, "001", candidate);
        Assert.IsTrue(moved.IsValid);
        Assert.IsTrue(moved.HasMoved);
        var actual = moved.Layout.Trees[0];
        for (var index = 0; index < original.Nodes.Count; index++)
        {
            Assert.AreEqual(original.Nodes[index].Bounds.Translate(-2400.5, -320.25), actual.Nodes[index].Bounds);
            Assert.AreEqual(original.Nodes[index].Id, actual.Nodes[index].Id);
            Assert.AreEqual(original.Nodes[index].ParentId, actual.Nodes[index].ParentId);
        }
        for (var index = 0; index < original.Edges.Count; index++)
            Assert.AreEqual(original.Edges[index].Translate(-2400.5, -320.25), actual.Edges[index]);
        Assert.AreEqual(original.Bounds.Translate(-2400.5, -320.25), actual.Bounds);
        Assert.AreSame(before.Trees[1], moved.Layout.Trees[1]);
        Assert.AreSame(before.Trees[2], moved.Layout.Trees[2]);
        Assert.IsLessThan(candidate.X, moved.Layout.CanvasBounds.X);
        AssertTreeAnchors(actual);
    }

    [TestMethod]
    public void RejectedMoveReturnsDragStartRatherThanTheLastValidPreviewAndLeavesOthersUnchanged()
    {
        var document = Backgrounds("001", "002");
        var start = _layout.Calculate(new() { Document = document });
        var validPreview = _placement.TryMove(start, "001", new(-900, -900));
        Assert.IsTrue(validPreview.IsValid);
        var rejected = _placement.TryMove(start, "001", start.BackgroundPositions["002"]);
        Assert.IsFalse(rejected.IsValid);
        Assert.IsFalse(rejected.HasMoved);
        Assert.AreSame(start, rejected.Layout);
        CollectionAssert.AreEqual(new[] { "002" }, rejected.CollidingBackgroundIds.ToArray());
        Assert.IsEmpty(document.View.BackgroundPositions);
    }

    [TestMethod]
    public void NoOpMoveReturnsTheSameLayoutWithoutAChangeNotification()
    {
        var start = _layout.Calculate(new() { Document = Backgrounds("001") });
        var result = _placement.TryMove(start, "001", start.Trees[0].Position);
        Assert.IsTrue(result.IsValid);
        Assert.IsFalse(result.HasMoved);
        Assert.AreSame(start, result.Layout);
    }

    [TestMethod]
    public void TreeBoundsTouchingIsValidButAnyInteriorOverlapIsRejected()
    {
        var start = _layout.Calculate(new() { Document = Backgrounds("001", "002") });
        var other = start.Trees[1];
        Assert.IsTrue(_placement.TryMove(start, "001", new(other.Bounds.Right, other.Position.Y)).IsValid);
        Assert.IsFalse(_placement.TryMove(start, "001", new(other.Bounds.Right - .001, other.Position.Y)).IsValid);
    }

    [TestMethod]
    public void EmptySpaceInsideADeepTreeBoundingRectangleStillBlocksAnotherTree()
    {
        var document = ConnectionScenarioFactory.Create("four-level");
        document.Backgrounds.Add(new() { Id = "002", Text = "別の背景" });
        var start = _layout.Calculate(new() { Document = document });
        var root = start.Trees[0];
        var emptyCandidate = new LayoutPoint(root.Position.X + 400, root.Position.Y);
        var candidateRect = new LayoutRect(emptyCandidate.X, emptyCandidate.Y, 280, 110);
        Assert.IsFalse(root.Nodes.Any(node => node.Bounds.Intersects(candidateRect)), "Test point must be in an empty part of the AABB.");
        Assert.IsFalse(_placement.TryMove(start, "002", emptyCandidate).IsValid);
    }

    [TestMethod]
    public void OccupancyIncludesActualCurveExtremaAndStrokeButNotControlPolygon()
    {
        var settings = new LayoutSettings { ConnectionStrokeWidth = 400 };
        var result = _layout.Calculate(new()
        {
            Document = ConnectionScenarioFactory.Create("single"), Settings = settings,
            BackgroundPositions = new Dictionary<string, LayoutPoint> { ["001"] = new(0, 0) }
        });
        var tree = result.Trees.Single();
        var expected = tree.Nodes.Select(node => node.Bounds)
            .Concat(tree.Edges.Select(edge => edge.Bounds.Inflate(200))).Aggregate((left, right) => left.Union(right));
        Assert.AreEqual(expected, tree.Bounds);
        Assert.IsLessThan(tree.Nodes.Min(node => node.Bounds.X), tree.Bounds.X);
        Assert.IsLessThanOrEqualTo(tree.Bounds.X - settings.CanvasMargin, result.CanvasBounds.X);
    }

    [TestMethod]
    public void StrokeOnlyOverlapAlsoRejectsADrag()
    {
        var document = ConnectionScenarioFactory.Create("single");
        document.Backgrounds.Add(new() { Id = "002", Text = "別背景" });
        var result = _layout.Calculate(new()
        {
            Document = document, Settings = new() { ConnectionStrokeWidth = 400 }
        });
        var tree = result.Trees[0];
        var x = tree.Bounds.X - 280 + 1;
        var candidate = new LayoutRect(x, tree.Position.Y, 280, 110);
        Assert.IsFalse(tree.Nodes.Any(node => node.Bounds.Intersects(candidate)));
        Assert.IsFalse(_placement.TryMove(result, "002", new(x, tree.Position.Y)).IsValid);
    }

    [TestMethod]
    public void RepeatedPlacementIsDeterministicAndOnlyChangedBackgroundsAreRebuilt()
    {
        var document = Backgrounds("001", "002");
        var first = _layout.Calculate(new() { Document = document });
        var second = _layout.Calculate(new()
        {
            Document = document, BackgroundPositions = first.BackgroundPositions,
            PreviousLayout = first, ChangedBackgroundIds = new HashSet<string>()
        });
        Assert.AreSame(first.Trees[0], second.Trees[0]);
        Assert.AreSame(first.Trees[1], second.Trees[1]);
        Assert.AreEqual(first.CanvasBounds, second.CanvasBounds);
        var changed = _layout.Calculate(new()
        {
            Document = document, BackgroundPositions = first.BackgroundPositions,
            PreviousLayout = first, ChangedBackgroundIds = new HashSet<string> { "002" }
        });
        Assert.AreSame(first.Trees[0], changed.Trees[0]);
        Assert.AreNotSame(first.Trees[1], changed.Trees[1]);
    }

    [TestMethod]
    public void ASettingsChangeInvalidatesTheCachedGeometry()
    {
        var document = Backgrounds("001");
        var before = _layout.Calculate(new() { Document = document });
        var after = _layout.Calculate(new()
        {
            Document = document, BackgroundPositions = before.BackgroundPositions, PreviousLayout = before,
            ChangedBackgroundIds = new HashSet<string>(), Settings = before.Settings with { NodeWidth = 320 }
        });
        Assert.AreNotSame(before.Trees[0], after.Trees[0]);
        Assert.AreEqual(320, after.Nodes[0].Bounds.Width);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(double.MaxValue)]
    public void InvalidOrUnrepresentableCoordinatesFailWithoutChangingInput(double coordinate)
    {
        var document = Backgrounds("001");
        var positions = new Dictionary<string, LayoutPoint> { ["001"] = new(coordinate, 0) };
        Assert.Throws<ArgumentOutOfRangeException>(() => _layout.Calculate(new() { Document = document, BackgroundPositions = positions }));
        Assert.IsEmpty(document.View.BackgroundPositions);
        Assert.AreEqual(coordinate, positions["001"].X);
        var before = _layout.Calculate(new() { Document = document });
        Assert.Throws<ArgumentOutOfRangeException>(() => _placement.TryMove(before, "001", new(coordinate, 0)));
        Assert.AreEqual(new LayoutPoint(96, 96), before.BackgroundPositions["001"]);
    }

    [TestMethod]
    public void UnrepresentableCanvasSpanFailsAtomically()
    {
        var document = Backgrounds("001", "002");
        var positions = new Dictionary<string, LayoutPoint> { ["001"] = new(-1e308, 0), ["002"] = new(1e308, 0) };
        Assert.Throws<ArgumentOutOfRangeException>(() => _layout.Calculate(new() { Document = document, BackgroundPositions = positions }));
        Assert.IsEmpty(document.View.BackgroundPositions);
    }

    private static UsdmDocument Backgrounds(params string[] ids) => new()
    {
        Backgrounds = ids.Select(id => new BackgroundItem { Id = id, Text = "背景" }).ToList()
    };

    private static void AssertTreeAnchors(BackgroundTreeLayout tree)
    {
        var nodes = tree.Nodes.ToDictionary(node => node.Key);
        foreach (var edge in tree.Edges)
        {
            Assert.AreEqual(nodes[edge.ParentId].Bounds.BottomCenter.X, edge.Start.X, Tolerance);
            Assert.AreEqual(nodes[edge.ParentId].Bounds.BottomCenter.Y, edge.Start.Y, Tolerance);
            Assert.AreEqual(nodes[edge.ChildId].Bounds.LeftCenter.X, edge.End.X, Tolerance);
            Assert.AreEqual(nodes[edge.ChildId].Bounds.LeftCenter.Y, edge.End.Y, Tolerance);
        }
    }

    private static void AssertNonOverlapping(LayoutResult result)
    {
        for (var i = 0; i < result.Trees.Count; i++)
            for (var j = i + 1; j < result.Trees.Count; j++)
                Assert.IsFalse(result.Trees[i].Bounds.Intersects(result.Trees[j].Bounds));
    }
}
