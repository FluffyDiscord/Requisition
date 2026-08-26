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

## Fix applied 2026-08-26 — one sweep, and recovery by handle

Three of the four bullets below were closed. What changed:

**The sweep now runs once.** `StorageWorldSystem.ExtractItem` walked every disk once per unit a
caller needed, because one item handle carries one stack's mod state and a caller holding only one
had to ask again — and `TryTakeExact` and `StorageRecovery.TakeBack` each carried that loop, at four
call sites between them. The rule moved to `Common/NetworkWithdrawal.cs`, free of Terraria and
parameterised by **how many items the caller can hold**: one for a withdrawal onto the cursor, as
many as it takes for a crafting step's ledger. Both callers fall out of the one rule, so there is no
second encoding to drift.

**A state boundary opens another handle instead of ending the sweep**, so a material spread over two
disks whose stacks carry different state now pays from both. At `handleLimit: 1` the draw is still
put back and the sweep still stops, which is what every UI and network caller has always seen.

**`TakeBack` recovers by handle.** Each handle the run inserted is asked for its own units first,
bounded by what the run actually stored — a stack that grew past that also holds units the player
owned, and taking it whole would destroy them. Plain units have no state to match on and still
recover by type, which is correct: they are interchangeable.

The match is `DiskData.ExtractStoredStack`, on item type, prefix, mod item data and mod-written
state **together**. `ExtractItemWithModData` was the obvious thing to reach for and is the wrong
tool: it carries no item type, so `StorageDiskBase`'s `{"archived": true}` — written identically by
every disk tier — matches across types, and it says nothing about `globalData`, so the player's
enchanted copy answers for the plain one the run made. Routing recovery through it would have
introduced a way to destroy an item of a different type than the one being recovered.

`ICraftingStorage.Extract` was **replaced** by `ExtractStacks` rather than joined by it, so the
re-entrant loop could not survive inside `TakeBack`.

## Not fixed

- **Multiplayer still fails silently.** `EndTrackingAndRespond` sends a success flag and
  `HandleOperationResponse` logs it to the debug file and discards it. Every denied storage
  operation — craft included — is still "click, nothing happens" for a client. The send path no
  longer plays the *pickup* sound, so a refusal is at least no longer confirmed as a success, but
  surfacing a reason needs a protocol change.
- **`RefundLedger.Refund` identifies conjured units by POSITION.** The same defect as the `TakeBack`
  one just fixed, at the site with the larger blast radius — `Refund` runs on every abort. It
  withholds conjured units from the end of `_taken`, which is a guess about which handle the run
  made, and the guess is wrong in a reachable case: the player owns unique `CHARM[own-a]` on disk 1
  and `CHARM[own-b]` on disk 2; the run conjures one, which lands on disk 1 after `own-a`
  (`StorageWorldSystem.InsertItem` walks disks in order, `DiskData.InsertItem` appends); a later
  3-unit draw yields `_taken = [own-a, conjured, own-b]`; withholding one from the end drops
  **`own-b`** and re-inserts the run's copy. The count balances, `own-b`'s state is gone.
  The fix is the same move: `MarkConjured` takes the handle, and `Refund` withholds handles matching
  it. Left alone here because this pass was scoped to the three bullets above, `Refund` had just
  been stabilised (`ID-04` guards it), and it is the delicate part of the transaction core.
  **`NW-09` is coupled to this defect**: the one-sweep drain folds into the most recent handle only,
  which is what preserves `_taken`'s order and therefore what end-withholding drops. When `Refund`
  recovers by identity, `NW-09`'s stated reason for existing becomes false and must be revisited.
- **Recovery by handle only reaches a product that landed as its own stack.** `ExtractStoredStack`
  matches on item type, prefix, mod item data and mod-written state together. When the conjured
  product *merged* into a stack the player already had, `DiskData.InsertItem` leaves the
  destination's `FullItemTag` in place (or has the mod rewrite it through `FoldInModState`), so
  nothing the handle can be re-serialised into will match it and the recovery falls back to the
  by-type draw. That fallback is correct there — merging only happens when the game and the mod
  agree those units are the same thing — but it does mean the precise path fires for stacks that
  stand for themselves and not for stateful stock that still pools.
  It is precision by **state**, not by object: two stacks carrying byte-identical state are
  indistinguishable in every observable respect, so taking either is equivalent. The size guard is
  what stops more units coming back than the run put in.
- **Within one disk, two drawn plain stacks with different state still lose it.**
  `DiskData.ExtractItem` sets the returned tag only when `AllDrawsShareModState`, so a bulk
  withdrawal that draws two plain stacks carrying different `globalData` returns them with **no**
  state — issue [05](05-extractitem-stamps-tag-on-whole-withdrawal.md)'s harm, one level down from
  where it was fixed. Reachable since [24](24-globaldata-treated-as-item-identity.md) stopped
  treating `globalData` as identity, so such stacks pool. Pre-existing and untouched here.
  The shape the next change to this area should take is to have `StackSelection.PlanWithdrawal`
  return **handles** rather than a flat draw list, planned over the network's matching slots rather
  than one disk's: it would close this and delete `NetworkWithdrawal` at the same time, at the cost
  of rewriting the `SL-*`/`SG-*` assertions.

## Verified by

`BD-*`, `ID-*`, `FX-*`, `NW-*`, `HB-*` and `PX-07` in `Tests/Program.cs`. Reverting only the loop in
`TryTakeExact` turns `BD-02*` and `FX-06*` red with the reported outcome — the craft produces
nothing and all 18 Door Pants stay put. Reverting only `Refund`'s direction turns `ID-04` red with
one of the player's three stacks left and two stateless copies in place of the others.

`ID-02` rules out merging handles, which `PlanWithdrawal` cannot produce today — it guards the rule,
not this change. `ID-04` is the guard on this change. `FX-*` runs against
`Tests/Fixtures/band-of-door.tsdump.txt`, a three-hop slice of the reported `/tsdump` that resolves
to the same three steps as the full 14,178-recipe graph.

`/tsdump` now writes item names on every storage and recipe line. Item type ids are assigned at load
time, so a dump without names cannot be read against anyone else's mod list.

`NW-*` covers the one-sweep drain against `FakeDiskNetwork`: that the network is swept for pooled
stock exactly once, that a state boundary opens a second handle, and that the handle budget is
honoured at every value from 0 to 13 rather than at sampled points. `NW-12` is the guard on
*unchanged* behaviour — `Tests/LegacySingleHandleDrain.cs` keeps the pre-change rule, and `NW-12`
sweeps a matrix of disk layouts asserting a one-item withdrawal still agrees with it everywhere.
Kept for the same reason `BuggyPreview` is: once the new implementation has replaced the old, a
committed copy of the old is the only thing that makes "unchanged" checkable.

`HB-*` covers recovery by handle. Reverting only `TakeBack`'s handle lookup turns `HB-01`, `HB-03`,
`HB-04a` and `HB-06a` red with the reported shape — `HB-01` reporting `got made`, the player's charm
destroyed and the run's copy left in its place — while `HB-02`'s unit count stays green, which is
the defect stated exactly: the arithmetic balances, the identity does not. `HB-05` stays green
throughout, pinning that plain interchangeable units still recover by type.

`Tests/FakeStorage.cs` now runs through `NetworkWithdrawal.Drain` rather than a second hand-written
copy of the rule, so `BD-*`, `ID-*`, `FX-*`, `PX-*` and `TX-*` exercise the shipped sweep.

Needs in-game testing: craft Band of Door; craft something whose material is spread over two disks;
upgrade a storage disk (always its own item, and consumed as an ingredient); confirm a craft that
cannot be paid for now prints a reason.

And, specific to this pass — **the two things no assertion can reach**, because
`StorageWorldSystem.cs` and `DiskData.cs` still cannot be linked into the runner:

- **That recovery by handle fires at all.** For a product that landed as its own stack it depends on
  `ItemIO.Save` on the still-unmutated produced item reproducing the tag `DiskData.InsertItem` stored
  for it, byte for byte. This was **not verified against tModLoader source** — no tModLoader assembly
  is reachable from the build environment. If it does not hold, every recovery quietly falls back to
  the type-based draw: today's behaviour, never worse, but 25-C would not actually be fixed in-game.
  Craft a multi-step chain that aborts while the player holds a stack of the intermediate's type, and
  confirm the player's stack is the one still there.
  `Tests/FakeStorage.cs` cannot stand in for this: its insert never merges, so it models only the
  own-stack case.
- **The adapter itself** — `DiskWithdrawal`'s state grouping, put-back, `_modifiedTracker` marking
  and `StorageVersion` bumping. The rule it carries out is asserted; the binding to real disks is not.

## Related

[05](05-extractitem-stamps-tag-on-whole-withdrawal.md),
[22](22-aborted-plan-keeps-its-intermediates.md),
[24](24-globaldata-treated-as-item-identity.md).
