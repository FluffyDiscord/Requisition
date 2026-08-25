# A craft was costed against a count the network could not hand over

**Severity:** HIGH — a green CRAFT button that did nothing at all
**Area:** crafting pool, withdrawal, crafting panel
**Status:** FIXED 2026-08-25 — READY FOR TESTING / HUMAN REVIEW

## Symptom

Band of Door (item 18120 = 1× Shackle + 20× Door Pants, at a Work Bench) showed as craftable and
the CRAFT button was green. Clicking it did nothing: no sound, no message, no item, no change to
storage. Indistinguishable from clicking dead panel background.

## Cause

Two counts that had to agree did not.

`StorageWorldSystem.GetItemCounts` and `DiskData.CountItem` sum **every** matching stack.
`StackSelection.PlanWithdrawal` delivers the sum of the **plain** stacks — or, when no plain stack
matched, exactly **one** unique stack.

Door Pants are armour (`maxStack = 1`), so 18 of them are 18 stacks. In build 0.5.15 — the one the
report came from, which predates [24](24-globaldata-treated-as-item-identity.md) — every stack
reported `IsUnique`. The panel costed the recipe against 18; the plan's last step asked storage for
20; the withdrawal handed back 1.

From there every layer behaved correctly and invisibly: `RefundLedger.TryTakeExact` refused the
short draw, `PlanExecutor` aborted and refunded, `ExecutePlan` returned air, and `ExecuteCraft`
returned without telling anyone.

`StorageWorldSystem.ExtractItem` made it worse across disks: the first disk was allowed the unique
fallback, and taking it **returned immediately**, so one unique stack on disk 1 masked hundreds of
pooled ones on disk 2.

## Fix

`StackSelection.WithdrawableCount` states the rule `PlanWithdrawal` already followed: the plain
stacks pool, and when none matched, the one unique stack the fallback takes. That fallback now takes
the **largest** unique stack rather than the first, so the count is achievable whatever order
storage is in.

`StorageWorldSystem.GetWithdrawableCounts` / `CountWithdrawable` answer it network-wide, and every
craftability path reads them instead of the raw sum — `RecipeResolver.GetAvailableItems`, the
crafting panel's `_cachedAvailable` and `ComputeMaxCraftAmount`, the favourites panel.
`GetItemCounts` keeps its meaning for the grid, which should still show all 18.

`ExtractItem` now drains pooled stock across the whole network before considering the fallback.

`PlanExecutor` and `MaterialConsumer` took back conjured products with a single `Extract` call,
which hands back one stack per call for a unique type — the ingredients were refunded **and** the
player kept what was made from them. `TakeBack` drains until the count is met.

`ExecuteCraft` had four bail-outs that returned in silence. Each now says why.

## Not fixed

The resolver has no notion of a per-type withdrawal ceiling, so it can still plan a step consuming
N units of a type whose stacks each stand for themselves — by sub-crafting the shortfall into
storage, where the new stacks are unique too. The craft then fails, but now says so instead of
doing nothing. Reaching it needs an item that is genuinely unique (ModItem save data, or a mod
overriding `CanStack`); ordinary armour pools, verified against the decompiled
`ItemLoader.CanStack`, which ignores `maxStack`.

## Verified by

`WD-*`, `BD-*`, `FX-*` and `PX-07` in `Tests/Program.cs`, red before the fix. `FX-*` runs against
`Tests/Fixtures/band-of-door.tsdump.txt`, a three-hop slice of the reported `/tsdump` that resolves
to the same three steps as the full 14,178-recipe graph. `BD-10` proves the same recipe goes through
once the stacks pool.

`/tsdump` now writes item names on every storage and recipe line, plus `withdrawable=` per type — a
count higher than it names a type held as stacks that each stand for themselves.

Needs in-game testing: craft Band of Door; craft something whose material is spread over two disks;
upgrade a storage disk (always unique, and consumed as an ingredient); confirm a craft that cannot
be paid for now prints a reason.

## Related

[05](05-extractitem-stamps-tag-on-whole-withdrawal.md),
[22](22-aborted-plan-keeps-its-intermediates.md),
[24](24-globaldata-treated-as-item-identity.md).
