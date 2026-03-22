# HUDCustomizer — AI Agent Working Brief

This file is written for AI agents performing open-ended feature work on HUDCustomizer. It assumes you have access to all source files. Read this before reading anything else.

---

## What this codebase is

A MelonLoader mod for MENACE (Il2Cpp, Unity 6, .NET 6) that lets players customise the tactical HUD via a JSON config file with in-game hot-reload. It is structured as a plugin loaded by Menace Modpack Loader.

The contributor reference is `CONTRIBUTOR_README.md`. Read it for implementation patterns, full source blocks, and the confirmed element tree data. This file tells you what to do; that file tells you how.

If your work touches the CombatFlyoverText integration (`CombatFlyoverCustomizer.cs`, `CombatFlyoverSettings`, or the `CombatFlyover` config section), also read `CombatFlyoverIntegration-AI.md`.

---

## Current codebase state

### What is complete and working

- UnitHUD and EntityHUD: scale, bar colours (Hitpoints/Armor/Suppression fill/preview/track), badge tint — fully implemented, scan-confirmed
- Faction health bar colours (infobox panel): fully implemented via UIConfig
- Tile highlight colours: all 23 slots fully implemented
- USS global theme colours: all 23 `[UssColor]` fields fully implemented
- Font overrides: all confirmed HUD types implemented (UnitHUD, EntityHUD, ObjectivesTracker, MissionInfoPanel, ObjectiveHUD, MovementHUD, BleedingWorldSpaceIcon, DropdownText)
- Hot-reload: working for all implemented systems
- Config I/O: working, with the following behaviour:
  - On first run, `HUDCustomizer.json` is generated from `BuildDefaultConfig()`
  - On subsequent runs, missing keys are filled from defaults and written back (merge-fill); existing user values are preserved
  - Malformed JSON is detected at load time -- the file is NOT overwritten; the exact line and column of the error is logged and defaults are used for the session only
  - On every successful load (initial and hot-reload), a backup is written to `UserData/HUDCustomizer/HUDCustomizer.json`
  - On `OnInitialize`, if `Mods/HUDCustomizer/HUDCustomizer.json` is absent but a UserData backup exists, it is restored automatically before load
  - `HUDCustomizerConfig` carries a `ConfigVersion` integer (currently 1) for future targeted migrations
- MovementVisualizer colours (ReachableColor, UnreachableColor): implemented
- TargetAimVisualizer colours (_UnlitColor, _EmissiveColor) and float
parameters: implemented
- Spent unit HUD opacity (`SpentUnitHUDOpacity`): implemented via `Patch_UnitHUD_SetOpacity`

### What is incomplete — actionable now

These gaps can be implemented without any additional scan or source data. All required information is already confirmed.

**1. SimpleWorldSpaceIcon scan patch — delete it**
`Patch_WorldSpaceIcon_Update_Scan` and `Scans.RunWorldSpaceIconScan()` exist to discover `SimpleWorldSpaceIcon`'s structure. That class has been confirmed from source to have no text, colour, or icon fields — there is nothing to customise. The scan infrastructure can be deleted entirely.
- Delete: `Patch_WorldSpaceIcon_Update_Scan` inner class in `HUDCustomizer.cs`
- Delete: its `harmony.PatchAll(typeof(Patch_WorldSpaceIcon_Update_Scan))` line in `RegisterPatches()`
- Delete: `RunWorldSpaceIconScan()` and `_worldSpaceIconScanned` from `Scans.cs`

**2. UIConfig rarity colours — not exposed**
Six rarity colour fields on `UIConfig` are confirmed from scan but not yet exposed in the config or set anywhere. They do not carry `[UssColor]` so they are direct colour values, not USS custom properties. Implementation follows the identical pattern as `FactionHealthBarColors` in `UnitCustomizer.cs`.

Confirmed fields and defaults (from scan log):
- `ColorCommonRarity`: 116, 108, 75
- `ColorCommonRarityNamed`: 216, 232, 203
- `ColorUncommonRarity`: 61, 117, 136
- `ColorUncommonRarityNamed`: 185, 208, 214
- `ColorRareRarity`: 189, 49, 49
- `ColorRareRarityNamed`: 252, 241, 240

**3. UIConfig mission colours — not exposed**
Five mission state colour fields confirmed from scan, not exposed. Same pattern as above.

Confirmed fields and defaults (from scan log):
- `ColorMissionPlayable`: 168, 152, 103
- `ColorMissionLocked`: 168, 152, 103
- `ColorMissionPlayed`: 113, 102, 69
- `ColorMissionPlayedArrow`: 75, 67, 44, A=0.50
- `ColorMissionUnplayable`: 115, 115, 115
- `ColorPositionMarkerDelayedAbility`: 0, 255, 255

**4. ObjectivesTracker progress bar colours — not exposed**
The tracker has a `ProgressBar` element with the same `Pickable > Fill / PreviewFill` structure as unit bars, confirmed by scan. The game sets fill colours from its own static constants (`FILL_COLOR`, `PREVIEW_FILL_COLOR`) — these can be overridden with inline styles using the same `ApplyBarColours()` pattern already in `UnitCustomizer.cs`. The bar element name is `"ProgressBar"`.

Confirmed element structure from scan:
```
ProgressBar
  Pickable            bg=(0,0,0,0.333)
    Label             fontSize=14
    Fill              bg=(0.961,0.851,0.847,1)
    PreviewFill       bg=(0.384,0.365,0.341,1)
    Border
    DarkLabelClip
```

### What is incomplete — blocked on scan confirmation

These cannot be fully implemented without running the game with `EnableScans: true` to confirm element names.

**BleedingWorldSpaceIcon element name**
`FontCustomizer.ApplyBleedingWorldSpaceIcon()` queries for `"TextElement"` using `QueryAndSet`. The source field is `private readonly Label m_TextElement`. Following the established naming pattern, `"TextElement"` is predicted to be correct but has not been scan-confirmed. The `QueryAndSet` call will log a warning at runtime if the name is wrong. Once confirmed, replace `QueryAndSet` with `SetFont(el.Q("TextElement", (string)null), ...)`.

**DropdownText — confirmed, implemented**
Element structure confirmed by scan: `Container > Icon + Label`. `Label` is the text element (fontSize=14, USS class `font-headline`). Hook point: `Init(String _text, Sprite _icon)` — fires once on creation with text already set. Implemented as `Patch_DropdownText_Init` in `HUDCustomizer.cs`, config key `DropdownText` in `HUDCustomizerConfig`. Covers all flyover text shown above units (AP changes, suppression, skill effects such as Taking Command).

**LineOfSightVisualizer — confirmed not implementable**
Scan confirmed: each 'LineOfSightLine(Clone)' child has three components that
resolve only to the base Il2Cpp 'Component' type -- no named Shapes bindings
are generated in this build.  No MeshRenderer children exist (Shapes uses
CommandBuffer rendering).  There is no accessible interop surface for colour
overrides.  The scan infrastructure has been removed.  Do not attempt to
re-implement without a future Il2Cpp build that generates Shapes bindings.

---

## File map — what to touch for each type of change

| Task | Files to modify |
|---|---|
| Add a new UIConfig colour group | `HUDConfig.cs` (config class + `BuildDefaultConfig()`), `USSCustomizer.cs` or `UnitCustomizer.cs` (apply + log), `HUDCustomizer.cs` (`LoadConfig()` summary call) |
| Increment config schema version | `HUDConfig.cs` (`ConfigVersion` default in `HUDCustomizerConfig` + value in `BuildDefaultConfig()`); add migration logic to `LoadConfig()` in `HUDCustomizer.cs` if needed |

**`LoadConfig()` summary call sequence — insert new calls at the end of this block:**

```csharp
UnitCustomizer.LogColourSummary();
FontCustomizer.LogFontSummary();
TileCustomizer.LogSummary();
USSCustomizer.LogSummary();
UnitCustomizer.LogFactionHealthBarSummary();
VisualizerCustomizer.LogSummary();
HUDCustomizerPlugin.LogSpentOpacitySummary();
// insert YourCustomiser.LogSummary() here
```

This block appears in `LoadConfig()` in `HUDCustomizer.cs` immediately after the `Log.Msg(...)` call that prints scale, origin, and reload key. Every system that applies settings must have a corresponding summary call here so config load output is complete and verifiable in the log.
| Add a new per-element HUD colour | `HUDConfig.cs`, relevant customiser `.cs`, `HUDCustomizer.cs` (patch + `ReapplyToLiveElements()` + summary call) |
| Add a new font target | `HUDConfig.cs` (`FontSettings` property + `BuildDefaultConfig()` entry), `FontCustomizer.cs` (`Apply()` switch + new `ApplyYourHUD()` + `LogFontSummary()`), `HUDCustomizer.cs` (patch class + `RegisterPatches()` + `ReapplyToLiveElements()` case) |
| Delete a scan patch | `HUDCustomizer.cs` (patch class + `RegisterPatches()`), `Scans.cs` (scan method + flag, if no other callers) |
| Add a new scan | `Scans.cs` (flag + method), `HUDCustomizer.cs` (call site in patch or lifecycle) |

---

## Hard constraints — never do these

**1. Do not modify the `BuildDefaultConfig()` string structure.**
It is a single C# verbatim string literal (`@"..."`). Do not split it into multiple strings or concatenations. The file must be written as one atomic `File.WriteAllText` call. Internal quotes are escaped as `""` — this is correct C# verbatim syntax, not a typo.

**2. Do not use `foreach` on `ve.GetClasses()`.**
The Il2Cpp-wrapped `IEnumerable<string>` returned by `GetClasses()` does not implement the C# enumerator contract. Always use `HUDCustomizerPlugin.GetClasses(ve)` instead. Using `foreach` directly will throw at runtime.

**3. Do not omit the second argument to `element.Q()`.**
Always write `element.Q("ElementName", (string)null)`. The single-argument overload may not resolve correctly through the Il2Cpp binding. This produces no compile error but silently returns null at runtime.

**4. Do not call `ToColor()` without checking `Enabled` first.**
`ToColor(entry, label)` does not check `entry.Enabled`. Always guard: `if (entry.Enabled) field = ToColor(entry, label)`.

**5. Do not use `is`, `as`, or direct inheritance assumptions between Il2Cpp types.**
Use `.Cast<T>()` when you are certain of the type (throws on failure). Use `.TryCast<T>()` for conditional checks (returns null on failure). Do not assume C# inheritance relationships match native inheritance.

**6. Every Harmony patch postfix must have a try/catch.**
A patch failure must never crash the game. The catch block should call `Log.Error(...)` with the patch class name. See any existing patch in `HUDCustomizer.cs` for the exact pattern.

**7. Every new patch class must be registered in `RegisterPatches()`.**
Use `harmony.PatchAll(typeof(YourPatchClass))` — never `harmony.PatchAll()` without a type argument.

**8. Do not patch `MonoBehaviour.OnEnable`.**
`AccessTools.DeclaredMethod` cannot resolve `OnEnable` on `UnityEngine.MonoBehaviour` in this Il2Cpp build — the method is stripped from the generated interop assembly. Attempting to patch it causes the entire plugin to fail initialization. Use a more specific hook on a known concrete type instead.

**Adding a new colour that comes from UIConfig:**
Use the `TileHighlightEntry` type in `HUDConfig.cs` and the `if (entry.Enabled) uiConfig.Field = ToColor(entry, label)` pattern. Call `TryApply()` from both `OnTacticalReady` and the hot-reload block in `OnUpdate`. See `USSCustomizer.cs` and `UnitCustomizer.ApplyFactionHealthBarColors()` for the two existing patterns.

**Adding a new colour that is set directly on a VisualElement:**
Use the string colour convention (`"R,G,B"` or `"R,G,B,A"`, empty = unchanged) and `TryParseColor` + `ve.style.backgroundColor` or `ve.style.unityBackgroundImageTintColor`. Register the element in `_registry` via `Register(el, "HudType")` so it is re-applied on hot-reload. Add a `case "HudType":` to `ReapplyToLiveElements()`. See `UnitCustomizer.ApplyBarColours()` for the pattern.

**Discovering an unknown class type responsible for a UI element:**
If the target class is not known, enumerate all types in the relevant namespace at `TacticalReady` using a throwaway mod (`TacticalTypeEnumeratorPlugin` pattern). Once candidate type names are identified, dump their public instance methods using `GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)` — this reveals the correct hook method without guessing. `Update` is a valid last resort but fires before the element has content; prefer a named method that receives the display data (e.g. `Init`, `SetText`, `Show`). The full `Menace.UI.Tactical` type inventory from scan is: `BaseHUD`, `BaseSkillBarItemSlot`, `BleedingWorldSpaceIcon`, `DelayedAbilityHUD`, `DropdownText`, `EntityHUD`, `ISkillBarElement`, `MissionInfoPanel`, `MovementHUD`, `ObjectiveHUD`, `ObjectivesTracker`, `OffmapAbilityButton`, `SelectedUnitPanel`, `SimpleSkillBarButton`, `SimpleWorldSpaceIcon`, `SkillBar`, `SkillBarButton`, `SkillBarSlotAccessory`, `SkillBarSlotWeapon`, `SkillUsesBar`, `StatusEffectIcon`, `StructureHUD`, `TacticalBarkPanel`, `TacticalUnitInfoStat`, `TurnOrderFactionSlot`, `TurnOrderPanel`, `UnitBadge`, `UnitHUD`, `UnitsTurnBar`, `UnitsTurnBarSlot`, `WorldSpaceIcon`.

**Implementing a new font target:**
Add a `FontSettings` property to `HUDCustomizerConfig`. Add a `case "HudType":` to `FontCustomizer.Apply()` and implement `ApplyYourHUD()`. Use `SetFont(el.Q(name, (string)null), Merge(cfg.Global, cfg.YourEntry), label)` for scan-confirmed element names, or `QueryAndSet(el, name, Merge(...), label)` for unconfirmed names. Add entries to `LogFontSummary()`.

**Overriding a value the game sets on a state transition (not every frame):**
Do not patch `OnUpdate` and read back the written value to detect state — `OnUpdate` may delegate to a private setter that only fires on transitions, and reading `style.*` or `resolvedStyle.*` after your own override will not reliably reflect the game's intent on subsequent calls. Instead, identify the private setter (e.g. `SetOpacity(float _opacity)`) from the game source and patch that directly. Use the method's parameter to detect the game's intent (`_opacity == 0.5f` = spent), and write your override value unconditionally when the parameter matches. For hot-reload, detect currently-affected elements by checking that their current inline style is not the game's "unaffected" value (e.g. `!Approximately(el.style.opacity.value, 1.0f)`).

**Removing a scan patch that is no longer needed:**
Delete the inner class. Remove its `harmony.PatchAll(typeof(...))` line from `RegisterPatches()`. If the corresponding `Scans` method has no other callers, delete it and its `_scanned` flag from `Scans.cs`.

**Unsure whether a field/element name is correct:**
Check the scan log output in `CONTRIBUTOR_README.md` Section 3 for confirmed names and RGBA values. If not listed there, it is unconfirmed — use `QueryAndSet` rather than `SetFont(el.Q(...))` so a warning is logged at runtime if the name is wrong.

---

## Runtime behaviour to know

- Hot-reload fires `LoadConfig()` → `FontCustomizer.InvalidateCache()` → `ReapplyToLiveElements()` → `TileCustomizer.TryApply()` → `USSCustomizer.TryApply()` → `UnitCustomizer.ApplyFactionHealthBarColors()`. Any new system that needs hot-reload support must be called in this sequence.
- `TileHighlighter.Exists()` returns false outside tactical scenes. `TileCustomizer.TryApply()` checks this and returns early silently if the singleton is unavailable. `USSCustomizer.TryApply()` and `UnitCustomizer.ApplyFactionHealthBarColors()` check `UIConfig.Get() != null` independently — UIConfig availability is not tied to TileHighlighter. Each `TryApply()` method guards its own singleton; none of them assume the others are available.
- Elements are registered in `_registry` keyed by native pointer (`el.Pointer`). Entries with `Pointer == IntPtr.Zero` are destroyed native objects and are pruned during `ReapplyToLiveElements()`. Do not hold references to `Il2CppInterfaceElement` objects outside the registry.
- `DebugLogging: true` in the config enables per-element `[DBG]` log lines via `HUDCustomizerPlugin.Debug(...)`. Always wrap verbose output in `Debug()` rather than `Log.Msg()`.

---

## Verification checklist

After implementing any change, confirm:

- [ ] Project compiles without errors or warnings
- [ ] New config fields appear in the generated JSON when `HUDCustomizer.json` is deleted and the game is launched (regeneration test)
- [ ] Hot-reload applies the new settings without error when F8 is pressed in a tactical mission
- [ ] `LoadConfig()` log output includes a summary line for the new system
- [ ] No `[HUDCustomizer]` errors or warnings appear in the MelonLoader log during normal play
- [ ] If a scan patch was deleted, no reference to its type remains in `RegisterPatches()`
- [ ] If a `Scans` method was deleted, no call site remains in any other file
