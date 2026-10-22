# M8e: Ewigstein

Status: approved design, not implemented. Last M8 sub-project in build order (8a skills, 8b reputation, 8c nemesis, 8f bosses, 8d knowledge: specs approved; 8e this). Implements GDD §14 "Ewigstein in gameplay" (canister, exposure, delivery) and the M8 line "first Ewigstein delivery quest with containment canister and in-raid exposure".

## Intent

The story's most important resource is also the riskiest haul. Shards are harvested at deposits that irradiate whoever stands near them, must be carried home in a heavy lead-lined canister (never the secure container), and are delivered to your government for story progress and a larger secure container.

## In scope

- `ewigstein_shard` item and two canister items (shielding containers).
- Secure container rejection of shards and canisters.
- Pure `ExposureModel` (meter, sources, decay, thresholds) and its effects.
- `EwigsteinDeposit` map node (exposure source, harvest action).
- Restrained presentation: hum, status icon, distortion, static.
- Scientist sells canisters.
- First delivery quest (7e data) and the `secure_case` reward item.

## Out of scope

Story missions, endings, zombie attraction, the double-agent route, alien artifacts other than Ewigstein, a compass item (the GDD's spinning needle waits for one), selling Ewigstein (never allowed).

## 1. Items

- `ewigstein_shard`: 1x1, 0.5 kg **(tune)**, `MaxStack` 1, `BaseValue = 0` (no trader buys it), `Insurable = false`.
- Canisters: new container kind with `ShieldsContents = true` and an accept rule (shards only).

| Canister | Size | Weight | Holds | Sold by |
|---|---|---|---|---|
| Small | 2x2 | 6 kg | 1 shard | Scientist, level 1 |
| Large | 2x3 | 12 kg | 3 shards | Scientist, level 2 |

- The secure container rejects shards and canisters (placement fails like any slot or grid mismatch).
- Shards in the stash are inert; exposure exists only in raids.

## 2. Exposure (`ExposureModel`, pure)

- Meter 0..100, starts at 0 every raid and is not saved.
- Gains **(tune)**: +1.5/s per unshielded shard carried (pockets, vest, rig, backpack, hands); deposit proximity within 12 m, up to +3/s scaling linearly with distance.
- Decay: -0.5/s while no source applies.
- Effects:

| Exposure | Effect |
|---|---|
| >= 25 | sway multiplier rising from 1.2 at 25 to 1.5 at 100 (`HandlingModel`) |
| >= 50 | vision distortion (slight ripple and colour shift; never a green glow) and radio static audio |
| >= 75 | HP loss 0.3/s split between thorax and stomach (`BodyHealth`) |

- No instant death; exposure kills only through HP loss. 8a Vitality's damage multiplier does not apply (not hit damage).
- Status icon "Exposure" with its level, shown while above 0 like other status icons (GDD §18).
- A faint hum plays near deposits and unshielded shards, louder with the source rate.

## 3. Deposits (`EwigsteinDeposit`)

- Map node: exposure radius 12 m, frosted unlit crystal look (placeholder art).
- Harvest: interact for 10 s **(tune)**; noise audible to AI within 25 m; damage cancels it.
- One shard per deposit per raid. The shard goes into a free canister slot if the player carries one, otherwise into carried grids unshielded.
- Map 1: 1-2 deposits (drain tunnel end, near the bunker portal), placed with the map requirements work. The translated 8d lab note describes their location.

## 4. First delivery quest

- Giver: your side's quartermaster (your government).
- Prerequisite: the 8d lab note translated (`RequiresKnowledgeIds`).
- Objective: FindInRaid, 1 `ewigstein_shard`; consumed at turn-in.
- Rewards: `secure_case` (new item, secure container category, 3x3 grid) into the stash; reputation +10 with your quartermaster and +10 with your faction (8b); a journal lore entry "First delivery" (8d).
- The player swaps the 2x2 pouch for the case in the hub loadout screen like any equipment.

## 5. Other rules

- On death a carried shard or canister is lost like any gear; a nemesis can take it. AI is never affected by exposure.
- Save: no new fields (exposure resets; canisters and shards are ordinary items).

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `ExposureModel` | pure | meter, sources, decay, thresholds |
| Shielding rule | pure | unshielded-shard count, canister accept rule, secure container rejection |
| `EwigsteinDeposit` | map node | proximity source, harvest action, noise |
| Exposure effects | nodes | sway, distortion, static, HP loss, status icon, hum |
| Item data, Scientist stock, quest data | data | content |

Tests: unshielded versus shielded shard counts, canister accepts shards only, secure container rejects shards and canisters, gain and decay rates, deposit falloff, thresholds and sway curve, HP loss split and no instant death, Vitality not applied, reset at raid start, harvest once per raid and noise, shard placement into a free canister first, quest prerequisite and rewards, shard cannot be sold.

## Decisions

1. Shards do not stack.
2. Two canister sizes, sold by the Scientist.
3. Thresholds at 25, 50 and 75; no instant death.
4. Harvested shards go into a free canister first.
5. Delivery goes to your quartermaster; reward is a 3x3 secure case.
6. The compass-needle cue waits for a compass item.

## Open items (none blocking)

- All rates, weights, radii and times are placeholder tuning.
- Deposit and lab-note placement on map 1 belong with the map requirements work.
