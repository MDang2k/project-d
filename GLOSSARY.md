# Ewigstein (Project D)

A single-player tactical extraction shooter set in a 1970 exclusion zone in former Germany. These are the terms the game's design and code share.

## Factions and actors

**Faction**:
A group of humans (or the Hollowed) with one allegiance and one default stance toward every other faction: Plünderer, Allied PMC, Soviet PMC, Deserters, and later holdouts and Abteilung Ewig.
_Avoid_: team, group, side (side is the player's choice, below)

**Faction id**:
The one lowercase name a faction goes by everywhere it is written down: in saves, reputation, text keys and data (`looters`, `allied_pmc`, `soviet_pmc`, `deserters`).
_Avoid_: faction key, faction name, faction string

**Side**:
The PMC a player contracts for, Allied or Soviet, chosen once per profile; it decides which faction is friendly to the player.
_Avoid_: player faction, team

**Actor**:
A kind of character the game can spawn, defined by one row of the actors sheet: its faction, stats, gear and behaviour (`looter`, `bandenfuehrer`, `allied_pmc`, `harker`).
_Avoid_: bot type, preset, AI preset, enemy type

**Rank**:
An actor's standing within its faction (follower, leader, escort, boss), which decides behaviour such as fleeing when its squad breaks; rank belongs to the actor, never to the faction.
_Avoid_: tier (tier is gear quality), level

**Plünderer**:
The scavenger faction of the zone (faction id `looters`), ragged followers often led by Bandenführer.
_Avoid_: looters, bandits, scavs (in player-facing text)

**Bandenführer**:
The leader rank of the Plünderer: an armed elite who leads a gang and never flees.
_Avoid_: bandit, gang boss, elite looter

**Gang**:
One Bandenführer with one to three Plünderer followers, moving and fighting as a closed squad.
_Avoid_: bandit group, looter squad

**Hostile by default**:
Whether one faction attacks another before reputation or raid events change anything; raid standing and nemeses can make an otherwise friendly character hostile.
_Avoid_: enemy faction, relation

## Persistence

**Profile**:
Everything about the player that survives between raids: stash, side, traders, quests, reputation, skills, knowledge, nemeses and insurance returns. One profile per game, in one autosave slot.
_Avoid_: account, save game, player data

**Stash**:
The player's hub storage grid, kept between raids.
_Avoid_: storage, bank, inventory (the inventory is what the character carries)

**Raid outcome**:
How a raid ends for the player: extracted, killed, or MIA (the raid timer ran out). Quitting or crashing mid-raid counts as killed.
_Avoid_: result, death type, end state

**Secure container**:
The small worn container whose contents the player keeps whatever the raid outcome.
_Avoid_: safe box, pouch, gamma

**Found in raid**:
An item that was not the player's when the raid started and that the player kept at its end; some quests accept only such items.
_Avoid_: FIR item, looted item

**Brought in**:
An item the player carried into the current raid; it can never become found in raid during that raid.
_Avoid_: own gear, starting kit
