# M8a: Skills

Date: 2026-10-16

Status: approved design, not implemented. First of five M8 sub-projects (8a skills; 8b reputation and friendly fire; 8c nemesis; 8d knowledge and journal; 8e Ewigstein). Implements GDD §12, which this spec extends from six to seven skills (Vitality added by the designer; GDD updated in the same commit).

## Intent

Skills improve through use; there is no player level. Seven skills, levels 1-10, diminishing XP for repeating the same action within a raid. Skills never decay and XP is kept on death. Each skill scales an existing tuning value in the movement, weight, weapon, health, medical or loot models.

## In scope

- `SkillData` resources (`data/skills/*.tres`) and a pure `SkillBook` (XP, levels, diminishing returns).
- Pure `SkillEffects` (multipliers per skill and level).
- Hooks in existing models; XP reporting from player-side nodes.
- Level-up HUD pulse, result-screen summary, hub Character tab.
- Save: `skills` map, migration in the next save version after 7e.

## Out of scope

Throwing (grenades are post-slice; Strength trains on carried weight only), skill effects on AI, skill decay, skill books or trainers, reputation (8b).

## 1. XP and levels

- `SkillData`: `Id`, `NameKey`, `XpTable[10]` (cumulative XP needed per level; level 1 = 0), per-action base XP, effect curve values.
- XP is counted only in raid scenes (dev ranges and the hub do not train).
- Sources:

| Skill | Trained by |
|---|---|
| Endurance | sprinting (per 5 s unit), mantles (existing `Player.MantleStarted`) |
| Strength | moving while carried weight is above 20 kg (per 5 s unit; more XP in heavier weight tiers) |
| Weapon Handling | completed reloads, mag checks, chamber checks, rounds loaded into magazines (per 5 rounds) |
| Recoil Control | shots fired |
| First Aid | completed med uses |
| Searching | items revealed while searching |
| Vitality | hits taken from hostile projectiles or melee, XP scaled by damage; bleeding, hunger and thirst ticks do not count |

- Diminishing returns per skill and action within one raid: `xp = base * max(floor, decay^n)`, where `n` counts earlier repeats of that action this raid. Defaults decay 0.9, floor 0.1 **(tune)**. Counters reset at raid start.
- No decay. XP is kept on death or MIA.
- Crash or quit mid-raid: the save only holds the raid-start snapshot, so that raid's XP is lost. (7e quest progress has the same limit.)

## 2. Effects

Linear from no change at level 1 to the maximum at level 10 **(tune)**. Effects apply immediately on level-up: nodes read the multipliers at use time.

| Skill | Level 10 effect | Hook |
|---|---|---|
| Endurance | leg stamina max and recovery +30% | `StaminaPool`, `MovementTuning` |
| Strength | weight thresholds +5 kg | `WeightTuning` |
| Weapon Handling | reload and mag-check time x0.75; exact mag count from level 8 | `ActionTuning`, `WeaponController.ExactMagCheck` |
| Recoil Control | recoil x0.8 | `RecoilTuning` |
| First Aid | med use time x0.7 | `MedicalSpec` |
| Searching | search time per item x0.7 | `LootContainerModel.SecondsPerItem` |
| Vitality | max HP of every body part +15%; incoming hit damage x0.9 after armor | `BodyHealth` max HP from `HealthConfig`; end of `DamageResolver.Resolve` |

Vitality's damage multiplier applies to hit damage (projectile, melee, blunt from stopped rounds), not to bleed, hunger or thirst damage. Max HP changes take effect at the next raid start (the body is built then); the damage multiplier applies immediately.

Skills affect the player only; AI uses its own presets.

## 3. Feedback

- In raid: brief HUD pulse on level-up ("Endurance 4"), `HudFade`, following `PlayerHud.AlwaysVisible`.
- Raid result screen: XP gained per skill and any level-ups.
- Hub: Character tab with level and progress bar per skill.

## 4. Save

- `skills: { id: xp }` (total XP per skill; level derived from `XpTable`).
- Added in the next save version after 7e (v6 if 7b-7e ship in order); migration adds an empty map. Earlier frozen fixtures must still load.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `SkillData` | data | XP table, action XP, effect values |
| `SkillBook` | pure | XP, levels, per-raid diminishing counters, level-up events |
| `SkillEffects` | pure | multipliers per skill and level |
| Player-side reporters | nodes | turn existing events into skill actions |
| Level-up pulse, result summary, Character tab | UI | feedback |

Tests: XP table and level boundaries, diminishing decay and floor, counters reset per raid, no XP outside raids, each effect at levels 1, 5 and 10, exact mag check from level 8, Vitality damage multiplier after armor and not on bleed ticks, max HP scaling, save round trip and migration.

## Decisions

1. XP only counts in raids.
2. Continuous actions count in 5-second units.
3. Exact mag count from Weapon Handling level 8.
4. Effects are linear between levels 1 and 10.
5. A crash or quit mid-raid loses that raid's XP.
6. Vitality's HP bonus applies from the next raid start; its damage reduction applies at once.

## Open items (none blocking)

- All XP and effect values are placeholder tuning.
