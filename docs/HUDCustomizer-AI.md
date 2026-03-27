# HUDCustomizer - AI Agent Working Brief

This file is written for AI agents performing open-ended feature work on HUDCustomizer. It assumes you have access to all source files. Read this before reading anything else.

---

## What this codebase is

A MelonLoader mod for MENACE (Il2Cpp, Unity 6, .NET 6) that lets players customise the tactical HUD via a JSON config file with in-game hot-reload. It is structured as a plugin loaded by Menace Modpack Loader.

The contributor reference is `CONTRIBUTOR_README.md`. Read it for implementation patterns, full source blocks, and the confirmed element tree data. This file tells you what to do; that file tells you how.

If your work touches the CombatFlyoverText integration (`CombatFlyoverCustomizer.cs`, `CombatFlyoverSettings`, or the `CombatFlyover` config section), also read `CombatFlyoverIntegration-AI.md`.

---

## Current codebase state

### What is complete and working

- UnitHUD and EntityHUD: scale, bar colours (Hitpoints/Armor/Suppression fill/preview/track), badge tint - fully implemented, scan-confirmed
- StructureHUD: inherits EntityHUD. Has its own dedicated patch (`Patch_StructureHUD_Init` on `StructureHUD.Init`) and independent `StructureHUDScale` config entry applied via `UnitCustomizer.Apply(el, Config.StructureHUDScale)`. Bar colours and fonts come from the EntityHUD pattern automatically.
- Faction health bar colours (infobox panel): fully implemented via UIConfig
- Rarity colours (6 fields: `ColorCommonRarity/Named`, `ColorUncommonRarity/Named`, `ColorRareRarity/Named`) and `ColorPositionMarkerDelayedAbility`: fully implemented via `UnitCustomizer.ApplyRarityColors()` - non-USS, `FactionHealthBarColors` pattern. Config section: `RarityColors`.
- Tile highlight colours: all 23 slots fully implemented
- USS global theme colours: 28 fields fully implemented - 23 general `[UssColor]` fields plus 5 mission state fields (`ColorMissionPlayable`, `ColorMissionLocked`, `ColorMissionPlayed`, `ColorMissionPlayedArrow`, `ColorMissionUnplayable`) via `USSCustomizer.TryApply()`. Config section: `USSColors`. Also applied in the Strategy scene via `OnSceneLoaded` → `ApplyUSSAfterDelay(0.5f)`.
- Tactical element tint/style overrides: implemented via `TacticalElementCustomizer.cs` (`TacticalUIStyles` config section, covers `SkillBarButton`, `BaseSkillBarItemSlot`, `SimpleSkillBarButton`, `TurnOrderFactionSlot`, `UnitsTurnBarSlot`, `SelectedUnitPanel`, `TacticalUnitInfoStat`, `DelayedAbilityHUD`) plus `ObjectivesTrackerProgressBar`.
- Font overrides: tactical expansion implemented (adds `SkillBarButton`, `BaseSkillBarItemSlot` weapon label path, `SimpleSkillBarButton`, `SelectedUnitPanel`, `TacticalUnitInfoStat`, `TurnOrderPanel`, `StatusEffectIcon`, `DropdownText`, `StructureHUD` on top of existing HUD targets)
- Hot-reload: working for all implemented systems
- Config I/O: working, with the following behaviour:
  - On first run, `HUDCustomizer.json` is generated from `BuildDefaultConfig()`
  - On subsequent runs, missing keys are filled from defaults and written back (merge-fill); existing user values are preserved
  - Malformed JSON is detected at load time -- the file is NOT overwritten; the exact line and column of the error is logged and defaults are used for the session only
  - On every successful load (initial and hot-reload), a backup is written to `UserData/HUDCustomizer/HUDCustomizer.json`
  - On `OnInitialize`, if `Mods/HUDCustomizer/HUDCustomizer.json` is absent but a UserData backup exists, it is restored automatically before load
  - `HUDCustomizerConfig` carries a `ConfigVersion` integer (currently 1) for future targeted migrations
- MovementVisualizer colours (ReachableColor, UnreachableColor): implemented
- TargetAimVisualizer: `_UnlitColor` (InRangeColor), `_EmissiveColor` (InRangeEmissiveColor + EmissiveIntensity), float parameters, and `OutOfRangeColor` - all implemented. `OutOfRangeColor` is applied via `MaterialPropertyBlock` in the `Patch_TargetAimVisualizer_UpdateAim` postfix; `UpdateAim()` does not read the native field for out-of-range rendering (root cause confirmed via Ghidra), so the MPB write is the operative fix. Config slot: `Visualizers.TargetAimVisualizer.OutOfRangeColor`.
- LineOfSightVisualizer line colour (LineColor): implemented - `Il2CppShapes.Line` components via indexed child traversal, `ColorStart`/`ColorEnd`, re-applied on every `Resize(int)` via `LOSResizePatch`. Config slot: `Visualizers.LineOfSight.LineColor`. Verified in log.
- Spent unit HUD opacity (`SpentUnitHUDOpacity`): implemented via `Patch_UnitHUD_SetOpacity`

### What is incomplete - actionable now

No mandatory implementation gaps remain for the current tactical expansion set.

Actionable item is validation-only:

**DelayedAbilityHUD world-marker finalisation**
`SetAbility` marker scan evidence exists. `SetProgressPct` marker-phase evidence did not fire in the last capture. If this needs re-validation, re-introduce temporary scan hooks and capture a focused log run.

---

## File map - what to touch for each type of change

| Task | Files to modify |
|---|---|
| Add a new UIConfig colour group | `HUDConfig.cs` (config class + `BuildDefaultConfig()`), `USSCustomizer.cs` or `UnitCustomizer.cs` (apply + log), `HUDCustomizer.cs` (`LoadConfig()` summary call) |
| Increment config schema version | `HUDConfig.cs` (`ConfigVersion` default in `HUDCustomizerConfig` + value in `BuildDefaultConfig()`); add migration logic to `LoadConfig()` in `HUDCustomizer.cs` if needed |
| Add a new per-element HUD tint/style | `HUDConfig.cs`, `TacticalElementCustomizer.cs` (new case + apply method + `LogSummary()` entry), `HUDCustomizer.cs` (patch + `RegisterPatches()` + `ReapplyToLiveElements()` passthrough) |
| Add a new font target | `HUDConfig.cs` (`FontSettings` property + `BuildDefaultConfig()` entry), `FontCustomizer.cs` (`Apply()` switch + new `ApplyYourHUD()` + `LogFontSummary()`), `HUDCustomizer.cs` (patch class + `RegisterPatches()` + `ReapplyToLiveElements()` case if scale applies) |
| Add a new per-element HUD colour (background, not tint) | `HUDConfig.cs`, relevant customiser (for unit-bar-style: `UnitCustomizer.cs`; for tactical UI: `TacticalElementCustomizer.cs`), `HUDCustomizer.cs` (patch + `ReapplyToLiveElements()` + summary call) |
| Delete a scan patch | `HUDCustomizer.cs` (patch class + `RegisterPatches()`), `Scans.cs` (scan method + flag, if no other callers) |
| Add a new scan | `Scans.cs` (flag + method), `HUDCustomizer.cs` (call site in patch or lifecycle) |

**`LoadConfig()` summary call sequence - insert new calls at the end of this block:**

```csharp
UnitCustomizer.LogColourSummary();
FontCustomizer.LogFontSummary();
TileCustomizer.LogSummary();
USSCustomizer.LogSummary();
UnitCustomizer.LogFactionHealthBarSummary();
UnitCustomizer.LogRarityColorSummary();
TacticalElementCustomizer.LogSummary();
VisualizerCustomizer.LogSummary();
VisualizerCustomizer.LogLineOfSightSummary();
HUDCustomizerPlugin.LogSpentOpacitySummary();
CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
CombatFlyoverCustomizer.LogSummary();
// insert YourCustomiser.LogSummary() here
```

This block appears in `LoadConfig()` in `HUDCustomizer.cs` immediately after the `Log.Msg(...)` call that prints version, scale, origin, and reload key. Every system that applies settings must have a corresponding summary call here so config load output is complete and verifiable in the log.

---

## Hard constraints - never do these

**1. Do not modify the `BuildDefaultConfig()` string structure.**
It is a single C# verbatim string literal (`@"..."`). Do not split it into multiple strings or concatenations. The file must be written as one atomic `File.WriteAllText` call. Internal quotes are escaped as `""` - this is correct C# verbatim syntax, not a typo.

**2. Do not use `foreach` on `ve.GetClasses()`.**
The Il2Cpp-wrapped `IEnumerable<string>` returned by `GetClasses()` does not implement the C# enumerator contract. Always use `HUDCustomizerPlugin.GetClasses(ve)` instead. Using `foreach` directly will throw at runtime.

**3. Do not omit the second argument to `element.Q()`.**
Always write `element.Q("ElementName", (string)null)`. The single-argument overload may not resolve correctly through the Il2Cpp binding. This produces no compile error but silently returns null at runtime.

**4. Do not call `ToColor()` without checking `Enabled` first.**
`ToColor(entry, label)` does not check `entry.Enabled`. Always guard: `if (entry.Enabled) field = ToColor(entry, label)`. `TacticalElementCustomizer.SetTint()` handles this guard internally - it is safe to call without a prior check.

**5. Do not use `is`, `as`, or direct inheritance assumptions between Il2Cpp types.**
Use `.Cast<T>()` when you are certain of the type (throws on failure). Use `.TryCast<T>()` for conditional checks (returns null on failure). Do not assume C# inheritance relationships match native inheritance.

**6. Every Harmony patch postfix must have a try/catch.**
A patch failure must never crash the game. The catch block should call `Log.Error(...)` with the patch class name. See any existing patch in `HUDCustomizer.cs` for the exact pattern.

**7. Every new patch class must be registered in `RegisterPatches()`.**
Use `harmony.PatchAll(typeof(YourPatchClass))` - never `harmony.PatchAll()` without a type argument.

**8. Do not patch `MonoBehaviour.OnEnable`.**
`AccessTools.DeclaredMethod` cannot resolve `OnEnable` on `UnityEngine.MonoBehaviour` in this Il2Cpp build - the method is stripped from the generated interop assembly. Attempting to patch it causes the entire plugin to fail initialization. Use a more specific hook on a known concrete type instead.

**Adding a new colour that comes from UIConfig:**
Use the `TileHighlightEntry` type in `HUDConfig.cs` and the `if (entry.Enabled) uiConfig.Field = ToColor(entry, label)` pattern. Call `TryApply()` from both `OnTacticalReady` and the hot-reload block in `OnUpdate`. See `USSCustomizer.cs` and `UnitCustomizer.ApplyFactionHealthBarColors()` for the two existing patterns.

**Adding a new tint override on a VisualElement:**
Use `TileHighlightEntry` in `HUDConfig.cs` and add a case to `TacticalElementCustomizer`. Call `SetTint(root.Q("ElementName", (string)null), cfg.YourEntry, "label")` - `SetTint` checks `Enabled` and skips silently if false. Register the element in `_registry` via `Register(el, "HudType")` and ensure `TacticalElementCustomizer.Apply(el, hudType)` is called from both the patch postfix and `ReapplyToLiveElements()`.

**Adding a new background colour override on a VisualElement:**
Use the string colour convention (`"R,G,B"` or `"R,G,B,A"`, empty = unchanged) and `TryParseColor` + `ve.style.backgroundColor`. Add the apply logic to `TacticalElementCustomizer` (or `UnitCustomizer` for bar-style elements). Register the element in `_registry` and ensure re-application on hot-reload.

**Discovering an unknown class type responsible for a UI element:**
If the target class is not known, enumerate all types in the relevant namespace at `TacticalReady` using a throwaway mod (`TacticalTypeEnumeratorPlugin` pattern). Once candidate type names are identified, dump their public instance methods using `GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)` - this reveals the correct hook method without guessing. `Update` is a valid last resort but fires before the element has content; prefer a named method that receives the display data (e.g. `Init`, `SetText`, `Show`). The full `Menace.UI.Tactical` type inventory from scan is: `BaseHUD`, `BaseSkillBarItemSlot`, `BleedingWorldSpaceIcon`, `DelayedAbilityHUD`, `DropdownText`, `EntityHUD`, `ISkillBarElement`, `MissionInfoPanel`, `MovementHUD`, `ObjectiveHUD`, `ObjectivesTracker`, `OffmapAbilityButton`, `SelectedUnitPanel`, `SimpleSkillBarButton`, `SimpleWorldSpaceIcon`, `SkillBar`, `SkillBarButton`, `SkillBarSlotAccessory`, `SkillBarSlotWeapon`, `SkillUsesBar`, `StatusEffectIcon`, `StructureHUD`, `TacticalBarkPanel`, `TacticalUnitInfoStat`, `TurnOrderFactionSlot`, `TurnOrderPanel`, `UnitBadge`, `UnitHUD`, `UnitsTurnBar`, `UnitsTurnBarSlot`, `WorldSpaceIcon`.

**Implementing a new font target:**
Add a `FontSettings` property to `HUDCustomizerConfig`. Add a `case "HudType":` to `FontCustomizer.Apply()` and implement `ApplyYourHUD()`. Use `SetFont(el.Q(name, (string)null), Merge(cfg.Global, cfg.YourEntry), label)` for scan-confirmed element names, or `QueryAndSet(el, name, Merge(...), label)` for unconfirmed names. Add entries to `LogFontSummary()`.

**Overriding a value the game sets on a state transition (not every frame):**
Do not patch `OnUpdate` and read back the written value to detect state. Instead, identify the private setter (e.g. `SetOpacity(float _opacity)`) from the game source and patch that directly. Use the method's parameter to detect the game's intent (`_opacity == 0.5f` = spent), and write your override value unconditionally when the parameter matches. For hot-reload, detect currently-affected elements by checking their current inline style against the game's "unaffected" value (e.g. `!Approximately(el.style.opacity.value, 1.0f)`).

**Removing a scan patch that is no longer needed:**
Delete the inner class. Remove its `harmony.PatchAll(typeof(...))` line from `RegisterPatches()`. If the corresponding `Scans` method has no other callers, delete it and its `_scanned` flag from `Scans.cs`.

**Unsure whether a field/element name is correct:**
Check the scan log output in `CONTRIBUTOR_README.md` (Section 10) for confirmed names and RGBA values. If not listed there, it is unconfirmed - use `QueryAndSet` rather than `SetFont(el.Q(...))` so a warning is logged at runtime if the name is wrong.

---

## Runtime behaviour to know

- Hot-reload fires `LoadConfig()` -> `FontCustomizer.InvalidateCache()` -> `ReapplyToLiveElements()` -> `TileCustomizer.TryApply()` -> `USSCustomizer.TryApply()` -> `UnitCustomizer.ApplyFactionHealthBarColors()` -> `UnitCustomizer.ApplyRarityColors()` -> `VisualizerCustomizer.TryApply()` -> `VisualizerCustomizer.TryApplyLineOfSight(Config)` -> `CombatFlyoverCustomizer.Apply(Config.CombatFlyover)`. Any new system that needs hot-reload support must be called in this sequence.
- `ReapplyToLiveElements()` applies systems in this order per live registry element: `UnitCustomizer` (where applicable, i.e. UnitHUD/EntityHUD/StructureHUD) -> `TacticalElementCustomizer` -> `FontCustomizer`. This order must be preserved when adding new cases.
- `TileHighlighter.Exists()` returns false outside tactical scenes. `TileCustomizer.TryApply()` checks this and returns early silently. `USSCustomizer.TryApply()` and `UnitCustomizer.ApplyFactionHealthBarColors()` check `UIConfig.Get() != null` independently. Each `TryApply()` guards its own singleton; none assume the others are available.
- USS colours are additionally applied in the Strategy scene: `OnSceneLoaded` detects `sceneName == "Strategy"` and starts `ApplyUSSAfterDelay(0.5f)`, which calls `USSCustomizer.TryApply()` after a 0.5 second delay. If you add a new USS-like system that should apply outside tactical, follow this pattern.
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
