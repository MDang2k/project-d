# Story part 4: endings

Status: approved design (story bible, part 4 of 5). Builds on parts 1-3. Spoiler document. Replaces the GDD §14 ending list with the meaning the core truth gives it; GDD keeps the short summary. Numbers marked **(tune)**.

## Intent

The decisive Ewigstein is the keystone, the heart of the lock. Where the player takes it decides the ending. Handing it to any government breaks the lock far from the zone; returning it reseals the prison; a hidden path frees the Prisoner. The choice is permanent (single autosave, no reloading, GDD pillar 3).

## 1. The choice

- After story mission 9 (The Deep Vault) the player carries the keystone out in a special **keystone casket** (container that shields it, heavy, high exposure even when shielded **(tune)**).
- Back in the hub each eligible ending is offered as a final mission:

| Ending | Offered by | Eligible when |
|---|---|---|
| 1. Your government | your quartermaster | always |
| 2. The other side | the opposite quartermaster | double-agent route open (mission 5) and reputation with that faction >= +0.5 (8b) **(tune)** |
| 3. Return and seal | Weiss (the Scientist) | always |
| 4. Free the Prisoner (hidden) | the Voice, a new broadcast on the hub radio | every Prisoner broadcast found (knowledge items, 8d) and at least 5 undelivered Ewigstein shards in the stash **(tune)** |

- Accepting a final mission locks the choice; the keystone cannot be offered to another party afterwards.

## 2. The endings

| # | Final mission | Outcome | Epilogue |
|---|---|---|---|
| 1 | The Long Haul: carry the keystone from the Cascade Site through the transit chain (The Labs, Bunker Entrance) to your side's extraction; Kommando Resonanz and the Wardens hunt the carrier | your side wins the energy race | years later frost appears in your own capital; the lock fails far from the zone. Weiss vanishes |
| 2 | The Crossing: deliver the keystone across the Border Strip to the other side; your own side's PMCs turn hostile (betrayal) | the other side wins | the player lives in exile; the same frost spreads elsewhere |
| 3 | The Seal: carry the keystone down into the vault heart and hold out while Weiss reseals the lattice; the Wardens stop attacking once they recognise it | the keystone goes home; both governments lose | frost recedes; lock >= 60: clean seal; lock < 60: the seal holds, scarred **(tune)**. Weiss stays as its keeper; the player walks out of the zone |
| 4 | The Voice Answers: use the hoarded shards to break the lattice at the vault while the Wardens and Kommando Resonanz both try to stop you | the Prisoner is free; Albrecht's bargain was never possible | silence: the zone goes quiet, then the world. Weiss dies trying to stop you |

- "Destroy it and seal the ruins" from the GDD becomes "return and seal": destroying the keystone would break the lock (part 1).

## 3. After the ending

- Epilogue and credits, then the game continues: the raid and trade loop goes on in a world shaped by the ending.

| Ending | Post-game world |
|---|---|
| 1, 2 | heavier frost, more zombies (post-slice), Kommando Resonanz remnants on inner maps |
| 3 | Cascade Site closed; lighter frost; the Wardens withdraw |
| 4 | the zone at its worst: maximum frost, exposure and hollowed |

- Final secure container tier (4x4) awarded for finishing any ending.
- The save records the ending; story flags stay so journal and traders reflect it.

## 4. System needs

- Keystone item and keystone casket container (heavy, shielding, exposure **(tune)**).
- Ending eligibility rules (pure, tested): reputation, mission flags, knowledge set, stash shard count.
- Final missions as story mission variants (scripted events on existing maps).
- Post-game world state applied from the ending flag (map content toggles, spawn tables, frost spots).

## Decisions

1. Four endings; the hidden one frees the Prisoner.
2. Ending 3 is "return and seal", not destruction.
3. The choice is permanent.
4. The game continues after the ending in an ending-shaped world.
5. Weiss's fate depends on the ending.

## Open items

- Epilogue presentation (stills, text, narration) is content for the writing and art passes.
- Final mission scripts are content.
