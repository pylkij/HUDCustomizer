# HUD Customizer - Getting Started

HUD Customizer has a lot of settings. Most of them do something useful, but not all of them will feel immediately impactful. This guide covers the areas worth prioritising if you want the biggest improvement to readability and gameplay clarity with the least amount of fiddling.

If you'd rather skip the setup entirely, a ready-to-use personal config is available as an optional download on the Nexus page.

---

## First: learn the hot-reload key

Before changing anything, know that you don't need to restart the game to see your changes. Press **F8** while in a tactical mission to reload the config instantly. This makes the whole process of dialling in settings much more approachable — edit the file, save it, press F8, and see the result immediately.

The key is configurable via the `ReloadKey` setting if F8 conflicts with something else.

---

## Tile highlight colours

This is the single highest-impact area in the mod. By default, the game uses white for almost every tile overlay — movement range, skill range, enemy view cones, AoE areas, and objectives all compete visually using the same colour. Giving each type of information its own distinct colour makes tactical situations dramatically easier to read at a glance, and significantly reduces the eye-strain of leaning in to figure out what a given highlighted tile means.

The most worthwhile slots to configure first:

- **Movement** — your unit's walkable range
- **Skill Range** — your attack and ability range
- **Enemy Skill / Enemy View** — what the enemy can see and reach
- **Objectives** — objective tile highlights
- **AoE** — area of effect zones

---

## Unit HUD

These settings control the floating bars above units on the battlefield. The HUD carries a lot of information in a compact space, and the defaults aren't always easy to read, especially at smaller scales.

The most worthwhile settings here:

- **Scale** (`UnitHUDScale`, `EntityHUDScale`, `StructureHUDScale`) — resize the floating HUDs to suit your preference. Larger makes information easier to read; smaller reduces visual clutter on a busy map.
- **HUD bar colours** — the fill, preview, and track colours for health, armour, and suppression bars. Useful for improving contrast or matching your tile highlight colour scheme.
- **Spent unit opacity** (`SpentUnitHUDOpacity`) — controls how dim a unit's HUD becomes after it has used its turn. Setting this to `1.0` keeps all HUDs at full visibility; lowering it makes spent units recede visually so your attention naturally goes to units that still have actions.
- **Transform origin** — controls which point of the HUD stays anchored when you scale it. The default keeps the bar pinned at the bottom, so scaling up makes it grow upward rather than into the unit below.

---

## Visualizers

These settings control the 3D lines drawn in the world during tactical play — the movement path on the ground and the aim arc when targeting. Like tile highlights, the defaults are all white, so different types of information blend together on a crowded map.

The most worthwhile:

- **Movement Visualizer** — separate colours for reachable and out-of-range path segments make it immediately obvious whether a destination is within reach.
- **Line of Sight Visualizer** — colouring the LOS rays makes them much easier to spot during targeting, particularly when multiple units have overlapping lines.

---

## Combat flyover text

This feature displays the results of each attack as floating text above the attacking unit — accuracy percentage, HP damage, and armour damage. It's enabled by default and works without any configuration.

A couple of settings worth adjusting:

**Colours** — each flyover type (accuracy, HP damage, armour damage) has its own colour. These use hex colour codes rather than the R/G/B format used elsewhere in the config — keep that in mind when picking values. The defaults are red for HP damage, blue for armour damage, and green for accuracy.

**Display duration** — `ExtraDisplaySeconds` and `FadeDurationScale` control how long each flyover is visible and how quickly it fades. The defaults are set conservatively so nothing is missed, but if the pace feels slow you can reduce both values. They interact with each other, so it takes a little trial and error to find the right balance — hot-reload makes this easy to iterate on.

---

## Everything else

The remaining settings — fonts, USS theme colours, faction health bar colours, rarity colours, and the tactical UI element tints — all work, but their effects are more subtle or situational.

**USS colours** are worth a mention: they apply game-wide across all screens, not just the tactical HUD, so changes here have broader reach than most other settings. Treat them as an advanced option and test carefully.

**Rarity colours** and **Tactical UI styles** (skill bar tints, turn order panel tints, etc.) are there if you want fine-grained control, but they're unlikely to be the first thing you reach for.
