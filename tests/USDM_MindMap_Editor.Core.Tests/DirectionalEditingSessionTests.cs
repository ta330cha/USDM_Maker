using USDM_MindMap_Editor.Core.Editing;
using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Serialization;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class DirectionalEditingSessionTests
{
    [TestMethod]
    [DataRow(.25, 0d, 0d)]
    [DataRow(1d, 0d, 0d)]
    [DataRow(2d, 0d, 0d)]
    [DataRow(.25, 470.5, -120.25)]
    [DataRow(1d, 470.5, -120.25)]
    [DataRow(2d, 470.5, -120.25)]
    public void ChildAdditionPushesTheRightFourLevelTreeIndependentlyOfViewportAndRoundTrips(
        double zoom, double panX, double panY)
    {
        var session = new DocumentEditingSession(CreateRightHandTree());
        session.SetViewport(zoom, panX, panY);
        session.MarkSaved();
        var before = session.Layout;
        var source = before.BackgroundPositions["001"];
        var right = before.Trees.Single(tree => tree.BackgroundId == "002");

        var id = session.AddNode(LayoutNodeKind.TopLevelRequirement, "001", "右へ広がる要求");

        Assert.AreEqual("002", id);
        Assert.AreEqual(source, session.Layout.BackgroundPositions["001"]);
        var moved = session.Layout.Trees.Single(tree => tree.BackgroundId == "002");
        Assert.AreEqual(322d, moved.Position.X);
        Assert.AreEqual(right.Position.Y, moved.Position.Y);
        AssertTranslation(right, moved, 222, 0);
        Assert.AreSame(before.Trees.Single(tree => tree.BackgroundId == "003"),
            session.Layout.Trees.Single(tree => tree.BackgroundId == "003"));
        CollectionAssert.AreEqual(new[] { "002" }, session.AutomaticallyMovedBackgroundIds.ToArray());
        Assert.IsTrue(session.HasUnsavedChanges);

        var yaml = session.Serialize();
        StringAssert.Contains(yaml, "format_version: 3");
        foreach (var runtimeKey in new[] { "previous_layout", "previous_bounds", "direction", "delta_x", "control_point", "edges:" })
            Assert.IsFalse(yaml.Contains(runtimeKey, StringComparison.Ordinal), runtimeKey);
        var restored = new DocumentEditingSession(new UsdmYamlSerializer().Deserialize(yaml));
        Assert.AreEqual(yaml, restored.Serialize());
        Assert.AreEqual(zoom, restored.Document.View.Zoom);
        Assert.AreEqual(panX, restored.Document.View.Pan.X);
        Assert.AreEqual(panY, restored.Document.View.Pan.Y);
    }

    [TestMethod]
    public void SizeCorrectionUsesTheLastCommittedBoundsAndShrinkDoesNotPullTreesBack()
    {
        var session = new DocumentEditingSession(CreateRightHandTree());
        session.AddNode(LayoutNodeKind.TopLevelRequirement, "001", "追加");
        var beforeMeasurement = session.Layout;
        var source = beforeMeasurement.BackgroundPositions["001"];
        var right = beforeMeasurement.Trees.Single(tree => tree.BackgroundId == "002");

        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(500, 110), ["requirement:002"] = new(280, 110)
        });

        Assert.AreEqual(source, session.Layout.BackgroundPositions["001"]);
        var moved = session.Layout.Trees.Single(tree => tree.BackgroundId == "002");
        Assert.AreEqual(487d, moved.Position.X);
        AssertTranslation(right, moved, 165, 0);
        session.MarkSaved();
        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize> { ["background:001"] = new(280, 110) });
        Assert.AreEqual(moved.Position, session.Layout.BackgroundPositions["002"]);
        Assert.IsFalse(session.HasUnsavedChanges, "縮小で位置や新たな未保存変更を作らない");

        var beforeDrag = session.Serialize();
        var revision = session.Revision;
        Assert.IsFalse(session.MoveBackground("001", moved.Position, revision).IsValid);
        Assert.AreEqual(beforeDrag, session.Serialize());
        Assert.AreEqual(revision, session.Revision);
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    [TestMethod]
    public void FailedDirectionalCorrectionDoesNotPublishAnyPositionOrViewChange()
    {
        var session = new DocumentEditingSession(CreateRightHandTree());
        session.AddNode(LayoutNodeKind.TopLevelRequirement, "001", "追加");
        session.MarkSaved();
        var layout = session.Layout;
        var document = session.Document;
        var yaml = session.Serialize();
        var revision = session.Revision;

        Assert.Throws<ArgumentException>(() => session.ApplyMeasuredSizes(
            new Dictionary<string, NodeSize> { ["background:001"] = new(double.MaxValue, 110) }));

        Assert.AreSame(layout, session.Layout);
        Assert.AreSame(document, session.Document);
        Assert.AreEqual(yaml, session.Serialize());
        Assert.AreEqual(revision, session.Revision);
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    private static void AssertTranslation(BackgroundTreeLayout before, BackgroundTreeLayout after, double dx, double dy)
    {
        Assert.AreEqual(before.Bounds.Translate(dx, dy), after.Bounds);
        Assert.HasCount(before.Nodes.Count, after.Nodes);
        Assert.HasCount(before.Edges.Count, after.Edges);
        for (var i = 0; i < before.Nodes.Count; i++)
        {
            Assert.AreEqual(before.Nodes[i].Bounds.Translate(dx, dy), after.Nodes[i].Bounds);
            Assert.AreEqual(before.Nodes[i].Id, after.Nodes[i].Id);
            Assert.AreEqual(before.Nodes[i].ParentId, after.Nodes[i].ParentId);
        }
        for (var i = 0; i < before.Edges.Count; i++)
            Assert.AreEqual(before.Edges[i].Translate(dx, dy), after.Edges[i]);
    }

    private static UsdmDocument CreateRightHandTree() => new()
    {
        Backgrounds = [new() { Id = "001", Text = "操作元" }, new() { Id = "002", Text = "右の四階層" }, new() { Id = "003", Text = "非衝突" }],
        Requirements =
        [
            new() { Id = "001", Request = "上位", ParentBackgroundId = "002" },
            new() { Id = "001-001", Request = "下位", ParentRequirementId = "001" }
        ],
        Specifications = [new() { Id = "001-001-001", Specification = "仕様", ParentRequirementId = "001-001" }],
        View = new()
        {
            BackgroundPositions = new()
            {
                ["001"] = new() { X = -200, Y = -100 },
                ["002"] = new() { X = 100, Y = -100 },
                ["003"] = new() { X = -2000, Y = -100 }
            }
        }
    };
}
