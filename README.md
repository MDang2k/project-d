# Ewigstein (codename Project D)

Single-player tactical extraction shooter with RPG progression, set in an alternate-history 1970 exclusion zone in former Germany. Godot 4.7.2 mono, C#. Design: [docs/GDD.md](docs/GDD.md). Specs: [docs/superpowers/specs/](docs/superpowers/specs/README.md).

## Current status

Last updated: 2026-11-01, checked against the code on `main` at `4f0d645`.

**In short:** the playable loop works from the hub through a raid and back (M0-M7). The current milestone is M8, and none of its six systems are built yet. Recent work went into three reworks outside the milestone list: AI combat v2, the animation system and weapon modding v2. Most of that still needs your playtest.

| Check | Result |
|---|---|
| `dotnet build` | 0 warnings, 0 errors |
| xUnit tests | 731 passed, 0 failed |
| GUT smoke test | 2 of 2 passed |
| Playtested in game | not checked here; only you can playtest |

## Milestones

| Milestone | Status | Notes |
|---|---|---|
| M0 Setup | **Code done** | benchmark on the weakest target machine still pending; renderer choice stays tentative until then |
| M1 Player | **Done** | movement, stances, granular crouch, lean, mantle, stamina, weight tiers, full-body first person |
| M2 Weapons | **Done** | projectiles, recoil, sway, ADS, resting, bipod, magazines, checks, phased reloads |
| M3 Damage | **Done** | hitboxes, per-limb health, status effects, meds, armor, energy and hydration, melee |
| M4 Inventory and loot | **Done** | grid inventory, equipment, containers, searching, secure container |
| M5 Human AI | **Done, being reworked** | see AI combat v2 below |
| M6 Raid loop | **Done on a test map** | spawns, timer, extraction, death and MIA. Map 1 is still a test blockout |
| M7 Hub | **Done** | stash, six traders, barter, insurance, six quests, autosave (save version 7) |
| **M8 Skills, reputation, signature systems** | **Current; not started** | table below |
| M9 Night raids | **Not started, no spec yet** | AI light zones are ready for a time-of-day value |
| M10 Slice polish | Not started | |

### M8 breakdown

All six specs are written; no system is built.

| Part | Status | Spec |
|---|---|---|
| 8a Skills, including Vitality | Not started | [m8a](docs/superpowers/specs/m8-signature/m8a-skills-design.md) |
| 8b Reputation and friendly fire | Not started; quests only store reputation numbers that nothing reads | [m8b](docs/superpowers/specs/m8-signature/m8b-reputation-friendly-fire-design.md) |
| 8c Nemesis | Not started | [m8c](docs/superpowers/specs/m8-signature/m8c-nemesis-design.md) |
| 8d Knowledge and journal | Not started | [m8d](docs/superpowers/specs/m8-signature/m8d-knowledge-journal-design.md) |
| 8e Ewigstein and exposure | Not started | [m8e](docs/superpowers/specs/m8-signature/m8e-ewigstein-design.md) |
| 8f Bosses | Not started; boss HP and data exist in the catalog, but no boss spawns | [m8f](docs/superpowers/specs/m8-signature/m8f-bosses-design.md) |

## Reworks in progress

Each spec has a detailed "Implementation status" table.

| System | Status | Main things left |
|---|---|---|
| [Composable actors](docs/superpowers/specs/ai/composable-actors-design.md) (AI stats from catalog sheets, F5 inspector) | **Built** | delete the old `AiPreset` after your playtest; player skills wait for M8a |
| [AI combat v2](docs/superpowers/specs/ai/ai-combat-v2-design.md) | **Steps 1-7 built, playtest pending** | bot stamina; corner pieing; armor retargeting; marksman behaviour; boss squads; voice barks; flashlight and NVG rules (M9); grenades (post-slice). No light zones, foliage or wall material tags are placed on maps yet |
| [Animation system](docs/superpowers/specs/animation/animation-system-design.md) | **Partly built** | see below |
| [Weapon modding v2](docs/superpowers/specs/weapons/weapon-modding-v2-design.md) | **Mostly built** | ammo recoil modifier; effective range display; drag and drop in the workshop; most part models |

**Animation, built:** locomotion graph, socket ADS, support-hand grips, spring recoil, inertia sway, high and low ready, view bob, foot IK, phased reloads with resume, whole-body turning, stance transitions, prone crawl and prone lean.

**Animation, not built:** additive layers (breathing, limp, tremor, flinch); authored clips for checks, melee, draw and item use; clips for pivot, stance and crawl; sprint carry pose; trigger finger; AI animation LOD. Bolt, pump, belt-fed and revolver mechanisms wait for the post-slice weapons.

## Not built yet

- **Maps:** the [raid map standard](docs/superpowers/specs/maps/raid-map-standard-design.md) pipeline, the [map 1 revision](docs/superpowers/specs/maps/map1-revision-design.md), transits and the seven other maps are design only. The game has one raid map.
- **Story:** acts, lock meter, story missions and endings are design only.
- **Factions:** the code has looters, bandits and both PMC sides. Garrison holdouts, Abteilung Ewig, the Hollowed, Die Wächter and Die Troika are design only.
- **Characters:** trader portraits, story characters and mission NPCs are design only.
- **Post-slice systems:** grenades, malfunctions, zombies, aliens.

## Content in the game

| Content | In the game | In the catalog |
|---|---|---|
| Firearms | 5: AK type, FAL type, EM-2, MG type, pistol | 64 weapon rows |
| Weapon parts | slice set | 78 rows |
| Traders and quests | 6 traders, 6 quests | |
| Raid maps | 1 test blockout | 8 designed |

| Art | Done | Still placeholder |
|---|---|---|
| Characters | player body, Allied and Soviet outfits, looter outfit with random faces, flat cap | other factions are capsules |
| Weapons | FAL model with parts; Makarov PM model, not in the game's weapon data yet | other weapons are boxes |
| Locomotion | Quaternius CC0 clips for forward stand and crouch movement | sides, diagonals and prone use a generated gait |

## Needs you

1. **Playtest AI combat v2.** Under-fire reaction, aim, cover, personalities and squads are all waiting on this. Removing the old AI presets waits on it too.
2. **Playtest the turning, stance and prone work.**
3. **Run the benchmark on your weakest machine.** This closes M0 and settles the renderer.
4. **Decide how the player reads raid time.** The wristwatch was removed from the model because it did not bend with the hand, so raids currently have no readout. GDD §15 still names the watch and needs updating once you decide.
5. **Choose the next step:** build M8, or get the M9 night raids spec written.

## Housekeeping

- `main` has 35 commits not pushed to `origin/main`, counting this update.
- Some spec Status lines are out of date, so trust this file and the "Implementation status" tables first:
  - M7b, M7c, M7d and M7e say "not implemented", but they are built.
  - The AI combat v2 header says step 1 is partly built; its table shows steps 1-7.
  - Composable actors lists phase 6 as not started; that phase is AI combat v2.

## Commands

```bash
dotnet build "Project D.csproj"
```

```bash
dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj
```

```bash
godot --headless --path . -s addons/gut/gut_cmdln.gd -gdir=res://tests -ginclude_subdirs -gexit
```

```bash
python tools/export_catalog.py
```
