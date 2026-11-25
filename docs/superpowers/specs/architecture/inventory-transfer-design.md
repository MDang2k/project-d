# Inventory transfer and found in raid

Date: 2026-11-25

Status: approved design, not implemented. A refactor plus one rule fix, from the architecture review of 2026-11-21 (candidate 6) and the design session that followed. Every decision below was confirmed by the designer. Build after the raid end refactor (`docs/superpowers/specs/architecture/raid-end-design.md`), which hosts the found-in-raid step.

## Intent

- **Found in raid** means one thing, decided in one place: an item you did not bring into the raid and kept at its end.
- The rules for moving items (loading ammo into a magazine, taking gear off a searched body, taking from a world container) live in one tested module, not in screen code.
- The raid inventory screen and the hub screen stop carrying two copies of the same grid code.

### Non-goals

- Changing which moves are allowed or how the screens look.
- Insurance, trading or quest rules (they only read the flag).

## 0. Current state

| Today | Where |
|---|---|
| `FoundInRaid` set by `MarkFound` in 6 places: drag from a container, drag from a body grid, drag from a body slot, double-click from a body, double-click from a container | `scripts/ui/InventoryScreen.cs` (`OnRelease` 473-548, `MarkFound` 577, `DoQuickAction`) |
| Also set by Ewigstein deposits and a hub dev path | `EwigsteinDeposit.cs` 74, `HubScreen.cs` 190 |
| Picking up from the ground (`Player.PickUp`) never sets it. Ground items come from the player's own drops, bots that flee and drop their backpack, and planted documents | `Player.cs` 561-581, `DroppedItem.Create` callers |
| Quest spec contradicts itself: "set when the player takes an item from a container or body" (line 43) versus "set on pickup" (decision 2) | `docs/superpowers/specs/m7-hub/m7e-quests-design.md` |
| Transfer rules (ammo onto magazine, body slot transfer with manual rollback, container take) inside `OnRelease` and `DoQuickAction` | `InventoryScreen.cs` |
| `HubScreen` copies `AddItemGrids`, `Row`, `Slot`, `Grid` and the press, release and move handlers | `HubScreen.cs` 395-458 and its input handlers |

## 1. Found in raid (decisions Q1, Q3, Q4, Q5)

- **Rule (designer):** an item is found in raid when it was **not yours at raid start** and you **kept it at raid end**. How you got it does not matter: container, body, ground, a fleeing bot's backpack, a document, a deposit.
- **Brought in:** at raid start every item the player carries (nested contents included) gets the saved flag `BroughtIn`. Because it is saved with the item, a crash mid-raid still knows.
- **At raid end** a new step in `RaidEnd.Settle`, right after the gear strip:
  - extracted: every kept item without `BroughtIn` gets `FoundInRaid`;
  - killed or MIA (including crash recovery): items in the **secure container** without `BroughtIn` get `FoundInRaid` (designer decision Q3: the secure container keeps what you put in it, flag included);
  - then `BroughtIn` is cleared on every item the player still has.
- An item already flagged found keeps the flag (found once, found for good), as today.
- **Stacks (decision Q5), when in doubt, not found:**
  - merging a brought-in stack with anything gives a brought-in stack;
  - splitting a brought-in stack gives two brought-in stacks;
  - rounds keep their own flag inside a magazine: your rounds loaded into a found magazine leave the magazine found and the rounds brought in.
- **Accepted consequence (Claude's call):** gear you lost to a nemesis and take back by killing it was not yours at raid start, so it comes home found in raid. You earned it.
- **Old saves (Claude's call):** a save written mid-raid before this change has no `BroughtIn` flags. The migration that adds the field sets `BroughtIn` on every carried item when `RaidInProgress` is true, so a crash recovery cannot flag the player's own secure container contents.
- **Removed:** every `MarkFound` call in `InventoryScreen`, the flag in `EwigsteinDeposit`, and `MarkFound` itself. The hub dev path that gives flagged meds stays (it builds test data).

## 2. The transfer module (decision Q2)

- `InventoryTransfer`, a plain C# module in `scripts/systems/`, owns every move that is more than a grid-to-grid move:
  - ammo dropped on a magazine starts loading (the node still runs the timed load);
  - gear from a searched body's worn slot into your grid or slot, with rollback when the place fails;
  - an item from a world container into your inventory (drag or double-click);
  - auto-store or equip for pickups (`Player.PickUp` uses it).
- One result type: done, refused (with a reason key for the HUD), or "load magazine" (the node starts the timed load).
- `InventoryScreen`, `HubScreen` and `Player.PickUp` call it; none of them change grids directly for these moves.

## 3. One grid panel for both screens (decision Q2)

- The grid and slot building code (`AddItemGrids`, `Row`, `Slot`, `Grid`) and the press, release and move handlers become one shared grid panel used by `InventoryScreen` and `HubScreen`. Each screen only says which grids and slots it shows and what a drop outside means (drop on the ground in raid; nothing in the hub).

## 4. Tests

- Found in raid: extracted with a container item, a body item, a ground pickup and your own re-picked rifle (only the first three flagged); death with a found item in the secure container (flagged) and one in the backpack (lost); crash recovery equals death; merged and split stacks; rounds in a found magazine; old mid-raid save migrated.
- `InventoryTransfer`: each move, each refusal, rollback leaves both inventories as before.
- The shared grid panel has no logic worth unit tests; the GUT smoke test opens both screens.

## 5. Docs updated with this spec

- Quest spec (m7e) line 43 and decision 2: replaced by a pointer to section 1 (done with this commit).
- Raid end spec section 1: the found-in-raid step added to the order (done with this commit).

## Decisions

1. Found in raid = not yours at raid start and kept at raid end, whatever the way you got it.
2. Move rules live in one `InventoryTransfer` module, and both screens share one grid panel.
3. On death, found items in the secure container keep their found status.
4. A saved `BroughtIn` flag set at raid start, read and cleared by `RaidEnd`.
5. Mixed stacks count as brought in; rounds keep their own flag inside a magazine.
