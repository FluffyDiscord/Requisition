using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Requisition.Systems;

namespace Requisition.Content.UI.CraftingTree
{
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

        private static int GetHoveredItemType()
        {
            int encItem = ModContent.GetInstance<Encyclopedia.EncyclopediaSystem>()?.GetGridHoveredItemType() ?? 0;
            if (encItem > 0) return encItem;

            var player = Main.LocalPlayer;
            for (int i = 0; i < 50; i++)
            {
                if (DriveBayUIState.IsMouseOverInventorySlot(i) && !player.inventory[i].IsAir)
                    return player.inventory[i].type;
            }

            if (Main.mouseItem != null && !Main.mouseItem.IsAir)
                return Main.mouseItem.type;

            if (Main.HoverItem != null && !Main.HoverItem.IsAir && Main.HoverItem.type > ItemID.None)
                return Main.HoverItem.type;

            return 0;
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            if (!_isOpen) return;

            int idx = layers.FindIndex(l => l.Name.Equals("Vanilla: Inventory"));
            if (idx == -1) return;

            layers.Insert(idx + 1, new LegacyGameInterfaceLayer(
                "Requisition: Crafting Tree",
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
