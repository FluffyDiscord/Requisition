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
    public class DriveBayUISystem : ModSystem
    {
        private const float MaxInteractDistance = 15f; // tiles

        private UserInterface _userInterface;
        private DriveBayUIState _uiState;
        private bool _isOpen;
        private Point16 _entityTilePos;

        private UserInterface _recoveryInterface;
        private DiskRecoveryUIState _recoveryState;
        private bool _recoveryOpen;

        public bool IsOpen => _isOpen;

        public bool IsMouseOverPanel() => _isOpen &&
            ((_uiState != null && _uiState.IsMouseOverPanel()) ||
             (_recoveryOpen && _recoveryState != null && _recoveryState.IsMouseOverPanel()));

        public override void Load()
        {
            if (!Main.dedServ)
            {
                _userInterface = new UserInterface();
                _uiState = new DriveBayUIState();
                _uiState.Activate();

                _recoveryInterface = new UserInterface();
                _recoveryState = new DiskRecoveryUIState();
                _recoveryState.Activate();
            }
        }

        public void OpenDriveBay(DriveBayEntity entity)
        {
            if (Main.dedServ)
                return;

            ModContent.GetInstance<CraftingCoreUISystem>()?.CloseCraftingCore();
            ModContent.GetInstance<TerminalUISystem>()?.CloseTerminal();

            _uiState.SetEntity(entity);
            _entityTilePos = entity.Position;
            _userInterface.SetState(_uiState);
            _isOpen = true;
            Main.playerInventory = true;
        }

        public void CloseDriveBay()
        {
            CloseDiskRecovery();
            _userInterface.SetState(null);
            _isOpen = false;
        }

        public void OpenDiskRecovery()
        {
            if (Main.dedServ) return;
            _recoveryState.Open();
            _recoveryInterface.SetState(_recoveryState);
            _recoveryOpen = true;
        }

        public void CloseDiskRecovery()
        {
            if (!_recoveryOpen) return;
            _recoveryState.ReturnDisk();
            _recoveryInterface.SetState(null);
            _recoveryOpen = false;
        }

        public override void PreUpdatePlayers()
        {
            if (_isOpen && !Main.dedServ)
            {
                if (_uiState.IsMouseOverPanel() || (_recoveryOpen && _recoveryState.IsMouseOverPanel()))
                    Main.LocalPlayer.mouseInterface = true;

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
                    CloseDriveBay();
                    return;
                }

                var playerTilePos = Main.LocalPlayer.Center / 16f;
                float dist = Vector2.Distance(playerTilePos, _entityTilePos.ToVector2());
                if (dist > MaxInteractDistance)
                {
                    CloseDriveBay();
                    return;
                }

                if (_recoveryOpen)
                    UIDrawHelpers.SafeUpdate(_recoveryInterface, gameTime);

                Main.hidePlayerCraftingMenu = true;
                UIDrawHelpers.SafeUpdate(_userInterface, gameTime);
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int inventoryIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Inventory"));
            if (inventoryIndex != -1 && _isOpen)
            {
                layers.Insert(inventoryIndex + 1, new LegacyGameInterfaceLayer(
                    "TerraStorage: Storage Block UI",
                    delegate
                    {
                        if (_uiState.IsMouseOverPanel() || (_recoveryOpen && _recoveryState.IsMouseOverPanel()))
                        {
                            Main.HoverItem = new Item();
                            Main.hoverItemName = string.Empty;
                        }
                        _userInterface.Draw(Main.spriteBatch, new GameTime());
                        return true;
                    },
                    InterfaceScaleType.UI));

                if (_recoveryOpen)
                {
                    layers.Insert(inventoryIndex + 2, new LegacyGameInterfaceLayer(
                        "TerraStorage: Disk Recovery UI",
                        delegate
                        {
                            _recoveryInterface.Draw(Main.spriteBatch, new GameTime());
                            return true;
                        },
                        InterfaceScaleType.UI));
                }
            }
        }
    }
}
