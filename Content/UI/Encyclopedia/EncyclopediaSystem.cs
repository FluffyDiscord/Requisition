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

        public int GetGridHoveredItemType() => _isOpen ? _state?.GetGridHoveredItemType() ?? 0 : 0;

        public override void Load()
        {
            if (Main.dedServ) return;

            ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "Encyclopedia", "Z");
            QueryKeybind = KeybindLoader.RegisterKeybind(Mod, "EncyclopediaQuery", "Z");

            _userInterface = new UserInterface();
            _state = new EncyclopediaState();
            _state.Activate();

            RequisitionWindows.Register(
                "TerraStorage: Encyclopedia",
                isOpen: () => _isOpen,
                isMouseOver: () => _state.IsMouseOverPanel(),
                update: gameTime => _userInterface.Update(gameTime),
                draw: DrawPanel);
        }

        private bool DrawPanel()
        {
            if (_state.IsMouseOverPanel())
            {
                Main.HoverItem = new Item();
                Main.hoverItemName = string.Empty;
            }
            _userInterface.Draw(Main.spriteBatch, new GameTime());
            return true;
        }

        public override void Unload()
        {
            ToggleKeybind = null;
            QueryKeybind = null;
        }

        public override void OnWorldLoad()
        {
            if (Main.dedServ) return;
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
            _state?.DeactivateSearch();
            _userInterface.SetState(null);
            _isOpen = false;
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (Main.dedServ) return;

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
        }

        private int GetHoveredItemType()
        {
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

    }
}
