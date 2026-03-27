# Tactical HUD Customizer - Contributor & Maintainer Reference

Tactical HUD Customizer is a MelonLoader mod for **MENACE** (Il2Cpp, Unity 6, .NET 6) that lets players customise the tactical HUD via a JSON config file. It supports hot-reload at runtime.

This document is written for contributors and AI agents working on the codebase. It covers architecture, file responsibilities, the patching strategy, and how to add new features. End-user documentation lives in the JSON config file itself as inline comments.

---

## Table of Contents

1. [Repository layout](#1-repository-layout)
2. [Architecture overview](#2-architecture-overview)
3. [System responsibilities](#3-system-responsibilities)
4. [Patching strategy](#4-patching-strategy)
5. [Config system](#5-config-system)
6. [Hot-reload lifecycle](#6-hot-reload-lifecycle)
7. [The scan system](#7-the-scan-system)
8. [Il2Cpp and UIToolkit notes](#8-il2cpp-and-uitoolkit-notes)
9. [Adding a new customisable system](#9-adding-a-new-customisable-system)
10. [Runtime scan status](#10-runtime-scan-status)

---

## 1. Repository layout

| File | Responsibility |
|---|---|
| `HUDCustomizer.cs` | Plugin entry point, lifecycle (`OnInitialize`, `OnTacticalReady`, `OnUpdate`), live element registry, Harmony patch declarations, shared helpers (`TryParseColor`, `ToColor`, `GetClasses`, `Debug`) |
| `HUDConfig.cs` | All config data classes (`HUDCustomizerConfig`, `FontSettings`, `TileHighlightEntry`, `VisualizersConfig`, and all sub-configs), plus the `HUDConfig` static class which owns `ConfigDir`, `ConfigPath`, `JsonOpts`, and `BuildDefaultConfig()` |
| `UnitCustomizer.cs` | Scale/transform-origin application to UnitHUD and EntityHUD elements; bar fill/preview/track colour overrides; badge tint; faction health bar colours via UIConfig; rarity colours (`ColorCommonRarity/Named`, `ColorUncommonRarity/Named`, `ColorRareRarity/Named`) and `ColorPositionMarkerDelayedAbility` via UIConfig |
| `FontCustomizer.cs` | Font asset cache; per-HUD font/size/colour application via UIToolkit `Q()` queries; `Merge()` (Global to per-element override resolution) |
| `TileCustomizer.cs` | Tile highlight colour overrides via `TileHighlighter.SetColorOverrides()` |
| `USSCustomizer.cs` | USS global theme colour overrides via `UIConfig.Get()` fields - 23 general USS fields plus 5 mission state fields (`ColorMissionPlayable/Locked/Played/PlayedArrow/Unplayable`) |
| `VisualizerCustomizer.cs` | Colour and parameter overrides for world-space 3D visualizers (`MovementVisualizer`, `TargetAimVisualizer`, `LineOfSightVisualizer`). Uses `FindObjectsOfType` and `MaterialPropertyBlock` rather than the UIElement registry - these are MonoBehaviours, not InterfaceElements. |
| `TacticalElementCustomizer.cs` | Non-font tint and style overrides for tactical UI elements introduced in Tier 2-4 (`SkillBarButton`, `BaseSkillBarItemSlot`, `SimpleSkillBarButton`, `TurnOrderFactionSlot`, `UnitsTurnBarSlot`, `SelectedUnitPanel`, `TacticalUnitInfoStat`, `DelayedAbilityHUD`, and the `ObjectivesTracker` progress bar). Dispatches on `hudType` string like `FontCustomizer`. Uses `TileHighlightEntry` for tints and string colours for background overrides. |
| `CombatFlyoverCustomizer.cs` | Bridge between HUDCustomizer's config system and the CombatFlyoverText plugin. Receives `CombatFlyoverSettings` from `LoadConfig()` and the hot-reload path via `Apply()`; exposes values to `CombatFlyoverPlugin` via public accessors; logs a summary line via `LogSummary()`. Has no Unity or game dependencies - pure config bridge. |
| `Scans.cs` | Development-only discovery scans (element trees, font assets, UIConfig colour values, material properties). All scans are gated on `EnableScans = true` in config and fire at most once per session. |

`HUDCustomizer.cs` is a `partial class` split across two declaration blocks in the same file: the first block contains the plugin body and helpers; the second contains all Harmony patch inner classes. Both blocks are `public partial class HUDCustomizerPlugin`.

---

## 2. Architecture overview

```
HUDCustomizerPlugin (entry point)
|
+-- OnInitialize
|     +-- RestoreConfigFromUserData()  <- restores UserData backup if mod-dir config missing
|     +-- LoadConfig()                 <- reads HUDCustomizer.json via HUDConfig
|     +-- GameState.TacticalReady += OnTacticalReady
|     +-- RegisterPatches()            <- registers all Harmony postfixes
|
+-- OnSceneLoaded
|     +-- [sceneName == "Strategy"] -> MelonCoroutines.Start(ApplyUSSAfterDelay(0.5f))
|           ApplyUSSAfterDelay: waits 0.5 s then calls USSCustomizer.TryApply()
|
+-- OnTacticalReady
|     +-- FontCustomizer.OnTacticalReady()   <- builds font asset cache
|     +-- TileCustomizer.TryApply()          <- singleton-based, no patch needed
|     +-- USSCustomizer.TryApply()           <- singleton-based, no patch needed
|     +-- UnitCustomizer.ApplyFactionHealthBarColors()
|     +-- UnitCustomizer.ApplyRarityColors()
|     +-- VisualizerCustomizer.TryApply()    <- FindObjectsOfType-based
|     +-- VisualizerCustomizer.TryApplyLineOfSight(Config)
|     +-- Scans.RunUIConfigScan()            <- dev only, gated on EnableScans
|
+-- OnUpdate (per frame)
|     +-- [ReloadKey pressed] -> LoadConfig, InvalidateCache,
|                               ReapplyToLiveElements, TryApply x4,
|                               ApplyRarityColors, TryApplyLineOfSight,
|                               CombatFlyoverCustomizer.Apply
|
+-- Harmony Patches (postfixes)
      +-- Each UIElement patch: Cast -> TacticalElementCustomizer.Apply /
      |                                  FontCustomizer.Apply -> Register
      +-- Each MonoBehaviour patch: apply directly to __instance fields / MaterialPropertyBlock
```

### Three patching strategies

**Harmony postfix patches on UIElements** are used for HUD elements that are instantiated per-unit or per-objective. A postfix fires after the game's own init method, receives `__instance`, casts it to `Il2CppInterfaceElement`, applies customisations, and registers the element in the live registry for hot-reload. Used for: `UnitHUD`, `EntityHUD`, `ObjectiveHUD`, `ObjectivesTracker`, `MissionInfoPanel`, `MovementHUD`, `BleedingWorldSpaceIcon`.

**Direct singleton calls** are used for systems that operate on a single shared instance. These are called from `OnTacticalReady` and from the hot-reload path in `OnUpdate`. No patch needed because there is only one object to update. Used for: `TileHighlighter` (tile highlight colours) and `UIConfig` (USS theme colours, faction health bar colours).

**Harmony postfix patches on MonoBehaviours + `FindObjectsOfType`** are used for world-space 3D visualizers that are not UIElements and cannot use the registry pattern. `VisualizerCustomizer.TryApply()` uses `Object.FindObjectsOfType<T>()` to locate all live instances and applies colours directly to component fields or via `MaterialPropertyBlock`. Patch postfixes re-apply material colours after methods that rebuild the mesh (e.g. `UpdateAim`), since mesh rebuilds may reset renderer state. Used for: `MovementVisualizer`, `TargetAimVisualizer`.

---

## 3. System responsibilities

### HUDCustomizerPlugin (`HUDCustomizer.cs`)

The plugin is the only class that implements `IModpackPlugin`. It owns:

- **`Config`** (`HUDCustomizerConfig`) - the single static config instance. All other classes read from this via `HUDCustomizerPlugin.Config`.
- **`_registry`** - a `Dictionary<IntPtr, (Il2CppInterfaceElement el, string hudType)>` keyed by native object pointer. Every patched UIElement is registered here so `ReapplyToLiveElements()` can re-apply all customisations on hot-reload without needing to intercept game calls again. Stale entries (pointer == `IntPtr.Zero`) are pruned on each reload. Note: visualizer MonoBehaviours are **not** registered here - they are re-discovered via `FindObjectsOfType` on each `TryApply()` call.
- **Shared helpers** - `TryParseColor`, `ToColor`, `GetClasses` (see Section 8), `Debug`.

#### ReapplyToLiveElements() - full source

This is the hot-reload re-application loop. When adding a new HUD type that needs scale or colour customisation beyond fonts, add a `case` to the switch. The `default` branch calls only `FontCustomizer.Apply()` - it does not call `UnitCustomizer.Apply()` because most HUD types do not have the bar/badge structure that `UnitCustomizer` expects.

```csharp
internal static void ReapplyToLiveElements()
{
    int count = 0;
    var dead = new List<IntPtr>();
    foreach (var kvp in _registry)
    {
        var (el, hudType) = kvp.Value;
        try
        {
            if (el.Pointer == IntPtr.Zero) { dead.Add(kvp.Key); continue; }

            switch (hudType)
            {
                case "UnitHUD":
                    UnitCustomizer.Apply(el, Config.UnitHUDScale);
                    // Re-apply spent opacity immediately on hot-reload; SetOpacity
                    // is transition-only so the patch won't fire until the next turn change.
                    if (Config.SpentUnitHUDOpacity >= 0f &&
                        !Mathf.Approximately(el.style.opacity.value, 1.0f))
                    {
                        el.style.opacity = new StyleFloat(
                            Mathf.Clamp(Config.SpentUnitHUDOpacity, 0f, 1f));
                    }
                    break;
                case "EntityHUD":
                    UnitCustomizer.Apply(el, Config.EntityHUDScale); break;
                case "StructureHUD":
                    UnitCustomizer.Apply(el, Config.StructureHUDScale); break;
                case "DropdownText":
                    // No bar or badge -- FontCustomizer handles everything.
                    break;
                default:
                    // Non-unit HUD types (ObjectivesTracker, MissionInfoPanel,
                    // ObjectiveHUD, etc.) have no bars or badge -- UnitCustomizer
                    // has nothing to apply to them.
                    break;
            }

            TacticalElementCustomizer.Apply(el, hudType);
            FontCustomizer.Apply(el, hudType);
            count++;
        }
        catch { dead.Add(kvp.Key); }
    }
    foreach (var ptr in dead) _registry.Remove(ptr);
    Log.Msg($"[HUDCustomizer] Hot-reload: {count} live element(s) updated ({dead.Count} stale removed).");
}
```

#### RegisterPatches() - full source

Every new patch class must be added here. `harmony.PatchAll(typeof(T))` registers all `[HarmonyPatch]`-attributed methods within that specific type - do not use `harmony.PatchAll()` without a type argument.

```csharp
private void RegisterPatches(HarmonyLib.Harmony harmony)
{
    harmony.PatchAll(typeof(Patch_UnitHUD_SetActor));
    harmony.PatchAll(typeof(Patch_UnitHUD_Show));
    harmony.PatchAll(typeof(Patch_EntityHUD_InitBars));
    harmony.PatchAll(typeof(Patch_ObjectiveHUD_SetObjective));
    harmony.PatchAll(typeof(Patch_ObjectivesTracker_Init));
    harmony.PatchAll(typeof(Patch_MissionInfoPanel_Init));
    harmony.PatchAll(typeof(Patch_UnitHUD_SetOpacity));
    harmony.PatchAll(typeof(Patch_MovementHUD_SetDestination));
    harmony.PatchAll(typeof(Patch_BleedingWorldSpaceIcon_SetText));
    harmony.PatchAll(typeof(Patch_MovementVisualizer_ShowPath));
    harmony.PatchAll(typeof(Patch_TargetAimVisualizer_UpdateAim));
    harmony.PatchAll(typeof(LOSResizePatch));
    harmony.PatchAll(typeof(Patch_DropdownText_Init));
    harmony.PatchAll(typeof(Patch_StructureHUD_Init));
    // Permanent tactical routing patches
    harmony.PatchAll(typeof(Patch_SkillBarButton_Init));
    harmony.PatchAll(typeof(Patch_SkillBarButton_Show));
    harmony.PatchAll(typeof(Patch_BaseSkillBarItemSlot_Init));
    harmony.PatchAll(typeof(Patch_SimpleSkillBarButton_SetText));
    harmony.PatchAll(typeof(Patch_TurnOrderFactionSlot_Init));
    harmony.PatchAll(typeof(Patch_UnitsTurnBarSlot_Init));
    harmony.PatchAll(typeof(Patch_UnitsTurnBarSlot_SetActor));
    harmony.PatchAll(typeof(Patch_SelectedUnitPanel_SetActor));
    harmony.PatchAll(typeof(Patch_TacticalUnitInfoStat_Init));
    harmony.PatchAll(typeof(Patch_TurnOrderPanel_UpdateFactions));
    harmony.PatchAll(typeof(Patch_StatusEffectIcon_InitSkillTemplate));
    harmony.PatchAll(typeof(Patch_StatusEffectIcon_InitSkill));
    harmony.PatchAll(typeof(Patch_DelayedAbilityHUD_SetAbility));
    harmony.PatchAll(typeof(Patch_DelayedAbilityHUD_SetProgressPct));
    Debug("Harmony patches registered.");
}
```

#### LoadConfig() summary call sequence

When adding a new customiser, add its `LogSummary()` call at the end of this block:

```csharp
Log.Msg($"Config loaded (v{Config.ConfigVersion}).  " +
        $"UnitScale={Config.UnitHUDScale:F2}  " +
        $"EntityScale={Config.EntityHUDScale:F2}  " +
        $"Origin=({Config.TransformOriginX:F0}%,{Config.TransformOriginY:F0}%)  " +
        $"ReloadKey={_reloadKey}");

UnitCustomizer.LogColourSummary();
FontCustomizer.LogFontSummary();
TileCustomizer.LogSummary();
USSCustomizer.LogSummary();
UnitCustomizer.LogFactionHealthBarSummary();
UnitCustomizer.LogRarityColorSummary();
TacticalElementCustomizer.LogSummary();
VisualizerCustomizer.LogSummary();
VisualizerCustomizer.LogLineOfSightSummary();
LogSpentOpacitySummary();
CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
CombatFlyoverCustomizer.LogSummary();
// insert YourCustomiser.LogSummary() here
```

`CombatFlyoverCustomizer.Apply()` is called here (as well as in the `OnUpdate` hot-reload block) because `LoadConfig()` also runs at startup where `OnUpdate` does not. Both call sites are required - removing either breaks one of the two paths.

---

### HUDConfig (`HUDConfig.cs`)

A pure-data file with no Unity or game dependencies beyond `System.IO` and `System.Text.Json`. It defines all config POCO classes and the `HUDConfig` static class:

```csharp
public static class HUDConfig
{
    public static readonly string ConfigDir  = Path.Combine("Mods", "HUDCustomizer");
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "HUDCustomizer.json");

    public static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    public static string BuildDefaultConfig() { ... }
}
```

`HUDCustomizerPlugin` exposes these via forwarding properties so call sites within the plugin body are unchanged:

```csharp
internal static string                ConfigDir  => HUDConfig.ConfigDir;
internal static string                ConfigPath => HUDConfig.ConfigPath;
internal static JsonSerializerOptions JsonOpts   => HUDConfig.JsonOpts;
```

`BuildDefaultConfig()` returns the full annotated JSON string as a single C# verbatim literal (`@"..."`). Do not split it - the file must be written atomically as one `File.WriteAllText` call. Quotes inside the verbatim literal are escaped as `""`.

`TileHighlightEntry` is the shared data class for all `Enabled/R/G/B/A` colour slots. Despite the name it is reused for USS colours (`USSColorsConfig`), faction health bars (`FactionHealthBarColorsConfig`), and visualizer colours (`MovementVisualizerConfig`, `TargetAimVisualizerConfig`). This is design debt - if refactored, these can be renamed or split without behaviour change.

`CombatFlyoverSettings` holds the config shape for the CombatFlyoverText integration: `Enabled`, three hex colour strings (`ColourHPDamage`, `ColourArmourDamage`, `ColourAccuracy`), and two floats (`ExtraDisplaySeconds`, `FadeDurationScale`). It is consumed exclusively by `CombatFlyoverCustomizer` - nothing else in HUDCustomizer reads it directly. When adding or changing a field here, the matching entry in `BuildDefaultConfig()` and the corresponding accessor in `CombatFlyoverCustomizer.cs` must be updated in the same change.

---

### UnitCustomizer (`UnitCustomizer.cs`)

Applies customisations to individual `UnitHUD` and `EntityHUD` elements. Called from patch postfixes and from `ReapplyToLiveElements()`.

#### Confirmed element tree (from scan log)

The full tree as observed at runtime. RGBA values are game defaults with no overrides active.

```
root (InterfaceElement)
  [0] Pickable                                    bg=(0,0,0,0)
        [0] Icons
              [0] Objective       classes=[unit-hud-icon]
              [1] Hidden          classes=[unit-hud-icon]
              [2] TargetPainted   classes=[unit-hud-icon]
              [3] Isolated        classes=[unit-hud-icon]
              [4] SkillCondition  classes=[unit-hud-icon]
        [1] (unnamed)             classes=[fill-parent]  tint=(0.5,0.5,0.5,1)
        [2] (unnamed)
  [1] Container                                   bg=(0,0,0,0)
        [0] DetailsContainer
              [0] Bars
                    [0] Suppression
                          [0] Pickable            bg=(0.2,0.2,0.2,0.7)  <- track bg
                                [0] Label         classes=[unity-text-element unity-label text]  fontSize=14
                                [1] Fill          bg=(0.804,0.718,0.420,1)  <- suppression fill default
                                [2] PreviewFill   bg=(0.953,0.851,0.497,1)
                                [3] Border
                                [4] DarkLabelClip
                    [1] Armor
                          [0] Pickable            bg=(0,0,0,0.333)      <- track bg
                                [0] Label         fontSize=14
                                [1] Fill          bg=(0.453,0.453,0.453,1)
                                [2] PreviewFill   bg=(0.349,0.349,0.349,1)
                                [3] Border
                                [4] DarkLabelClip
                    [2] Hitpoints
                          [0] Pickable            bg=(0,0,0,0.333)      <- track bg
                                [0] Label         fontSize=14
                                [1] Fill          bg=(0.569,0.612,0.392,1)
                                [2] PreviewFill   bg=(0.731,0.887,0.414,1)
                                [3] Border
                                [4] DarkLabelClip
        [1] ContainedBadge  classes=[unit-hud-icon]  tint=(1,1,1,1)    <- badge tint
```

`Border` and `DarkLabelClip` at positions [3] and [4] are not currently targeted by `UnitCustomizer`. They exist but are transparent in defaults.

Bar colours use `style.backgroundColor`. Badge tint uses `style.unityBackgroundImageTintColor`. Both are inline overrides that take precedence over USS class styles.

#### Bar colour queries - full source

`barName` is one of `"Hitpoints"`, `"Armor"`, `"Suppression"` - the confirmed UXML element names:

```csharp
private static void ApplyBarColours(Il2CppInterfaceElement root,
                                    string barName,
                                    string fillColor,
                                    string previewColor,
                                    string trackColor)
{
    if (string.IsNullOrEmpty(fillColor) &&
        string.IsNullOrEmpty(previewColor) &&
        string.IsNullOrEmpty(trackColor))
        return;

    var barVe     = root.Q(barName,      (string)null);
    var trackVe   = barVe?.Q("Pickable", (string)null);
    var fillVe    = trackVe?.Q("Fill",        (string)null);
    var previewVe = trackVe?.Q("PreviewFill", (string)null);

    if (barVe == null)
    {
        HUDCustomizerPlugin.Log.Warning(
            $"[UnitCustomizer] ApplyBarColours: '{barName}' not found");
        return;
    }

    SetBackground(trackVe,   trackColor,   $"{barName}.Pickable.bg");
    SetBackground(fillVe,    fillColor,    $"{barName}.Fill.bg");
    SetBackground(previewVe, previewColor, $"{barName}.PreviewFill.bg");
}
```

#### Faction health bar colours

Set on `UIConfig.Get()`, not per-element. Controls the infobox health bar shown when a unit is selected - not the floating entity HUD bars above units. The two systems are independent.

```csharp
public static void ApplyFactionHealthBarColors()
{
    var uiConfig = Il2CppMenace.UI.UIConfig.Get();
    var f = HUDCustomizerPlugin.Config.FactionHealthBarColors;

    if (f.HealthBarFillColorPlayerUnits.Enabled)
        uiConfig.HealthBarFillColorPlayerUnits =
            HUDCustomizerPlugin.ToColor(f.HealthBarFillColorPlayerUnits, "HealthBarFillColorPlayerUnits");
    // ... same pattern for all 8 slots:
    // HealthBarPreviewColorPlayerUnits, HealthBarFillColorAllies, HealthBarPreviewColorAllies,
    // HealthBarFillColorEnemies, HealthBarPreviewColorEnemies,
    // HealthBarSectionColorPlayerUnits, HealthBarSectionColorEnemies
}
```

#### Rarity colours and ColorPositionMarkerDelayedAbility

Rarity fields (`ColorCommonRarity` etc.) and `ColorPositionMarkerDelayedAbility` do not carry the `[UssColor]` attribute - confirmed from `dump.cs`. They are direct colour values used by specific systems and do not propagate through the USS property system. These are implemented in `UnitCustomizer.ApplyRarityColors()` via the same `FactionHealthBarColors` pattern (direct field write on `UIConfig.Get()`). Config class: `RarityColorsConfig` (6 rarity slots + `ColorPositionMarkerDelayedAbility`). Log summary: `UnitCustomizer.LogRarityColorSummary()`.

**ObjectiveHUD and MissionInfoPanel** are text-only elements confirmed from source and scan. `ObjectiveHUD` has no bars, backgrounds, or tintable elements beyond icon sprites (which use USS class colouring, not inline style tints). `MissionInfoPanel` is two label elements inside a plain container. The font-only implementation for both is correct and complete unless icon tinting or background colours are explicitly desired in the future.

**ObjectivesTracker progress bar** (`ProgressBar > Pickable > Fill/PreviewFill`) is configurable via `ObjectivesTrackerProgressBar` in config and applied through `TacticalElementCustomizer.ApplyObjectivesTrackerProgressBar(...)`. Applied from `Patch_ObjectivesTracker_Init` and re-applied from `ReapplyToLiveElements()`.

**StructureHUD** inherits `EntityHUD`. It has its own dedicated patch (`Patch_StructureHUD_Init` on `StructureHUD.Init`) and its own `StructureHUDScale` config entry, applied via `UnitCustomizer.Apply(el, Config.StructureHUDScale)`. Bar colours and fonts from the `EntityHUD` pattern apply automatically. The `Patch_EntityHUD_InitBars` guard (`TryCast<UnitHUD>`) does not exclude `StructureHUD`, so if `StructureHUD` also inherits `InitBars`, verify in-game whether both patches fire to avoid double-application.

---

### TacticalElementCustomizer (`TacticalElementCustomizer.cs`)

Handles non-font tint and style overrides for the tactical UI element types introduced in Tier 2-4. Called from patch postfixes and from `ReapplyToLiveElements()` — always in the order `UnitCustomizer` → `TacticalElementCustomizer` → `FontCustomizer`.

Dispatches on `hudType` using a switch, identical in structure to `FontCustomizer.Apply()`. When adding a new tactical element type that needs tint or background overrides, add a `case` here and implement the corresponding private `ApplyYourType()` method.

Uses two colour conventions:
- **`TileHighlightEntry` tints** (via `SetTint`) for `style.unityBackgroundImageTintColor` — icon tints, overlay tints. Caller checks `entry.Enabled`; `SetTint` skips disabled entries silently.
- **String background colours** (via `SetBackground`) for `style.backgroundColor` — progress bar tracks, fills. Uses `TryParseColor`; empty string = leave unchanged.

**Handled types and their config paths:**

| hudType | Config path | Applied fields |
|---|---|---|
| `SkillBarButton` | `TacticalUIStyles.SkillBarButton` | `SkillIcon` tint, `SelectedOverlay` tint, `HoverOverlay` tint, `PreviewOpacity` (float, -1 = unchanged) |
| `BaseSkillBarItemSlot` | `TacticalUIStyles.BaseSkillBarItemSlot` | `Background` tint, `ItemIcon` tint, `Cross` tint |
| `SimpleSkillBarButton` | `TacticalUIStyles.SimpleSkillBarButton` | `Hover` tint |
| `TurnOrderFactionSlot` | `TacticalUIStyles.TurnOrderFactionSlot` | `InactiveMask` tint, `Selected` tint, `InactiveIcon` tint |
| `UnitsTurnBarSlot` | `TacticalUIStyles.UnitsTurnBarSlot` | `Overlay` tint, `Selected` tint, `Portrait` tint |
| `SelectedUnitPanel` | `TacticalUIStyles.SelectedUnitPanel` | `Portrait` tint, `UnitWindowHeader` tint |
| `TacticalUnitInfoStat` | `TacticalUIStyles.TacticalUnitInfoStat` | `Icon` tint |
| `DelayedAbilityHUD` | `TacticalUIStyles.DelayedAbilityHUD` | `Progress` tint |
| `ObjectivesTracker` | `ObjectivesTrackerProgressBar` | `ProgressBar > Pickable` bg, `Fill` bg, `PreviewFill` bg |

`TurnOrderPanel` and `StatusEffectIcon` route through `TacticalElementCustomizer.Apply()` but have no cases currently — they fall through to the default (no-op). Font-only for now.

`LogSummary()` counts all enabled overrides across all types and logs a single summary line.

---

### FontCustomizer (`FontCustomizer.cs`)

Font assets are loaded once at `TacticalReady` via `Resources.FindObjectsOfTypeAll<Font>()` into `_fontCache`. The cache is invalidated and rebuilt on hot-reload.

#### Apply() dispatch switch - full source

`Apply(element, hudType)` dispatches on the `hudType` string passed explicitly from each patch postfix. When adding a new HUD type, add a `case` here and implement the corresponding `ApplyYourHUD()` method:

```csharp
public static void Apply(Il2CppInterfaceElement element, string hudType)
{
    if (element == null) return;
    if (!_cacheValid) BuildFontCache();

    switch (hudType)
    {
        case "UnitHUD":
        case "EntityHUD":
            ApplyUnitHUD(element);
            break;
        case "ObjectivesTracker":
            ApplyObjectivesTracker(element);
            break;
        case "MissionInfoPanel":
            ApplyMissionInfoPanel(element);
            break;
        case "ObjectiveHUD":
            ApplyObjectiveHUD(element);
            break;
        case "MovementHUD":
            ApplyMovementHUD(element);
            break;
        case "BleedingWorldSpaceIcon":
            ApplyBleedingWorldSpaceIcon(element);
            break;
        default:
            ApplyGlobalFallback(element);
            break;
    }
}
```

#### Merge() - override resolution - full source

`Merge(global, specific)` is always called as `Merge(cfg.Global, cfg.PerElementEntry)`. A value is "unset" if `Font == ""`, `Size <= 0`, or `Color == ""`:

```csharp
private static (Font font, float size, string color) Merge(FontSettings global, FontSettings specific)
{
    Font   font  = Resolve(specific) ?? Resolve(global);
    float  size  = (specific?.Size  > 0f)                 ? specific.Size
                 : (global?.Size    > 0f)                 ? global.Size  : 0f;
    string color = !string.IsNullOrEmpty(specific?.Color) ? specific.Color
                 : !string.IsNullOrEmpty(global?.Color)   ? global.Color : "";
    return (font, size, color);
}
```

#### SetFont vs QueryAndSet

Use `SetFont(el.Q(name, (string)null), settings, label)` for element names **confirmed by scan**.

Use `QueryAndSet(el, name, settings, label)` for names **not yet scan-confirmed**. `QueryAndSet` logs a warning if the element is not found and settings were non-empty:

```csharp
private static void QueryAndSet(VisualElement root, string elementName,
                                (Font font, float size, string color) settings,
                                string debugLabel)
{
    var ve = root.Q(elementName, (string)null);
    if (ve == null)
    {
        if (settings.font != null || settings.size > 0f || !string.IsNullOrEmpty(settings.color))
            HUDCustomizerPlugin.Log.Warning(
                $"[FontCustomizer] QueryAndSet: element '{elementName}' not found " +
                $"for '{debugLabel}' -- settings will not be applied. " +
                $"Check element scan log for the correct name.");
        return;
    }
    SetFont(ve, settings, debugLabel);
}
```

#### Confirmed element names per HUD (source + scan verified)

| HUD type | Element name | Config field | Source field | fontSize default | Confirmation |
|---|---|---|---|---|---|
| UnitHUD / EntityHUD | `Label` | `UnitBarLabel` | - | 14 | Scan |
| UnitHUD / EntityHUD | `DarkLabel` | `UnitBarLabel` | - | 14 | Scan |
| ObjectivesTracker | `ObjectivesTrackerHeadline` | `ObjTrackerHeadline` | - | 12 | Scan |
| ObjectivesTracker | `SecondaryObjectivesHeadline` | `ObjSecondaryHeadline` | - | 12 | Scan |
| ObjectivesTracker | `RewardPoints` | `ObjRewardPoints` | `m_RewardPointsLabel` | 16 | Scan + source |
| ObjectivesTracker | `Label` | `ObjTrackerLabel` | - | 14 | Scan |
| ObjectivesTracker | `DarkLabel` | `ObjTrackerLabel` | - | 14 | Scan |
| ObjectivesTracker | `Points` | `ObjTrackerPoints` | - | 16 | Scan (SetFontAll - repeats per entry) |
| ObjectivesTracker | `Description` | `ObjTrackerDescription` | - | 10 | Scan (SetFontAll - repeats per entry) |
| MissionInfoPanel | `MissionName` | `MissionName` | `m_MissionNameLabel` | 12 | Scan + source |
| MissionInfoPanel | `MissionDuration` | `MissionDuration` | `m_MissionDurationLabel` | 12 | Scan + source |
| ObjectiveHUD | `ObjectiveName` | `ObjectiveNameLabel` | `m_NameLabel` | 13 | Scan + source |
| ObjectiveHUD | `ObjectiveState` | `ObjectiveStateLabel` | `m_StateLabel` | 14 | Scan + source |
| ObjectiveHUD | `Line` | *(not exposed)* | - | 14 | Scan only - separator line, not currently configurable |
| MovementHUD | `CostLabel` | `MovementCostLabel` | - | 16 | Scan |
| MovementHUD | `ActionLabel` | `MovementActionLabel` | - | 14 | Scan |
| BleedingWorldSpaceIcon | `TextElement` | `BleedingIconText` | `m_TextElement` (Label) | - | Scan confirmed - `QueryAndSet` replaced with `SetFont(el.Q("TextElement", (string)null), ...)` |

**Note on `ObjectiveName` fontSize:** The scan shows 13, not 14. This differs from the config comment which says the default matches `ObjectiveState` (14). Do not change the config default - the scan value is authoritative.

**Note on source field name vs UXML name:** UIToolkit UXML element names are set in the `.uxml` file, not derived from the C# field name. The `m_` prefix is stripped and camelCase becomes PascalCase: `m_NameLabel` → `ObjectiveName`, `m_MissionDurationLabel` → `MissionDuration`. The scan confirms these translations. For `BleedingWorldSpaceIcon.m_TextElement`, scan confirmed the UXML name is `TextElement` - consistent with this pattern.

---

### TileCustomizer (`TileCustomizer.cs`)

Reads `TileHighlighter.Instance()` (singleton, tactical scenes only) and calls `SetColorOverrides()`. Safe to call when the singleton does not exist - `TryApply()` returns early via `TileHighlighter.Exists()`.

Each slot in `TileHighlightColorOverrides` has an `Enabled` flag. Setting `Enabled = false` tells the game to ignore the slot and use its own default - it does not zero the colour value. The `Resolve()` helper manages this:

```csharp
private static Il2CppColorOverride Resolve(Il2CppColorOverride existing,
                                           TileHighlightEntry  entry,
                                           string              label)
{
    if (!entry.Enabled)
    {
        return new Il2CppColorOverride { Enabled = false, Color = existing.Color };
    }
    var color = new Color(entry.R / 255f, entry.G / 255f, entry.B / 255f, entry.A);
    return new Il2CppColorOverride { Enabled = true, Color = color };
}
```

---

### USSCustomizer (`USSCustomizer.cs`)

Sets `Color` fields directly on `UIConfig.Get()`. These fields carry the `[UssColor]` attribute in the game source, meaning they map to USS custom properties that affect **all UI screens game-wide**. Fields without `[UssColor]` (rarity colours, health bar colours, `ColorPositionMarkerDelayedAbility`) are direct colour values used by specific systems - they can still be set via `UIConfig` but do not propagate through the USS property system.

The five mission state colour fields (`ColorMissionPlayable` etc.) carry `[UssColor]` - confirmed from `dump.cs`. These are implemented via `USSCustomizer.TryApply()` alongside the existing 23 USS fields. Config class: `USSColorsConfig` (extended with 5 mission state slots).

**UIConfig field inventory with confirmed game defaults** (from scan log):

| Field | `[UssColor]` | Default (RGB) |
|---|---|---|
| `ColorNormal` | Yes | 225, 225, 225 |
| `ColorBright` | Yes | 255, 214, 127 |
| `ColorNormalTransparent` | Yes | 225, 225, 225, A=0.07 |
| `ColorInteract` | Yes | 187, 175, 149 |
| `ColorInteractDark` | Yes | 122, 115, 98 |
| `ColorInteractHover` | Yes | 238, 226, 189 |
| `ColorInteractSelected` | Yes | 215, 192, 116 |
| `ColorInteractSelectedText` | Yes | 0, 0, 0 |
| `ColorDisabled` | Yes | 72, 72, 72 |
| `ColorDisabledHover` | Yes | 199, 199, 199 |
| `ColorTooltipBetter` | Yes | 0, 184, 0 |
| `ColorTooltipWorse` | Yes | 229, 0, 0 |
| `ColorTooltipNormal` | Yes | 225, 225, 225 |
| `ColorPositive` | Yes | 72, 191, 147 |
| `ColorNegative` | Yes | 180, 67, 65 |
| `ColorWarning` | Yes | 255, 50, 50 |
| `ColorDarkBg` | Yes | 22, 25, 24 |
| `ColorWindowCorner` | Yes | 233, 212, 111 |
| `ColorTopBar` | Yes | 225, 225, 225 |
| `ColorTopBarDark` | Yes | 160, 171, 163 |
| `ColorProgressBarNormal` | Yes | 225, 225, 225 |
| `ColorProgressBarBright` | Yes | 232, 205, 124 |
| `ColorEmptySlotIcon` | Yes | 65, 86, 90 |
| `ColorCommonRarity` | No | 116, 108, 75 |
| `ColorCommonRarityNamed` | No | 216, 232, 203 |
| `ColorUncommonRarity` | No | 61, 117, 136 |
| `ColorUncommonRarityNamed` | No | 185, 208, 214 |
| `ColorRareRarity` | No | 189, 49, 49 |
| `ColorRareRarityNamed` | No | 252, 241, 240 |
| `HealthBarFillColorPlayerUnits` | No | 145, 156, 100 |
| `HealthBarPreviewColorPlayerUnits` | No | 186, 226, 105 |
| `HealthBarFillColorAllies` | No | 138, 151, 161 |
| `HealthBarPreviewColorAllies` | No | 184, 199, 211 |
| `HealthBarFillColorEnemies` | No | 204, 104, 106 |
| `HealthBarPreviewColorEnemies` | No | 240, 75, 75 |
| `HealthBarSectionColorPlayerUnits` | No | 95, 114, 35 |
| `HealthBarSectionColorEnemies` | No | 172, 44, 45 |
| `ColorMissionPlayable` | Yes | 168, 152, 103 |
| `ColorMissionLocked` | Yes | 168, 152, 103 |
| `ColorMissionPlayed` | Yes | 113, 102, 69 |
| `ColorMissionPlayedArrow` | Yes | 75, 67, 44, A=0.50 |
| `ColorMissionUnplayable` | Yes | 115, 115, 115 |
| `ColorPositionMarkerDelayedAbility` | No | 0, 255, 255 |

---

### VisualizerCustomizer (`VisualizerCustomizer.cs`)

Applies colour and parameter overrides to world-space 3D visualizer MonoBehaviours. These are not UIElements - they cannot use the `_registry` pattern and are not patched at init time. Instead, `TryApply()` uses `Object.FindObjectsOfType<T>()` to locate all live instances on each call. This is safe because these visualizers are scene-level singletons in practice.

#### MovementVisualizer - confirmed working

Fields set directly on the component instance:

| Field | Type | Config slot | Notes |
|---|---|---|---|
| `ReachableColor` | `Color` | `MovementVisualizer.ReachableColor` | Polyline and dot colour for in-range path |
| `UnreachableColor` | `Color` | `MovementVisualizer.UnreachableColor` | Polyline colour for out-of-range path |

Re-applied in `Patch_MovementVisualizer_ShowPath` postfix, since the game sets these from its own state when `ShowPath()` is called.

#### TargetAimVisualizer - confirmed working (partial)

The aim line mesh is rebuilt every call to `UpdateAim()`. Material colours are applied via `MaterialPropertyBlock` on the child `MeshRenderer` (GameObject name: `'mesh'`), and re-applied in `Patch_TargetAimVisualizer_UpdateAim` postfix after each rebuild.

**Shader confirmed:** `HDRP/Unlit` on material `'Aiming (Instance)'`.

| Property | Shader property | Config slot | Notes |
|---|---|---|---|
| In-range line tint | `_UnlitColor` | `TargetAimVisualizer.InRangeColor` | Base colour tint of the line texture. Default: white (RGBA 1,1,1,1 = no tint) |
| In-range bloom hue | `_EmissiveColor` (HDR) | `TargetAimVisualizer.InRangeEmissiveColor` + `EmissiveIntensity` | HDR emissive. R/G/B set hue via `TileHighlightEntry`. `EmissiveIntensity` float controls brightness multiplier separately. Game default intensity ≈ 15. |
| Out-of-range colour | `_UnlitColor` (MPB) | `TargetAimVisualizer.OutOfRangeColor` | Applied via `MaterialPropertyBlock` in the `Patch_TargetAimVisualizer_UpdateAim` postfix. `UpdateAim()` hardcodes white (`0x3f800000`) for the out-of-range path and does not read the native `OutOfRangeColor` field (confirmed via Ghidra decompilation of `GameAssembly.dll`). The fix: `ReapplyTargetAimVisualizerColors()` reads `_UnlitColor` back from the material after `UpdateAim()` runs to detect range state, then writes `OutOfRangeColor` as `_UnlitColor` via MPB after the game's write. The postfix fires again on the next `UpdateAim()` call. The `_Color` property returns `HasProperty("_Color") = true` on this shader but is a legacy compatibility stub - writes via MPB have no visible effect; do not use it. |

Float parameters set directly on the component instance (sentinel `-1` = leave unchanged):

| Field | Config slot |
|---|---|
| `AnimationScrollSpeed` | `TargetAimVisualizer.AnimationScrollSpeed` |
| `Width` | `TargetAimVisualizer.Width` |
| `MinimumHeight` | `TargetAimVisualizer.MinimumHeight` |
| `MaximumHeight` | `TargetAimVisualizer.MaximumHeight` |
| `DistanceToHeightScale` | `TargetAimVisualizer.DistanceToHeightScale` |

#### LineOfSightVisualizer - confirmed working

**Renderer type:** `Il2CppShapes.Line` from `Il2CppShapesRuntime.dll` (namespace `Il2CppShapes`, class `Line`) - confirmed via Ghidra, `dump.cs`, and runtime verification. Earlier scan findings that identified the components as `UnityEngine.LineRenderer` or "Shapes library types with no bindings" were both incorrect and have been retracted. The DLL is present and bindings are generated.

Pool structure: `List<Line[]> m_Lines`, 3 `Line` entries per group. Colour is written only in `Resize(int)` via `ColorStart`/`ColorEnd` on each `Line`. `GetComponentsInChildren<T>` throws a fatal Il2CppInterop type-initialiser exception for this type - indexed `GetChild(i).GetComponent<Il2CppShapes.Line>()` traversal is required.

Fade pattern per group (i % 3): index 0 = fade-in (`ColorStart` alpha=0, `ColorEnd` alpha=A), index 1 = solid (both alpha=A), index 2 = fade-out (`ColorStart` alpha=A, `ColorEnd` alpha=0).

| Component | Config slot | Notes |
|---|---|---|
| All `Il2CppShapes.Line` children | `LineOfSight.LineColor` | Fade pattern: index 0 = fade-in, index 1 = solid, index 2 = fade-out (i % 3). Re-applied after every `Resize(int)` via `LOSResizePatch`. |

Implementation: `LOSResizePatch` (private method - resolved via `AccessTools.Method`), `VisualizerCustomizer.ApplyLineOfSightColor`, `VisualizerCustomizer.TryApplyLineOfSight`. Verified in log. Config slot: `Visualizers.LineOfSight.LineColor` (`TileHighlightEntry`).

---

### Types confirmed as not customisable

| Type | Reason |
|---|---|
| `SkillUsesBar` | Notch colours driven by USS class only; no inline override surface |
| `UnitBadge` | `IDisposable` wrapper; badge tint already covered by `ContainedBadge` in `UnitHUD` element tree |
| `TacticalBarkPanel` | Fields are audio visualisation scaffolding only (float arrays, waveform columns); no text or colour fields |
| `OffmapAbilityButton` | Button image and state colours entirely USS-driven via `SelectableImageButton` base; no direct surface |
| `UnitsTurnBar` | Layout container wrapper only; no Color or Label fields |
| `BaseHUD` | Positional/rendering flags only; no Color or Label fields |
| `WorldSpaceIcon` | Confirmed empty - no fields at all; consistent with `SimpleWorldSpaceIcon` findings |
| `SimpleWorldSpaceIcon` | No fields beyond UXML path constant (`simple_world_space_icon`); no `m_TextElement`, no icon reference, no colour fields. Scan infrastructure deleted. |
| `SkillBar` | Layout container wrapper only; no Color or Label fields |
| `ISkillBarElement` | Interface definition only |

---

## 4. Patching strategy

All patches are Harmony `Postfix` patches registered in `RegisterPatches()`. Each is a private static inner class of `HUDCustomizerPlugin`.

### Patching strategy key

| Pattern | When to use |
|---|---|
| *Registry pattern* | Type inherits `InterfaceElement` / `InteractiveElement` / `BaseHUD`. Use Harmony postfix + `Register(el, "TypeName")` + `ReapplyToLiveElements()` case. |
| *Standalone* | Type does not inherit a UIElement base. Use direct field access via the patch `__instance`; do not register. |

### The standard patch pattern - full source

Every functional patch follows this exact structure. The try/catch is always present - a patch failure must never crash the game:

```csharp
[HarmonyPatch(typeof(Il2CppUnitHUD), nameof(Il2CppUnitHUD.SetActor))]
private static class Patch_UnitHUD_SetActor
{
    private static void Postfix(Il2CppUnitHUD __instance)
    {
        try
        {
            var el = __instance.Cast<Il2CppInterfaceElement>();
            UnitCustomizer.Apply(el, Config.UnitHUDScale);
            FontCustomizer.Apply(el, "UnitHUD");
            Register(el, "UnitHUD");
        }
        catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_UnitHUD_SetActor: {ex}"); }
    }
}
```

Three things always happen: cast, apply, register.

### The EntityHUD/UnitHUD guard - full source

`EntityHUD` is the base class; `UnitHUD` is a subtype. Without the guard, patching `EntityHUD.InitBars` would fire for both types and double-apply to every `UnitHUD`:

```csharp
[HarmonyPatch(typeof(Il2CppEntityHUD), "InitBars")]
private static class Patch_EntityHUD_InitBars
{
    private static void Postfix(Il2CppEntityHUD __instance)
    {
        if (__instance.TryCast<Il2CppUnitHUD>() != null) return; // skip UnitHUD instances
        try
        {
            var el = __instance.Cast<Il2CppInterfaceElement>();
            UnitCustomizer.Apply(el, Config.EntityHUDScale);
            FontCustomizer.Apply(el, "EntityHUD");
            Register(el, "EntityHUD");
        }
        catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_EntityHUD_InitBars: {ex}"); }
    }
}
```

### Scan-only patch pattern - full source

Patches that exist only to fire a scan use a `_scanned` flag to fire once per session:

```csharp
[HarmonyPatch(typeof(Il2CppObjectivesTracker), nameof(Il2CppObjectivesTracker.Init))]
private static class Patch_ObjectivesTracker_Init
{
    private static bool _scanned = false;
    private static void Postfix(Il2CppObjectivesTracker __instance)
    {
        try
        {
            var el = __instance.Cast<Il2CppInterfaceElement>();
            FontCustomizer.Apply(el, "ObjectivesTracker");
            Register(el, "ObjectivesTracker");
            if (!_scanned) { _scanned = true; Scans.RunElementScan(el, "ObjectivesTracker"); }
        }
        catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_ObjectivesTracker_Init: {ex}"); }
    }
}
```

To remove a scan: delete the `_scanned` field and the `if (!_scanned)` line. If the patch applies no customisations at all, delete the entire class and its `RegisterPatches()` entry.

### Casting note for ObjectivesTracker

`ObjectivesTracker` inherits from `BaseButton`, not `InterfaceElement` directly. The `.Cast<Il2CppInterfaceElement>()` still works because `BaseButton` ultimately inherits from `InterfaceElement` in the native hierarchy - Il2Cpp interop resolves this correctly. However, if you ever need to cast to `BaseButton`-specific methods, you must cast to `Il2CppMenace.UI.BaseButton` first.

### Patch selection rationale

| Patch class | Method patched | Slot | Why this method |
|---|---|---|---|
| `Patch_UnitHUD_SetActor` | `UnitHUD.SetActor` | - | Primary init - called when a unit is assigned to the HUD |
| `Patch_UnitHUD_Show` | `UnitHUD.Show` | - | Re-applies in case the game resets inline styles on show |
| `Patch_UnitHUD_SetOpacity` | `UnitHUD.SetOpacity` | - | Intercepts the spent-dim call (0.5); active-restore calls (1.0) pass through unmodified |
| `Patch_EntityHUD_InitBars` | `EntityHUD.InitBars` | - | Bar init for non-unit entities; UnitHUD excluded by `TryCast` guard |
| `Patch_StructureHUD_Init` | `StructureHUD.Init` | - | Dedicated init hook for structure HUDs; enables independent `StructureHUDScale` |
| `Patch_ObjectiveHUD_SetObjective` | `ObjectiveHUD.SetObjective` | - | Per-objective init; fires once per objective instance |
| `Patch_ObjectivesTracker_Init` | `ObjectivesTracker.Init` | - | Single init point for the tracker panel |
| `Patch_MissionInfoPanel_Init` | `MissionInfoPanel.Init` | - | Single init point for the mission info panel |
| `Patch_MovementHUD_SetDestination` | `MovementHUD.SetDestination` | - | Fires on each movement target selection; re-applies because labels update each call |
| `Patch_BleedingWorldSpaceIcon_SetText` | `BleedingWorldSpaceIcon.SetText` | - | Fires on creation and each text update; confirmed in source |
| `Patch_DropdownText_Init` | `DropdownText.Init` | - | Fires once on creation with text already set; covers all tactical flyover text |
| `Patch_SkillBarButton_Init` | `SkillBarButton.Init` | - | Primary init for skill bar buttons |
| `Patch_SkillBarButton_Show` | `SkillBarButton.Show` | - | Re-applies in case the game resets inline styles on show |
| `Patch_BaseSkillBarItemSlot_Init` | `BaseSkillBarItemSlot.Init` | - | Covers both `SkillBarSlotWeapon` and `SkillBarSlotAccessory` via base class |
| `Patch_SimpleSkillBarButton_SetText` | `SimpleSkillBarButton.SetText` | - | Fires on creation with text already set (same rationale as `BleedingWorldSpaceIcon`) |
| `Patch_TurnOrderFactionSlot_Init` | `TurnOrderFactionSlot.Init` | - | Fires once per slot on creation |
| `Patch_UnitsTurnBarSlot_Init` | `UnitsTurnBarSlot.Init` | - | Initial setup of turn bar slot |
| `Patch_UnitsTurnBarSlot_SetActor` | `UnitsTurnBarSlot.SetActor` | - | Re-applies when the slot's unit changes; likely resets inline styles |
| `Patch_SelectedUnitPanel_SetActor` | `SelectedUnitPanel.SetActor` | - | Fires on unit selection change; `Init` fires without actor data |
| `Patch_TacticalUnitInfoStat_Init` | `TacticalUnitInfoStat.Init` | - | Fires once per stat row; covers all instances automatically via registry |
| `Patch_TurnOrderPanel_UpdateFactions` | `TurnOrderPanel.UpdateFactions` | - | Earliest reliable point where the panel has content; no `Init` method present |
| `Patch_StatusEffectIcon_InitSkillTemplate` | `StatusEffectIcon.Init(SkillTemplate)` | - | Resolved via reflection loop; one of two `Init` overloads |
| `Patch_StatusEffectIcon_InitSkill` | `StatusEffectIcon.Init(Skill)` | - | Resolved via reflection loop; second `Init` overload |
| `Patch_DelayedAbilityHUD_SetAbility` | `DelayedAbilityHUD.SetAbility` | - | Fires on ability assignment; also runs delayed marker scan |
| `Patch_DelayedAbilityHUD_SetProgressPct` | `DelayedAbilityHUD.SetProgressPct` | - | Re-applies on each progress update; may reset `Progress` inline styles |
| `Patch_MovementVisualizer_ShowPath` | `MovementVisualizer.ShowPath` | - | Re-applies colour after each path update, since the game resets colours from its own state |
| `Patch_TargetAimVisualizer_UpdateAim` | `TargetAimVisualizer.UpdateAim` | - | Re-applies `MaterialPropertyBlock` (InRangeColor + OutOfRangeColor fix) after each mesh rebuild |
| `LOSResizePatch` | `LineOfSightVisualizer.Resize(int)` | - | Private method resolved via `AccessTools.Method`; re-applies LOS line colour after each pool resize |

Slot numbers are confirmed from game source files. Harmony resolves methods by name, not slot - the slots are documented here for virtual dispatch verification only.

---

## 5. Config system

### Schema overview

The JSON config uses four encoding conventions:

| Convention | Used by | Format |
|---|---|---|
| Inline string | Unit/entity HUD bar colours, badge tint | `"R, G, B"` or `"R, G, B, A"` - R/G/B 0-255 integers, A 0.0-1.0 float. `""` = leave unchanged. |
| `TileHighlightEntry` object | Tile highlights, USS colours, faction health bars, visualizer colours | `{ "Enabled": bool, "R": 0-255, "G": 0-255, "B": 0-255, "A": 0.0-1.0 }`. `Enabled: false` = leave unchanged. |
| `FontSettings` object | All font overrides | `{ "Font": "name-or-empty", "Size": float-or-0, "Color": "R,G,B-or-empty" }` |
| Float sentinel | Visualizer float parameters | Numeric value to apply; `-1` = leave unchanged (game default). `0` is a valid value for some parameters so `-1` is used instead of `0` as the sentinel. |

### Serialisation options

```csharp
public static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
{
    WriteIndented               = true,
    PropertyNameCaseInsensitive = true,  // field names are case-insensitive
    AllowTrailingCommas         = true,  // tolerates trailing commas in hand-edited files
    ReadCommentHandling         = JsonCommentHandling.Skip,  // allows // comments in JSON
};
```

Unknown JSON properties are silently ignored. Missing properties use C# default values from class constructors.

---

## 6. Hot-reload lifecycle

Pressing the reload key (default `F8`) while in a tactical scene triggers this exact sequence from `OnUpdate()`:

```csharp
if (Input.GetKeyDown(_reloadKey))
{
    Log.Msg($"[{_reloadKey}] Hot-reload triggered.");
    LoadConfig();
    FontCustomizer.InvalidateCache();
    ReapplyToLiveElements();
    TileCustomizer.TryApply();
    USSCustomizer.TryApply();
    UnitCustomizer.ApplyFactionHealthBarColors();
    UnitCustomizer.ApplyRarityColors();
    VisualizerCustomizer.TryApply();
    VisualizerCustomizer.TryApplyLineOfSight(Config);
    CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
}
```

1. `LoadConfig()` - reads and deserialises the JSON file. On parse failure, backs up the broken file to `.bak` and regenerates defaults via `HUDConfig.BuildDefaultConfig()`. Also calls `CombatFlyoverCustomizer.Apply()` and `CombatFlyoverCustomizer.LogSummary()` internally as part of the summary sequence.
2. `FontCustomizer.InvalidateCache()` - clears `_fontCache` so it is rebuilt on the next `Apply()` call.
3. `ReapplyToLiveElements()` - iterates `_registry`, re-applies scale/colours/fonts to UIElements, prunes stale entries.
4. `TileCustomizer.TryApply()` - re-applies tile highlight overrides to the `TileHighlighter` singleton.
5. `USSCustomizer.TryApply()` - re-applies USS theme colour overrides to `UIConfig` (23 general + 5 mission state fields).
6. `UnitCustomizer.ApplyFactionHealthBarColors()` - re-applies faction health bar colours to `UIConfig`.
7. `UnitCustomizer.ApplyRarityColors()` - re-applies rarity colours and `ColorPositionMarkerDelayedAbility` to `UIConfig`.
8. `VisualizerCustomizer.TryApply()` - re-discovers live visualizer instances via `FindObjectsOfType` and re-applies all colour and parameter overrides.
9. `VisualizerCustomizer.TryApplyLineOfSight(Config)` - re-discovers live `LineOfSightVisualizer` instances and re-applies `LineColor`.
10. `CombatFlyoverCustomizer.Apply(Config.CombatFlyover)` - pushes the freshly-loaded config to the CombatFlyoverText bridge. This second call (the first is inside `LoadConfig()`) is necessary because `LoadConfig()` also runs at startup where this `OnUpdate` block does not execute. Colour and timing changes take effect on the next flyover that fires after the reload.

Hot-reload is only fully functional in tactical scenes. `TileHighlighter.Exists()` returns false outside tactical, so `TryApply()` silently no-ops. `VisualizerCustomizer.TryApply()` will find no instances outside tactical and silently returns. The config reload and log summary still fire regardless of scene.

USS colours are additionally applied in the **Strategy scene** via `OnSceneLoaded`. Because `UIConfig` is not available immediately on scene load, `ApplyUSSAfterDelay(0.5f)` runs as a coroutine and calls `USSCustomizer.TryApply()` after a 0.5 second delay. This ensures USS theme colours take effect on the strategy map without requiring a hot-reload.

---

## 7. The scan system

`Scans.cs` contains discovery utilities used during development. **All scans are disabled by default** (`EnableScans: false`) and each fires at most once per session regardless of hot-reload.

Scans write to `Log.Msg()` unconditionally - they do not respect `DebugLogging`. All scan entry points call `CheckGate()` first:

```csharp
private static bool CheckGate()
{
    return HUDCustomizerPlugin.Config?.EnableScans == true;
}
```

### Current scans

| Scan method | Trigger | Purpose | Status |
|---|---|---|---|
| `RunUIConfigScan()` | `OnTacticalReady` | Dumps all `Color` fields from `UIConfig.Get()` | All fields now documented in Section 3 (USSCustomizer table). Delete when no longer needed. |
| `RunFontScan(fontCache)` | `FontCustomizer.OnTacticalReady()` | Dumps IStyle font properties and all loaded `Font` asset names | Font list confirmed. Delete when no longer needed. |
| `RunElementScan(element, label)` | Various patch postfixes | Dumps child element tree (max depth 5) and all text elements | Delete per call-site once that HUD's structure is confirmed |
| `RunTargetAimMaterialScan(instance)` | `Patch_TargetAimVisualizer_UpdateAim` | Dumps all shader properties on the aim material's `MeshRenderer` | Confirmed: shader is `HDRP/Unlit`, colour property is `_UnlitColor`, emissive is `_EmissiveColor`. Delete once no longer needed. |

### Scan call-sites that carry DELETE comments

- `Patch_ObjectiveHUD_SetObjective` - scan call only; patch itself stays
- `Patch_ObjectivesTracker_Init` - scan call only; patch itself stays
- `Patch_MissionInfoPanel_Init` - scan call only; patch itself stays
- `Patch_UnitHUD_OnUpdate_Scan` - done deleted (entire class was scan-only)
- `Patch_BleedingWorldSpaceIcon_SetText` - scan call only; patch itself stays (element name confirmed, `QueryAndSet` replaced with direct `el.Q()`)
- `Patch_WorldSpaceIcon_Update_Scan` - done deleted (entire class was scan-only)
- `Patch_TargetAimVisualizer_UpdateAim` - scan call only; the patch itself stays (it re-applies material colours)

---

## 8. Il2Cpp and UIToolkit notes

### Il2Cpp casting

Menace is an Il2Cpp game. Managed C# types generated by Il2CppInterop do not share a common managed inheritance hierarchy - `UnitHUD` is not a subtype of `InterfaceElement` in C#, even though it is at the native level. You cannot use `is` or `as` between generated Il2Cpp types:

```csharp
// Throws InvalidCastException if the native type is not compatible.
// Use when you are certain of the type and want a hard failure if wrong.
var el = __instance.Cast<Il2CppInterfaceElement>();

// Returns null if not compatible.
// Use for conditional type checks (e.g. the EntityHUD/UnitHUD guard).
if (__instance.TryCast<Il2CppUnitHUD>() != null) return;
```

Both methods verify type compatibility at the native level through the Il2Cpp bridge. A failed `.Cast<T>()` throws - this is caught by the try/catch present in every patch postfix.

### Il2Cpp field reflection limitations

Private fields on Il2Cpp types are not accessible via standard C# reflection. `GetType().GetFields(NonPublic | Instance)` on an Il2Cpp-wrapped object returns only the C# proxy class's own infrastructure fields (`isWrapped`, `pooledPtr`) - not the actual game fields. This is a fundamental limitation of Il2CppInterop.

Consequences for this codebase:
- Any future feature requiring access to private Il2Cpp fields must either: (a) target a public field/method on the same type, (b) use a child `Component` that has a registered Il2Cpp binding, or (c) use direct field-offset access via Il2Cpp pointer arithmetic.

Similarly, `GetComponent<T>()` only works if Il2CppInterop has generated a binding for `T`. If `GetComponent` returns only the raw base `Component` type, the binding is missing - but the component may still be accessible via a known field offset on the parent type (see `LineOfSightVisualizer` in Section 3 for an example of how Ghidra resolved this).

### GetClasses() - why the helper exists and full source

`VisualElement.GetClasses()` in Il2Cpp interop returns an `Il2Cpp`-wrapped `IEnumerable<string>` whose enumerator does **not** implement the C# `IEnumerator` contract. Calling `foreach` on it directly will throw at runtime. The workaround drives the enumerator via reflection:

```csharp
internal static List<string> GetClasses(VisualElement ve)
{
    var result = new List<string>();
    try
    {
        var enumerableObj = ve.GetClasses();
        if (enumerableObj == null) return result;
        var getEnum = enumerableObj.GetType()
            .GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance);
        if (getEnum == null) return result;
        var enumerator = getEnum.Invoke(enumerableObj, null);
        var moveNext   = enumerator.GetType()
            .GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
        var current    = enumerator.GetType()
            .GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        if (moveNext == null || current == null) return result;
        while ((bool)moveNext.Invoke(enumerator, null))
        {
            var val = current.GetValue(enumerator);
            if (val != null) result.Add(val.ToString());
        }
    }
    catch { }
    return result;
}
```

**Always use `HUDCustomizerPlugin.GetClasses(ve)`** rather than `ve.GetClasses()` directly. The outer `catch {}` is intentional - if reflection fails for any reason the method returns an empty list rather than crashing.

### UIToolkit Q() - the null className argument

`element.Q(name, className)` finds the first descendant matching name and/or USS class. In Il2Cpp interop, both parameters are required. When querying by element name only, always pass `(string)null` as the second argument:

```csharp
// Correct
var ve = element.Q("Hitpoints", (string)null);

// Wrong -- the single-argument overload may not resolve correctly via Il2Cpp binding
var ve = element.Q("Hitpoints");
```

### Inline styles vs USS class styles

`ve.style.*` sets inline styles. Inline styles always take precedence over USS class styles. HUDCustomizer works exclusively through inline overrides - it never modifies stylesheets. This means:

- Overrides apply immediately when set
- They survive USS reloads
- The game's own code can overwrite them if it also sets inline styles after our postfix runs - this is why `UnitHUD.Show` is patched in addition to `UnitHUD.SetActor`, and why visualizer colours are re-applied in patch postfixes rather than only in `TryApply()`

### MaterialPropertyBlock pattern for MonoBehaviour renderers

For visualizers that use `MeshRenderer`, colour overrides are applied via `MaterialPropertyBlock` rather than modifying `renderer.material` directly. This avoids creating persistent material asset copies:

```csharp
var block = new MaterialPropertyBlock();
renderer.GetPropertyBlock(block);          // read existing state
block.SetColor("_UnlitColor", color);      // apply override
renderer.SetPropertyBlock(block);          // write back
```

`GetPropertyBlock` + `SetPropertyBlock` must always be paired - reading first preserves any other properties already set on the block. The block is local to the call site; it is not cached.

### Font loading constraints

`Font.CreateDynamicFontFromOSFont` is stripped in this Il2Cpp build. OS fonts cannot be loaded at runtime. Only fonts returned by `Resources.FindObjectsOfTypeAll<Font>()` are available. The confirmed list from scan:

```
Jura-Regular, Jura-Bold, OCRAStd, Inconsolata-SemiBold, NotInter-Regular
NotoSansJP-Regular, NotoSansKR-Regular, NotoSansSC-Regular, NotoSansTC-Regular
```

`LegacyRuntime` and `LiberationSans` are also returned by the scan but are Unity built-ins. They can technically be used but are not designed for this game's UI.

If a configured font name is not found in the cache, `FontCustomizer.Resolve()` logs a warning listing available names and returns null (leaving the element's current font unchanged).

### TryParseColor - full source

All inline string colours go through this helper. R/G/B are 0-255 integers (parsed as floats, divided by 255). A is 0.0-1.0. Out-of-range values are clamped with a warning. An empty string returns `false` silently - this is the "leave unchanged" sentinel and is expected:

```csharp
internal static bool TryParseColor(string s, out Color color)
{
    color = Color.white;
    if (string.IsNullOrWhiteSpace(s)) return false;

    var parts = s.Split(',');
    if (parts.Length < 3)
    {
        Log.Warning($"[HUDCustomizer] TryParseColor: expected R,G,B or R,G,B,A " +
                    $"(e.g. '255,128,0' or '255,128,0,0.5'), got '{s}'");
        return false;
    }

    if (!float.TryParse(parts[0].Trim(), ..., out float r) ||
        !float.TryParse(parts[1].Trim(), ..., out float g) ||
        !float.TryParse(parts[2].Trim(), ..., out float b))
    {
        Log.Warning($"[HUDCustomizer] TryParseColor: could not parse R,G,B from '{s}'");
        return false;
    }

    // R/G/B clamped to 0-255 with warning if out of range
    // A defaults to 1f if omitted; parsed and clamped to 0.0-1.0 if present

    color = new Color(r / 255f, g / 255f, b / 255f, a);
    return true;
}
```

### ToColor - TileHighlightEntry converter

The caller is always responsible for checking `entry.Enabled` before calling `ToColor`. `ToColor` does not check `Enabled` itself:

```csharp
internal static Color ToColor(TileHighlightEntry entry, string label)
{
    Debug($"  [Color] SET {label} -> RGB({(int)entry.R},{(int)entry.G},{(int)entry.B}) A({entry.A:F2})");
    return new Color(entry.R / 255f, entry.G / 255f, entry.B / 255f, entry.A);
}
```

---

## 9. Adding a new customisable system

### Step 1 - Identify the target

- **Per-element HUD** (spawned per unit/objective/etc): needs a Harmony patch on its init method
- **Singleton** (one shared instance like `TileHighlighter` or `UIConfig`): call directly from `OnTacticalReady` and the hot-reload block
- **MonoBehaviour visualizer** (world-space 3D component): use `FindObjectsOfType` in `VisualizerCustomizer.TryApply()` and re-apply in a postfix on the method that resets state

If the element structure is unknown, enable `EnableScans: true`, run the game into a tactical mission, and check the MelonLoader log for `[ElemScan/...]` output.

### Step 2 - Add config fields in HUDConfig.cs

Add properties to `HUDCustomizerConfig` with format and game-default comments:

```csharp
// Your new system colours
// Format: "R, G, B" or "R, G, B, A"  (R/G/B = 0-255 integers, A = 0.0-1.0 float)
// Game defaults observed in scan:
//   SomeElement fill: 100, 150, 200  (blue)
public string SomeElementFillColor { get; set; } = "";
```

Add the matching entry to `BuildDefaultConfig()` in the same format as existing entries. The JSON string is a verbatim literal - all internal quotes must be escaped as `""`.

### Step 3 - Implement the customiser

**For a per-element HUD system**, add a patch to the `Patches` partial class block in `HUDCustomizer.cs`:

```csharp
[HarmonyPatch(typeof(Il2CppYourHUD), nameof(Il2CppYourHUD.YourInitMethod))]
private static class Patch_YourHUD_YourInitMethod
{
    private static bool _scanned = false;   // include during development; remove when confirmed
    private static void Postfix(Il2CppYourHUD __instance)
    {
        try
        {
            var el = __instance.Cast<Il2CppInterfaceElement>();
            TacticalElementCustomizer.Apply(el, "YourHUD");  // if adding tint/style overrides
            FontCustomizer.Apply(el, "YourHUD");
            Register(el, "YourHUD");
            if (!_scanned) { _scanned = true; Scans.RunElementScan(el, "YourHUD"); }
        }
        catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_YourHUD_YourInitMethod: {ex}"); }
    }
}
```

Register the patch in `RegisterPatches()`:

```csharp
harmony.PatchAll(typeof(Patch_YourHUD_YourInitMethod));
```

If the new type needs colour or scale re-application on hot-reload, add a `case` to `ReapplyToLiveElements()`:

```csharp
case "YourHUD":
    YourCustomizer.Apply(el, someParameter);
    break;
```

**For a singleton system**, add `TryApply()` to the appropriate customiser. Call it from both `OnTacticalReady` and the hot-reload block in `OnUpdate`.

**For a MonoBehaviour visualizer**, add the apply logic to `VisualizerCustomizer.TryApply()` using the `FindObjectsOfType` pattern. If the game resets state in a specific method, add a postfix patch on that method that calls back into `VisualizerCustomizer` to re-apply - see `Patch_MovementVisualizer_ShowPath` and `Patch_TargetAimVisualizer_UpdateAim` for the pattern.

### Step 4 - Add to FontCustomizer if the HUD has configurable text

1. Add `case "YourHUD":` to the switch in `FontCustomizer.Apply()`
2. Implement `ApplyYourHUD(Il2CppInterfaceElement el)` - use `QueryAndSet` until element names are scan-confirmed, then switch to `SetFont(el.Q(name, (string)null), ...)`
3. Add entries to `LogFontSummary()`

### Step 5 - Add a summary log call

Add `YourCustomizer.LogSummary()` and call it from the summary block in `LoadConfig()`.

### Step 6 - Remove scan scaffolding when done

- Remove the `_scanned` field and `if (!_scanned)` block from the patch
- If the patch was scan-only, delete the entire class and its `RegisterPatches()` entry
- If a `Scans` method is no longer called from anywhere, delete it from `Scans.cs`

---

## 10. Runtime scan status

Source: `C:\Program Files (x86)\Steam\steamapps\common\Menace\MelonLoader\Latest.log`
Latest reviewed run: `2026-03-26 20:43` (America/Vancouver)

### Confirmed (scan fired)

| Type | Confirmed element names |
|---|---|
| `SkillBarButton` | `SkillIcon`, `HoverOverlay`, `SelectedOverlay`, `Cross`, `HotkeyLabel`, `UsesLabel`, `ActionPointsLabel` |
| `BaseSkillBarItemSlot` | `Background`, `IconContainer`, `ItemIcon`, `Cross` |
| `SimpleSkillBarButton` | `Icon`, `Label` |
| `TurnOrderFactionSlot` | `InactiveMask`, `InactiveIcon`, `Selected` |
| `UnitsTurnBarSlot` | `Portrait`, `Badge`, `Overlay`, `Selected` |
| `UnitsTurnBarSlot.SetActor` | Same element tree as `UnitsTurnBarSlot` |
| `SelectedUnitPanel` | `ActionPointsLabel`, `ConditionLabel`, `UnitName`, `LeaderName` (plus expected container hierarchy) |
| `TacticalUnitInfoStat` | `Icon`, `Value` |
| `TurnOrderPanel` | `RoundNumber`, `Factions` |
| `StatusEffectIcon.InitSkill` | `StackCount` |
| `DelayedAbilityHUD` | `Progress`, `DisabledIcon` |

### Incomplete - scan did not fire in last evidence capture

- `SkillBarButton.Show`
- `StatusEffectIcon.InitSkillTemplate`
- `DelayedAbilityHUD.SetProgressPct`

### Element names not yet scan-confirmed

The following implemented types have not had their UXML element names confirmed by scan. Use `QueryAndSet` rather than direct `el.Q()` calls until confirmed:

- `SkillBarButton` (hook: `Init`, re-apply hook: `Show`)
- `BaseSkillBarItemSlot` (hook: `Init`)
- `SkillBarSlotWeapon` - `m_NameLabel` only; base fields covered by `BaseSkillBarItemSlot` scan
- `SimpleSkillBarButton` (hook: `SetText`)
- `TurnOrderFactionSlot` (hook: `Init`)
- `UnitsTurnBarSlot` (hook: `Init`, re-apply hook: `SetActor`)
- `SelectedUnitPanel` (hook: `SetActor`)
- `TurnOrderPanel` (hook: `UpdateFactions`)
- `StatusEffectIcon` - `InitSkillTemplate` overload only; `InitSkill` overload confirmed above

### Delayed marker validation

- `DelayedAbility Marker Scan [SetAbility]` fired. `UIConfig.ColorPositionMarkerDelayedAbility` logged successfully. `m_WorldSpaceMarkerMaterial` was null at this phase.
- `DelayedAbility Marker Scan [SetProgressPct]` did not fire (incomplete).
- Selector choices for `DelayedAbilityHUD` remain based on currently confirmed names. The three incomplete items above are tracked as deferred evidence gaps.

**Implementation note:** Step 10 cleanup removed temporary scan scaffolding from `HUDCustomizer.cs`. Keep incomplete items above tracked until evidence is captured.
