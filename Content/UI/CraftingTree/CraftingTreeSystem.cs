using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using TerraStorage.Systems;

namespace TerraStorage.Content.UI.CraftingTree
{
    // ModSystem that manages the Crafting Tree window lifecycle: keybind registration,
    // open/close logic, update ticking, and draw-layer injection.
    public class CraftingTreeSystem : ModSystem
    {
        private CraftingTreeState _state;
        private bool _isOpen;

        public static ModKeybind OpenTreeKeybind { get; private set; }

        public bool IsTreeOpen => _isOpen;

        public override void Load()
        {
            if (Main.dedServ) return;

            OpenTreeKeybind = KeybindLoader.RegisterKeybind(Mod, "CraftingTree", "X");
            _state = new CraftingTreeState();
            _state.Activate();
        }

        public override void Unload()
        {
            OpenTreeKeybind = null;
        }

        public void OpenTree(int itemType)
        {
            if (Main.dedServ) return;

            // Load saved position/size or default to center screen
            if (UIPositionStore.TryGetSize("craftingtree", out float w, out float h))
                _state.SetSize(w, h);

            if (UIPositionStore.TryGet("craftingtree", out float x, out float y))
            {
                _state.SetPosition(x, y);
            }
            else
            {
                _state.SetPosition(
                    (Main.screenWidth - 900) / 2f,
                    (Main.screenHeight - 550) / 2f);
            }

            _state.OpenForItem(itemType);
            _isOpen = true;
        }

        public void CloseTree()
        {
            if (!_isOpen) return;
            var (x, y) = _state.GetPosition();
            var (w, h) = _state.GetSize();
            UIPositionStore.SaveWithSize("craftingtree", x, y, w, h);
            _isOpen = false;
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (Main.dedServ) return;

            // Check keybind to open tree on hovered inventory item
            if (OpenTreeKeybind?.JustPressed == true)
            {
                int hoveredItem = GetHoveredItemType();
                if (hoveredItem > 0)
                {
                    if (_isOpen)
                        CloseTree();
                    OpenTree(hoveredItem);
                }
                else if (_isOpen)
                {
                    CloseTree();
                }
            }

            if (_isOpen)
                _state.Update(gameTime);
        }

        // Gets the item type currently under the mouse cursor in the player's inventory
        // or any visible UI slot.
        private static int GetHoveredItemType()
        {
            // Check the encyclopedia grid (works during UpdateUI, before Draw sets Main.HoverItem)
            int encItem = ModContent.GetInstance<Encyclopedia.EncyclopediaSystem>()?.GetGridHoveredItemType() ?? 0;
            if (encItem > 0) return encItem;

            // Check if hovering over an inventory slot
            var player = Main.LocalPlayer;
            for (int i = 0; i < 50; i++)
            {
                if (DriveBayUIState.IsMouseOverInventorySlot(i) && !player.inventory[i].IsAir)
                    return player.inventory[i].type;
            }

            // Check mouse item
            if (Main.mouseItem != null && !Main.mouseItem.IsAir)
                return Main.mouseItem.type;

            // Check HoverItem (set by various UI elements)
            if (Main.HoverItem != null && !Main.HoverItem.IsAir && Main.HoverItem.type > ItemID.None)
                return Main.HoverItem.type;

            return 0;
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            if (!_isOpen) return;

            // Insert above inventory layer so the tree draws over everything
            int idx = layers.FindIndex(l => l.Name.Equals("Vanilla: Inventory"));
            if (idx == -1) return;

            // Input-blocking layer BEFORE inventory — prevents clicks passing through
            layers.Insert(idx, new LegacyGameInterfaceLayer(
                "TerraStorage: Crafting Tree Input",
                delegate
                {
                    if (_state.IsMouseOverPanel())
                    {
                        Main.LocalPlayer.mouseInterface = true;
                        Main.mouseLeftRelease = false;
                        Main.mouseRightRelease = false;
                    }
                    return true;
                },
                InterfaceScaleType.UI));

            // Draw layer AFTER inventory (idx shifted by 1 from the insert above)
            layers.Insert(idx + 2, new LegacyGameInterfaceLayer(
                "TerraStorage: Crafting Tree",
                delegate
                {
                    if (_state.IsMouseOverPanel())
                    {
                        Main.HoverItem = new Item();
                        Main.hoverItemName = string.Empty;
                    }
                    _state.DrawTree(Main.spriteBatch);
                    return true;
                },
                InterfaceScaleType.UI));
        }
    }
}
