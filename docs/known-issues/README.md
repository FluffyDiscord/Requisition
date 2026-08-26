# Known issues

Defects found during the 2026-08-24 audit. One file each, numbered by severity as first triaged.
Every entry was verified against the source or by a runnable probe before being fixed — none were
speculative.

**All 32 defects are fixed and awaiting testing / human review.** Each file carries a
`## Fix applied` section describing what changed and, where the change has no unit-test surface,
what still needs to be exercised in-game.

A second audit on 2026-08-25 found nine more — three resolver, four networking, two performance —
recorded together in [23](23-agent-audit-2026-08-25.md). Read it before touching the resolver: 23a,
23b and 23c are each the same shape as a fix from the first audit that was applied at one site but
not at the second or third encoding of the same rule.

[22](22-aborted-plan-keeps-its-intermediates.md) was found on 2026-08-25 by doing the test-coverage
work [21](21-untested-fixes.md) asked for — an aborted multi-step craft refunded the materials and
kept the intermediate made from them. It is worth reading for how it was found: not by reading the
refund path (which had been read carefully twice) but by writing down what that path was supposed
to guarantee.

Not shipped in the `.tmod` (`build.txt` `buildIgnore` covers `*.md`).

## Item duplication / loss

| # | Sev | Issue | Verified by |
|---|-----|-------|-------------|
| [01](01-disk-upgrade-undercharges.md) | CRITICAL | Disk tier upgrade completed after under-paying | `TX-*` |
| [02](02-server-upgrade-no-material-check.md) | HIGH | Server upgraded a disk with no material check | `TX-*`, multiplayer |
| [03](03-executeplan-unchecked-extract-insert.md) | HIGH | `ExecutePlan` ignored extraction shortfall and insert leftover | `PX-*` |
| [22](22-aborted-plan-keeps-its-intermediates.md) | HIGH | An aborted craft refunded the materials AND kept what it made | `PX-03c`, `TX-06b` |
| [04](04-defragment-destroys-per-instance-data.md) | HIGH | `Defragment` destroyed and duplicated per-instance mod data | `DF-*` |
| [05](05-extractitem-stamps-tag-on-whole-withdrawal.md) | HIGH | `ExtractItem` stamped one stack's tag onto the whole withdrawal | `SL-*` |
| [12](12-storagediskbase-clone-drops-fullitemtag.md) | MEDIUM | `StorageDiskBase.Clone` dropped `FullItemTag` | multiplayer |
| [13](13-partial-deposit-reports-failure.md) | MEDIUM | Partial deposit reported failure, skipping the delta broadcast | `DP-*` |
| [23d](23-agent-audit-2026-08-25.md) | HIGH | Abort refund overflowed a full network and destroyed materials | `AF-*` |
| [23e](23-agent-audit-2026-08-25.md) | CRITICAL | Withdrawal routed by a client-supplied index; `-1` broadcast it | multiplayer |
| [24](24-globaldata-treated-as-item-identity.md) | HIGH | A `globalData` key was read as "this stack is its own item" — nothing ever stacked | `SI-*` |
| [25](25-craft-costed-against-a-count-it-cannot-withdraw.md) | HIGH | A step paid for twenty units with one `Extract` call and took one — green button, silent no-op | `BD-*` `ID-*` `FX-*` |

## Recipe grid disagreed with the craft button

| # | Sev | Issue | Verified by |
|---|-----|-------|-------------|
| [06](06-list-flag-skips-shared-pool-confirm.md) | HIGH | Shared-pool confirm skipped when every slot looked satisfied | `LF-dup*` |
| [07](07-canproduce-ignores-maxdepth.md) | HIGH | `CanProduce` ignored `MaxDepth` — depth slider was inert | `MD-*` |
| [08](08-prefilter-missing-output-cycle-seed.md) | HIGH | Prefilter planned routes looping through the item being crafted | `LF-loop*` |
| [10](10-resolveingredienttype-partial-stock-lockin.md) | MEDIUM | Partial own-type stock blocked recipe-group substitutes | `GM-*` |
| [11](11-prefilter-ignores-accepted-groups.md) | MEDIUM | Prefilter was blind to the recipe's `AcceptedGroups` | `LF-grp*`, `IC-*` |
| [17](17-resolverecursive-leaves-pool-spent.md) | LOW | `ResolveRecursive` returned false with the caller's pool spent | `PR-*` |
| [18](18-maxdepth-cut-precedes-stock-check.md) | LOW | Depth cut charged a level for a plain stock lookup | `DL-*`, `MD-*` |
| [19](19-preview-collapses-duplicate-slots.md) | HIGH | Preview collapsed duplicate ingredient slots | `DS-*` |
| [23a](23-agent-audit-2026-08-25.md) | HIGH | Preview filled a slot from the stock of the item being crafted | `FD-*` |
| [23b](23-agent-audit-2026-08-25.md) | HIGH | A group slot one level down committed to a single member | `NG-*` |
| [23c](23-agent-audit-2026-08-25.md) | HIGH | Ingredient cache keyed without the cycle-guard seed | `IO-*` |
| [20](20-depth-origin-off-by-one.md) | CRITICAL | Feasibility queries started one depth level too shallow | `MD-*`, `DL-*` |

[20](20-depth-origin-off-by-one.md) was **introduced by the fix for [07](07-canproduce-ignores-maxdepth.md)**
and caught by a second review round. Worth reading even if you never touch this code: a correct-looking
fix, a passing test, and the original symptom still reachable — because the test sampled the depth
range instead of sweeping it.

## Stale UI

| # | Sev | Issue | Verified by |
|---|-----|-------|-------------|
| [09](09-output-slot-cache-ignores-disk-set.md) | HIGH | Output-slot stock cache survived a disk-set change | `RC-*` |
| [14](14-recipe-conditions-snapshotted-once.md) | MEDIUM | Recipe conditions snapshotted once per full refresh | `RC-08`..`RC-10a` |
| [15](15-favorites-version-not-polled.md) | MEDIUM | Favorites toggled elsewhere never re-filtered the grid | `RC-05`, `RC-06` |
| [16](16-favorites-hit-rects-outside-clip.md) | MEDIUM | Favorites hit rects built for rows the scissor clips away | `HR-*` |

## Also fixed, no file of its own

- Detail panel blamed the wrong ingredient — `craftableShortfall` was gated on whole-plan
  feasibility, so a freely sub-craftable ingredient painted red while the real blocker looked
  healthier for holding partial stock. Now `IngredientView.Satisfiable`. Tests `BI-*`, `SA-*`, `SC-*`.
  *This was the originally reported bug.*
- `CoreStep.Consumed` assigned instead of accumulating for duplicate slots. Tests `RS-*`.
- `CanSubCraftRemainder` seeded the cycle guard but left the output's stock in the pool. Tests `FC-*`.

## The invariant these all serve

Three components answer "can this be crafted", and they must agree:

| component | entry point |
|---|---|
| recipe grid colour | `CoreResolver.RecheckRecipeCraftable` |
| ingredient squares | `CoreResolver.ComputeIngredientPreview` -> `Satisfiable` |
| craft button | `CoreResolver.ResolveRecursive` / `TryResolveRecipe` |

Every issue above was one of them drifting from the other two. `SatisfiableAgreesWithThePlan`
(`SA-*`) and `ListFlagAgreesWithCraftButton` (`LF-*`) exist to pin that agreement — extend them
rather than adding a one-off test when this area changes again.

## Known remaining divergence

**Resolved 2026-08-25.** This section previously claimed `ResolveIngredientType` was "used only by
the shared-confirm ordering now" and that "the plan and preview agree". Both were false:
`CanProduce` still called it, so every recursive feasibility check committed a group slot to one
member — see [23b](23-agent-audit-2026-08-25.md). `CanProduce` now calls `CanFillSlot`, and the two
encodings are one helper.

**Resolved 2026-08-26.** The last item here was `Defragment` rescanning the target's stacks for every
donor stack. `Common/MergeCandidateIndex.cs` closes it — see [23i](23-agent-audit-2026-08-25.md).

## Test suite

`cd Tests && dotnet run` — 423 assertions, zero dependencies, links the shipped source directly.
The real-game benchmark reads `ts_recipe_dump.txt` from the tModLoader save folder when present
(produce one in-game with `/tsdump` next to a Terminal); full craftability revalidation over
14 178 recipes runs in 2 ms. Scenario fixtures live in `Tests/Fixtures/*.tsdump.txt` — scoped
slices of a real dump, so a failure names the recipe that broke.

Coverage was uneven until 2026-08-25: everything in the resolver group was asserted and the
item-movement and UI groups were not, because the runner cannot link files that touch
`Terraria.Item`, `TagCompound` or `Main.*`. [21](21-untested-fixes.md) records the five extractions
that closed that gap — the transaction core, the stack-selection rules, the panel's refresh stamps,
row visibility, and the deposit arithmetic — and what deliberately stays in-game-only.

Suite prefixes, so a failure names its area: `TX`/`PX` transaction, `SL`/`DF` stack selection,
`RC` panel refresh, `HR` hit rects, `DP` deposit, `MD`/`DL`/`SA`/`LF` resolver depth and agreement,
`FC`/`TC` UI caches and click arbitration, `FD`/`NG`/`IO`/`AF` the 2026-08-25 audit,
`BD`/`ID`/`FX` paying for a step from stacks that each stand for themselves,
`MX` the defragment merge-candidate index.
