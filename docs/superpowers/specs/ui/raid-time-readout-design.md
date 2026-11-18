# Raid time readout

Date: 2026-11-05

Status: approved design, not implemented. Replaces the wristwatch design (character visuals spec section 1, superseded by its decision 11 when the watch was removed from the player model). Designer decision 2026-11-05: "press O to display the time, like in Tarkov". Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Rule

- Pressing **O** shows the raid time left on screen for a few seconds, then it fades. There is no permanent timer (GDD §18 no-HUD rule: this is a brief indicator, like the stance and fire-mode indicators).
- No animation, no check action, no item: it works at any moment, also while aiming, moving or in the inventory screen.

## Display (Claude's call)

- Text: `Time left 12:34`, minutes and seconds of the raid timer (`RaidSession.RemainingS`), counting down live while shown.
- Position: top right, the HUD's existing font and colour; small and plain.
- Shown for 4 s **(tune)**, then a 0.5 s fade. Pressing O again while shown restarts the 4 s.
- Under 2 minutes left the text turns the HUD's warning colour **(tune)**.
- Not shown while the pause menu, the raid result screen or a transition screen is up.

## Input

- New input action `raid_time`, default key **O**. Rebindable through the controls settings (menus and settings spec section 3.3, group Interaction). Adding it to `project.godot` is approved by this decision.

## What changes elsewhere

- GDD §15 "Reading the time": the O readout replaces the analog wristwatch line.
- Character visuals spec: the wristwatch section stays superseded; its open item points here. Raids no longer need a fixed start clock time or a deadline sentence on the deploy screen for this purpose (the deploy screen may still show the raid length).
- Text through `GameText` keys (`hud.raid_time`).

## Tests

- xUnit: formatting of the remaining seconds (`12:34`, `0:05`, never negative), the show and fade timer, re-press restarts, warning threshold.
- Playtest: press O at the start, mid-raid and in the last two minutes; check it is readable in noon light and does not linger.

## Decisions

1. Press O (rebindable) to see the raid time left for about 4 s; no permanent timer, no watch.

## Open items

- A double press to list known extracts (Tarkov's double O) is not part of this; closed 2026-11-18: the paper map item covers learning extracts (`docs/superpowers/specs/maps/paper-maps-chance-extracts-design.md`).
