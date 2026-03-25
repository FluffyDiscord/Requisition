using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using TerraStorage.Systems;

namespace TerraStorage.Content.UI
{
    /// <summary>
    /// A floating, collapsible, pinnable panel that shows every recipe the player has
    /// favorited via <see cref="StoragePlayerSystem"/>. For each recipe it renders the
    /// output item and each ingredient with a live have/need count drawn from storage
    /// and the player's inventory. Supports drag-to-move, scroll wheel, and a ghost
    /// (semi-transparent) mode when pinned and the inventory is closed.
    /// </summary>
    public class UIFavoritedRecipesPanel : UIState
    {
        private const float PanelWidth     = 290f;
        private const float HeaderHeight   = 26f;
        private const float MaxBodyH       = 380f;

        // Slot sizes
        private const float OutputSlotSize = 36f;
        private const float IngSlotSize    = 28f;
        private const float SlotGap        = 2f;

        // Position (screen-space, persisted between sessions)
        public float PanelLeft = 460f;
        public float PanelTop  = 200f;

        public bool IsCollapsed { get; private set; } = false;
        public bool IsPinned    { get; private set; } = false;

        /// <summary>Checks if the mouse is over the panel bounds.</summary>
        public bool IsMouseOverPanel()
        {
            var m = Main.MouseScreen;
            float bodyH = IsCollapsed ? 0f : Math.Min(ComputeBodyHeight(StoragePlayerSystem.Local.FavoritedRecipes), MaxBodyH);
            float totalH = HeaderHeight + bodyH;
            return m.X >= PanelLeft && m.X <= PanelLeft + PanelWidth
                && m.Y >= PanelTop && m.Y <= PanelTop + totalH;
        }

        private bool _dragging;
        private Vector2 _dragOffset;
        private bool _prevMouseLeft;
        private float _scrollOffset;
        private float _maxScroll;

        private List<Guid> _diskIds = new();

        // Hit-test rects rebuilt each frame
        private Rectangle _headerRect;
        private Rectangle _collapseBtnRect;
        private Rectangle _pinBtnRect;

        // Output slot rects per recipe — built during DrawBody, checked during next Update
        private readonly List<(int recipeIdx, Rectangle rect)> _recipeOutputRects = new();

        // Hover tooltip state (set during DrawBody, read after draw)
        private string _hoveredTooltip;

        public void SetDiskIds(List<Guid> ids) => _diskIds = ids ?? new();

        public void TogglePinned()
        {
            IsPinned = !IsPinned;
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            bool justClicked = Main.mouseLeft && !_prevMouseLeft && !UIClickBlocker.IsConsumed;
            _prevMouseLeft = Main.mouseLeft;

            // Ghost mode: pinned but inventory is closed — only the collapse button works.
            bool ghostMode = IsPinned && !Main.playerInventory;

            // In ghost mode only the collapse button is interactive
            if (ghostMode)
            {
                if (_collapseBtnRect.Contains(Main.MouseScreen.ToPoint()))
                {
                    Main.LocalPlayer.mouseInterface = true;
                    if (justClicked)
                    {
                        UIClickBlocker.Consume();
                        IsCollapsed = !IsCollapsed;
                        _scrollOffset = 0f;
                        SoundEngine.PlaySound(SoundID.MenuTick);
                    }
                }
                return;
            }

            var panelRect = GetPanelRect();
            if (panelRect.Contains(Main.MouseScreen.ToPoint()))
            {
                Main.LocalPlayer.mouseInterface = true;
                Terraria.GameInput.PlayerInput.LockVanillaMouseScroll("TerraStorage:FavPanel");
                if (justClicked)
                    UIClickBlocker.Consume();
            }

            if (_dragging)
            {
                if (!Main.mouseLeft)
                {
                    _dragging = false;
                    UIPositionStore.Save("favpanel", PanelLeft, PanelTop);
                }
                else
                {
                    PanelLeft = Main.MouseScreen.X - _dragOffset.X;
                    PanelTop  = Main.MouseScreen.Y - _dragOffset.Y;
                    PanelLeft = Math.Clamp(PanelLeft, 0, Main.screenWidth  - PanelWidth);
                    PanelTop  = Math.Clamp(PanelTop,  0, Main.screenHeight - HeaderHeight);
                }
                return;
            }

            if (justClicked)
            {
                bool alt = Microsoft.Xna.Framework.Input.Keyboard.GetState()
                    .IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftAlt)
                    || Microsoft.Xna.Framework.Input.Keyboard.GetState()
                    .IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightAlt);

                if (alt && !IsCollapsed)
                {
                    var mouse = Main.MouseScreen.ToPoint();
                    foreach (var (recipeIdx, rect) in _recipeOutputRects)
                    {
                        if (rect.Contains(mouse))
                        {
                            if (recipeIdx >= 0 && recipeIdx < Recipe.numRecipes)
                                StoragePlayerSystem.Local.ToggleRecipeFavorite(Main.recipe[recipeIdx]);
                            SoundEngine.PlaySound(SoundID.MenuTick);
                            UIClickBlocker.Consume();
                            return;
                        }
                    }
                }

                if (_collapseBtnRect.Contains(Main.MouseScreen.ToPoint()))
                {
                    IsCollapsed = !IsCollapsed;
                    _scrollOffset = 0f;
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    return;
                }
                if (_pinBtnRect.Contains(Main.MouseScreen.ToPoint()))
                {
                    IsPinned = !IsPinned;
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    return;
                }
                if (_headerRect.Contains(Main.MouseScreen.ToPoint()))
                {
                    _dragging   = true;
                    _dragOffset = Main.MouseScreen - new Vector2(PanelLeft, PanelTop);
                }
            }
        }

        public override void ScrollWheel(UIScrollWheelEvent evt)
        {
            base.ScrollWheel(evt);
            bool ghostMode = IsPinned && !Main.playerInventory;
            if (!ghostMode && !IsCollapsed && GetPanelRect().Contains(Main.MouseScreen.ToPoint()))
            {
                _scrollOffset -= evt.ScrollWheelValue / 120f * 20f;
                _scrollOffset  = Math.Clamp(_scrollOffset, 0f, Math.Max(0f, _maxScroll));
            }
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            _hoveredTooltip = null;

            bool ghostMode  = IsPinned && !Main.playerInventory;
            float alpha     = ghostMode ? 0.55f : 1f;  // text/icon alpha
            float bgAlpha   = ghostMode ? 0f    : 1f;  // background alpha — hidden in ghost mode

            var favorites = StoragePlayerSystem.Local.FavoritedRecipes;

            float bodyH  = IsCollapsed ? 0f : Math.Min(ComputeBodyHeight(favorites), MaxBodyH);
            float totalH = HeaderHeight + bodyH;

            var panelRect = new Rectangle((int)PanelLeft, (int)PanelTop, (int)PanelWidth, (int)totalH);

            // Panel background — hidden in ghost mode
            if (bgAlpha > 0f)
            {
                UIDrawHelpers.DrawUnderlay(spriteBatch, panelRect);
                Utils.DrawInvBG(spriteBatch, panelRect, new Color(15, 20, 50) * 0.82f * bgAlpha);
            }

            // ── Header ──────────────────────────────────────────────────────
            _headerRect = new Rectangle((int)PanelLeft, (int)PanelTop, (int)(PanelWidth - 52), (int)HeaderHeight);

            // Collapse button
            _collapseBtnRect = new Rectangle((int)(PanelLeft + PanelWidth - 50), (int)PanelTop + 3, 22, 20);
            bool colHov = !ghostMode && _collapseBtnRect.Contains(Main.MouseScreen.ToPoint());
            Color colBg = colHov ? new Color(83, 104, 181) : new Color(43, 54, 101);
            Utils.DrawInvBG(spriteBatch, _collapseBtnRect, colBg * (ghostMode ? 0.4f : 1f));
            Utils.DrawBorderString(spriteBatch, IsCollapsed ? "▲" : "▼",
                new Vector2(_collapseBtnRect.X + 4, _collapseBtnRect.Y + 2),
                Color.White * alpha, 0.7f);
            if (colHov) Main.hoverItemName = IsCollapsed ? "Expand" : "Collapse";

            // Pin button — visible only when inventory is open
            _pinBtnRect = new Rectangle((int)(PanelLeft + PanelWidth - 26), (int)PanelTop + 3, 22, 20);
            if (!ghostMode)
            {
                bool pinHov = _pinBtnRect.Contains(Main.MouseScreen.ToPoint());
                Color pinBg = IsPinned
                    ? new Color(100, 80, 20)
                    : (pinHov ? new Color(83, 104, 181) : new Color(43, 54, 101));
                Utils.DrawInvBG(spriteBatch, _pinBtnRect, pinBg);
                Utils.DrawBorderString(spriteBatch, "📌",
                    new Vector2(_pinBtnRect.X + 3, _pinBtnRect.Y + 1),
                    IsPinned ? Color.Gold : Color.White, 0.6f);
                if (pinHov) Main.hoverItemName = IsPinned ? "Unpin (hide when inventory closes)" : "Pin (keep visible)";
            }

            Utils.DrawBorderString(spriteBatch, "★ Favorited Recipes",
                new Vector2(PanelLeft + 6, PanelTop + 5), Color.Gold * alpha, 0.75f);

            if (IsCollapsed) return;

            // ── Body ────────────────────────────────────────────────────────
            float viewH   = bodyH;
            float fullH   = ComputeBodyHeight(favorites);
            _maxScroll    = Math.Max(0f, fullH - viewH);
            _scrollOffset = Math.Clamp(_scrollOffset, 0f, _maxScroll);

            // Scissor clip ensures items scrolled above/below the body area are hidden.
            // Clip rect must be in physical pixels (UIScale applied), not logical UI units.
            var clipRect = new Rectangle(
                (int)(PanelLeft  * Main.UIScale),
                (int)((PanelTop + HeaderHeight) * Main.UIScale),
                (int)(PanelWidth * Main.UIScale),
                (int)(bodyH      * Main.UIScale));

            var savedScissor = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            var rs = new Microsoft.Xna.Framework.Graphics.RasterizerState { ScissorTestEnable = true };
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.AnisotropicClamp,
                DepthStencilState.None, rs, null, Main.UIScaleMatrix);
            spriteBatch.GraphicsDevice.ScissorRectangle = clipRect;

            float y = PanelTop + HeaderHeight - _scrollOffset;
            DrawBody(spriteBatch, favorites, ref y, ghostMode, alpha);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.AnisotropicClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
            spriteBatch.GraphicsDevice.ScissorRectangle = savedScissor;

            // Scrollbar strip — thumb height proportional to visible vs. total content height.
            if (!ghostMode && _maxScroll > 0f)
            {
                float sbH    = bodyH;
                float sbX    = PanelLeft + PanelWidth - 5;
                float sbTop  = PanelTop + HeaderHeight;
                float thumbH = Math.Max(12, sbH * (viewH / (viewH + _maxScroll)));
                float thumbY = sbTop + (_scrollOffset / _maxScroll) * (sbH - thumbH);
                Utils.DrawInvBG(spriteBatch, new Rectangle((int)sbX, (int)sbTop, 4, (int)sbH), new Color(20, 28, 60) * 0.7f);
                Utils.DrawInvBG(spriteBatch, new Rectangle((int)sbX, (int)thumbY, 4, (int)thumbH), new Color(89, 116, 213) * 0.9f);
            }

            if (favorites.Count == 0)
            {
                Utils.DrawBorderString(spriteBatch, "No favorited recipes.",
                    new Vector2(PanelLeft + 8, PanelTop + HeaderHeight + 6), Color.Gray * alpha, 0.7f);
            }

            // Apply tooltip gathered during body draw
            if (_hoveredTooltip != null)
                Main.hoverItemName = _hoveredTooltip;
        }

        private void DrawBody(SpriteBatch spriteBatch, IReadOnlyCollection<int> favorites, ref float y, bool ghostMode, float alpha)
        {
            _recipeOutputRects.Clear();
            var mouse = Main.MouseScreen.ToPoint();

            const float RowH      = OutputSlotSize + 4f;  // total height per recipe row
            const float innerOut  = OutputSlotSize - 6f;
            const float innerIng  = IngSlotSize    - 4f;
            const float ingOffset = (OutputSlotSize - IngSlotSize) / 2f; // center ing slots vertically

            y += 3f;

            foreach (int recipeIdx in favorites)
            {
                if (recipeIdx < 0 || recipeIdx >= Recipe.numRecipes) continue;
                var recipe = Main.recipe[recipeIdx];

                float rowY = y;

                // ── Output slot (left) ───────────────────────────────────────
                var outRect = new Rectangle((int)(PanelLeft + 4f), (int)rowY, (int)OutputSlotSize, (int)OutputSlotSize);
                if (!ghostMode)
                    Utils.DrawInvBG(spriteBatch, outRect, new Color(63, 82, 151) * 0.5f);
                int itemType = recipe.createItem.type;
                DrawItemInSlot(spriteBatch, itemType, outRect, innerOut, alpha);
                _recipeOutputRects.Add((recipeIdx, outRect));

                if (!ghostMode && outRect.Contains(mouse))
                {
                    var hoverItem = new Item();
                    hoverItem.SetDefaults(itemType);
                    Main.HoverItem  = hoverItem;
                    _hoveredTooltip = hoverItem.Name + "\nAlt+Click to unfavorite";
                }

                // ── Ingredient slots (to the right) ──────────────────────────
                float ingX = PanelLeft + 4f + OutputSlotSize + 4f;
                float ingY = rowY + ingOffset;

                foreach (var ingredient in recipe.requiredItem)
                {
                    if (ingredient.type <= ItemID.None) continue;

                    // Stop drawing if we'd overflow the panel
                    if (ingX + IngSlotSize > PanelLeft + PanelWidth - 8f) break;

                    int needed    = ingredient.stack;
                    int inStorage = _diskIds.Count > 0
                        ? StorageWorldSystem.Instance.CountItem(_diskIds, ingredient.type)
                        : 0;
                    int inInv = CountPlayerItem(ingredient.type);
                    int have  = inStorage + inInv;

                    // Green = enough, yellow = partial, red = none.
                    Color countColor = have >= needed ? Color.LightGreen
                                     : have > 0      ? Color.Yellow
                                     :                 Color.IndianRed;

                    var slotRect = new Rectangle((int)ingX, (int)ingY, (int)IngSlotSize, (int)IngSlotSize);

                    if (!ghostMode)
                        Utils.DrawInvBG(spriteBatch, slotRect, new Color(43, 56, 110) * 0.5f);
                    DrawItemInSlot(spriteBatch, ingredient.type, slotRect, innerIng, alpha);

                    // Have/need count in bottom-right corner
                    string countText = $"{have}/{needed}";
                    var countSize = FontAssets.MouseText.Value.MeasureString(countText) * 0.45f;
                    Utils.DrawBorderString(spriteBatch, countText,
                        new Vector2(slotRect.Right - countSize.X - 1f, slotRect.Bottom - countSize.Y),
                        countColor * alpha, 0.45f);

                    if (!ghostMode && slotRect.Contains(mouse))
                    {
                        var hoverItem = new Item();
                        hoverItem.SetDefaults(ingredient.type);
                        Main.HoverItem  = hoverItem;
                        _hoveredTooltip = $"{hoverItem.Name}\n{have}/{needed}";
                    }

                    ingX += IngSlotSize + SlotGap;
                }

                y += RowH;
            }
        }

        private static float ComputeBodyHeight(IReadOnlyCollection<int> favorites)
        {
            const float RowH = OutputSlotSize + 4f;
            float total = 3f;

            foreach (int recipeIdx in favorites)
            {
                if (recipeIdx < 0 || recipeIdx >= Recipe.numRecipes) continue;
                total += RowH;
            }
            return total;
        }

        private Rectangle GetPanelRect()
        {
            float bodyH = IsCollapsed ? 0f : Math.Min(ComputeBodyHeight(StoragePlayerSystem.Local.FavoritedRecipes), MaxBodyH);
            return new Rectangle((int)PanelLeft, (int)PanelTop, (int)PanelWidth, (int)(HeaderHeight + bodyH));
        }

        private static void DrawItemInSlot(SpriteBatch spriteBatch, int itemType, Rectangle slotRect, float maxInner, float alpha)
        {
            Main.instance.LoadItem(itemType);
            var texture = TextureAssets.Item[itemType].Value;
            var srcRect = Main.itemAnimations[itemType] != null
                ? Main.itemAnimations[itemType].GetFrame(texture)
                : texture.Frame();

            float scale   = 1f;
            float maxDim  = Math.Max(srcRect.Width, srcRect.Height);
            if (maxDim > maxInner) scale = maxInner / maxDim;

            var center = new Vector2(slotRect.X + slotRect.Width / 2f, slotRect.Y + slotRect.Height / 2f);
            spriteBatch.Draw(texture, center, srcRect, Color.White * alpha, 0f,
                new Vector2(srcRect.Width / 2f, srcRect.Height / 2f), scale, SpriteEffects.None, 0f);
        }

        private static int CountPlayerItem(int itemType)
        {
            int count = 0;
            var player = Main.LocalPlayer;
            for (int i = 0; i < 50; i++)
                if (player.inventory[i] != null && !player.inventory[i].IsAir && player.inventory[i].type == itemType)
                    count += player.inventory[i].stack;
            return count;
        }
    }
}
