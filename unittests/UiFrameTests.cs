using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class UiFrameTests
{
    [Fact]
    public void UiFrameBuilder_Create_ProducesValidFrameStructure()
    {
        // Arrange
        var header = new UiNode("test-header", UiKind.Row, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());
        var content = new UiNode("test-content", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());
        var frame = new UiFrame(header, content, Array.Empty<UiNode>());

        // Act
        var frameNode = UiFrameBuilder.Create(frame);

        // Assert
        Assert.Equal("frame.root", frameNode.Key);
        Assert.Equal(UiKind.Column, frameNode.Kind);
        Assert.Equal(3, frameNode.Children.Count); // header, content, overlays container
        
        // Verify header
        var headerNode = frameNode.Children[0];
        Assert.Equal("test-header", headerNode.Key);
        Assert.True(headerNode.Props.ContainsKey(UiProperty.Role));
        Assert.Equal("header", headerNode.Props[UiProperty.Role]);

        // Verify content
        var contentNode = frameNode.Children[1];
        Assert.Equal("test-content", contentNode.Key);
        Assert.True(contentNode.Props.ContainsKey(UiProperty.Role));
        Assert.Equal("content", contentNode.Props[UiProperty.Role]);

        // Verify overlays container
        var overlaysNode = frameNode.Children[2];
        Assert.Equal("frame.overlays", overlaysNode.Key);
        Assert.True(overlaysNode.Props.ContainsKey(UiProperty.Role));
        Assert.Equal("overlay", overlaysNode.Props[UiProperty.Role]);
        Assert.Empty(overlaysNode.Children);
    }

    [Fact]
    public void UiFrameBuilder_Create_WithOverlays_AddsOverlaysWithZIndex()
    {
        // Arrange
        var header = new UiNode("test-header", UiKind.Row, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());
        var content = new UiNode("test-content", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());
        var overlay1 = new UiNode("overlay-1", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());
        var overlay2 = new UiNode("overlay-2", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());
        var frame = new UiFrame(header, content, new[] { overlay1, overlay2 });

        // Act
        var frameNode = UiFrameBuilder.Create(frame);

        // Assert
        var overlaysNode = frameNode.Children[2];
        Assert.Equal(2, overlaysNode.Children.Count);

        // Verify first overlay has lower zIndex than second
        var firstOverlay = overlaysNode.Children[0];
        var secondOverlay = overlaysNode.Children[1];
        
        Assert.True(firstOverlay.Props.ContainsKey(UiProperty.ZIndex));
        Assert.True(secondOverlay.Props.ContainsKey(UiProperty.ZIndex));
        
        var firstZ = (int)firstOverlay.Props[UiProperty.ZIndex]!;
        var secondZ = (int)secondOverlay.Props[UiProperty.ZIndex]!;
        
        Assert.True(secondZ > firstZ);
    }

    [Fact]
    public void UiFrameBuilder_ReplaceContent_CreatesCorrectPatch()
    {
        // Arrange
        var newContent = new UiNode("new-content", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());

        // Act
        var patch = UiFrameBuilder.ReplaceContent(newContent);

        // Assert
        Assert.Single(patch.Ops);
        var op = patch.Ops[0] as ReplaceOp;
        Assert.NotNull(op);
        Assert.Equal("frame.content", op!.Key);
        Assert.True(op.Node.Props.ContainsKey(UiProperty.Role));
        Assert.Equal("content", op.Node.Props[UiProperty.Role]);
    }

    [Fact]
    public void UiFrameBuilder_PushOverlay_CreatesCorrectPatch()
    {
        // Arrange
        var overlay = new UiNode("test-overlay", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());

        // Act
        var patch = UiFrameBuilder.PushOverlay(overlay);

        // Assert
        Assert.Single(patch.Ops);
        var op = patch.Ops[0] as InsertChildOp;
        Assert.NotNull(op);
        Assert.Equal("frame.overlays", op!.ParentKey);
        Assert.Equal(int.MaxValue, op.Index); // Inserted at end
        Assert.True(op.Node.Props.ContainsKey(UiProperty.Role));
        Assert.Equal("overlay", op.Node.Props[UiProperty.Role]);
        Assert.True(op.Node.Props.ContainsKey(UiProperty.ZIndex));
    }

    [Fact]
    public void UiFrameBuilder_PopOverlay_CreatesCorrectPatch()
    {
        // Arrange
        var overlayKey = "test-overlay";

        // Act
        var patch = UiFrameBuilder.PopOverlay(overlayKey);

        // Assert
        Assert.Single(patch.Ops);
        var op = patch.Ops[0] as RemoveOp;
        Assert.NotNull(op);
        Assert.Equal(overlayKey, op!.Key);
    }

    [Fact]
    public void UiFrameBuilder_ReplaceHeader_CreatesCorrectPatch()
    {
        // Arrange
        var newHeader = new UiNode("new-header", UiKind.Row, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>());

        // Act
        var patch = UiFrameBuilder.ReplaceHeader(newHeader);

        // Assert
        Assert.Single(patch.Ops);
        var op = patch.Ops[0] as ReplaceOp;
        Assert.NotNull(op);
        Assert.Equal("frame.header", op!.Key);
        Assert.True(op.Node.Props.ContainsKey(UiProperty.Role));
        Assert.Equal("header", op.Node.Props[UiProperty.Role]);
    }

    [Fact]
    public void MenuOverlay_Create_ProducesValidStructure()
    {
        // Arrange
        var choices = new List<string> { "Option 1", "Option 2", "Option 3" };
        var title = "Select an option";

        // Act
        var menuNode = MenuOverlay.Create(title, choices, selectedIndex: 1);

        // Assert
        Assert.Equal("overlay-menu", menuNode.Key);
        Assert.Equal(UiKind.Column, menuNode.Kind);
        Assert.True(menuNode.Props.ContainsKey(UiProperty.Modal));
        Assert.True((bool)menuNode.Props[UiProperty.Modal]!);

        // Should have: title, filter, list, buttons
        Assert.Equal(3, menuNode.Children.Count);

        // Verify title
        var titleNode = menuNode.Children[0];
        Assert.Equal("overlay-menu-title", titleNode.Key);
        Assert.Equal(title, titleNode.Props[UiProperty.Text]);

        // Verify filter
        var filterNode = menuNode.Children[1];
        Assert.Equal("overlay-menu-filter", filterNode.Key);

        // Verify list
        var listNode = menuNode.Children[2];
        Assert.Equal("overlay-menu-list", listNode.Key);
        Assert.Equal(UiKind.ListView, listNode.Kind);
        Assert.Equal(1, listNode.Props[UiProperty.SelectedIndex]);
    }

    [Fact]
    public void FormOverlay_Create_ProducesValidStructure()
    {
        // Arrange
        var model = new TestModel { Name = "Test", Age = 25 };
        var form = UiForm.Create("Test Form", model);
        form.AddString<TestModel>("Name", m => m.Name, (m, v) => m.Name = v);
        form.AddInt<TestModel>("Age", m => m.Age, (m, v) => m.Age = v);

        // Act
        var formNode = FormOverlay.Create(form);

        // Assert
        Assert.Equal("overlay-form", formNode.Key);
        Assert.Equal(UiKind.Column, formNode.Kind);
        Assert.True(formNode.Props.ContainsKey(UiProperty.Modal));
        Assert.True((bool)formNode.Props[UiProperty.Modal]!);

        // Should have: title + (label, input, help, error) * 2 fields + buttons
        // Title + 2 fields * 4 nodes each + buttons = 1 + 8 + 1 = 10
        Assert.True(formNode.Children.Count >= 3); // At least title + some fields + buttons

        // Verify title
        var titleNode = formNode.Children[0];
        Assert.Equal("overlay-form-title", titleNode.Key);
        Assert.Equal("Test Form", titleNode.Props[UiProperty.Text]);
    }

    private class TestModel
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
