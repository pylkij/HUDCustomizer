# HUDCustomizer — Pre-1.0 Investigation Summary

This document summarises the findings of the pre-1.0 settings investigation. It covers every system examined, what was found, what is implementable, and a recommended implementation priority. Implementation details, exact field names, and hook methods are in `CONTRIBUTOR_README.md` — this document is the decision-making layer above it.

---

## Investigation methods used

| Tier | Method | Used for |
|---|---|---|
| 1 | dump.cs PowerShell / Python extraction | UIConfig field cross-check; all 22 Tactical UI types |
| 2 | Dummy DLL field layout | Field offsets, inheritance chains, hook method signatures |
| 3 | Runtime namespace enumerator | Skipped — dump.cs provided sufficient information |
| 4 | Ghidra decompilation of `GameAssembly.dll` | `TargetAimVisualizer.OutOfRangeColor`; `LineOfSightVisualizer` component identification |

---

## Corrections to existing documentation

Two findings from previous work were incorrect and have been fixed in `CONTRIBUTOR_README.md`.

**Mission colour fields carry `[UssColor]`**
`ColorMissionPlayable`, `ColorMissionLocked`, `ColorMissionPlayed`, `ColorMissionPlayedArrow`, and `ColorMissionUnplayable` were documented as non-USS direct values. The dump confirms all five carry `[UssColor]`. They should be implemented via `USSCustomizer.TryApply()` alongside the existing 23 USS fields — not via the `FactionHealthBarColors` pattern.

**`LineOfSightVisualizer` is implementable**
The original scan declared this impossible due to "Shapes library types with no Il2CppInterop bindings." Ghidra decompilation showed there is no Shapes library — the components are standard Unity `LineRenderer` instances. The scan failed because of how it resolved components, not because of missing bindings. Full implementation is viable.

**`TargetAimVisualizer.OutOfRangeColor` root cause identified**
The field write was always succeeding but having no effect because `UpdateAim()` hardcodes white into the out-of-range rendering path without reading the field. The fix is a one-line addition to the existing `Patch_TargetAimVisualizer_UpdateAim` postfix.

---

## Confirmed gaps — ready to implement

These require no further investigation. All field names, hook methods, and implementation patterns are confirmed in `CONTRIBUTOR_README.md` Section 10 or 11.

### UIConfig colours not yet exposed

| Group | Fields | Count | USS? | Pattern |
|---|---|---|---|---|
| Rarity colours | `ColorCommonRarity/Named`, `ColorUncommonRarity/Named`, `ColorRareRarity/Named` | 6 | No | `UnitCustomizer` / FactionHealthBar pattern |
| Mission state colours | `ColorMissionPlayable/Locked/Played/PlayedArrow/Unplayable` | 5 | Yes | `USSCustomizer.TryApply()` |
| Delayed ability marker | `ColorPositionMarkerDelayedAbility` | 1 | No | `UnitCustomizer` / FactionHealthBar pattern |

### ObjectivesTracker progress bar

Bar element confirmed by scan: `ProgressBar > Pickable > Fill / PreviewFill`. Uses the same `ApplyBarColours()` pattern as unit bars. Hook already exists in `Patch_ObjectivesTracker_Init`.

### StructureHUD scale

`StructureHUD` inherits `EntityHUD` — bar colours and fonts already apply for free via `Patch_EntityHUD_InitBars`. Only a `StructureHUDScale` config entry is missing.

---

## New systems identified — implementable

All field names are from `dump.cs`. Element names within UXML are unconfirmed and require `RunElementScan` before switching from `QueryAndSet` to direct `el.Q()` calls.

### Skill bar — SkillBarButton

The highest-value new target. Covers every ability button in the tactical skill bar.

Customisable: skill icon tint, selected/hover overlay tints, AP label font, uses label font, hotkey label font, preview opacity. Hook: `Init(UITactical)` + re-apply on `Show()`.

### Skill bar slots — BaseSkillBarItemSlot

Covers both `SkillBarSlotWeapon` and `SkillBarSlotAccessory` via one base-class patch.

Customisable: slot background tint, item icon tint, unusable cross overlay tint. `SkillBarSlotWeapon` additionally has a weapon name label. Hook: `Init(UITactical)`.

### SimpleSkillBarButton

Covers the End Turn button and similar simple action buttons.

Customisable: label font, hotkey label font, hover overlay tint. Hook: `SetText(string)`.

### Turn order — TurnOrderFactionSlot

Per-faction slot in the initiative queue.

Customisable: inactive mask tint, selected highlight tint, inactive icon tint. Hook: `Init(FactionTemplate, ...)`.

### Turn order — UnitsTurnBarSlot

Per-unit slot in the turn order bar.

Customisable: overlay tint, selected highlight tint, portrait tint. Hook: `Init(UITactical)` + re-apply on `SetActor(Actor)`.

### Selected unit panel — SelectedUnitPanel

The unit detail panel shown when a unit is selected.

Customisable: portrait tint, header background tint, condition label font, AP label font. Hook: `SetActor(Actor, bool)`.

### TacticalUnitInfoStat

Individual stat rows inside `SelectedUnitPanel`.

Customisable: stat value label font, stat icon tint. Hook: `Init(PropertyDisplayConfig, float)`.

### Turn order panel — TurnOrderPanel

The round counter panel.

Customisable: round number label font. Hook: `UpdateFactions()`.

### Status effect icons — StatusEffectIcon

Stack count label on buff/debuff icons.

Customisable: stack count label font. Hook: `Init(SkillTemplate)` and `Init(Skill)`.

### DelayedAbilityHUD

Delayed off-map ability progress indicator. Hybrid patching — both a UIElement surface and a world-space material.

Customisable: progress element fill tint (inline style), world-space marker colour (test `ColorPositionMarkerDelayedAbility` via UIConfig first before attempting direct Material patch). Hook: `SetAbility(...)` + re-apply on `SetProgressPct(float)`.

### LineOfSightVisualizer

Line-of-sight ray visualisation. Components confirmed as standard Unity `LineRenderer` via Ghidra.

Customisable: `startColor` and `endColor` on each `LineRenderer` instance. Access via `FindObjectsOfType<LineOfSightVisualizer>()`, iterate `m_Lines`, `GetComponent<LineRenderer>()` per entry. Re-apply hook: postfix on `SetVisible(bool)`.

### TargetAimVisualizer OutOfRangeColor (WIP fix)

Root cause confirmed. `UpdateAim()` hardcodes white for the out-of-range path without reading `OutOfRangeColor`. Fix: apply configured colour as `_UnlitColor` via `MaterialPropertyBlock` in the existing `Patch_TargetAimVisualizer_UpdateAim` postfix. One-line change.

---

## Confirmed as not customisable

| Type | Reason |
|---|---|
| `SkillUsesBar` | Notch colours USS-only, no inline override surface |
| `UnitBadge` | Badge tint already covered via `ContainedBadge` in `UnitHUD` element tree |
| `TacticalBarkPanel` | Audio visualisation fields only |
| `OffmapAbilityButton` | Entirely USS state-driven via `SelectableImageButton` base |
| `UnitsTurnBar` | Layout container wrapper, no colour or label fields |
| `BaseHUD` | Positional and rendering flags only |
| `WorldSpaceIcon` | Confirmed empty — no fields |
| `SkillBar` | Layout container wrapper, no colour or label fields |
| `ISkillBarElement` | Interface only |
| `LineOfSightVisualizer` (old finding) | Overturned — see above |

---

## Cleanup required before 1.0

These are code hygiene items with no user-facing impact, but should be done before release.

| Item | Action |
|---|---|
| `Patch_WorldSpaceIcon_Update_Scan` | Delete entire class + `RegisterPatches()` entry + `Scans.RunWorldSpaceIconScan()` |
| `Patch_UnitHUD_OnUpdate_Scan` | Delete entire class + `RegisterPatches()` entry |
| `BleedingWorldSpaceIcon` element name | Run with `EnableScans: true` to confirm `"TextElement"` prediction, then replace `QueryAndSet` with `SetFont(el.Q(...))` |
| `VisualizerCustomizer.LogSummary()` | Remove "not supported" note for `LineOfSightVisualizer` |
| `README.md` (user-facing) | Remove "not supported" note for `LineOfSightVisualizer` |

---

## Implementation priority

Ordered by expected player-visible impact. Items within the same tier can be done in any order.

### Tier 1 — High impact, low effort

These are either one-liners or follow a pattern already in the codebase with confirmed field data.

1. **`TargetAimVisualizer.OutOfRangeColor` fix** — One line in an existing postfix. Completes a half-working feature.
2. **UIConfig mission colours** (5 fields) — Identical pattern to existing USS fields. Affects the campaign map.
3. **UIConfig rarity colours** (6 fields) — Identical pattern to `FactionHealthBarColors`. Affects item/unit rarity display throughout the UI.
4. **`ColorPositionMarkerDelayedAbility`** — Single field, same pattern as rarity colours.
5. **Pre-1.0 cleanup** — Scan patch deletion and `BleedingWorldSpaceIcon` confirmation. No risk.

### Tier 2 — High impact, moderate effort

Require new config classes and patches but follow well-established patterns.

6. **`SkillBarButton`** — Most visible part of the tactical UI. Icon tint, overlay tints, three label fonts.
7. **`BaseSkillBarItemSlot`** — One patch covers both weapon and accessory slots. Background and icon tints.
8. **`StructureHUD` scale** — Near-zero effort; one config field addition.
9. **`ObjectivesTracker` progress bar** — Patch already exists, just needs config fields and `ApplyBarColours()` call.
10. **`SelectedUnitPanel`** — Portrait tint, header tint, two label fonts. Prominent panel.

### Tier 3 — Moderate impact, moderate effort

Meaningful but affect less frequently visible UI elements.

11. **`TurnOrderFactionSlot`** — Faction queue colouring. Visible throughout every tactical mission.
12. **`UnitsTurnBarSlot`** — Per-unit turn order slot. Overlay, selected, and portrait tints.
13. **`SimpleSkillBarButton`** — End Turn and similar buttons. Two label fonts, hover tint.
14. **`TacticalUnitInfoStat`** — Stat rows in the unit panel. Font and icon tint.
15. **`SkillBarSlotWeapon`** — Weapon name label font, on top of base slot coverage.

### Tier 4 — Lower impact or higher complexity

16. **`LineOfSightVisualizer`** — Implementable but requires iterating a `Den.Tools.Splines.Line[]` list via Il2Cpp field access, which is more involved than the standard `FindObjectsOfType` pattern. LOS lines are visible but brief.
17. **`DelayedAbilityHUD`** — Progress element tint is straightforward; world-space marker should be tested via UIConfig first. Delayed abilities are an uncommon mechanic.
18. **`TurnOrderPanel`** — Round number font only. Minor.
19. **`StatusEffectIcon`** — Stack count font only. Minor.

---

## UIConfig field inventory — complete

For reference, the full confirmed field list from `dump.cs`. Memory offsets are from the actual build.

| Field | Offset | `[UssColor]` | Implemented |
|---|---|---|---|
| `ColorNormal` | `0x150` | Yes | ✅ |
| `ColorBright` | `0x160` | Yes | ✅ |
| `ColorNormalTransparent` | `0x170` | Yes | ✅ |
| `ColorInteract` | `0x180` | Yes | ✅ |
| `ColorInteractDark` | `0x190` | Yes | ✅ |
| `ColorInteractHover` | `0x1A0` | Yes | ✅ |
| `ColorInteractSelected` | `0x1B0` | Yes | ✅ |
| `ColorInteractSelectedText` | `0x1C0` | Yes | ✅ |
| `ColorDisabled` | `0x1D0` | Yes | ✅ |
| `ColorDisabledHover` | `0x1E0` | Yes | ✅ |
| `ColorTooltipBetter` | `0x1F0` | Yes | ✅ |
| `ColorTooltipWorse` | `0x200` | Yes | ✅ |
| `ColorTooltipNormal` | `0x210` | Yes | ✅ |
| `ColorPositive` | `0x220` | Yes | ✅ |
| `ColorNegative` | `0x230` | Yes | ✅ |
| `ColorWarning` | `0x240` | Yes | ✅ |
| `ColorDarkBg` | `0x250` | Yes | ✅ |
| `ColorWindowCorner` | `0x260` | Yes | ✅ |
| `ColorTopBar` | `0x270` | Yes | ✅ |
| `ColorTopBarDark` | `0x280` | Yes | ✅ |
| `ColorProgressBarNormal` | `0x290` | Yes | ✅ |
| `ColorProgressBarBright` | `0x2A0` | Yes | ✅ |
| `ColorEmptySlotIcon` | `0x2B0` | Yes | ✅ |
| `ColorCommonRarity` | `0x2C0` | No | ❌ |
| `ColorCommonRarityNamed` | `0x2D0` | No | ❌ |
| `ColorUncommonRarity` | `0x2E0` | No | ❌ |
| `ColorUncommonRarityNamed` | `0x2F0` | No | ❌ |
| `ColorRareRarity` | `0x300` | No | ❌ |
| `ColorRareRarityNamed` | `0x310` | No | ❌ |
| `HealthBarFillColorPlayerUnits` | `0x328` | No | ✅ |
| `HealthBarPreviewColorPlayerUnits` | `0x338` | No | ✅ |
| `HealthBarFillColorAllies` | `0x348` | No | ✅ |
| `HealthBarPreviewColorAllies` | `0x358` | No | ✅ |
| `HealthBarFillColorEnemies` | `0x368` | No | ✅ |
| `HealthBarPreviewColorEnemies` | `0x378` | No | ✅ |
| `HealthBarSectionColorPlayerUnits` | `0x388` | No | ✅ |
| `HealthBarSectionColorEnemies` | `0x398` | No | ✅ |
| `ColorMissionPlayable` | `0x500` | Yes | ❌ |
| `ColorMissionLocked` | `0x510` | Yes | ❌ |
| `ColorMissionPlayed` | `0x520` | Yes | ❌ |
| `ColorMissionPlayedArrow` | `0x530` | Yes | ❌ |
| `ColorMissionUnplayable` | `0x540` | Yes | ❌ |
| `ColorPositionMarkerDelayedAbility` | `0x608` | No | ❌ |
