using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Requisition.Content.UI.CraftingTree
{
    // A single node in the Crafting Tree graph. Each node represents an item type
    // and holds references to child nodes in two directions:
    // - Children: items this item can craft INTO (expand right)
    // - SourceChildren: ingredient items needed to CREATE this item (expand left)
    // Children are lazily expanded on user click.
    public class CraftingTreeNode
    {
        //Item type ID for this node.
        public int ItemType { get; }

        //Recipe that produces this node's item (null for the root node).
        public Recipe Recipe { get; }

        //Parent node in the tree (null for root).
        public CraftingTreeNode Parent { get; set; }

        //Child nodes — items this item is used in (right side).
        public List<CraftingTreeNode> Children { get; } = new();

        //Source nodes — ingredients needed to create this item (left side).
        public List<CraftingTreeNode> SourceChildren { get; } = new();

        //Whether right-side children are expanded.
        public bool IsExpanded { get; set; }

        //Whether right-side children have been computed.
        public bool ChildrenLoaded { get; set; }

        //Whether left-side source nodes are expanded.
        public bool IsSourceExpanded { get; set; }

        //Whether left-side source nodes have been computed.
        public bool SourceChildrenLoaded { get; set; }

        //True if this node lives on the left (source) side of the tree.
        public bool IsSourceSide { get; set; }

                // True if this node represents a cycle back to an ancestor.
        // Cycle nodes are drawn differently and cannot be expanded.
        // 
        public bool IsCycleNode { get; set; }

        //Target position in graph-space (set during layout).
        public Vector2 Position { get; set; }

        //Current animated position in graph-space (lerps toward Position).
        public Vector2 AnimatedPosition { get; set; }

        //Animation progress from 0 (start) to 1 (fully arrived).
        public float AnimationProgress { get; set; } = 1f;

        //True if this node is being collapsed (animating back to parent then removed).
        public bool IsCollapsing { get; set; }

        //True if this ingredient node's parent recipe accepts group substitutes for it.
        public bool IsGroupIngredient { get; set; }

        //The recipe group id when <see cref="IsGroupIngredient"/> is true.
        public int GroupId { get; set; }

        //Opacity multiplier for fade-out during collapse animation.
        public float Opacity { get; set; } = 1f;

        //Size of this node's rendered box.
        public static readonly Vector2 NodeSize = new(52, 52);

        //Spacing between sibling nodes vertically.
        public const float SiblingSpacing = 16f;

        //Spacing between parent and child columns horizontally.
        public const float LevelSpacing = 100f;

        public CraftingTreeNode(int itemType, Recipe recipe = null)
        {
            ItemType = itemType;
            Recipe = recipe;
        }

        // Checks if the given item type exists anywhere in the ancestor chain,
        // used for cycle detection during child expansion.
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
