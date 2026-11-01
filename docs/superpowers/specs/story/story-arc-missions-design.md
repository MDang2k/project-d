# Story part 2: arc, lock meter, story missions and Kommando Resonanz

Date: 2026-10-21

Status: approved design (story bible, part 2 of 5). Builds on `docs/superpowers/specs/story/story-core-truth-design.md`. Spoiler document. Later parts: 3 characters, 4 endings, 5 names and background. Numbers marked **(tune)**.

## Intent

Three acts. The truth (Ewigstein is a lock, the ruins are a prison) is revealed at the midpoint, so the second half turns every delivery into a conscious choice. A hidden lock meter driven by the player's deliveries decides how badly the zone decays. In late Act 2 a human enemy surfaces: Kommando Resonanz, Reich holdouts who want to use the Prisoner for their own ends.

## 1. Acts

| Act | Name | Player knows | Zone |
|---|---|---|---|
| 1 | Contract | Ewigstein is a power source both sides pay for | odd hints: frost, the numbers station |
| 2 | Doubt | from mission 4: Ewigstein is a lock; delivering it weakens the prison | decays with the lock meter; both governments push for more shards; Kommando Resonanz surfaces late in the act |
| 3 | Wardens | the Wardens are real and hostile; the keystone decides everything | the lock is near failing; endings (part 4) |

## 2. Lock meter (pure logic, saved)

- Hidden value, starts at 100.
- Each Ewigstein delivery to any government: -3 **(tune)**. Delivering the keystone (part 4): a large drop.
- Each shard destroyed through the seal path (part 4): +2 **(tune)**.
- Killing Kommando Resonanz members carrying shards and destroying those shards counts as destruction.
- Thresholds **(tune)**:

| Lock | Effect |
|---|---|
| <= 85 | frost spreads onto outer maps (more frost-touched spots) |
| <= 70 | zombies in The Labs and at night (post-slice) |
| <= 55 | Labs breach starts earlier; deposit exposure rises; Kommando Resonanz may surface (section 4) |
| <= 40 | the Wardens arrive: Act 3 starts |

- The value is never shown; the Scientist's journal comments on the readings ("they worsen", "they steady").
- **Act 3 trigger:** lock <= 40 **or** story mission 8 complete, whichever first, so the story cannot stall when the player refuses to deliver.

## 3. Story missions

Unique raid variants with scripted events, mostly given by the Scientist (GDD §14). Each needs the previous mission plus a player achievement (knowledge item, trader level or map unlock).

| # | Act | Mission | Map (variant) | Reveals | Reward |
|---|---|---|---|---|---|
| 1 | 1 | First Delivery (the 8e quest) | Bunker Entrance | Ewigstein exists; your side pays for it | 3x3 secure case |
| 2 | 1 | The Silent Survey | Hochwald, night | a missing survey team at the radar station; first numbers-station broadcast, from nowhere | frequency knowledge |
| 3 | 1 | The Kreuzdorf Ward | Kreuzdorf | 1965 clinic records of "hollowed" patients; one hollowed survivor in the morgue | lore, Scientist trust |
| 4 | 2 | Resonanz | The Labs, archive | full Resonanz logs; midpoint reveal: Ewigstein is a lock, the ruins are a prison | Labs access, 3x4 secure case |
| 5 | 2 | Two Masters | Falkenberg base | 1965 orders show both governments knew more than they said; both quartermasters press for shards; the opposite side approaches you | double-agent route opens |
| 6 | 2 | The Voice | Hochwald station, night | the broadcast decoded: the Prisoner is speaking; hallucination events (sway, false footsteps, frost) | lore |
| 7 | 2 | The Open Door | Bunker Entrance | the sealed bunker portal opens from inside: Kommando Resonanz surfaces; their plan is found (section 4) | lore; Kommando appear on inner maps |
| 8 | 3 | First Contact | The Labs, containment | the Wardens appear, hostile to everyone; survive and extract | Wardens' script readable (8d) |
| 9 | 3 | The Deep Vault | Cascade Site | the keystone, the decisive Ewigstein heart of the lock; Kommando Resonanz race you to it | sets up the final choice |
| 10+ | 3 | Ending missions | per ending | part 4 | part 4 (final secure container tier) |

- Missions 2 and 6 are night variants (need M9).
- Mission 7 uses map 1's sealed bunker door, the hook the GDD left open (§19).

## 4. Kommando Resonanz (new faction)

- **Who:** the surviving Project Ewigstein security detachment and the researchers loyal to the old programme. They sealed themselves into the deep bunkers during the 1965 Cascade and have lived there for five years. Fictional unit name and insignia; no real Nazi symbols (same rule as the item catalog). Depicted as villains.
- **What they want:** the Reich back. They plan to use Ewigstein, and the Prisoner itself, to restart the war and win it.
- **What they know:** more about the Prisoner than anyone outside the Scientist. They believe they can bargain with it and harness it, and treat its whispers as a resource. They are wrong; the story lets the player see their hubris (their own records show members going hollow).
- **When:** they surface in story mission 7, or earlier once the lock falls to 55 or below, whichever first. From then on squads appear on inner maps (Bunker Entrance, Falkenberg base, The Labs; Cascade Site in Act 3).
- **How they fight:** elite AI preset, hostile to all factions. Zone relic and prototype gear (StG 45, Gerät 69, prototype vest T4, gas masks, Ewigstein-tuned weapons). They prioritise any player carrying Ewigstein.
- **Loot:** their bodies drop relic and prototype gear and sometimes shards.
- **System needs:** a faction entry in the hostility matrix (GDD §16), an elite AI preset, spawn rules gated by story state and lock meter, and a "target Ewigstein carriers" priority in AI target selection.

## 5. Save

Story flags (missions complete, current act, Kommando surfaced), lock strength. Next save version after the systems already shipped.

## Decisions

1. Three acts, truth revealed at the midpoint.
2. Hidden lock meter driven by deliveries; destroying shards restores it.
3. Act 3 starts at lock <= 40 or after mission 8.
4. Kommando Resonanz surface in late Act 2 (mission 7 or lock <= 55); they want the Reich back and think they can use the Prisoner.
5. Map 1's sealed bunker door is opened by Kommando Resonanz in mission 7.

## Open items

- Mission scripts, dialogue and document texts are content for a writing pass.
- Names (Kommando Resonanz, the Prisoner, the Wardens) are working names until part 5.
