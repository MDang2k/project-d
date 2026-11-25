# Raid end: one module settles every raid

Date: 2026-11-25

Status: approved design, not implemented. A refactor from the architecture review of 2026-11-21 (candidate 3) and the design session that followed. Every decision below was confirmed by the designer. Build after the save parts refactor (`docs/superpowers/specs/architecture/save-parts-design.md`) and before the faction rules refactor.

## Intent

- What the player keeps and loses at the end of a raid is decided in one place, in one written order, and tested for every way a raid can end.
- A crash mid-raid (counted as a death at next launch) goes through exactly the same rules as a death in the raid.
- The nemesis, the kill line and the result screen all read one record of who killed the player.

### Non-goals

- Transits (world map spec): `RaidEnd` gets a transit outcome when that spec is built.
- The death cam and result screen presentation (death and ragdoll spec): they stay in nodes and only read the summary.
- Changing any rule: insurance, nemesis, knowledge, quest, skill and outcome rules stay as they are; only where and in what order they are called moves.

## 0. Current state

| Step today | Where |
|---|---|
| Paid extract cost from carried banknotes; capture the slot each lost item was worn in (`LostSlots`); strip gear (`RaidOutcomeRules.Apply`); fire `Ended` | `RaidManager.OnEnded` (`scripts/raid/RaidManager.cs` 42-55) |
| `run.Record`; `skills.EndRaid`; `quests.OnExtracted`; `InsuranceService.OnRaidEnd` and `TryDeliver`; `RaidNemesis.OnRaidEnded` (reads `RaidManager.LostSlots`, comment "before the profile is saved"); `RaidKnowledge.OnRaidEnded`; `SaveManager.EndRaid`; result screen | `RaidLevel.OnRaidEnded` (`scripts/raid/RaidLevel.cs` 105-148) |
| Crash recovery: strip gear, `run.Record`, insurance through a callback; no skills, knowledge countdown or nemesis | `RaidRecovery.Apply`, `SaveManager.Load` |

The order is held by comments; no test covers it.

## 1. The module (decisions Q1, Q3, Q4)

- `RaidEnd` is a plain C# module (no Godot types) in `scripts/systems/`. One entry point: `RaidEnd.Settle(RaidEndInput input, GameProfile profile, RunProgress run) → RaidEndSummary`.
- **`RaidEndInput`:** outcome; extracted zone id (or none); paid extract cost; `KillerInfo?` (section 2); the random source (`Func<double>`); the trader level lookup insurance needs.
- **It changes the profile and run directly**, in this order, which is today's order written down once:
  1. paid extract cost from carried storage grids (never the secure container);
  2. capture the worn slot of every item about to be lost;
  3. strip gear by the outcome rules;
  4. `run.Record`;
  5. skills: end the raid (XP is kept on every outcome);
  6. quests: extraction objectives (extracted only);
  7. insurance: roll for lost gear, age and deliver returns;
  8. nemesis: register the killer with the lost gear minus insurance returns and the captured slots (only with a `KillerInfo` that names an AI);
  9. knowledge: unlock carried documents (extracted only); count every translation down (every outcome).
- **`RaidEndSummary`** for the result screen: outcome, lost items, kept items, insurance result, nemesis result, knowledge lines, skill gains.
- **Nodes only gather facts and show results:** `RaidManager.OnEnded` stops changing the inventory and only reports the outcome; `RaidNemesis` stops reading `RaidManager.LostSlots` and only supplies `KillerInfo`; `RaidLevel.OnRaidEnded` builds the input, calls `RaidEnd.Settle`, then `SaveManager.EndRaid()`, then the death delay and result screen.
- **Saving stays outside** `RaidEnd` (decision Q4).

## 2. One killer record (decision Q7)

- `KillerInfo`: the attacker's nemesis id (if any), faction, actor, weapon type, distance, body part of the killing hit, whether the last damage was a hit, bleeding or exposure, and the place (nearest landmark).
- Filled once by `RaidNemesis` from its hit-attribution window plus the killing-hit data the death and ragdoll spec records on `HealthComponent`.
- Read by `RaidEnd` (nemesis step) and by the kill line on the result screen (`KillLine`, death and ragdoll spec section 4), so the two always agree.
- No killer (bleed-out with no attributed attacker, MIA, crash recovery): `null`.

## 3. Crash recovery (decision Q2)

- `RaidRecovery` calls `RaidEnd.Settle` with outcome Killed, no killer, and the run and profile from the save, instead of its own strip-and-insure path. A crashed raid therefore also ends skills for the raid and counts translations down, like any death.
- `SaveManager.Load` saves after it, as today.

## 4. Tests (decision Q6)

xUnit on an in-memory profile, one scenario per way a raid ends: extracted free, extracted paid, killed by an AI, killed by bleeding (no killer), MIA, crash recovery. Each checks the order-dependent rules:

- the secure container is kept on every outcome;
- the paid cost never comes out of the secure container;
- a nemesis's gear never contains an item insurance returns;
- documents unlock only on extraction, translations count down on every outcome;
- skill XP is kept on every outcome;
- extraction quest objectives advance only on extraction;
- crash recovery and an in-raid death with no killer give the same profile.

## 5. Build order (decision Q5)

Save parts refactor, then this, then the faction rules refactor, then the Plünderer merge. The death and ragdoll spec's kill line builds on `KillerInfo` from this work.

## Decisions

1. One pure `RaidEnd` module settles every raid: it changes the profile and run in one written order and returns a summary for the screen.
2. Crash recovery goes through the same module as a death.
3. Nodes only gather facts (outcome, zone, cost, killer) and show the summary; the inventory changes move into `RaidEnd`.
4. Saving stays outside `RaidEnd`.
5. Built after the save parts refactor and before the faction rules refactor.
6. One test per way a raid ends, checking every order-dependent rule.
7. One `KillerInfo` record feeds both the nemesis and the kill line.
