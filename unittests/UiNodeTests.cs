using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CSChat.Tests
{
    public class UiNodeTests
    {
        [Fact]
        public void UiNode_CreatesWithRequiredProperties()
        {
            var node = new UiNode(
                "test-key",
                UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Hello" },
                Array.Empty<UiNode>()
            );

            Assert.Equal("test-key", node.Key);
            Assert.Equal(UiKind.Label, node.Kind);
            Assert.Equal("Hello", node.Props[UiProperty.Text]);
            Assert.Empty(node.Children);
        }

        [Fact]
        public void UiPatch_Replace_CreatesReplaceOp()
        {
            var newNode = new UiNode(
                "test-key",
                UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "New" },
                Array.Empty<UiNode>()
            );

            var patch = UiPatch.Replace("test-key", newNode);

            Assert.Single(patch.Ops);
            Assert.IsType<ReplaceOp>(patch.Ops[0]);
            var replaceOp = (ReplaceOp)patch.Ops[0];
            Assert.Equal("test-key", replaceOp.Key);
            Assert.Equal(newNode, replaceOp.Node);
        }

        [Fact]
        public void UiNodeTree_SetRoot_ValidatesUniqueKeys()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("child1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A" }, Array.Empty<UiNode>()),
                    new UiNode("child2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B" }, Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);
            Assert.NotNull(tree.Root);
            Assert.Equal("root", tree.Root.Key);
        }

        [Fact]
        public void UiNodeTree_SetRoot_ThrowsOnDuplicateKeys()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("dup", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A" }, Array.Empty<UiNode>()),
                    new UiNode("dup", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B" }, Array.Empty<UiNode>())
                }
            );

            var ex = Assert.Throws<InvalidOperationException>(() => tree.SetRoot(root));
            Assert.Contains("Duplicate keys", ex.Message);
        }

        [Fact]
        public void UiNodeTree_ApplyUpdateProps_UpdatesNode()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Original" },
                Array.Empty<UiNode>()
            );

            tree.SetRoot(root);

            var newProps = new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Updated" };
            tree.ApplyUpdateProps("root", newProps);

            Assert.Equal("Updated", tree.Root!.Props[UiProperty.Text]);
        }

        [Fact]
        public void UiNodeTree_ApplyUpdateProps_ThrowsOnMissingKey()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Original" },
                Array.Empty<UiNode>()
            );

            tree.SetRoot(root);

            var newProps = new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Updated" };
            Assert.Throws<KeyNotFoundException>(() => tree.ApplyUpdateProps("nonexistent", newProps));
        }

        [Fact]
        public void UiNodeTree_ApplyInsertChild_InsertsChild()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("child1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A" }, Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);

            var newChild = new UiNode("child2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("root", 1, newChild);

            Assert.Equal(2, tree.Root!.Children.Count);
            Assert.Equal("child2", tree.Root.Children[1].Key);
        }

        [Fact]
        public void UiNodeTree_ApplyRemove_RemovesNode()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("child1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A" }, Array.Empty<UiNode>()),
                    new UiNode("child2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B" }, Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);
            tree.ApplyRemove("child1");

            Assert.Single(tree.Root!.Children);
            Assert.Equal("child2", tree.Root.Children[0].Key);
        }

        [Fact]
        public void UiNodeTree_SetFocus_ThrowsOnNonFocusableNode()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Label, // Labels are not focusable
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Test" },
                Array.Empty<UiNode>()
            );

            tree.SetRoot(root);

            Assert.Throws<InvalidOperationException>(() => tree.SetFocus("root"));
        }

        [Fact]
        public void UiNodeTree_SetFocus_SucceedsOnFocusableNode()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Button, // Buttons are focusable
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Click" },
                Array.Empty<UiNode>()
            );

            tree.SetRoot(root);
            tree.SetFocus("root");

            Assert.Equal("root", tree.FocusedKey);
        }

        [Fact]
        public void UiNodeTree_ApplyPatch_IsAtomic()
        {
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("child1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A" }, Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);

            // Patch with one valid and one invalid operation
            var patch = new UiPatch(
                new UpdatePropsOp("child1", new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Updated" }),
                new UpdatePropsOp("nonexistent", new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Fail" })
            );

            // Should throw and rollback
            Assert.Throws<KeyNotFoundException>(() => tree.ApplyPatch(patch));

            // Original state should be preserved
            Assert.Equal("A", tree.Root!.Children[0].Props[UiProperty.Text]);
        }

        [Fact]
        public void UiNodeTree_MultipleSequentialInserts_MaintainsTreeIntegrity()
        {
            // This test validates the fix for the bug where sequential inserts would fail
            // because the tree wasn't being properly rebuilt after the first insert
            var tree = new UiNodeTree();
            
            // Create a ChatSurface-like structure: root > toolbar, messages, composer
            var root = new UiNode(
                "chat-root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("toolbar", UiKind.Row, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>()),
                    new UiNode("messages", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>()),
                    new UiNode("composer", UiKind.Row, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);

            // First insert - add a message to the messages container
            var msg1 = new UiNode("msg-1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "First message" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("messages", 0, msg1);

            // Verify first insert succeeded
            Assert.Single(tree.Root!.Children[1].Children);
            Assert.Equal("msg-1", tree.Root.Children[1].Children[0].Key);

            // Second insert - add another message (this is where the bug occurred)
            var msg2 = new UiNode("msg-2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Second message" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("messages", 1, msg2);

            // Verify second insert succeeded
            Assert.Equal(2, tree.Root!.Children[1].Children.Count);
            Assert.Equal("msg-2", tree.Root.Children[1].Children[1].Key);

            // Third insert - ensure continued operations work
            var msg3 = new UiNode("msg-3", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Third message" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("messages", 2, msg3);

            // Verify third insert succeeded
            Assert.Equal(3, tree.Root!.Children[1].Children.Count);
            Assert.Equal("msg-3", tree.Root.Children[1].Children[2].Key);

            // Verify the entire tree structure is still intact
            Assert.Equal("chat-root", tree.Root.Key);
            Assert.Equal(3, tree.Root.Children.Count);
            Assert.Equal("toolbar", tree.Root.Children[0].Key);
            Assert.Equal("messages", tree.Root.Children[1].Key);
            Assert.Equal("composer", tree.Root.Children[2].Key);
        }

        [Fact]
        public void UiNodeTree_InsertChild_DeepNesting_PreservesAllAncestors()
        {
            // Test that inserting into deeply nested nodes preserves all ancestor structure
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "level1",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode(
                        "level2",
                        UiKind.Column,
                        new Dictionary<UiProperty, object?>(),
                        new[]
                        {
                            new UiNode(
                                "level3",
                                UiKind.Column,
                                new Dictionary<UiProperty, object?>(),
                                Array.Empty<UiNode>()
                            )
                        }
                    )
                }
            );

            tree.SetRoot(root);

            // Insert into the deepest level
            var deepChild = new UiNode("deep-child", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Deep" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("level3", 0, deepChild);

            // Verify all levels are still present and correct
            Assert.Equal("level1", tree.Root!.Key);
            Assert.Equal("level2", tree.Root.Children[0].Key);
            Assert.Equal("level3", tree.Root.Children[0].Children[0].Key);
            Assert.Equal("deep-child", tree.Root.Children[0].Children[0].Children[0].Key);

            // Now add another child at level3 to ensure subsequent operations work
            var deepChild2 = new UiNode("deep-child-2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Deep 2" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("level3", 1, deepChild2);

            Assert.Equal(2, tree.Root!.Children[0].Children[0].Children.Count);
            Assert.Equal("deep-child-2", tree.Root.Children[0].Children[0].Children[1].Key);
        }

        [Fact]
        public void UiNodeTree_MixedOperations_MaintainsConsistency()
        {
            // Test that mixing insert, update, and other operations maintains tree consistency
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("container", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);

            // Insert a child
            var child1 = new UiNode("child1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Original" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("container", 0, child1);

            // Update the child's props
            tree.ApplyUpdateProps("child1", new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Updated" });

            // Insert another child
            var child2 = new UiNode("child2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Second" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("container", 1, child2);

            // Verify both operations succeeded
            Assert.Equal(2, tree.Root!.Children[0].Children.Count);
            Assert.Equal("Updated", tree.Root.Children[0].Children[0].Props[UiProperty.Text]);
            Assert.Equal("Second", tree.Root.Children[0].Children[1].Props[UiProperty.Text]);

            // Insert at the beginning
            var child0 = new UiNode("child0", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "First" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("container", 0, child0);

            // Verify insertion at beginning worked
            Assert.Equal(3, tree.Root!.Children[0].Children.Count);
            Assert.Equal("child0", tree.Root.Children[0].Children[0].Key);
            Assert.Equal("child1", tree.Root.Children[0].Children[1].Key);
            Assert.Equal("child2", tree.Root.Children[0].Children[2].Key);
        }

        [Fact]
        public void UiNodeTree_InsertChild_WithSiblings_PreservesSiblingStructure()
        {
            // Ensure that inserting into one branch doesn't affect sibling branches
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("branch-a", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>()),
                    new UiNode("branch-b", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>()),
                    new UiNode("branch-c", UiKind.Column, new Dictionary<UiProperty, object?>(), Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);

            // Insert into branch-b
            var childB1 = new UiNode("child-b1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B1" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("branch-b", 0, childB1);

            // Verify branch-a and branch-c are unchanged
            Assert.Empty(tree.Root!.Children[0].Children); // branch-a
            Assert.Single(tree.Root.Children[1].Children); // branch-b
            Assert.Empty(tree.Root.Children[2].Children); // branch-c

            // Insert into branch-b again
            var childB2 = new UiNode("child-b2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B2" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("branch-b", 1, childB2);

            // Verify other branches still unchanged
            Assert.Empty(tree.Root!.Children[0].Children); // branch-a
            Assert.Equal(2, tree.Root.Children[1].Children.Count); // branch-b
            Assert.Empty(tree.Root.Children[2].Children); // branch-c

            // Insert into branch-a
            var childA1 = new UiNode("child-a1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A1" }, Array.Empty<UiNode>());
            tree.ApplyInsertChild("branch-a", 0, childA1);

            // Verify all branches
            Assert.Single(tree.Root!.Children[0].Children); // branch-a
            Assert.Equal(2, tree.Root.Children[1].Children.Count); // branch-b
            Assert.Empty(tree.Root.Children[2].Children); // branch-c
        }

        [Fact]
        public void UiNodeTree_ApplyInsertChild_ThrowsDetailedErrorOnMissingParent()
        {
            // Validate that error messages are helpful for debugging
            var tree = new UiNodeTree();
            
            var root = new UiNode(
                "root",
                UiKind.Column,
                new Dictionary<UiProperty, object?>(),
                new[]
                {
                    new UiNode("child1", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "A" }, Array.Empty<UiNode>())
                }
            );

            tree.SetRoot(root);

            var newChild = new UiNode("child2", UiKind.Label, new Dictionary<UiProperty, object?> { [UiProperty.Text] = "B" }, Array.Empty<UiNode>());
            
            var ex = Assert.Throws<KeyNotFoundException>(() => tree.ApplyInsertChild("nonexistent-parent", 0, newChild));
            
            // Error message should include helpful information
            Assert.Contains("nonexistent-parent", ex.Message);
            Assert.Contains("child2", ex.Message);
            Assert.Contains("Available keys", ex.Message);
        }
    }
}
