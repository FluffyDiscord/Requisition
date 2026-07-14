using System.Collections.Generic;

namespace TerraStorage.Content.UI
{
    // Z-order model for Requisition's floating windows: which one is on top, and which one a click
    // at a given position belongs to.
    //
    // Deliberately free of Terraria and XNA types so Tests.csproj can compile it directly and
    // exercise the arbitration rule without the game.
    public sealed class WindowStackCore
    {
        private readonly List<int> _zOrder = new();
        private readonly List<int> _keepAbove = new();
        private int _nextHandle;

        //Registered windows from bottom to top.
        public IReadOnlyList<int> ZOrder => _zOrder;

        // Registers a new window on top of the stack and returns its handle. keepAbove names a
        // window this one is a child dialog of: raising the parent also raises the child, so
        // clicking the parent can never bury the dialog it opened.
        public int Register(int keepAbove = -1)
        {
            int handle = _nextHandle++;
            _zOrder.Add(handle);
            _keepAbove.Add(keepAbove);
            return handle;
        }

        // Moves a window to the top, preserving the relative order of the rest, then lifts any
        // child that must stay above it. Unknown or already-top handles are a no-op.
        public void Raise(int handle)
        {
            MoveToTop(handle);

            for (int child = 0; child < _keepAbove.Count; child++)
            {
                if (_keepAbove[child] == handle)
                    MoveToTop(child);
            }
        }

        private void MoveToTop(int handle)
        {
            int index = _zOrder.IndexOf(handle);
            if (index < 0 || index == _zOrder.Count - 1)
                return;

            _zOrder.RemoveAt(index);
            _zOrder.Add(handle);
        }

        // The topmost window whose hovered flag is set, or -1 when the cursor is over none.
        // hovered is indexed by handle; a closed window is passed as false.
        public int TopMatching(IReadOnlyList<bool> hovered)
        {
            for (int i = _zOrder.Count - 1; i >= 0; i--)
            {
                int handle = _zOrder[i];
                if (handle < hovered.Count && hovered[handle])
                    return handle;
            }
            return -1;
        }

        public void Clear()
        {
            _zOrder.Clear();
            _keepAbove.Clear();
            _nextHandle = 0;
        }
    }
}
