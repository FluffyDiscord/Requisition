using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace TerraStorage.Content.UI.CraftingTree
{
    /// <summary>
    /// A single node in the Crafting Tree graph. Each node represents an item type
    /// and holds references to child nodes in two directions:
    /// - <see cref="Children"/>: items this item can craft INTO (expand right)
    /// - <see cref="SourceChildren"/>: ingredient items needed to CREATE this item (expand left)
    /// Children are lazily expanded on user click.
    /// </summary>
    public class CraftingTreeNode
    {
        /// <summary>Item type ID for this node.</summary>
        public int ItemType { get; }

        /// <summary>Recipe that produces this node's item (null for the root node).</summary>
        public Recipe Recipe { get; }

        /// <summary>Parent node in the tree (null for root).</summary>
        public CraftingTreeNode Parent { get; set; }

        /// <summary>Child nodes — items this item is used in (right side).</summary>
        public List<CraftingTreeNode> Children { get; } = new();

        /// <summary>Source nodes — ingredients needed to create this item (left side).</summary>
        public List<CraftingTreeNode> SourceChildren { get; } = new();

        /// <summary>Whether right-side children are expanded.</summary>
        public bool IsExpanded { get; set; }

        /// <summary>Whether right-side children have been computed.</summary>
        public bool ChildrenLoaded { get; set; }

        /// <summary>Whether left-side source nodes are expanded.</summary>
        public bool IsSourceExpanded { get; set; }

        /// <summary>Whether left-side source nodes have been computed.</summary>
        public bool SourceChildrenLoaded { get; set; }

        /// <summary>True if this node lives on the left (source) side of the tree.</summary>
        public bool IsSourceSide { get; set; }

        /// <summary>
        /// True if this node represents a cycle back to an ancestor.
        /// Cycle nodes are drawn differently and cannot be expanded.
        /// </summary>
        public bool IsCycleNode { get; set; }

        /// <summary>Target position in graph-space (set during layout).</summary>
        public Vector2 Position { get; set; }

        /// <summary>Current animated position in graph-space (lerps toward Position).</summary>
        public Vector2 AnimatedPosition { get; set; }

        /// <summary>Animation progress from 0 (start) to 1 (fully arrived).</summary>
        public float AnimationProgress { get; set; } = 1f;

        /// <summary>True if this node is being collapsed (animating back to parent then removed).</summary>
        public bool IsCollapsing { get; set; }

        /// <summary>True if this ingredient node's parent recipe accepts group substitutes for it.</summary>
        public bool IsGroupIngredient { get; set; }

        /// <summary>The recipe group id when <see cref="IsGroupIngredient"/> is true.</summary>
        public int GroupId { get; set; }

        /// <summary>Opacity multiplier for fade-out during collapse animation.</summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>Size of this node's rendered box.</summary>
        public static readonly Vector2 NodeSize = new(52, 52);

        /// <summary>Spacing between sibling nodes vertically.</summary>
        public const float SiblingSpacing = 16f;

        /// <summary>Spacing between parent and child columns horizontally.</summary>
        public const float LevelSpacing = 100f;

        public CraftingTreeNode(int itemType, Recipe recipe = null)
        {
            ItemType = itemType;
            Recipe = recipe;
        }

        /// <summary>
        /// Checks if the given item type exists anywhere in the ancestor chain,
        /// used for cycle detection during child expansion.
        /// </summary>
        public bool IsAncestor(int itemType)
        {
            var current = Parent;
            while (current != null)
            {
                if (current.ItemType == itemType)
                    return true;
                current = current.Parent;
            }
            return false;
        }
    }
}
