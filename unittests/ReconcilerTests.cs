using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class ReconcilerTests
{
    private static UiNode Node(string key, UiKind kind, IReadOnlyDictionary<UiProperty, object?>? props = null, params UiNode[] children)
    {
        return new UiNode(key, kind, props ?? new Dictionary<UiProperty, object?>(), children);
    }

    [Fact]
    public void BuildPatch_NullPrev_EmitsReplace()
    {
        var r = new UiReconciler();
        var next = Node("root", UiKind.Column,
            new Dictionary<UiProperty, object?> { [UiProperty.Role] = "frame" },
            Node("child", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Hi" })
        );

        var patch = r.BuildPatch(null, next);
        Assert.Single(patch.Ops);
        var op = Assert.IsType<ReplaceOp>(patch.Ops[0]);
        Assert.Equal("root", op.Key);
        Assert.Equal(next, op.Node);
    }

    [Fact]
    public void BuildPatch_KindChange_EmitsReplace()
    {
        var r = new UiReconciler();
        var prev = Node("n", UiKind.Row);
        var next = Node("n", UiKind.Column);
        var patch = r.BuildPatch(prev, next);
        var op = Assert.Single(patch.Ops) as ReplaceOp;
        Assert.NotNull(op);
        Assert.Equal("n", op!.Key);
        Assert.Equal(next, op.Node);
    }

    [Fact]
    public void BuildPatch_PropsChange_EmitsUpdateProps()
    {
        var r = new UiReconciler();
        var prev = Node("n", UiKind.TextBox, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "a", [UiProperty.Placeholder] = "ph" });
        var next = Node("n", UiKind.TextBox, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "b", [UiProperty.Placeholder] = "ph" });
        var patch = r.BuildPatch(prev, next);
        var op = Assert.Single(patch.Ops) as UpdatePropsOp;
        Assert.NotNull(op);
        Assert.Equal("n", op!.Key);
        Assert.True(op.Props.ContainsKey(UiProperty.Text));
        Assert.Equal("b", op.Props[UiProperty.Text]);
        Assert.False(op.Props.ContainsKey(UiProperty.Placeholder));
    }

    [Fact]
    public void BuildPatch_InsertAndRemoveChildren()
    {
        var r = new UiReconciler();
        var prev = Node("p", UiKind.Column, null,
            Node("a", UiKind.Label),
            Node("b", UiKind.Label)
        );
        var next = Node("p", UiKind.Column, null,
            Node("b", UiKind.Label), // keep b
            Node("c", UiKind.Label)  // insert c, remove a
        );
        var patch = r.BuildPatch(prev, next);
        // Expect remove a, maybe move b, insert c
        Assert.Contains(patch.Ops, o => o is RemoveOp ro && ro.Key == "a");
        Assert.Contains(patch.Ops, o => o is InsertChildOp ico && ico.ParentKey == "p" && ico.Node.Key == "c" && ico.Index == 1);
    }

    [Fact]
    public void BuildPatch_ReorderChildren_UsesRemoveInsert()
    {
        var r = new UiReconciler();
        var prev = Node("p", UiKind.Column, null,
            Node("a", UiKind.Label),
            Node("b", UiKind.Label),
            Node("c", UiKind.Label)
        );
        var next = Node("p", UiKind.Column, null,
            Node("c", UiKind.Label),
            Node("a", UiKind.Label),
            Node("b", UiKind.Label)
        );
        var patch = r.BuildPatch(prev, next);
        // We expect at least one remove+insert for reordering
        Assert.Contains(patch.Ops, o => o is RemoveOp ro && (ro.Key == "a" || ro.Key == "b" || ro.Key == "c"));
        Assert.Contains(patch.Ops, o => o is InsertChildOp ico && ico.ParentKey == "p");
    }

    [Fact]
    public void BuildPatch_StylesChange_EmitsReplace()
    {
        var r = new UiReconciler();
        var prev = Node("n", UiKind.Label).WithStyles(Style.AlignLeft);
        var next = Node("n", UiKind.Label).WithStyles(Style.AlignRight);
        var patch = r.BuildPatch(prev, next);
        var op = Assert.Single(patch.Ops) as ReplaceOp;
        Assert.NotNull(op);
        Assert.Equal("n", op!.Key);
        Assert.Equal(next, op.Node);
    }
}
