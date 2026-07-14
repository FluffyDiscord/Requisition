using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TerraStorage.Content.UI.Elements;

namespace TerraStorage.Content.UI
{
    // One Requisition window as the arbiter sees it: when it is live, where it is, how it updates,
    // and how it draws. Windows that own a UserInterface pass its Update; the Crafting Tree and the
    // Favorites toggle button, which have none, pass their own call. RequisitionUISystem wraps every
    // one of them in the same click suppression, so it does not matter which kind a window is.
    internal sealed class RequisitionWindow
    {
        public int Handle;
        public string LayerName;
        public Func<bool> IsOpen;
        public Func<bool> IsMouseOver;
        public Action<GameTime> Update;
        public Func<bool> Draw;
        public bool HidesVanillaCraftingMenu;
        public bool WasOpen;
    }

    // Registry of every Requisition window, held in explicit z-order.
    //
    // RequisitionUISystem drives it; VanillaInputBlocker reads MouseOverAny to decide whether the
    // vanilla UI underneath should see this frame's cursor at all.
    internal static class RequisitionWindows
    {
        private static readonly WindowStackCore Stack = new();
        private static readonly List<RequisitionWindow> Windows = new();
        private static bool[] _hovered = Array.Empty<bool>();

        public static IReadOnlyList<RequisitionWindow> All => Windows;
        public static IReadOnlyList<int> ZOrder => Stack.ZOrder;

        public static RequisitionWindow Register(
            string layerName,
            Func<bool> isOpen,
            Func<bool> isMouseOver,
            Action<GameTime> update,
            Func<bool> draw,
            bool hidesVanillaCraftingMenu = false,
            int keepAbove = -1)
        {
            var window = new RequisitionWindow
            {
                Handle = Stack.Register(keepAbove),
                LayerName = layerName,
                IsOpen = isOpen,
                IsMouseOver = isMouseOver,
                Update = update,
                Draw = draw,
                HidesVanillaCraftingMenu = hidesVanillaCraftingMenu,
            };
            Windows.Add(window);
            return window;
        }

        // Handles are assigned in registration order, so they index Windows directly.
        public static RequisitionWindow ByHandle(int handle)
            => handle >= 0 && handle < Windows.Count ? Windows[handle] : null;

        // Computed on every read, never cached for the frame: the input blockers consult this during
        // the draw phase while the windows are updated during the update phase. A cached value would
        // still report "hovered" for a window that closed in between, leaving the vanilla inventory
        // blocked for a frame after the panel had already vanished.
        public static bool MouseOverAny
        {
            get
            {
                foreach (RequisitionWindow window in Windows)
                {
                    if (window.IsOpen() && window.IsMouseOver())
                        return true;
                }
                return false;
            }
        }

        public static bool AnyWindowInteracting => UIClickBlocker.GestureActive;

        public static void Raise(int handle) => Stack.Raise(handle);

        public static int TopWindowAtMouse()
        {
            if (_hovered.Length < Windows.Count)
                _hovered = new bool[Windows.Count];

            for (int i = 0; i < Windows.Count; i++)
            {
                RequisitionWindow window = Windows[i];
                _hovered[window.Handle] = window.IsOpen() && window.IsMouseOver();
            }

            return Stack.TopMatching(_hovered);
        }

        public static void Clear()
        {
            Stack.Clear();
            Windows.Clear();
            _hovered = Array.Empty<bool>();
        }
    }
}
