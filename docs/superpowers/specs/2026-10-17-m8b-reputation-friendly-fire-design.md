# M8b: Reputation and friendly fire

Status: approved design, not implemented. Second of five M8 sub-projects (8a skills, spec approved; 8b this; 8c nemesis; 8d knowledge and journal; 8e Ewigstein). Implements GDD §5 (reputation, friendly PMCs, friendly fire).

## Intent

Reputation becomes real: it moves through quests, friendly fire and dealing with the opposite quartermaster, and it changes prices, trader service and how your own side treats you in raids. Trader levels stay quest-only (7c ruling, confirmed for 8b).

## In scope

- Pure `ReputationBook` over the existing 7e reputation map.
- Friendly-fire penalties and per-raid hostility (`FriendlyFireTracker`).
- Opposite-quartermaster trade penalty.
- Price modifier and service refusal in trading and insurance.
- Faction hostility at raid start for low own-faction reputation.
- Reputation rewards added to the six slice quests (data).
- HUD pulse, result-screen lines, hub Character tab and trader-screen display.

## Out of scope

Reputation-based trader level-ups, the "unlock the opposite quartermaster through reputation" path (quests remain the only unlock), Ewigstein double-agent penalties, any effect of Bandit reputation, nemesis (8c).

## 1. Storage and targets

- Reputation is stored as integer hundredths, clamped to -100..+100, displayed as -1.00..+1.00. This reuses 7e's `QuestLog.Reputation` int map and the save v5 `reputation` field; no migration.
- Target ids: trader ids as in `data/traders/` (`allied_qm`, `soviet_qm`, `smuggler`, `medic`, `engineer`, `scientist`) and faction ids `allied_pmc`, `soviet_pmc`, `bandits`.
- Missing entries read as 0. Bandit reputation is tracked with no effect in the slice.

## 2. Sources **(tune)**

| Source | Change |
|---|---|
| Quest rewards | as authored. Each of the six slice quests gains giver +5 to +10; quests from your side's quartermaster also give your own faction +5. |
| Hitting a friendly PMC (your side's faction) | that faction -2, your quartermaster -2 |
| Killing a friendly PMC | that faction -15, your quartermaster -15 (replaces the hit penalty for the killing hit) |
| Each completed trade (buy, sell, barter) with the opposite quartermaster | your own faction -1 |

- A friendly hit is a projectile or melee hit dealt by the player (7e kill attribution carries the attacker) on an AI whose faction is your side's faction.
- The opposite-quartermaster penalty is small so being on good terms with both stays possible (GDD §5).

## 3. Effects **(tune)**

- **Prices:** with `rep` in -1..+1 for that trader, buy prices and insurance premiums are multiplied by `1 - 0.10 * rep`; sell payouts by `1 + 0.10 * rep`. Rounding as in 7c (buy and premium up, payout down).
- **Refusal:** a trader below -0.50 refuses buy, sell, barter, insurance and accepting new quests. Turning in active quests still works. The trader screen shows the reason.
- **Raid-start hostility:** if your own faction is below -0.30, that faction is hostile to the player for the whole raid.
- **Per-raid hostility:** 3 friendly hits (a kill counts) on one faction in one raid make that faction hostile to the player for the rest of the raid. Resets each raid.
- **Mechanism:** a per-raid "hostile to player" faction set sits on top of `FactionRelations.IsHostile` for AI-versus-player checks only; AI-versus-AI relations are unchanged.

## 4. Feedback

- In raid: HUD pulse when a faction turns hostile ("Allied PMCs are hostile"), `HudFade`, following `PlayerHud.AlwaysVisible`.
- Raid result screen: reputation changes from that raid (per target, signed).
- Hub Character tab: every trader and faction reputation.
- Trader screen: current reputation, price modifier, refusal reason.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `ReputationBook` | pure | read, apply, clamp, thresholds |
| `FriendlyFireTracker` | pure | per-raid hits per faction, hostile set, penalties |
| `PriceModel` (7c) | pure, extended | reputation factor |
| Trade and insurance services | pure, extended | refusal check, opposite-quartermaster penalty |
| AI target check | node, extended | per-raid hostile set for the player |
| Slice quest data | data | reputation rewards |
| Pulse, result lines, Character tab, trader display | UI | feedback |

Tests: clamp at ±100, price factor at -1, 0 and +1 with rounding, refusal below -0.50 and turn-in still allowed, friendly hit and kill penalties (kill not double-charged), third hit turns faction hostile and resets next raid, raid-start hostility below -0.30, opposite-quartermaster trade penalty, AI-versus-AI unaffected, quest reputation rewards applied.

## Decisions

1. Integer hundredths, reusing the 7e map.
2. Friendly-fire penalties apply to both the faction and your quartermaster.
3. Refusing traders still accept turn-ins.
4. The reputation unlock path for the opposite quartermaster is deferred.
5. Reputation rewards are added to the six slice quests.

## Open items (none blocking)

- All amounts and thresholds are placeholder tuning.
