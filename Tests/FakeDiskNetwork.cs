using System.Collections.Generic;
using TerraStorage.Common;

namespace TerraStorage.Tests
{
    // A disk network with no Terraria in it. Each disk is a list of stacks; a stack carries the mod
    // state that decides whether two draws may share one returned item, and a stack that stands for
    // itself is never pooled.
    //
    // PooledDraws and StandaloneDraws count how many times a disk was asked, which is the falsifiable
    // form of "a step needing N units walks every disk N times".
    public sealed class FakeDiskNetwork : IWithdrawalNetwork
    {
        private sealed class Stack
        {
            public int Units;
            public string State;
            public bool IsStandalone;
        }

        private sealed class Draw
        {
            public int DiskIndex;
            public List<Stack> From = new();
            public List<int> Units = new();
        }

        private readonly List<List<Stack>> _disks = new();
        private readonly List<Draw> _draws = new();
        private readonly List<string> _stateGroups = new();

        public int PooledDraws;
        public int StandaloneDraws;

        public int TotalDraws => PooledDraws + StandaloneDraws;

        public FakeDiskNetwork WithDisk() { _disks.Add(new List<Stack>()); return this; }

        // Pooled stock carrying one mod state, as a single stack.
        public FakeDiskNetwork WithPooled(int diskIndex, int units, string state)
        {
            _disks[diskIndex].Add(new Stack { Units = units, State = state });
            return this;
        }

        // Stacks that each stand for themselves - armour, a storage disk, anything a mod refuses to
        // stack. One unit each unless told otherwise.
        public FakeDiskNetwork WithStandalone(int diskIndex, int stackCount, int unitsEach = 1)
        {
            for (int stack = 0; stack < stackCount; stack++)
                _disks[diskIndex].Add(new Stack { Units = unitsEach, IsStandalone = true, State = "standalone" + _disks[diskIndex].Count });
            return this;
        }

        public int DiskCount => _disks.Count;

        public int UnitsOn(int diskIndex)
        {
            int total = 0;
            foreach (Stack stack in _disks[diskIndex])
                total += stack.Units;
            return total;
        }

        public int SlotsOn(int diskIndex) => _disks[diskIndex].Count;

        public int TotalUnits
        {
            get
            {
                int total = 0;
                for (int disk = 0; disk < _disks.Count; disk++)
                    total += UnitsOn(disk);
                return total;
            }
        }

        public DrawnUnits DrawPooled(int diskIndex, int amount)
        {
            PooledDraws++;
            return TakeFrom(diskIndex, amount, standalone: false);
        }

        public DrawnUnits DrawStandalone(int diskIndex, int amount)
        {
            StandaloneDraws++;

            // "Pooled stock is drained first, and a stack that stands for itself comes out only
            // when nothing pooled matched" is StackSelection.PlanWithdrawal's own rule, which
            // TakeFrom already applies - a second hand-written copy of it here would be exactly the
            // drift 23a/23b/23c are each an instance of.
            return TakeFrom(diskIndex, amount, standalone: true);
        }

        public void PutBack(DrawnUnits draw)
        {
            Draw record = _draws[draw.DrawIndex];
            List<Stack> stacks = _disks[record.DiskIndex];

            for (int index = 0; index < record.From.Count; index++)
            {
                Stack stack = record.From[index];
                stack.Units += record.Units[index];

                if (!stacks.Contains(stack))
                    stacks.Add(stack);
            }
        }

        // Which stacks a draw comes from is StackSelection.PlanWithdrawal's decision, the same one
        // DiskData.ExtractItem carries out. Deciding it a second time here would be a second
        // encoding of the rule NW-* exists to test.
        private DrawnUnits TakeFrom(int diskIndex, int amount, bool standalone)
        {
            if (amount <= 0)
                return DrawnUnits.Nothing(diskIndex);

            List<Stack> stacks = _disks[diskIndex];
            var matching = new List<StackSlot>();

            for (int index = 0; index < stacks.Count; index++)
            {
                matching.Add(new StackSlot
                {
                    Index = index,
                    Stack = stacks[index].Units,
                    IsUnique = stacks[index].IsStandalone
                });
            }

            var draws = StackSelection.PlanWithdrawal(matching, amount, standalone, out bool standaloneStack);
            if (draws.Count == 0 || (standalone && !standaloneStack))
                return DrawnUnits.Nothing(diskIndex);

            var record = new Draw { DiskIndex = diskIndex };
            int taken = 0;
            string state = stacks[draws[0].Index].State;
            bool everyDrawSharesState = true;

            foreach (var draw in draws)
            {
                Stack stack = stacks[draw.Index];
                if (stack.State != state)
                    everyDrawSharesState = false;

                stack.Units -= draw.Count;
                record.From.Add(stack);
                record.Units.Add(draw.Count);
                taken += draw.Count;
            }

            // Mirrors DiskData.ExtractItem: a stack drained to nothing gives up its slot, so what the
            // next withdrawal sees - and what a put-back has to restore - is a shorter disk.
            stacks.RemoveAll(s => s.Units <= 0);

            if (taken <= 0)
                return DrawnUnits.Nothing(diskIndex);

            _draws.Add(record);

            // Mirrors DiskData.AllDrawsShareModState: state rides along only when every stack drawn
            // from carried the same state.
            string reported = everyDrawSharesState ? state : null;
            return new DrawnUnits(diskIndex, _draws.Count - 1, taken, StateGroupOf(reported));
        }

        private int StateGroupOf(string state)
        {
            for (int group = 0; group < _stateGroups.Count; group++)
            {
                if (_stateGroups[group] == state)
                    return group;
            }

            _stateGroups.Add(state);
            return _stateGroups.Count - 1;
        }
    }
}
