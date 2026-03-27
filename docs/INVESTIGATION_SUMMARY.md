# HUDCustomizer - Pre-1.0 Investigation Summary

This document summarises the findings of the pre-1.0 settings investigation. It covers every system examined, what was found, what is implementable, and a recommended implementation priority. Implementation details, exact field names, and hook methods are in `CONTRIBUTOR_README.md` - this document is the decision-making layer above it.

Status note: this document now includes post-investigation completion updates. Where historical recommendation text remains, treat the "Implementation priority" section and `PLAN.md` "Current Status" as authoritative for current state.

---

## Investigation methods used

| Tier | Method | Used for |
|---|---|---|
| 1 | dump.cs PowerShell / Python extraction | UIConfig field cross-check; all 22 Tactical UI types |
| 2 | Dummy DLL field layout | Field offsets, inheritance chains, hook method signatures |
| 3 | Runtime namespace enumerator | Skipped - dump.cs provided sufficient information |
| 4 | Ghidra decompilation of `GameAssembly.dll` | `TargetAimVisualizer.OutOfRangeColor`; `LineOfSightVisualizer` component identification |

---

## Corrections to existing documentation

Two findings from previous work were incorrect and have been fixed in `CONTRIBUTOR_README.md`.

**Mission colour fields carry `[UssColor]`**
`ColorMissionPlayable`, `ColorMissionLocked`, `ColorMissionPlayed`, `ColorMissionPlayedArrow`, and `ColorMissionUnplayable` were documented as non-USS direct values. The dump confirms all five carry `[UssColor]`. They should be implemented via `USSCustomizer.TryApply()` alongside the existing 23 USS fields - not via the `FactionHealthBarColors` pattern.

**`LineOfSightVisualizer` is implementable - and implemented**
The original scan declared this impossible due to "Shapes library types with no Il2CppInterop bindings." Ghidra decompilation and runtime verification confirmed the components are `Il2CppShapes.Line` instances from `Il2CppShapesRuntime.dll` (namespace `Il2CppShapes`, class `Line`). An earlier intermediate finding incorrectly identified them as `UnityEngine.LineRenderer` - that was also retracted. Colour is written only in `Resize(int)` via `ColorStart`/`ColorEnd` on each `Line`. `GetComponentsInChildren<T>` throws a fatal Il2CppInterop exception for this type; indexed `GetChild(i).GetComponent<Il2CppShapes.Line>()` traversal is required. Fully implemented via `LOSResizePatch`, `ApplyLineOfSightColor`, and `TryApplyLineOfSight`. Verified in log.

**`TargetAimVisualizer.OutOfRangeColor` root cause identified and fixed**
The field write was always succeeding but having no effect because `UpdateAim()` hardcodes white into the out-of-range rendering path without reading the field. Fixed by applying `OutOfRangeColor` as `_UnlitColor` via `MaterialPropertyBlock` in the `Patch_TargetAimVisualizer_UpdateAim` postfix (after `UpdateAim()` runs, so the override is never overwritten until the next call). Fully implemented.

---

## Confirmed gaps - implemented in Tier 1

The following items were confirmed gaps at investigation time and have since been implemented.

### UIConfig colours - done implemented

| Group | Fields | Count | USS? | Pattern | Status |
|---|---|---|---|---|---|
| Rarity colours | `ColorCommonRarity/Named`, `ColorUncommonRarity/Named`, `ColorRareRarity/Named` | 6 | No | `UnitCustomizer.ApplyRarityColors()` | done Done |
| Mission state colours | `ColorMissionPlayable/Locked/Played/PlayedArrow/Unplayable` | 5 | Yes | `USSCustomizer.TryApply()` | done Done |
| Delayed ability marker | `ColorPositionMarkerDelayedAbility` | 1 | No | `UnitCustomizer.ApplyRarityColors()` | done Done |

### ObjectivesTracker progress bar - completed

Bar element confirmed by scan: ProgressBar > Pickable > Fill / PreviewFill.
Implemented via ObjectivesTrackerProgressBar config + TacticalElementCustomizer.ApplyObjectivesTrackerProgressBar(...) in Patch_ObjectivesTracker_Init and hot-reload reapply.

### StructureHUD scale - completed

StructureHUDScale has been added to config/default JSON and wired through Patch_StructureHUD_Init plus ReapplyToLiveElements() ("StructureHUD" case).

---

## New systems identified - implementable

All field names are from `dump.cs`. Element names within UXML are unconfirmed and require `RunElementScan` before switching from `QueryAndSet` to direct `el.Q()` calls.

### Skill bar - SkillBarButton

The highest-value new target. Covers every ability button in the tactical skill bar.

Customisable: skill icon tint, selected/hover overlay tints, AP label font, uses label font, hotkey label font, preview opacity. Hook: `Init(UITactical)` + re-apply on `Show()`.

### Skill bar slots - BaseSkillBarItemSlot

Covers both `SkillBarSlotWeapon` and `SkillBarSlotAccessory` via one base-class patch.

Customisable: slot background tint, item icon tint, unusable cross overlay tint. `SkillBarSlotWeapon` additionally has a weapon name label. Hook: `Init(UITactical)`.

### SimpleSkillBarButton

Covers the End Turn button and similar simple action buttons.

Customisable: label font, hotkey label font, hover overlay tint. Hook: `SetText(string)`.

### Turn order - TurnOrderFactionSlot

Per-faction slot in the initiative queue.

Customisable: inactive mask tint, selected highlight tint, inactive icon tint. Hook: `Init(FactionTemplate, ...)`.

### Turn order - UnitsTurnBarSlot

Per-unit slot in the turn order bar.

Customisable: overlay tint, selected highlight tint, portrait tint. Hook: `Init(UITactical)` + re-apply on `SetActor(Actor)`.

### Selected unit panel - SelectedUnitPanel

The unit detail panel shown when a unit is selected.

Customisable: portrait tint, header background tint, condition label font, AP label font. Hook: `SetActor(Actor, bool)`.

### TacticalUnitInfoStat

Individual stat rows inside `SelectedUnitPanel`.

Customisable: stat value label font, stat icon tint. Hook: `Init(PropertyDisplayConfig, float)`.

### Turn order panel - TurnOrderPanel

The round counter panel.

Customisable: round number label font. Hook: `UpdateFactions()`.

### Status effect icons - StatusEffectIcon

Stack count label on buff/debuff icons.

Customisable: stack count label font. Hook: `Init(SkillTemplate)` and `Init(Skill)`.

### DelayedAbilityHUD

Delayed off-map ability progress indicator. Hybrid patching - both a UIElement surface and a world-space material.

Customisable: progress element fill tint (inline style), world-space marker colour (test `ColorPositionMarkerDelayedAbility` via UIConfig first before attempting direct Material patch). Hook: `SetAbility(...)` + re-apply on `SetProgressPct(float)`.

### LineOfSightVisualizer done COMPLETED

Line-of-sight ray visualisation. Renderer type confirmed as `Il2CppShapes.Line` (from `Il2CppShapesRuntime.dll`, namespace `Il2CppShapes`). Pool structure: `List<Line[]> m_Lines`, 3 `Line` entries per group (fade-in, solid, fade-out). Colour written only in `Resize(int)` via `ColorStart`/`ColorEnd`. `GetComponentsInChildren<T>` is fatal for this type - indexed child traversal required.

Implemented: `LOSResizePatch` postfixes `Resize(int)` to re-apply colour after each pool growth. `ApplyLineOfSightColor` applies the fade pattern to all children. `TryApplyLineOfSight` handles `TacticalReady` and hot-reload. `LogLineOfSightSummary` confirmed in log. Config slot: `Visualizers.LineOfSight.LineColor`.

### TargetAimVisualizer OutOfRangeColor done COMPLETED

Root cause confirmed and fixed. `UpdateAim()` hardcodes white for the out-of-range path without reading `OutOfRangeColor`. Fixed by writing `OutOfRangeColor` as `_UnlitColor` via `MaterialPropertyBlock` in the `Patch_TargetAimVisualizer_UpdateAim` postfix, after `UpdateAim()` runs. Range state is detected by reading `_UnlitColor` back from the material (white = in-range, game-set; non-white = override already applied). Config slot: `Visualizers.TargetAimVisualizer.OutOfRangeColor`.

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
| `WorldSpaceIcon` | Confirmed empty - no fields |
| `SkillBar` | Layout container wrapper, no colour or label fields |
| `ISkillBarElement` | Interface only |
| `LineOfSightVisualizer` (old finding) | Overturned - implemented. See Corrections and New systems above. |

---

## Cleanup completed before 1.0

All pre-1.0 cleanup items have been completed.

| Item | Status |
|---|---|
| `Patch_WorldSpaceIcon_Update_Scan` | done Deleted - class, `RegisterPatches()` entry, and `Scans.RunWorldSpaceIconScan()` all removed |
| `Patch_UnitHUD_OnUpdate_Scan` | done Deleted - class and `RegisterPatches()` entry removed |
| `BleedingWorldSpaceIcon` element name | done Confirmed as `"TextElement"` by scan - `QueryAndSet` replaced with `SetFont(el.Q(...))` |
| `VisualizerCustomizer.LogSummary()` | done Done - "not supported" note removed |
| `README.md` (user-facing) | done Done - "not supported" note for `LineOfSightVisualizer` removed |

---

## Implementation priority

Ordered by expected player-visible impact. Items within the same tier can be done in any order.

### Tier 1 - done All completed

1. **`TargetAimVisualizer.OutOfRangeColor` fix** done - Applied via `MaterialPropertyBlock` in `Patch_TargetAimVisualizer_UpdateAim` postfix. Fully functional.
2. **UIConfig mission colours** (5 fields) done - Implemented via `USSCustomizer.TryApply()`. Affects the campaign map.
3. **UIConfig rarity colours** (6 fields) done - Implemented via `UnitCustomizer.ApplyRarityColors()`. Affects item/unit rarity display throughout the UI.
4. **`ColorPositionMarkerDelayedAbility`** done - Implemented alongside rarity colours in `UnitCustomizer.ApplyRarityColors()`.
5. **Pre-1.0 cleanup** done - Scan patches deleted, `BleedingWorldSpaceIcon` confirmed, `README.md` note removed.

### Tier 2 - completed

6. **SkillBarButton** - completed.
7. **BaseSkillBarItemSlot** - completed.
8. **StructureHUD scale** - completed.
9. **ObjectivesTracker progress bar** - completed.
10. **SelectedUnitPanel** - completed.

### Tier 3 - completed

11. **TurnOrderFactionSlot** - completed.
12. **UnitsTurnBarSlot** - completed.
13. **SimpleSkillBarButton** - completed.
14. **TacticalUnitInfoStat** - completed.
15. **SkillBarSlotWeapon** - completed.

### Tier 4 - partially completed

16. **LineOfSightVisualizer** - completed.
17. **DelayedAbilityHUD** - partial (progress tint + marker validation wiring completed; final marker behavior remains evidence-gated for SetProgressPct).
18. **TurnOrderPanel** - completed.
19. **StatusEffectIcon** - completed (template-overload runtime evidence remains pending from the last capture).

---

## UIConfig field inventory - complete

For reference, the full confirmed field list from `dump.cs`. Memory offsets are from the actual build.

| Field | Offset | `[UssColor]` | Implemented |
|---|---|---|---|
| `ColorNormal` | `0x150` | Yes | done |
| `ColorBright` | `0x160` | Yes | done |
| `ColorNormalTransparent` | `0x170` | Yes | done |
| `ColorInteract` | `0x180` | Yes | done |
| `ColorInteractDark` | `0x190` | Yes | done |
| `ColorInteractHover` | `0x1A0` | Yes | done |
| `ColorInteractSelected` | `0x1B0` | Yes | done |
| `ColorInteractSelectedText` | `0x1C0` | Yes | done |
| `ColorDisabled` | `0x1D0` | Yes | done |
| `ColorDisabledHover` | `0x1E0` | Yes | done |
| `ColorTooltipBetter` | `0x1F0` | Yes | done |
| `ColorTooltipWorse` | `0x200` | Yes | done |
| `ColorTooltipNormal` | `0x210` | Yes | done |
| `ColorPositive` | `0x220` | Yes | done |
| `ColorNegative` | `0x230` | Yes | done |
| `ColorWarning` | `0x240` | Yes | done |
| `ColorDarkBg` | `0x250` | Yes | done |
| `ColorWindowCorner` | `0x260` | Yes | done |
| `ColorTopBar` | `0x270` | Yes | done |
| `ColorTopBarDark` | `0x280` | Yes | done |
| `ColorProgressBarNormal` | `0x290` | Yes | done |
| `ColorProgressBarBright` | `0x2A0` | Yes | done |
| `ColorEmptySlotIcon` | `0x2B0` | Yes | done |
| `ColorCommonRarity` | `0x2C0` | No | done |
| `ColorCommonRarityNamed` | `0x2D0` | No | done |
| `ColorUncommonRarity` | `0x2E0` | No | done |
| `ColorUncommonRarityNamed` | `0x2F0` | No | done |
| `ColorRareRarity` | `0x300` | No | done |
| `ColorRareRarityNamed` | `0x310` | No | done |
| `HealthBarFillColorPlayerUnits` | `0x328` | No | done |
| `HealthBarPreviewColorPlayerUnits` | `0x338` | No | done |
| `HealthBarFillColorAllies` | `0x348` | No | done |
| `HealthBarPreviewColorAllies` | `0x358` | No | done |
| `HealthBarFillColorEnemies` | `0x368` | No | done |
| `HealthBarPreviewColorEnemies` | `0x378` | No | done |
| `HealthBarSectionColorPlayerUnits` | `0x388` | No | done |
| `HealthBarSectionColorEnemies` | `0x398` | No | done |
| `ColorMissionPlayable` | `0x500` | Yes | done |
| `ColorMissionLocked` | `0x510` | Yes | done |
| `ColorMissionPlayed` | `0x520` | Yes | done |
| `ColorMissionPlayedArrow` | `0x530` | Yes | done |
| `ColorMissionUnplayable` | `0x540` | Yes | done |
| `ColorPositionMarkerDelayedAbility` | `0x608` | No | done |





