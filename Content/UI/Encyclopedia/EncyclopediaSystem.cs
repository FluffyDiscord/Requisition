using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using TerraStorage.Content.UI.Elements;
using TerraStorage.Systems;

namespace TerraStorage.Content.UI.Encyclopedia
{
    public class EncyclopediaSystem : ModSystem
    {
        private UserInterface _userInterface;
        private EncyclopediaState _state;
        private bool _isOpen;

        public static ModKeybind ToggleKeybind { get; private set; }
        public static ModKeybind QueryKeybind { get; private set; }

        public bool IsOpen => _isOpen;

                // Returns the item type hovered in the encyclopedia grid, or 0.
        // Safe to call from UpdateUI.
        // 
        public int GetGridHoveredItemType() => _isOpen ? _state?.GetGridHoveredItemType() ?? 0 : 0;

        public override void Load()
        {
            if (Main.dedServ) return;

            ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "Encyclopedia", "Z");
            QueryKeybind = KeybindLoader.RegisterKeybind(Mod, "EncyclopediaQuery", "Z");

            _userInterface = new UserInterface();
            _state = new EncyclopediaState();
            _state.Activate();
        }

        public override void Unload()
        {
            ToggleKeybind = null;
            QueryKeybind = null;
        }

        public override void OnWorldLoad()
        {
            if (Main.dedServ) return;
            // Pre-populate the item grid so first open is instant.
            _state?.Open();
        }

        public void OpenEncyclopedia()
        {
            if (Main.dedServ) return;

            _state.Open();
            _userInterface.SetState(_state);
            _isOpen = true;
        }

        public void OpenForItem(int itemType)
        {
            if (Main.dedServ || itemType <= 0) return;

            _state.OpenForItem(itemType);
            _userInterface.SetState(_state);
            _isOpen = true;
        }

        public void CloseEncyclopedia()
        {
            _userInterface.SetState(null);
            _isOpen = false;
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (Main.dedServ) return;

            // Query takes priority over Toggle when hovering an item,
            // so both keybinds can share the same key without conflict.
            bool handled = false;
            if (QueryKeybind?.JustPressed == true)
            {
                int hoveredItem = GetHoveredItemType();
                if (hoveredItem > 0)
                {
                    if (_isOpen)
                        CloseEncyclopedia();
                    OpenForItem(hoveredItem);
                    handled = true;
                }
            }

            if (!handled && ToggleKeybind?.JustPressed == true)
            {
                if (_isOpen)
                    CloseEncyclopedia();
                else
                    OpenEncyclopedia();
            }

            if (_isOpen)
                UIDrawHelpers.SafeUpdate(_userInterface, gameTime);
        }

        private int GetHoveredItemType()
        {
            // Check our own grid first (works during UpdateUI, before Draw sets Main.HoverItem)
            // Only if the browse pane is visible (prevents queries on hidden grid)
            if (_isOpen && _state.IsBrowsePaneVisible())
            {
                int gridItem = _state.GetGridHoveredItemType();
                if (gridItem > 0) return gridItem;
            }

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

            // Input-blocking layer before inventory
            layers.Insert(idx, new LegacyGameInterfaceLayer(
                "TerraStorage: Encyclopedia Input",
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

            // Draw layer after inventory
            layers.Insert(idx + 2, new LegacyGameInterfaceLayer(
                "TerraStorage: Encyclopedia",
                delegate
                {
                    if (_state.IsMouseOverPanel())
                    {
                        Main.HoverItem = new Item();
                        Main.hoverItemName = string.Empty;
                    }
                    _userInterface.Draw(Main.spriteBatch, new GameTime());
                    return true;
                },
                InterfaceScaleType.UI));
        }
    }
}
