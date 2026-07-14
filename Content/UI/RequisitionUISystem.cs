using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace TerraStorage.Content.UI
{
    // Sole owner of Requisition's interface layers and per-frame window updates.
    //
    // Windows update top-to-bottom and draw bottom-to-top, so the window the player sees on top is
    // the one that consumes the click. UIClickBlocker keeps its existing "first to consume wins"
    // contract; running the update loop in z-order is what turns that into "topmost wins".
    //
    // Layers anchor above "Vanilla: Mouse Text", which places every window above the inventory, the
    // info accessories, the settings button and the builder toggles, while leaving tooltips, chat
    // and the cursor on top. Nothing clickable in vanilla is left above a Requisition window.
    public class RequisitionUISystem : ModSystem
    {
        private const string AnchorLayer = "Vanilla: Mouse Text";
        private const string FallbackAnchorLayer = "Vanilla: Inventory";

        private bool _prevLeft;
        private bool _prevRight;
        private bool _prevMiddle;

        public override void Unload() => RequisitionWindows.Clear();

        public override void UpdateUI(GameTime gameTime)
        {
            if (Main.dedServ)
                return;

            // Before any window updates, so nothing can consume a click into the previous frame,
            // and the real button state is recorded before any suppression can zero it.
            UIClickBlocker.BeginFrame(Main.mouseLeft, Main.mouseRight, Main.mouseMiddle);

            bool left = Main.mouseLeft;
            bool right = Main.mouseRight;
            bool middle = Main.mouseMiddle;
            bool pressEdge = (left && !_prevLeft) || (right && !_prevRight) || (middle && !_prevMiddle);
            _prevLeft = left;
            _prevRight = right;
            _prevMiddle = middle;

            RaiseNewlyOpenedWindows();

            // Skipped mid-drag: a drag that sweeps the cursor across another window must not
            // reorder the stack under the player's hand.
            if (pressEdge && !RequisitionWindows.AnyWindowInteracting)
            {
                int top = RequisitionWindows.TopWindowAtMouse();
                if (top != -1)
                    RequisitionWindows.Raise(top);
            }

            UpdateWindowsTopFirst(gameTime);
        }

        // Registration order is ModSystem load order, which has nothing to do with what the player
        // sees. Without this a window opened later could render beneath one opened earlier.
        private static void RaiseNewlyOpenedWindows()
        {
            foreach (RequisitionWindow window in RequisitionWindows.All)
            {
                bool open = window.IsOpen();
                if (open && !window.WasOpen)
                    RequisitionWindows.Raise(window.Handle);
                window.WasOpen = open;
            }
        }

        private static void UpdateWindowsTopFirst(GameTime gameTime)
        {
            IReadOnlyList<int> zOrder = RequisitionWindows.ZOrder;
            for (int i = zOrder.Count - 1; i >= 0; i--)
            {
                RequisitionWindow window = RequisitionWindows.ByHandle(zOrder[i]);

                // Re-checked per window rather than once up front: a system's own UpdateUI may close
                // its window earlier this same frame, and ModSystem update order is not guaranteed.
                if (window == null || !window.IsOpen())
                    continue;

                UpdateSuppressed(window, gameTime);
            }
        }

        // Hides a click that a window above already consumed. Every button is zeroed, not just left
        // and right: a middle-click is a click, and a window that still saw it would act on a press
        // meant for the window on top of it.
        //
        // Applied centrally, in the one place that wraps every window, so it also covers the Crafting Tree and the
        // Favorites toggle button, which have no UserInterface and poll Main.mouse* directly.
        private static void UpdateSuppressed(RequisitionWindow window, GameTime gameTime)
        {
            if (!UIClickBlocker.IsConsumed)
            {
                window.Update(gameTime);
                return;
            }

            bool savedLeft = Main.mouseLeft;
            bool savedRight = Main.mouseRight;
            bool savedMiddle = Main.mouseMiddle;
            Main.mouseLeft = false;
            Main.mouseRight = false;
            Main.mouseMiddle = false;
            try
            {
                window.Update(gameTime);
            }
            finally
            {
                Main.mouseLeft = savedLeft;
                Main.mouseRight = savedRight;
                Main.mouseMiddle = savedMiddle;
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            if (Main.dedServ)
                return;

            bool anyOpen = false;
            foreach (RequisitionWindow window in RequisitionWindows.All)
            {
                if (!window.IsOpen())
                    continue;

                anyOpen = true;

                // Per window, never "any window is open": the Favorites toggle button is live on
                // every frame the inventory is open, so a blanket flag here would hide the vanilla
                // crafting menu permanently, for every player, with no Requisition window in sight.
                if (window.HidesVanillaCraftingMenu)
                    Main.hidePlayerCraftingMenu = true;
            }

            if (!anyOpen)
                return;

            int anchor = layers.FindIndex(layer => layer.Name.Equals(AnchorLayer));
            if (anchor == -1)
            {
                anchor = layers.FindIndex(layer => layer.Name.Equals(FallbackAnchorLayer));
                if (anchor == -1)
                    return;
                anchor++;
            }

            IReadOnlyList<int> zOrder = RequisitionWindows.ZOrder;
            for (int i = 0; i < zOrder.Count; i++)
            {
                RequisitionWindow window = RequisitionWindows.ByHandle(zOrder[i]);
                if (window == null || !window.IsOpen())
                    continue;

                layers.Insert(anchor++, new LegacyGameInterfaceLayer(
                    window.LayerName,
                    () => window.Draw(),
                    InterfaceScaleType.UI));
            }
        }
    }
}
