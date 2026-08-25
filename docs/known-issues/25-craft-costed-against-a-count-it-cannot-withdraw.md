# A step asked storage for twenty and took one

**Severity:** HIGH — a green CRAFT button that did nothing at all
**Area:** crafting transaction, withdrawal, crafting panel
**Status:** FIXED 2026-08-25 — READY FOR TESTING / HUMAN REVIEW

## Symptom

Band of Door (item 18120 = 1× Shackle + 20× Door Pants, at a Work Bench) showed as craftable and
the CRAFT button was green. Clicking it did nothing: no sound, no message, no item, no change to
storage. Indistinguishable from clicking dead panel background.

## Cause

`RefundLedger.TryTakeExact` paid for a step with **one** `Extract` call.

`Extract` is best-effort. `StackSelection.PlanWithdrawal` drains plain stacks, but a stack that
stands for itself is only ever taken **alone** — its mod state describes those units and no others,
so folding several into one returned item is [05](05-extractitem-stamps-tag-on-whole-withdrawal.md).
One call therefore answers a request for twenty with one.

Door Pants are armour (`maxStack = 1`), so 18 of them are 18 stacks. In build 0.5.15 — the one the
report came from, which predates [24](24-globaldata-treated-as-item-identity.md) — every stack
reported `IsUnique`. The step asked for 20 and got 1.

Everything downstream then behaved correctly and invisibly: `TryTakeExact` refused the short draw,
`PlanExecutor` aborted and refunded, `ExecutePlan` returned air, and `ExecuteCraft` returned without
telling anyone.

## Fix

`TryTakeExact` loops until the amount is met or storage stops yielding. The one-stack-at-a-time rule
is untouched — each draw is kept as **its own handle** in `_taken`, so no stack's state is folded
into another's. Twenty stacks pay for twenty units.

`RefundLedger.Refund` then had to change direction. It withheld conjured units from the **front** of
`_taken`, but a step's product is inserted after the stock the player already had, so extraction
hands it back **last**: the front of the list is always the player's own stacks. A failed craft put
the right count back and the wrong items — the player's stacks dropped, stateless copies in their
place. With one handle per type that was invisible; with one handle per stack it is the difference
between keeping an enchanted item and losing it. `Refund` now withholds from the end.

`PlanExecutor.Abort`, `TryStoreIntermediate` and `MaterialConsumer.TryStockUp` each carried their own
copy of the same take-back loop; they are now one `StorageRecovery.TakeBack`.

`IsDiskUpgradeStep` now refuses a step consuming more than one disk. Only one GUID is read and one
result Item built, so a batched step would stamp that GUID on every disk it made. Nothing registers
such a recipe today; before the loop, `TryTakeExact` refused the draw and hid it.

`StorageWorldSystem.ExtractItem` let the first disk fall back to a unique stack and **returned**,
so one such stack on disk 1 masked pooled stock on disk 2. It now drains pooled stock across the
whole network before the fallback applies.

Draining across disks then needed the guard `DiskData.AllDrawsShareModState` already applies within
one: `result ??= extracted; result.stack = totalExtracted` folded every disk's units into the first
disk's Item, wearing the first disk's tag. Two weapons of the same type on two disks came back as
one 2-stack carrying one weapon's state, and re-inserting split that state across both — the other
weapon's gone. `DiskData.ExtractItem` now reports the tag its result carries, and `ExtractItem`
stops folding when a disk's state would be discarded, putting that draw straight back into the disk
whose slots it just freed. Callers already handle a short return; `TryTakeExact` asks again.

`ExecuteCraft` had four bail-outs that returned in silence. Each now says why.

## Not fixed

- **A step needing N units walks every disk N times.** `TryTakeExact` loops, and each iteration
  re-runs the whole network sweep. Fine for the twenty stacks this was reported over; worth
  measuring before it matters on a large network. The fix both callers now want is for
  `ExtractItem` to drain up to `count` in one sweep and return a `List<Item>` of per-stack handles —
  which would also let the state-mismatch case above hand back both disks' units instead of
  stopping at the first.
- **Multiplayer still fails silently.** `EndTrackingAndRespond` sends a success flag and
  `HandleOperationResponse` logs it to the debug file and discards it. Every denied storage
  operation — craft included — is still "click, nothing happens" for a client. The send path no
  longer plays the *pickup* sound, so a refusal is at least no longer confirmed as a success, but
  surfacing a reason needs a protocol change.
- **`TakeBack` recovers by type, not by handle.** For a type whose stacks stand for themselves it
  may take back the player's own stack rather than the one the run conjured. The unit arithmetic
  balances; the identity does not. `ExtractItemWithFullItemTag` / `ExtractItemWithModData` already
  exist for handle-precise recovery. Pre-existing, unchanged in kind by this fix.
- **`StorageWorldSystem` and `DiskData` are not linked into the test project**, so the two-pass
  drain and everything above is verified only in-game.

## Verified by

`BD-*`, `ID-*`, `FX-*` and `PX-07` in `Tests/Program.cs`. Reverting only the loop in
`TryTakeExact` turns `BD-02*` and `FX-06*` red with the reported outcome — the craft produces
nothing and all 18 Door Pants stay put. Reverting only `Refund`'s direction turns `ID-04` red with
one of the player's three stacks left and two stateless copies in place of the others.

`ID-02` rules out merging handles, which `PlanWithdrawal` cannot produce today — it guards the rule,
not this change. `ID-04` is the guard on this change. `FX-*` runs against
`Tests/Fixtures/band-of-door.tsdump.txt`, a three-hop slice of the reported `/tsdump` that resolves
to the same three steps as the full 14,178-recipe graph.

`/tsdump` now writes item names on every storage and recipe line. Item type ids are assigned at load
time, so a dump without names cannot be read against anyone else's mod list.

Needs in-game testing: craft Band of Door; craft something whose material is spread over two disks;
upgrade a storage disk (always its own item, and consumed as an ingredient); confirm a craft that
cannot be paid for now prints a reason.

## Related

[05](05-extractitem-stamps-tag-on-whole-withdrawal.md),
[22](22-aborted-plan-keeps-its-intermediates.md),
[24](24-globaldata-treated-as-item-identity.md).
