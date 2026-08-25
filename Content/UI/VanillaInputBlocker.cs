using System;
using System.Reflection;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerraStorage.Content.UI
{
    // Stops a click on a Requisition window from also reaching the vanilla UI underneath it.
    //
    // Vanilla exposes no settable "the interface is busy" flag: PlayerInput.IgnoreMouseInterface is
    // computed, Main.blockMouse only guards the hair sliders, and Main.LocalPlayer.mouseInterface is
    // reset in Main.DoDraw before the interface layers run, so a mod cannot signal through it. Two
    // hooks are needed, and neither covers the other:
    //
    //   Hook A forces IgnoreMouseInterface true, the gate every vanilla UI already honours. It is
    //          the only thing guarding the hotbar while the inventory is closed, which is when the
    //          Crafting Tree, the Encyclopedia and a pinned Favorites panel are still open.
    //   Hook B hides the cursor from the inventory layer, because Main.DrawLoadoutButtons hit-tests
    //          the raw coordinates and consults no gate at all. Displacing from a neighbouring
    //          layer would not work: every layer's Draw() restores the real coordinates first.
    //
    // Both stand down for gamepad users: vanilla hit-tests the snapped virtual cursor there, and
    // Requisition registers no UILinkPoints, so blocking would leave neither UI reachable.
    internal class VanillaInputBlocker : ILoadable
    {
        // Far outside any UI rect at any resolution or UI scale.
        private const int OffScreen = -9999;

        public void Load(Mod mod)
        {
            if (Main.dedServ)
                return;

            PropertyInfo ignoreMouseInterface =
                typeof(PlayerInput).GetProperty(nameof(PlayerInput.IgnoreMouseInterface));
            MethodInfo getter = ignoreMouseInterface?.GetGetMethod();
            if (getter == null)
                throw new InvalidOperationException(
                    "Requisition: PlayerInput.IgnoreMouseInterface getter not found. tModLoader's input " +
                    "API has changed, so Requisition's panels can no longer block clicks from reaching " +
                    "the vanilla UI underneath them.");

            MonoModHooks.Add(getter, (Func<Func<bool>, bool>)ForceInterfaceInert);
            On_Main.DrawInterface_27_Inventory += HideCursorFromInventory;
        }

        public void Unload()
        {
            if (Main.dedServ)
                return;

            On_Main.DrawInterface_27_Inventory -= HideCursorFromInventory;
        }

        private static bool ForceInterfaceInert(Func<bool> orig)
            => orig() || (!PlayerInput.UsingGamepad && RequisitionWindows.MouseOverAny);

        private static void HideCursorFromInventory(
            On_Main.orig_DrawInterface_27_Inventory orig, Main self)
        {
            if (PlayerInput.UsingGamepad || !RequisitionWindows.MouseOverAny)
            {
                orig(self);
                return;
            }

            int mouseX = Main.mouseX;
            int mouseY = Main.mouseY;
            Main.mouseX = OffScreen;
            Main.mouseY = OffScreen;
            try
            {
                orig(self);
            }
            finally
            {
                Main.mouseX = mouseX;
                Main.mouseY = mouseY;
            }
        }
    }
}
