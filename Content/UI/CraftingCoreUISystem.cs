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
    // ModSystem that manages the Crafting Core UI lifecycle: opening/closing the
    // station-module panel, auto-closing when the player moves too far or closes
    // their inventory, blocking vanilla shift+click, and injecting the draw layer
    // into Terraria's interface stack. 
    public class CraftingCoreUISystem : ModSystem
    {
        private const float MaxInteractDistance = 15f; // tiles

        private UserInterface _userInterface;
        private CraftingCoreUIState _uiState;
        private bool _isOpen;
        private Point16 _entityTilePos;

        public override void Load()
        {
            if (!Main.dedServ)
            {
                _userInterface = new UserInterface();
                _uiState = new CraftingCoreUIState();
                _uiState.Activate();
            }
        }

        public void OpenCraftingCore(CraftingCoreEntity entity)
        {
            if (Main.dedServ)
                return;

            // Only one TerraStorage UI open at a time
            ModContent.GetInstance<DriveBayUISystem>()?.CloseDriveBay();
            ModContent.GetInstance<TerminalUISystem>()?.CloseTerminal();

            _uiState.SetEntity(entity);
            _entityTilePos = entity.Position;
            _userInterface.SetState(_uiState);
            _isOpen = true;
            Main.playerInventory = true;
        }

        public void CloseCraftingCore()
        {
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
                    CloseCraftingCore();
                    return;
                }

                var playerTilePos = Main.LocalPlayer.Center / 16f;
                float dist = Vector2.Distance(playerTilePos, _entityTilePos.ToVector2());
                if (dist > MaxInteractDistance)
                {
                    CloseCraftingCore();
                    return;
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
                    "TerraStorage: Crafting Core UI",
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
