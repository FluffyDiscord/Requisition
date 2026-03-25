using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.UI;
using TerraStorage.Content.Tiles;

namespace TerraStorage.Content.UI
{
    /// <summary>
    /// ModSystem that manages the Terminal UI lifecycle: opening/closing the Terminal panel,
    /// auto-closing when the player moves too far or closes their inventory, blocking vanilla
    /// shift+click while the panel is open, and injecting the draw layer into Terraria's
    /// interface stack.
    /// </summary>
    public class TerminalUISystem : ModSystem
    {
        private const float MaxInteractDistance = 15f; // tiles

        private UserInterface _userInterface;
        private TerminalUIState _uiState;
        private bool _isOpen;
        private bool _remoteOpen;
        private Point16 _entityTilePos;

        public bool IsTerminalOpen => _isOpen;

        public override void Load()
        {
            if (!Main.dedServ)
            {
                _userInterface = new UserInterface();
                _uiState = new TerminalUIState();
                _uiState.Activate();
            }
        }

        public void OpenTerminal(TerminalEntity entity)
        {
            if (Main.dedServ) return;
            ModContent.GetInstance<DriveBayUISystem>()?.CloseDriveBay();
            ModContent.GetInstance<CraftingCoreUISystem>()?.CloseCraftingCore();
            _uiState.SetTerminal(entity);
            _entityTilePos = entity.Position;
            _userInterface.SetState(_uiState);
            _isOpen = true;
            _remoteOpen = false;
            Main.playerInventory = true;
        }

        /// <summary>
        /// Opens the terminal UI without a proximity requirement (used by the Remote Terminal item).
        /// </summary>
        public void OpenTerminalRemote(TerminalEntity entity)
        {
            OpenTerminal(entity);
            _remoteOpen = true;
        }

        public void CloseTerminal()
        {
            if (!(TerraStorageClientConfig.Instance?.RememberSearchQuery ?? true))
                _uiState.ClearSearch();
            _userInterface.SetState(null);
            _isOpen = false;
        }

        public override void PreUpdatePlayers()
        {
            if (_isOpen && !Main.dedServ)
            {
                bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
                if (shift)
                {
                    for (int i = 0; i < 50; i++)
                    {
                        if (DriveBayUIState.IsMouseOverInventorySlot(i))
                        {
                            Main.LocalPlayer.mouseInterface = true;
                            break;
                        }
                    }
                }
            }
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (_isOpen)
            {
                if (!Main.playerInventory)
                {
                    CloseTerminal();
                    return;
                }

                if (!_remoteOpen)
                {
                    var playerTilePos = Main.LocalPlayer.Center / 16f;
                    float dist = Vector2.Distance(playerTilePos, _entityTilePos.ToVector2());
                    if (dist > MaxInteractDistance)
                    {
                        CloseTerminal();
                        return;
                    }
                }

                UIDrawHelpers.SafeUpdate(_userInterface, gameTime);
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int inventoryIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Inventory"));
            if (inventoryIndex != -1 && _isOpen)
            {
                // Block vanilla shift+click and hide crafting menu
                layers.Insert(inventoryIndex, new LegacyGameInterfaceLayer(
                    "TerraStorage: Shift Click Block",
                    delegate
                    {
                        Main.hidePlayerCraftingMenu = true;
                        if (_uiState.IsMouseOverPanel())
                        {
                            Main.LocalPlayer.mouseInterface = true;
                            Main.mouseLeftRelease = false;
                            Main.mouseRightRelease = false;
                        }
                        bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
                        if (shift && Main.mouseLeft && Main.mouseLeftRelease)
                        {
                            for (int i = 0; i < 50; i++)
                            {
                                if (DriveBayUIState.IsMouseOverInventorySlot(i))
                                {
                                    Main.mouseLeftRelease = false;
                                    break;
                                }
                            }
                        }
                        return true;
                    },
                    InterfaceScaleType.UI));

                layers.Insert(inventoryIndex + 2, new LegacyGameInterfaceLayer(
                    "TerraStorage: Terminal UI",
                    delegate
                    {
                        if (_uiState.IsMouseOverPanel())
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
}
