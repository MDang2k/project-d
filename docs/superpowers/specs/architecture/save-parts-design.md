# Save parts: every piece of saved state saves itself

Date: 2026-11-21

Status: approved design, not implemented. A refactor from the architecture review of 2026-11-21 (candidate 2) and the design session that followed. Every decision below was confirmed by the designer. Do it **first**, before the faction rules refactor (`docs/superpowers/specs/architecture/faction-rules-design.md`) and the Plünderer merge.

## Intent

- Adding a profile feature means one module plus one line, not lockstep edits to four files (`GameProfile`, `SaveModels`, `SaveSerializer.ToFile` and `Apply`, `SaveMigrator`).
- Fix the stash leak: weapon parts detached while loading nemesis gear and insurance returns stay in a static list and land in the stash on the **next** load.
- A broken save never half-loads: either the whole file loads or the backup does.

### Non-goals

- Changing the save file format: the JSON stays key for key as today (no migration in this work).
- Changing `SaveMigrator` (it already works on raw JSON, one step per version) or `SaveFileStore` (main file, `.bak`, quarantine).

## 0. Current state

| Today | Where |
|---|---|
| `SaveFile`: one positional record with 19 fields, version 10 | `scripts/systems/SaveModels.cs` |
| `ToFile` builds every field by reaching into each part; `Apply` writes back into internals (`Quests.SetEntry`, the `Nemesis` constructor, `ReplaceTraders`) | `scripts/systems/SaveSerializer.cs` 77-171 |
| Only `SkillBook` and `KnowledgeBook` own a `Snapshot()` / `Load()` pair | `scripts/systems/skills`, `scripts/systems/knowledge` |
| `[ThreadStatic] _detached` is drained into the stash at line 113-118, then refilled by `LoadCharacter` (nemesis gear, line 150) and `FromSave` (insurance returns, line 157) and kept for the next `Apply` | `SaveSerializer.cs` 195, 261 |
| JSON parse errors quarantine the file and load the backup; an exception inside `Apply` is not caught and leaves the profile half-filled | `SaveFileStore.Load`, `SaveManager.Load` |
| `GameProfile.Reset()` has no callers and does not clear skills, knowledge, nemeses or reputation | `GameProfile.cs` 106-116 |
| `GameState.Profile` is get-only | `scripts/core/GameState.cs` 15 |

## 1. Save parts (decision Q1)

- Every piece of saved state is a **save part** with one small interface: `Write(JsonObject root)` writes its own top-level keys; `Read(JsonObject root, LoadContext ctx)` reads them. Each part keeps its own small save types next to it.
- Parts, each owning exactly the keys it owns today:

| Part | JSON keys |
|---|---|
| run counters | `Counters` |
| quick-use bar | `QuickUse` |
| character inventory | `Character` |
| side | `Side` |
| stash | `Stash` |
| traders | `Traders`, `UnlockedTraders` |
| insurance returns | `InsuranceReturns` |
| quests | `Quests`, `Tracked`, `PendingRewards`, `Reputation` (reputation lives in the quest log today; it moves with it) |
| examined parts | `Examined` |
| presets | `Presets` |
| skills | `Skills` |
| nemeses | `Nemeses` |
| knowledge | `Knowledge` |

- The key names above are illustrative; the real names are whatever the current JSON uses (check against the fixture, section 4). `Version` and `RaidInProgress` stay with the serializer.
- `SaveSerializer` becomes a loop over one ordered list of parts. The list order is today's `Apply` order and is the only place load order is written down.
- The 19-field `SaveFile` record is removed; `SaveSerializer.Parse` returns the read result (section 3).

## 2. The load context (decision Q2)

- `LoadContext` carries the item lookup, the warnings list and the **detached parts** list, one instance per load (no statics).
- Any part that loads items (character, stash, nemesis gear, insurance returns) adds parts that no longer fit their slot to `ctx.Detached`.
- After **every** part has read, the serializer places `ctx.Detached` into the loaded stash, dropping with a warning when the stash is full. This fixes the leak: nemesis gear and insurance returns are read before the stash is topped up.

## 3. All or nothing (decision Q6)

- Parts read into a **fresh** `GameProfile` and a fresh run state, inside the parse callback given to `SaveFileStore.Load`. If any part throws, the file counts as unreadable: it is quarantined and the backup is tried, exactly like a JSON error today.
- Only a completely read result replaces the live profile and run state. `GameState.Profile` gets a way to be replaced (a setter or `ReplaceProfile`; **builder's choice**), and `RunState.Current` likewise.
- `RaidRecovery` (raid in progress at launch counts as death) runs after the swap, as today.

## 4. Tests (decision Q5)

- **Format fixture:** a real version-10 save committed under `tests/ProjectD.Tests/fixtures/` with every part filled (quests, a nemesis with gear, an insurance return, knowledge, skills, presets, a part that no longer fits its slot). Reading it and writing it back gives the same JSON meaning (key order and whitespace aside).
- **Per part:** write then read gives the same state.
- **Leak regression:** a nemesis whose gear holds a detached part: after one load the part is in the stash; a second load of a clean file adds nothing.
- **All or nothing:** a fixture where one part is corrupt: the load reports unreadable, and the live profile is untouched.
- Existing save tests (`SaveSerializerTests`, `SaveMigratorTests`, `SaveFileStoreTests`, the feature round-trips) pass, adjusted only for the removed `SaveFile` record.

## 5. Also

- Delete `GameProfile.Reset()` (decision Q3).
- Order of work (decision Q4): this refactor, then the faction rules refactor (its nemesis-id change touches only the nemeses part), then the Plünderer merge.

## Decisions

1. Every piece of saved state is a save part that writes and reads its own JSON keys; the serializer loops over an ordered list; the file format does not change.
2. Detached parts go through a per-load context and into the stash after every part has read.
3. `GameProfile.Reset()` is deleted.
4. Done before the faction rules refactor and the Plünderer merge.
5. A committed version-10 save fixture pins the format; per-part round trips and a leak regression test.
6. Loads are all or nothing: parts read into a fresh profile, and any failure falls back to the backup without touching the live profile.
