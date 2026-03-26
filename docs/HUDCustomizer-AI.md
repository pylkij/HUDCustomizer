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
- Rarity colours (6 fields: `ColorCommonRarity/Named`, `ColorUncommonRarity/Named`, `ColorRareRarity/Named`) and `ColorPositionMarkerDelayedAbility`: fully implemented via `UnitCustomizer.ApplyRarityColors()` — non-USS, `FactionHealthBarColors` pattern. Config section: `RarityColors`.
- Tile highlight colours: all 23 slots fully implemented
- USS global theme colours: 28 fields fully implemented — 23 general `[UssColor]` fields plus 5 mission state fields (`ColorMissionPlayable`, `ColorMissionLocked`, `ColorMissionPlayed`, `ColorMissionPlayedArrow`, `ColorMissionUnplayable`) via `USSCustomizer.TryApply()`. Config section: `USSColors`.
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
- TargetAimVisualizer: `_UnlitColor` (InRangeColor), `_EmissiveColor` (InRangeEmissiveColor + EmissiveIntensity), float parameters, and `OutOfRangeColor` — all implemented. `OutOfRangeColor` is applied via `MaterialPropertyBlock` in the `Patch_TargetAimVisualizer_UpdateAim` postfix; `UpdateAim()` does not read the native field for out-of-range rendering (root cause confirmed via Ghidra), so the MPB write is the operative fix. Config slot: `Visualizers.TargetAimVisualizer.OutOfRangeColor`.
- LineOfSightVisualizer line colour (LineColor): implemented — `Il2CppShapes.Line` components via indexed child traversal, `ColorStart`/`ColorEnd`, re-applied on every `Resize(int)` via `LOSResizePatch`. Config slot: `Visualizers.LineOfSight.LineColor`. Verified in log.
- Spent unit HUD opacity (`SpentUnitHUDOpacity`): implemented via `Patch_UnitHUD_SetOpacity`

### What is incomplete — actionable now

These gaps can be implemented without any additional scan or source data. All required information is already confirmed.

**1. ObjectivesTracker progress bar colours — not exposed**
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

### What is incomplete — blocked on scan

These cannot be fully implemented without running the game with `EnableScans: true` to confirm element names.

*(No items currently blocked on scan — all pending scan confirmations have been resolved.)*

**BleedingWorldSpaceIcon element name — ✅ confirmed**
UXML element name confirmed as `TextElement` by scan. `FontCustomizer.ApplyBleedingWorldSpaceIcon()` updated from `QueryAndSet` to `SetFont(el.Q("TextElement", (string)null), ...)`. Complete.

**DropdownText — confirmed, implemented**
Element structure confirmed by scan: `Container > Icon + Label`. `Label` is the text element (fontSize=14, USS class `font-headline`). Hook point: `Init(String _text, Sprite _icon)` — fires once on creation with text already set. Implemented as `Patch_DropdownText_Init` in `HUDCustomizer.cs`, config key `DropdownText` in `HUDCustomizerConfig`. Covers all flyover text shown above units (AP changes, suppression, skill effects such as Taking Command).

**LineOfSightVisualizer — implemented**
Renderer type: `Il2CppShapes.Line` from `Il2CppShapesRuntime.dll` (namespace `Il2CppShapes`, class `Line`). The original scan finding ("no named Shapes bindings") was incorrect — the DLL is present and bindings are generated. An intermediate finding that the components were `UnityEngine.LineRenderer` was also incorrect and retracted.

Pool structure: `List<Line[]> m_Lines`, 3 `Line` entries per group (fade-in, solid, fade-out). Colour is written only in `Resize(int)` via `ColorStart`/`ColorEnd` — never in `Update()` or `SetVisible()`. `GetComponentsInChildren<T>` throws a fatal Il2CppInterop type-initialiser exception for `Il2CppShapes.Line` — use indexed `GetChild(i).GetComponent<Il2CppShapes.Line>()` traversal only.

Fade pattern per group (i % 3): index 0 = fade-in (`ColorStart` alpha=0, `ColorEnd` alpha=A), index 1 = solid (both alpha=A), index 2 = fade-out (`ColorStart` alpha=A, `ColorEnd` alpha=0).

Implementation: `LOSResizePatch` postfixes `Resize(int)` (private — requires `AccessTools.Method`). `VisualizerCustomizer.ApplyLineOfSightColor` applies the fade pattern. `TryApplyLineOfSight` handles `TacticalReady` and hot-reload. `LogLineOfSightSummary` writes a summary line. Config: `Visualizers.LineOfSight.LineColor` (`TileHighlightEntry`). Do not write `ColorMode` — it is inherited from the prefab.

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
UnitCustomizer.LogRarityColorSummary();
VisualizerCustomizer.LogSummary();
VisualizerCustomizer.LogLineOfSightSummary();
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

- Hot-reload fires `LoadConfig()` → `FontCustomizer.InvalidateCache()` → `ReapplyToLiveElements()` → `TileCustomizer.TryApply()` → `USSCustomizer.TryApply()` → `UnitCustomizer.ApplyFactionHealthBarColors()` → `UnitCustomizer.ApplyRarityColors()` → `VisualizerCustomizer.TryApply()` → `VisualizerCustomizer.TryApplyLineOfSight(Config)` → `CombatFlyoverCustomizer.Apply(Config.CombatFlyover)`. Any new system that needs hot-reload support must be called in this sequence.
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
