# Faction rules: one table for factions, rank rules in actor data

Date: 2026-11-21

Status: approved design, not implemented. A behaviour-preserving refactor to do **after** the save parts refactor (`docs/superpowers/specs/architecture/save-parts-design.md`; the nemesis id change then touches only the nemeses part) and **before** the Plünderer merge (`docs/superpowers/specs/factions/faction-looks-pluenderer-design.md`), from the architecture review of 2026-11-21 (candidate 1) and the design session that followed. Terms as in `GLOSSARY.md` (Faction, Faction id, Actor, Rank, Hostile by default). Every decision below was confirmed by the designer.

## Intent

- Facts about a faction are written once, in one module, and every caller asks that module. Today they are mapped by hand in about 12 files with four key schemes (enum name, lowercase text key, snake id, actor id), and no test ties them together.
- Rules that differ by rank (flee, shout, grenades) live in actor data, so the merge (Bandenführer versus follower in one faction) and any later faction need no code.
- The merge becomes one module edit plus data.

### Non-goals

- The player's side (dev override, `Profile.Side`, `PlayerConfig.PlayerFaction`): out of scope.
- In-raid hostility (raid standing through reputation, nemeses hostile to the player): stays where it is.
- Any behaviour change, except the grenade roll in section 3.

## 1. The faction table (decision Q1, Q4)

- One record per faction, in one C# table: `FactionInfo { Faction, Id, TextKey, ReputationId?, NameListKey, DefaultActor, HostileByDefault(other) }`, read through `Factions.Of(faction)` and `Factions.ById(id)`.
- **Faction id** is one lowercase id used everywhere: `looters`, `bandits` (until the merge), `allied_pmc`, `soviet_pmc`, `deserters`. `TextKey` is `faction.<id>`.
- It replaces and deletes:

| Today | Where |
|---|---|
| `FactionRelations.IsHostile` | `scripts/systems/Faction.cs` |
| `ReputationBook.FactionId` switch | `scripts/systems/reputation/ReputationBook.cs` |
| `ReputationSummary.Factions` list | `scripts/systems/reputation/ReputationSummary.cs` (derived: factions with a reputation id) |
| `NemesisNames.KeyOf` switch | `scripts/systems/nemesis/NemesisNames.cs` |
| `RaidNemesis.ActorFor` switch | `scripts/raid/RaidNemesis.cs` (uses `DefaultActor` until the nemesis saves its actor, merge spec) |
| `"faction." + f.ToString().ToLowerInvariant()` (6 places: `QuestText`, `RaidNemesis`, `NemesisScreen`, `PlayerHud`, and others) | use `TextKey` |
| duplicate text keys `faction.alliedpmc` / `faction.allied_pmc` | `GameText`: keep `faction.<id>` only |

- **Hostile by default only (Q4):** the table answers the base matrix; `AiHuman.HostileTo` still combines it with `RaidStanding` and the per-AI `HostileToPlayer` flag.

## 2. Rank rules in actor data (decision Q2, Q7)

New actors sheet columns, inherited through `Parent` like every stat and read through `Actor`/`StatSet`:

| Column | Replaces | Values for the refactor commit (behaviour unchanged) |
|---|---|---|
| `squad.flees_on_break` (bool) | `AiHuman.Squad.LowTier` (`Looters or Bandits or Tier <= 1`) | true on `looter`, `bandit` and every row with tier ≤ 1; false on `human_base` and all others. The tier rule leaves the code |
| `throw.shouts` (bool) | `AiGrenadeLoadout.Shouts` (`Looters or Bandits`) | true on `looter` and `bandit` |
| `throw.grenade_table` (text) | `AiGrenadeLoadout.Roll` switch (the "placeholder until a Grenade slot" table) | section 3 |

- The build session edits `docs/design/item_catalog.xlsx` for these columns and rows and re-runs `tools/export_actors.py` (designer decision 2026-11-21), listing the changed cells in the commit message.

## 3. Grenade table format (decision Q10)

- A cell is a list of independent rolls separated by `;`; each roll is `item:chance`, and `a|b|c:chance` picks one of the items at random when the roll succeeds.
- Values (the old conditional "second frag only if you have one" becomes a flat chance with the same average):

| Actor | `throw.grenade_table` |
|---|---|
| `allied_pmc` | `frag_grenade:0.6; frag_grenade:0.15; smoke_grenade_west:0.2` |
| `soviet_pmc` | `offensive_frag_grenade|defensive_frag_grenade:0.6; offensive_frag_grenade:0.15; smoke_grenade_east:0.2` (the 70/30 offensive/defensive split becomes a weighted pick: `offensive_frag_grenade|offensive_frag_grenade|defensive_frag_grenade` is acceptable, or a weight syntax `item*7`; **builder's choice**, keep it in one parser with tests) |
| Deserter rows | `frag_grenade|offensive_frag_grenade|egg_grenade:1.0; frag_grenade|offensive_frag_grenade|egg_grenade:0.5` |
| `bandit` | `stick_grenade|egg_grenade:0.25` |
| `looter` | `stick_grenade:0.1` |

- A pure parser and roller (`GrenadeTable.Parse`, `Roll(rng)`), tested; `AiGrenadeLoadout` becomes a call into it with the actor's cell.

## 4. Waves configured by their entries (decision Q8)

- `WaveSpawner` and `WaveDirector` stop checking `kind == "bandit"`. Each wave entry carries its own settings: `telegraph` (`none` | `flare`), `target` (`posts` | `player_area`), `chain` (node count, default 8). Today's bandit entries get `telegraph: flare`, `target: player_area`, `chain: 6`; PMC entries keep the defaults.
- Entries are in the map layout JSON (`data/maps/border_strip.layout.json`) and map 1's wave data; the actor id becomes only "what to spawn".

## 5. Saves store faction ids (decision Q6)

- Save version 11: nemesis `Faction` is written as the faction id (`"bandits"`), not the enum name (`"Bandits"`). The migrator converts v10 names to ids.
- Loading an unknown faction id no longer drops the nemesis: it warns and falls back to `looters`.
- The Plünderer merge then adds `bandits` → `looters` to the same migration step (it ships in the commit after this one; if both land before a release, one v11 step does both).

## 6. Tests (decision Q5)

- For every `Faction`: `Factions.Of` exists; its `TextKey` resolves in `GameText`; its `NameListKey` exists in `data/ai/nemesis_names.json`; its `DefaultActor` exists in `data/actors/actors.json`; its `ReputationId` is what `ReputationBook` uses; `ById(Of(f).Id) == f`.
- Hostile by default: the current matrix, cell by cell.
- Actor columns: `looter` and `bandit` flee and shout, a PMC does not; a tier-1 row flees.
- `GrenadeTable`: parse, the pick-one syntax, rolls over many seeds match the chances.
- Save: v10 file with `"Bandits"` nemesis loads as `bandits`; an unknown id loads with a warning, not dropped.
- Existing tests (reputation, nemesis, squads, waves, quests) pass unchanged.

## 7. Build order (decision Q3)

1. **This refactor**, one commit, all tests passing, no behaviour change except section 3's grenade roll.
2. **The Plünderer merge** (faction looks spec, build step 1): delete `Faction.Bandits`, set `bandenfuehrer` to `squad.flees_on_break = false`, add `bandits` → `looters` to the migration, rename wave entries' actor.

## Decisions

1. One `FactionInfo` table with one faction id per faction replaces every per-file faction switch.
2. Rank rules (flee, shout, grenades) are actor data on the actors sheet, inherited through `Parent`.
3. Done before the Plünderer merge, as its own behaviour-preserving commit.
4. The table holds only hostility by default; in-raid hostility stays in `AiHuman.HostileTo`.
5. Tests tie every faction's ids together.
6. Saves store faction ids from v11; unknown ids fall back instead of dropping a nemesis.
7. Squad fleeing is one actor column; the tier rule leaves the code.
8. Wave behaviour is wave entry data, not an actor-id check.
9. The player's side is out of scope.
10. Grenade odds become independent rolls with the same averages.
