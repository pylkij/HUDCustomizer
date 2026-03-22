# HUDCustomizer — Contributor & Maintainer Reference

HUDCustomizer is a MelonLoader mod for **MENACE** (Il2Cpp, Unity 6, .NET 6) that lets players customise the tactical HUD via a JSON config file. It supports hot-reload at runtime.

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
10. [Known gaps and incomplete work](#10-known-gaps-and-incomplete-work)

---

## 1. Repository layout

| File | Responsibility |
|---|---|
| `HUDCustomizer.cs` | Plugin entry point, lifecycle (`OnInitialize`, `OnTacticalReady`, `OnUpdate`), live element registry, Harmony patch declarations, shared helpers (`TryParseColor`, `ToColor`, `GetClasses`, `Debug`) |
| `HUDConfig.cs` | All config data classes (`HUDCustomizerConfig`, `FontSettings`, `TileHighlightEntry`, `VisualizersConfig`, and all sub-configs), plus the `HUDConfig` static class which owns `ConfigDir`, `ConfigPath`, `JsonOpts`, and `BuildDefaultConfig()` |
| `UnitCustomizer.cs` | Scale/transform-origin application to UnitHUD and EntityHUD elements; bar fill/preview/track colour overrides; badge tint; faction health bar colours via UIConfig |
| `FontCustomizer.cs` | Font asset cache; per-HUD font/size/colour application via UIToolkit `Q()` queries; `Merge()` (Global to per-element override resolution) |
| `TileCustomizer.cs` | Tile highlight colour overrides via `TileHighlighter.SetColorOverrides()` |
| `USSCustomizer.cs` | USS global theme colour overrides via `UIConfig.Get()` fields |
| `VisualizerCustomizer.cs` | Colour and parameter overrides for world-space 3D visualizers (`MovementVisualizer`, `TargetAimVisualizer`). Uses `FindObjectsOfType` and `MaterialPropertyBlock` rather than the UIElement registry — these are MonoBehaviours, not InterfaceElements. |
| `VisualizerCustomizer.cs` | Colour and parameter overrides for world-space 3D visualizers (`MovementVisualizer`, `TargetAimVisualizer`). Uses `FindObjectsOfType` and `MaterialPropertyBlock` rather than the UIElement registry — these are MonoBehaviours, not InterfaceElements. |
| `CombatFlyoverCustomizer.cs` | Bridge between HUDCustomizer's config system and the CombatFlyoverText plugin. Receives `CombatFlyoverSettings` from `LoadConfig()` and the hot-reload path via `Apply()`; exposes values to `CombatFlyoverPlugin` via public accessors; logs a summary line via `LogSummary()`. Has no Unity or game dependencies — pure config bridge. |
| `Scans.cs` | Development-only discovery scans (element trees, font assets, UIConfig colour values, material properties). All scans are gated on `EnableScans = true` in config and fire at most once per session. |

`HUDCustomizer.cs` is a `partial class` split across two declaration blocks in the same file: the first block contains the plugin body and helpers; the second contains all Harmony patch inner classes. Both blocks are `public partial class HUDCustomizerPlugin`.

---

## 2. Architecture overview

```
HUDCustomizerPlugin (entry point)
|
+-- OnInitialize
|     +-- LoadConfig()          <- reads HUDCustomizer.json via HUDConfig
|     +-- GameState.TacticalReady += OnTacticalReady
|     +-- RegisterPatches()     <- registers all Harmony postfixes
|
+-- OnTacticalReady
|     +-- FontCustomizer.OnTacticalReady()   <- builds font asset cache
|     +-- TileCustomizer.TryApply()          <- singleton-based, no patch needed
|     +-- USSCustomizer.TryApply()           <- singleton-based, no patch needed
|     +-- UnitCustomizer.ApplyFactionHealthBarColors()
|     +-- VisualizerCustomizer.TryApply()    <- FindObjectsOfType-based
|     +-- Scans.RunUIConfigScan()            <- dev only, gated on EnableScans
|
+-- OnUpdate (per frame)
|     +-- [ReloadKey pressed] -> LoadConfig, InvalidateCache,
|                               ReapplyToLiveElements, TryApply x4
|
+-- Harmony Patches (postfixes)
      +-- Each UIElement patch: Cast -> customiser.Apply / FontCustomizer.Apply -> Register
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

- **`Config`** (`HUDCustomizerConfig`) — the single static config instance. All other classes read from this via `HUDCustomizerPlugin.Config`.
- **`_registry`** — a `Dictionary<IntPtr, (Il2CppInterfaceElement el, string hudType)>` keyed by native object pointer. Every patched UIElement is registered here so `ReapplyToLiveElements()` can re-apply all customisations on hot-reload without needing to intercept game calls again. Stale entries (pointer == `IntPtr.Zero`) are pruned on each reload. Note: visualizer MonoBehaviours are **not** registered here — they are re-discovered via `FindObjectsOfType` on each `TryApply()` call.
- **Shared helpers** — `TryParseColor`, `ToColor`, `GetClasses` (see Section 8), `Debug`.

#### ReapplyToLiveElements() — full source

This is the hot-reload re-application loop. When adding a new HUD type that needs scale or colour customisation beyond fonts, add a `case` to the switch. The `default` branch calls only `FontCustomizer.Apply()` — it does not call `UnitCustomizer.Apply()` because most HUD types do not have the bar/badge structure that `UnitCustomizer` expects.

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
                    UnitCustomizer.Apply(el, Config.UnitHUDScale); break;
                case "EntityHUD":
                    UnitCustomizer.Apply(el, Config.EntityHUDScale); break;
                default:
                    // Non-unit HUD types (ObjectivesTracker, MissionInfoPanel,
                    // ObjectiveHUD, etc.) have no bars or badge -- UnitCustomizer
                    // has nothing to apply to them.
                    break;
            }

            FontCustomizer.Apply(el, hudType);
            count++;
        }
        catch { dead.Add(kvp.Key); }
    }
    foreach (var ptr in dead) _registry.Remove(ptr);
    Log.Msg($"[HUDCustomizer] Hot-reload: {count} live element(s) updated ({dead.Count} stale removed).");
}
```

#### RegisterPatches() — full source

Every new patch class must be added here. `harmony.PatchAll(typeof(T))` registers all `[HarmonyPatch]`-attributed methods within that specific type — do not use `harmony.PatchAll()` without a type argument.

```csharp
private void RegisterPatches(HarmonyLib.Harmony harmony)
{
    harmony.PatchAll(typeof(Patch_UnitHUD_SetActor));
    harmony.PatchAll(typeof(Patch_UnitHUD_Show));
    harmony.PatchAll(typeof(Patch_EntityHUD_InitBars));
    harmony.PatchAll(typeof(Patch_ObjectiveHUD_SetObjective));
    harmony.PatchAll(typeof(Patch_ObjectivesTracker_Init));
    harmony.PatchAll(typeof(Patch_MissionInfoPanel_Init));
    harmony.PatchAll(typeof(Patch_UnitHUD_OnUpdate_Scan));
    harmony.PatchAll(typeof(Patch_MovementHUD_SetDestination));
    harmony.PatchAll(typeof(Patch_BleedingWorldSpaceIcon_SetText));
    harmony.PatchAll(typeof(Patch_WorldSpaceIcon_Update_Scan));
    harmony.PatchAll(typeof(Patch_MovementVisualizer_ShowPath));
    harmony.PatchAll(typeof(Patch_TargetAimVisualizer_UpdateAim));
    Debug("Harmony patches registered.");
}
```

#### LoadConfig() summary call sequence

When adding a new customiser, add its `LogSummary()` call at the end of this block:

```csharp
Log.Msg($"Config loaded.  " +
        $"UnitScale={Config.UnitHUDScale:F2}  " +
        $"EntityScale={Config.EntityHUDScale:F2}  " +
        $"Origin=({Config.TransformOriginX:F0}%,{Config.TransformOriginY:F0}%)  " +
        $"ReloadKey={_reloadKey}");

UnitCustomizer.LogColourSummary();
FontCustomizer.LogFontSummary();
TileCustomizer.LogSummary();
USSCustomizer.LogSummary();
UnitCustomizer.LogFactionHealthBarSummary();
VisualizerCustomizer.LogSummary();
LogSpentOpacitySummary();
CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
CombatFlyoverCustomizer.LogSummary();
// insert YourCustomiser.LogSummary() here
```

`CombatFlyoverCustomizer.Apply()` is called here (as well as in the `OnUpdate` hot-reload block) because `LoadConfig()` also runs at startup where `OnUpdate` does not. Both call sites are required — removing either breaks one of the two paths.

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

`BuildDefaultConfig()` returns the full annotated JSON string as a single C# verbatim literal (`@"..."`). Do not split it — the file must be written atomically as one `File.WriteAllText` call. Quotes inside the verbatim literal are escaped as `""`.

`TileHighlightEntry` is the shared data class for all `Enabled/R/G/B/A` colour slots. Despite the name it is reused for USS colours (`USSColorsConfig`), faction health bars (`FactionHealthBarColorsConfig`), and visualizer colours (`MovementVisualizerConfig`, `TargetAimVisualizerConfig`). This is design debt — if refactored, these can be renamed or split without behaviour change.

`CombatFlyoverSettings` holds the config shape for the CombatFlyoverText integration: `Enabled`, three hex colour strings (`ColourHPDamage`, `ColourArmourDamage`, `ColourAccuracy`), and two floats (`ExtraDisplaySeconds`, `FadeDurationScale`). It is consumed exclusively by `CombatFlyoverCustomizer` — nothing else in HUDCustomizer reads it directly. When adding or changing a field here, the matching entry in `BuildDefaultConfig()` and the corresponding accessor in `CombatFlyoverCustomizer.cs` must be updated in the same change.

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

#### Bar colour queries — full source

`barName` is one of `"Hitpoints"`, `"Armor"`, `"Suppression"` — the confirmed UXML element names:

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

Set on `UIConfig.Get()`, not per-element. Controls the infobox health bar shown when a unit is selected — not the floating entity HUD bars above units. The two systems are independent.

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

---

### FontCustomizer (`FontCustomizer.cs`)

Font assets are loaded once at `TacticalReady` via `Resources.FindObjectsOfTypeAll<Font>()` into `_fontCache`. The cache is invalidated and rebuilt on hot-reload.

#### Apply() dispatch switch — full source

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

#### Merge() — override resolution — full source

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
| UnitHUD / EntityHUD | `Label` | `UnitBarLabel` | — | 14 | Scan |
| UnitHUD / EntityHUD | `DarkLabel` | `UnitBarLabel` | — | 14 | Scan |
| ObjectivesTracker | `ObjectivesTrackerHeadline` | `ObjTrackerHeadline` | — | 12 | Scan |
| ObjectivesTracker | `SecondaryObjectivesHeadline` | `ObjSecondaryHeadline` | — | 12 | Scan |
| ObjectivesTracker | `RewardPoints` | `ObjRewardPoints` | `m_RewardPointsLabel` | 16 | Scan + source |
| ObjectivesTracker | `Label` | `ObjTrackerLabel` | — | 14 | Scan |
| ObjectivesTracker | `DarkLabel` | `ObjTrackerLabel` | — | 14 | Scan |
| ObjectivesTracker | `Points` | `ObjTrackerPoints` | — | 16 | Scan (SetFontAll — repeats per entry) |
| ObjectivesTracker | `Description` | `ObjTrackerDescription` | — | 10 | Scan (SetFontAll — repeats per entry) |
| MissionInfoPanel | `MissionName` | `MissionName` | `m_MissionNameLabel` | 12 | Scan + source |
| MissionInfoPanel | `MissionDuration` | `MissionDuration` | `m_MissionDurationLabel` | 12 | Scan + source |
| ObjectiveHUD | `ObjectiveName` | `ObjectiveNameLabel` | `m_NameLabel` | 13 | Scan + source |
| ObjectiveHUD | `ObjectiveState` | `ObjectiveStateLabel` | `m_StateLabel` | 14 | Scan + source |
| ObjectiveHUD | `Line` | *(not exposed)* | — | 14 | Scan only — separator line, not currently configurable |
| MovementHUD | `CostLabel` | `MovementCostLabel` | — | 16 | Scan |
| MovementHUD | `ActionLabel` | `MovementActionLabel` | — | 14 | Scan |
| BleedingWorldSpaceIcon | `TextElement` | `BleedingIconText` | `m_TextElement` (Label) | — | Source only — scan not yet run |

**Note on `ObjectiveName` fontSize:** The scan shows 13, not 14. This differs from the config comment which says the default matches `ObjectiveState` (14). Do not change the config default — the scan value is authoritative.

**Note on source field name vs UXML name:** UIToolkit UXML element names are set in the `.uxml` file, not derived from the C# field name. The `m_` prefix is stripped and camelCase becomes PascalCase: `m_NameLabel` → `ObjectiveName`, `m_MissionDurationLabel` → `MissionDuration`. The scan confirms these translations. For `BleedingWorldSpaceIcon.m_TextElement`, this pattern predicts the UXML name is `TextElement`, but scan confirmation is still needed.

---

### TileCustomizer (`TileCustomizer.cs`)

Reads `TileHighlighter.Instance()` (singleton, tactical scenes only) and calls `SetColorOverrides()`. Safe to call when the singleton does not exist — `TryApply()` returns early via `TileHighlighter.Exists()`.

Each slot in `TileHighlightColorOverrides` has an `Enabled` flag. Setting `Enabled = false` tells the game to ignore the slot and use its own default — it does not zero the colour value. The `Resolve()` helper manages this:

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

Sets `Color` fields directly on `UIConfig.Get()`. These fields carry the `[UssColor]` attribute in the game source, meaning they map to USS custom properties that affect **all UI screens game-wide**. Fields without `[UssColor]` (rarity colours, health bar colours, `ColorPositionMarkerDelayedAbility`) are direct colour values used by specific systems — they can still be set via `UIConfig` but do not propagate through the USS property system.

**Note:** The five mission state colour fields (`ColorMissionPlayable` etc.) carry `[UssColor]` — confirmed from `dump.cs`. Earlier documentation incorrectly listed them as non-USS direct values. Implement them via `USSCustomizer.TryApply()`, not via the `FactionHealthBarColors` pattern.

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

Applies colour and parameter overrides to world-space 3D visualizer MonoBehaviours. These are not UIElements — they cannot use the `_registry` pattern and are not patched at init time. Instead, `TryApply()` uses `Object.FindObjectsOfType<T>()` to locate all live instances on each call. This is safe because these visualizers are scene-level singletons in practice.

#### MovementVisualizer — confirmed working

Fields set directly on the component instance:

| Field | Type | Config slot | Notes |
|---|---|---|---|
| `ReachableColor` | `Color` | `MovementVisualizer.ReachableColor` | Polyline and dot colour for in-range path |
| `UnreachableColor` | `Color` | `MovementVisualizer.UnreachableColor` | Polyline colour for out-of-range path |

Re-applied in `Patch_MovementVisualizer_ShowPath` postfix, since the game sets these from its own state when `ShowPath()` is called.

#### TargetAimVisualizer — confirmed working (partial)

The aim line mesh is rebuilt every call to `UpdateAim()`. Material colours are applied via `MaterialPropertyBlock` on the child `MeshRenderer` (GameObject name: `'mesh'`), and re-applied in `Patch_TargetAimVisualizer_UpdateAim` postfix after each rebuild.

**Shader confirmed:** `HDRP/Unlit` on material `'Aiming (Instance)'`.

| Property | Shader property | Config slot | Notes |
|---|---|---|---|
| In-range line tint | `_UnlitColor` | `TargetAimVisualizer.InRangeColor` | Base colour tint of the line texture. Default: white (RGBA 1,1,1,1 = no tint) |
| In-range bloom hue | `_EmissiveColor` (HDR) | `TargetAimVisualizer.InRangeEmissiveColor` + `EmissiveIntensity` | HDR emissive. R/G/B set hue via `TileHighlightEntry`. `EmissiveIntensity` float controls brightness multiplier separately. Game default intensity ≈ 15. |
| Out-of-range colour | `OutOfRangeColor` field | `TargetAimVisualizer.OutOfRangeColor` | **WIP — does not currently take effect.** The field is written but the game's rendering path does not read it as expected. Source file required to determine correct patch point. |

Float parameters set directly on the component instance (sentinel `-1` = leave unchanged):

| Field | Config slot |
|---|---|
| `AnimationScrollSpeed` | `TargetAimVisualizer.AnimationScrollSpeed` |
| `Width` | `TargetAimVisualizer.Width` |
| `MinimumHeight` | `TargetAimVisualizer.MinimumHeight` |
| `MaximumHeight` | `TargetAimVisualizer.MaximumHeight` |
| `DistanceToHeightScale` | `TargetAimVisualizer.DistanceToHeightScale` |

**`_Color` property:** `HasProperty("_Color")` returns true on this shader but the property is a legacy compatibility stub — writes to it via `MaterialPropertyBlock` have no visible effect. Do not use it.

#### LineOfSightVisualizer — implementable (original finding overturned)

**Corrected finding:** The runtime scan incorrectly identified the components as Shapes library types. Ghidra decompilation of `Resize()` and `SetVisible()` confirmed the components are standard Unity `LineRenderer` instances. See Section 10 for full details and implementation guidance.

---

## 4. Patching strategy

All patches are Harmony `Postfix` patches registered in `RegisterPatches()`. Each is a private static inner class of `HUDCustomizerPlugin`.

### The standard patch pattern — full source

Every functional patch follows this exact structure. The try/catch is always present — a patch failure must never crash the game:

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

### The EntityHUD/UnitHUD guard — full source

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

### Scan-only patch pattern — full source

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

`ObjectivesTracker` inherits from `BaseButton`, not `InterfaceElement` directly. The `.Cast<Il2CppInterfaceElement>()` still works because `BaseButton` ultimately inherits from `InterfaceElement` in the native hierarchy — Il2Cpp interop resolves this correctly. However, if you ever need to cast to `BaseButton`-specific methods, you must cast to `Il2CppMenace.UI.BaseButton` first.

### Patch selection rationale

| Patch class | Method patched | Slot | Why this method |
|---|---|---|---|
| `Patch_UnitHUD_SetActor` | `UnitHUD.SetActor` | — | Primary init — called when a unit is assigned to the HUD |
| `Patch_UnitHUD_Show` | `UnitHUD.Show` | — | Re-applies in case the game resets inline styles on show |
| `Patch_EntityHUD_InitBars` | `EntityHUD.InitBars` | — | Bar init for non-unit entities; UnitHUD excluded by `TryCast` guard |
| `Patch_ObjectiveHUD_SetObjective` | `ObjectiveHUD.SetObjective` | — | Per-objective init; fires once per objective instance |
| `Patch_ObjectivesTracker_Init` | `ObjectivesTracker.Init` | — | Single init point for the tracker panel |
| `Patch_MissionInfoPanel_Init` | `MissionInfoPanel.Init` | — | Single init point for the mission info panel |
| `Patch_MovementHUD_SetDestination` | `MovementHUD.SetDestination` | — | Fires on each movement target selection; re-applies because labels update each call |
| `Patch_BleedingWorldSpaceIcon_SetText` | `BleedingWorldSpaceIcon.SetText` | — | Fires on creation and each text update; confirmed in source |
| `Patch_MovementVisualizer_ShowPath` | `MovementVisualizer.ShowPath` | — | Re-applies colour after each path update, since the game resets colours from its own state |
| `Patch_TargetAimVisualizer_UpdateAim` | `TargetAimVisualizer.UpdateAim` | — | Re-applies `MaterialPropertyBlock` after each mesh rebuild |
| `Patch_UnitHUD_OnUpdate_Scan` | `UnitHUD.OnUpdate` | 155 | **Scan-only** — entire patch is temporary; DELETE when UnitHUD structure is confirmed |
| `Patch_WorldSpaceIcon_Update_Scan` | `WorldSpaceIcon.Update` | 142 | **Scan-only** — catches `SimpleWorldSpaceIcon` which does not override `Update`; `BleedingWorldSpaceIcon` excluded by type name check because it overrides `Update` at slot 142; DELETE once confirmed |

Slot numbers are confirmed from game source files. Harmony resolves methods by name, not slot — the slots are documented here for virtual dispatch verification only.

---

## 5. Config system

### Schema overview

The JSON config uses four encoding conventions:

| Convention | Used by | Format |
|---|---|---|
| Inline string | Unit/entity HUD bar colours, badge tint | `"R, G, B"` or `"R, G, B, A"` — R/G/B 0-255 integers, A 0.0-1.0 float. `""` = leave unchanged. |
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
    VisualizerCustomizer.TryApply();
    CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
}
```

1. `LoadConfig()` — reads and deserialises the JSON file. On parse failure, backs up the broken file to `.bak` and regenerates defaults via `HUDConfig.BuildDefaultConfig()`. Also calls `CombatFlyoverCustomizer.Apply()` and `CombatFlyoverCustomizer.LogSummary()` internally as part of the summary sequence.
2. `FontCustomizer.InvalidateCache()` — clears `_fontCache` so it is rebuilt on the next `Apply()` call.
3. `ReapplyToLiveElements()` — iterates `_registry`, re-applies scale/colours/fonts to UIElements, prunes stale entries.
4. `TileCustomizer.TryApply()` — re-applies tile highlight overrides to the `TileHighlighter` singleton.
5. `USSCustomizer.TryApply()` — re-applies USS theme colour overrides to `UIConfig`.
6. `UnitCustomizer.ApplyFactionHealthBarColors()` — re-applies faction health bar colours to `UIConfig`.
7. `VisualizerCustomizer.TryApply()` — re-discovers live visualizer instances via `FindObjectsOfType` and re-applies all colour and parameter overrides.
8. `CombatFlyoverCustomizer.Apply(Config.CombatFlyover)` — pushes the freshly-loaded config to the CombatFlyoverText bridge. This second call (the first is inside `LoadConfig()`) is necessary because `LoadConfig()` also runs at startup where this `OnUpdate` block does not execute. Colour and timing changes take effect on the next flyover that fires after the reload.

Hot-reload is only fully functional in tactical scenes. `TileHighlighter.Exists()` returns false outside tactical, so `TryApply()` silently no-ops. `VisualizerCustomizer.TryApply()` will find no instances outside tactical and silently returns. The config reload and log summary still fire regardless of scene.

---

## 7. The scan system

`Scans.cs` contains discovery utilities used during development. **All scans are disabled by default** (`EnableScans: false`) and each fires at most once per session regardless of hot-reload.

Scans write to `Log.Msg()` unconditionally — they do not respect `DebugLogging`. All scan entry points call `CheckGate()` first:

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
| `RunWorldSpaceIconScan(element, typeName)` | `Patch_WorldSpaceIcon_Update_Scan` | Discovers `WorldSpaceIcon` subtypes; fires once per concrete type via `HashSet<string>` | `SimpleWorldSpaceIcon` has no text or colour elements — confirmed from source. Entire scan patch can be deleted. |
| `RunTargetAimMaterialScan(instance)` | `Patch_TargetAimVisualizer_UpdateAim` | Dumps all shader properties on the aim material's `MeshRenderer` | Confirmed: shader is `HDRP/Unlit`, colour property is `_UnlitColor`, emissive is `_EmissiveColor`. Delete once no longer needed. |

### Scan call-sites that carry DELETE comments

- `Patch_ObjectiveHUD_SetObjective` — scan call only; patch itself stays
- `Patch_ObjectivesTracker_Init` — scan call only; patch itself stays
- `Patch_MissionInfoPanel_Init` — scan call only; patch itself stays
- `Patch_UnitHUD_OnUpdate_Scan` — **entire patch class** is scan-only; delete the whole class
- `Patch_BleedingWorldSpaceIcon_SetText` — scan call only; patch itself stays
- `Patch_WorldSpaceIcon_Update_Scan` — **entire patch class** is scan-only; delete the whole class
- `Patch_TargetAimVisualizer_UpdateAim` — scan call only; the patch itself stays (it re-applies material colours)

---

## 8. Il2Cpp and UIToolkit notes

### Il2Cpp casting

Menace is an Il2Cpp game. Managed C# types generated by Il2CppInterop do not share a common managed inheritance hierarchy — `UnitHUD` is not a subtype of `InterfaceElement` in C#, even though it is at the native level. You cannot use `is` or `as` between generated Il2Cpp types:

```csharp
// Throws InvalidCastException if the native type is not compatible.
// Use when you are certain of the type and want a hard failure if wrong.
var el = __instance.Cast<Il2CppInterfaceElement>();

// Returns null if not compatible.
// Use for conditional type checks (e.g. the EntityHUD/UnitHUD guard).
if (__instance.TryCast<Il2CppUnitHUD>() != null) return;
```

Both methods verify type compatibility at the native level through the Il2Cpp bridge. A failed `.Cast<T>()` throws — this is caught by the try/catch present in every patch postfix.

### Il2Cpp field reflection limitations

Private fields on Il2Cpp types are not accessible via standard C# reflection. `GetType().GetFields(NonPublic | Instance)` on an Il2Cpp-wrapped object returns only the C# proxy class's own infrastructure fields (`isWrapped`, `pooledPtr`) — not the actual game fields. This is a fundamental limitation of Il2CppInterop.

Consequences for this codebase:
- Any future feature requiring access to private Il2Cpp fields must either: (a) target a public field/method on the same type, (b) use a child `Component` that has a registered Il2Cpp binding, or (c) use direct field-offset access via Il2Cpp pointer arithmetic.

Similarly, `GetComponent<T>()` only works if Il2CppInterop has generated a binding for `T`. If `GetComponent` returns only the raw base `Component` type, the binding is missing — but the component may still be accessible via a known field offset on the parent type (see `LineOfSightVisualizer` in Section 10 for an example of how Ghidra resolved this).

### GetClasses() — why the helper exists and full source

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

**Always use `HUDCustomizerPlugin.GetClasses(ve)`** rather than `ve.GetClasses()` directly. The outer `catch {}` is intentional — if reflection fails for any reason the method returns an empty list rather than crashing.

### UIToolkit Q() — the null className argument

`element.Q(name, className)` finds the first descendant matching name and/or USS class. In Il2Cpp interop, both parameters are required. When querying by element name only, always pass `(string)null` as the second argument:

```csharp
// Correct
var ve = element.Q("Hitpoints", (string)null);

// Wrong -- the single-argument overload may not resolve correctly via Il2Cpp binding
var ve = element.Q("Hitpoints");
```

### Inline styles vs USS class styles

`ve.style.*` sets inline styles. Inline styles always take precedence over USS class styles. HUDCustomizer works exclusively through inline overrides — it never modifies stylesheets. This means:

- Overrides apply immediately when set
- They survive USS reloads
- The game's own code can overwrite them if it also sets inline styles after our postfix runs — this is why `UnitHUD.Show` is patched in addition to `UnitHUD.SetActor`, and why visualizer colours are re-applied in patch postfixes rather than only in `TryApply()`

### MaterialPropertyBlock pattern for MonoBehaviour renderers

For visualizers that use `MeshRenderer`, colour overrides are applied via `MaterialPropertyBlock` rather than modifying `renderer.material` directly. This avoids creating persistent material asset copies:

```csharp
var block = new MaterialPropertyBlock();
renderer.GetPropertyBlock(block);          // read existing state
block.SetColor("_UnlitColor", color);      // apply override
renderer.SetPropertyBlock(block);          // write back
```

`GetPropertyBlock` + `SetPropertyBlock` must always be paired — reading first preserves any other properties already set on the block. The block is local to the call site; it is not cached.

### Font loading constraints

`Font.CreateDynamicFontFromOSFont` is stripped in this Il2Cpp build. OS fonts cannot be loaded at runtime. Only fonts returned by `Resources.FindObjectsOfTypeAll<Font>()` are available. The confirmed list from scan:

```
Jura-Regular, Jura-Bold, OCRAStd, Inconsolata-SemiBold, NotInter-Regular
NotoSansJP-Regular, NotoSansKR-Regular, NotoSansSC-Regular, NotoSansTC-Regular
```

`LegacyRuntime` and `LiberationSans` are also returned by the scan but are Unity built-ins. They can technically be used but are not designed for this game's UI.

If a configured font name is not found in the cache, `FontCustomizer.Resolve()` logs a warning listing available names and returns null (leaving the element's current font unchanged).

### TryParseColor — full source

All inline string colours go through this helper. R/G/B are 0-255 integers (parsed as floats, divided by 255). A is 0.0-1.0. Out-of-range values are clamped with a warning. An empty string returns `false` silently — this is the "leave unchanged" sentinel and is expected:

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

### ToColor — TileHighlightEntry converter

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

### Step 1 — Identify the target

- **Per-element HUD** (spawned per unit/objective/etc): needs a Harmony patch on its init method
- **Singleton** (one shared instance like `TileHighlighter` or `UIConfig`): call directly from `OnTacticalReady` and the hot-reload block
- **MonoBehaviour visualizer** (world-space 3D component): use `FindObjectsOfType` in `VisualizerCustomizer.TryApply()` and re-apply in a postfix on the method that resets state

If the element structure is unknown, enable `EnableScans: true`, run the game into a tactical mission, and check the MelonLoader log for `[ElemScan/...]` output.

### Step 2 — Add config fields in HUDConfig.cs

Add properties to `HUDCustomizerConfig` with format and game-default comments:

```csharp
// Your new system colours
// Format: "R, G, B" or "R, G, B, A"  (R/G/B = 0-255 integers, A = 0.0-1.0 float)
// Game defaults observed in scan:
//   SomeElement fill: 100, 150, 200  (blue)
public string SomeElementFillColor { get; set; } = "";
```

Add the matching entry to `BuildDefaultConfig()` in the same format as existing entries. The JSON string is a verbatim literal — all internal quotes must be escaped as `""`.

### Step 3 — Implement the customiser

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
            YourCustomizer.Apply(el);                // if adding a new customiser
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

**For a MonoBehaviour visualizer**, add the apply logic to `VisualizerCustomizer.TryApply()` using the `FindObjectsOfType` pattern. If the game resets state in a specific method, add a postfix patch on that method that calls back into `VisualizerCustomizer` to re-apply — see `Patch_MovementVisualizer_ShowPath` and `Patch_TargetAimVisualizer_UpdateAim` for the pattern.

### Step 4 — Add to FontCustomizer if the HUD has configurable text

1. Add `case "YourHUD":` to the switch in `FontCustomizer.Apply()`
2. Implement `ApplyYourHUD(Il2CppInterfaceElement el)` — use `QueryAndSet` until element names are scan-confirmed, then switch to `SetFont(el.Q(name, (string)null), ...)`
3. Add entries to `LogFontSummary()`

### Step 5 — Add a summary log call

Add `YourCustomizer.LogSummary()` and call it from the summary block in `LoadConfig()`.

### Step 6 — Remove scan scaffolding when done

- Remove the `_scanned` field and `if (!_scanned)` block from the patch
- If the patch was scan-only, delete the entire class and its `RegisterPatches()` entry
- If a `Scans` method is no longer called from anywhere, delete it from `Scans.cs`

---

## 10. Known gaps and incomplete work

### TargetAimVisualizer OutOfRangeColor — root cause confirmed, fix known

**Root cause (confirmed via Ghidra decompilation of `GameAssembly.dll`):**

`UpdateAim()` has two colour paths that both feed into a single `MaterialPropertyBlock.SetColor` call on `m_MeshRenderer` at the end of the function:

- **In-range path:** reads `OutOfRangeColor` (`this+0x30`) into local variables `uVar3/22/23/24` and passes them to `SetColor`. Despite the field name, this is the branch where the field is actually used.
- **Out-of-range path (`bVar1 == 1`):** hardcodes `0x3f800000` (1.0f = white) directly into those same local variables, **never reading `OutOfRangeColor` at all**, then passes white to `SetColor`.

`SetLineMode()` does not touch colour at all — it only toggles mesh object visibility for indirect fire mode.

`Generate2DMesh()` writes to `m_Colors` (`this+0x98`) but those vertex colours only control fade alpha at the line ends (RGB is always hardcoded `0xffffff`). Vertex colours are irrelevant to the hue problem.

**Conclusion:** The field write in `ApplyTargetAimVisualizer()` succeeds, but `UpdateAim()` overwrites the `MaterialPropertyBlock` with white on every out-of-range frame, after our postfix has already run — so it is never visible. The existing `Patch_TargetAimVisualizer_UpdateAim` postfix fires after `UpdateAim()`, which is correct, but `ApplyTargetAimVisualizer()` currently only sets `_UnlitColor` and `_EmissiveColor` — it does not set the out-of-range colour via `MaterialPropertyBlock`.

**Fix:** In `Patch_TargetAimVisualizer_UpdateAim`, after the game sets the `MaterialPropertyBlock` colour to white for out-of-range targets, apply `OutOfRangeColor` as `_UnlitColor` via `MaterialPropertyBlock` in the postfix. Since the postfix already has access to the `MeshRenderer` and builds a `MaterialPropertyBlock`, the fix is to set `_UnlitColor` to the configured `OutOfRangeColor` when the aim is out of range.

**Detecting out-of-range in the postfix:** `bVar1` is a local variable in `UpdateAim()` and is not stored on the instance. The most reliable detection is to read the `_UnlitColor` back from the `MaterialPropertyBlock` after the game sets it — if it equals white (`RGBA 1,1,1,1`) and the user has configured a non-white `OutOfRangeColor`, apply the override. Alternatively, apply `OutOfRangeColor` unconditionally as `_UnlitColor` in the postfix regardless of range state, and remove the separate `instance.OutOfRangeColor` field write from `ApplyTargetAimVisualizer()` entirely, since the field is not the operative rendering path.

**Files:** `VisualizerCustomizer.cs` → `ApplyTargetAimVisualizer()` and `Patch_TargetAimVisualizer_UpdateAim`.

---

### LineOfSightVisualizer — original finding overturned, implementable

**Original finding (incorrect):** The runtime scan concluded the components were Shapes library types with no Il2CppInterop bindings. This was wrong on two counts — there is no Shapes library in this build, and the `Line` type in `m_Lines` is `Den.Tools.Splines.Line`, a pure spline geometry data class with no renderer.

**Corrected finding (from Ghidra decompilation of `Resize` and `SetVisible`, confirmed via dump.cs):**

`LineOfSightVisualizer` manages a pool of prefab instances via `m_Lines` (`List<Den.Tools.Splines.Line[]>`, `this+0x30`) and `m_Prefab` (`this+0x20`). The three non-Transform components on each `'LineOfSightLine(Clone)'` GameObject that the runtime scan failed to resolve are standard Unity `LineRenderer` components — not a third-party library. Il2CppInterop failed to resolve them at scan time due to the generic `Component` cast path used in the scan, not due to missing bindings.

Evidence from Ghidra:
- `Resize()` calls `GetComponent<T>()` with type token `DAT_18399fe80`, iterates 3 components per instance, and calls colour setters on each.
- The two colour setter functions (`FUN_181ca48c0` / `FUN_181ca4790`) both use type token `DAT_18396f1d0`, write a `Color` struct to field offsets `0x5c–0x68` and `0xa8–0xb4` respectively, then call a material property update and a mesh dirty function. These are the `LineRenderer.startColor` and `LineRenderer.endColor` property setters.
- `SetVisible()` calls `GameObject.SetActive(bool)` on each line's GameObject — no colour access.

**To implement:** Use `FindObjectsOfType<LineOfSightVisualizer>()`. For each instance, iterate the `m_Lines` list (Il2Cpp field access at `this+0x30`). Each `Line[]` array element corresponds to a pooled GameObject — get the `LineRenderer` component via `GetComponent<LineRenderer>()` and set `startColor` / `endColor`. Re-apply hook: patch `SetVisible(bool _visible)` as a postfix, since this is called each time the game activates LOS lines — same pattern as `Patch_MovementVisualizer_ShowPath`.

**Status:** Scan infrastructure already removed. `VisualizerCustomizer.LogSummary()` and `README.md` incorrectly document this as unsupported — both need updating when implementation begins.

**Files:** `VisualizerCustomizer.cs`, `HUDConfig.cs`, `HUDCustomizer.cs` (new patch on `LineOfSightVisualizer.SetVisible`).

---

### BleedingWorldSpaceIcon — element name unconfirmed by scan

**What exists:** `Patch_BleedingWorldSpaceIcon_SetText` fires correctly. `FontCustomizer.ApplyBleedingWorldSpaceIcon()` uses `QueryAndSet(el, "TextElement", ...)`.

**Source analysis:** The field is declared as `private readonly Label m_TextElement`. In UIToolkit, the UXML element name is set in the `.uxml` file at `Tactical/Elements/bleeding_world_space_icon`. Following the same translation pattern used by all other confirmed elements (`m_NameLabel` → `ObjectiveName`, `m_MissionDurationLabel` → `MissionDuration`), the predicted UXML name is `TextElement`. The current `QueryAndSet` call will log a warning if this prediction is wrong — run with `EnableScans: true` to confirm.

**What is missing:** Scan confirmation. Once confirmed, change `QueryAndSet` to `SetFont(el.Q("TextElement", (string)null), ...)` in `ApplyBleedingWorldSpaceIcon()`.

**Files:** `FontCustomizer.cs` → `ApplyBleedingWorldSpaceIcon()`.

---

### SimpleWorldSpaceIcon — no customisable content

**Conclusion:** `SimpleWorldSpaceIcon` has no fields beyond its UXML path constant (`simple_world_space_icon`). It contains no `m_TextElement`, no icon reference, and no colour fields. There is nothing to customise.

**Action required:** Delete `Patch_WorldSpaceIcon_Update_Scan` entirely (the whole class) and remove it from `RegisterPatches()`. Delete `Scans.RunWorldSpaceIconScan()` from `Scans.cs` if it has no other callers.

**Files:** `HUDCustomizer.cs` → `Patch_WorldSpaceIcon_Update_Scan` and `RegisterPatches()`, `Scans.cs` → `RunWorldSpaceIconScan()`.

---

### UIConfig rarity and mission colours — confirmed but not exposed

**What exists:** All fields confirmed from `dump.cs` with exact default values (see UIConfig table in Section 3).

**What is missing:** None of the rarity, mission state, or `ColorPositionMarkerDelayedAbility` fields are exposed in the config or set in any customiser.

**`[UssColor]` status (corrected from earlier docs):**
- Rarity fields (`ColorCommonRarity` etc.) — **no** `[UssColor]`. Direct colour values; follow the `FactionHealthBarColors` pattern (`UnitCustomizer.cs`).
- Mission state fields (`ColorMissionPlayable` etc.) — **yes** `[UssColor]`, confirmed from `dump.cs`. Follow the `USSCustomizer.TryApply()` pattern, not `FactionHealthBarColors`.
- `ColorPositionMarkerDelayedAbility` — **no** `[UssColor]`. Direct colour value; follow the `FactionHealthBarColors` pattern.

**To implement:** Add a new config class (e.g. `RarityColorsConfig`) for the rarity and `ColorPositionMarkerDelayedAbility` fields, and add mission colour fields to `USSColorsConfig`. Apply rarity/delayed-ability fields in a new section of `UnitCustomizer.cs` following the `if (entry.Enabled) uiConfig.FieldName = ToColor(...)` pattern. Apply mission colour fields inside `USSCustomizer.TryApply()` alongside the existing USS fields.

**Files:** `USSCustomizer.cs`, `UnitCustomizer.cs`, `HUDConfig.cs`.

---

### UnitHUD element scan — ready to delete

**Status:** The element tree is fully confirmed by scan log (see complete tree in Section 3). `Patch_UnitHUD_OnUpdate_Scan` has served its purpose.

**Action required:** Delete the entire `Patch_UnitHUD_OnUpdate_Scan` class and remove it from `RegisterPatches()`.

**Files:** `HUDCustomizer.cs` → `Patch_UnitHUD_OnUpdate_Scan` and `RegisterPatches()`.

---

### ObjectivesTracker progress bar — colour customisation not implemented

**What exists:** The scan confirms the `ProgressBar` inside `BottomRow` has the same `Pickable > Fill/PreviewFill` structure as unit bars. The game source shows `FILL_COLOR` and `PREVIEW_FILL_COLOR` as `private static readonly Color` fields — the game sets these from its own constants, not from `UIConfig`. They can be overridden via inline styles just like unit bars.

**What is missing:** No colour customisation has been implemented for the tracker's progress bar. `UnitCustomizer.ApplyBarColours()` targets bars by name — `"ProgressBar"` is the element name in the scan, with `Pickable > Fill/PreviewFill` as children. The existing `ApplyBarColours()` method could be called with `"ProgressBar"` on the `ObjectivesTracker` element if appropriate config fields were added.

**To implement:** Add config fields (e.g. `ObjTrackerBarFillColor`, etc.), add `Apply()` logic to `UnitCustomizer.cs` or a new customiser, call it from `Patch_ObjectivesTracker_Init`, and add a `case "ObjectivesTracker":` to `ReapplyToLiveElements()`.

**Files:** `HUDCustomizer.cs` → `Patch_ObjectivesTracker_Init` and `ReapplyToLiveElements()`, `UnitCustomizer.cs` or new file, `HUDConfig.cs`.

---

### ObjectiveHUD, MissionInfoPanel — font only, confirmed complete

**Status:** Both have been confirmed as text-only elements from source and scan. `ObjectiveHUD` has no bars, backgrounds, or tintable elements beyond icon sprites (which use USS class colouring, not inline style tints). `MissionInfoPanel` is two label elements inside a plain container.

**No further action needed** unless icon tinting or background colours are desired in the future. The current font-only implementation is correct for both.

---

## 11. Dump.cs investigation findings — Menace.UI.Tactical types

All 22 target types extracted from `Assembly-CSharp` via `dump.cs`. Findings are grouped by verdict.

**Patching strategy key:**
- *Registry pattern* — type inherits `InterfaceElement` / `InteractiveElement` / `BaseHUD`; use Harmony postfix + `Register(el, "TypeName")` + `ReapplyToLiveElements()` case.
- *Standalone* — type does not inherit a UIElement base; use direct field access via the patch instance; do not register.

---

### SkillBarButton — high value, implement

**Inherits:** `BaseButton, ISkillBarElement` → `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_SkillIcon` (`VisualElement`) — icon background tint via `style.unityBackgroundImageTintColor`
- `m_SelectedOverlay` (`VisualElement`) — selected state overlay tint
- `m_HoverOverlay` (`VisualElement`) — hover state overlay tint
- `m_ActionPointsLabel` (`Label`) — font/colour
- `m_UsesLabel` (`Label`) — font/colour
- `m_HotkeyLabel` (`Label`) — font/colour

**Hook:** `Init(UITactical _ui)` — fires once on creation. Re-apply hook: `Show()` (game may reset inline styles on show, same pattern as `UnitHUD`).

**Note:** `m_PreviewButtonOpacity` and `m_PreviewAnimationProgress` are animation state floats managed by the game's own update loop (`Update(float, bool)`). Do not write to them directly — they will be overwritten each frame.

**Element names:** Not scan-confirmed. Use `QueryAndSet` / `RunElementScan` to confirm UXML names before switching to direct `el.Q()` calls.

---

### BaseSkillBarItemSlot — high value, implement

**Inherits:** `BaseButton, ISkillBarElement` → `InterfaceElement`. Registry pattern. Both `SkillBarSlotWeapon` and `SkillBarSlotAccessory` inherit from this — one patch on the base covers both slot types.

**Customisable fields:**
- `m_Background` (`VisualElement`) — slot background tint
- `m_ItemIcon` (`VisualElement`) — item icon tint
- `m_Cross` (`VisualElement`) — the unusable/disabled X overlay tint

**Hook:** `Init(UITactical _ui)` — virtual, overridden by both subclasses. Patch the base class method; apply the `TryCast<SkillBarSlotWeapon>` / `TryCast<SkillBarSlotAccessory>` guard pattern if subclass-specific behaviour is needed, otherwise the base patch covers both.

**Element names:** Not scan-confirmed. Use `RunElementScan` at `Init` time.

---

### SkillBarSlotWeapon — partial, implement alongside BaseSkillBarItemSlot

**Inherits:** `BaseSkillBarItemSlot`. Registry pattern.

**Customisable fields beyond base:**
- `m_NameLabel` (`Label`) — weapon name label font/colour

**Hook:** `Init(UITactical _ui)` (override). A separate patch on `SkillBarSlotWeapon.Init` is needed only if the weapon name label requires its own font config entry. Otherwise covered by the `BaseSkillBarItemSlot` patch.

---

### SkillBarSlotAccessory — no own fields

**Inherits:** `BaseSkillBarItemSlot`. No fields beyond the base class. Fully covered by `BaseSkillBarItemSlot` patch. No separate patch needed.

---

### SimpleSkillBarButton — partial, implement

**Inherits:** `BaseButton` → `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_Label` (`Label`) — button text font/colour
- `m_HotkeyLabel` (`Label`) — hotkey label font/colour
- `m_Hover` (`VisualElement`) — hover overlay tint

**Hook:** `SetText(string _text)` — fires on creation with text already populated. This is the correct hook (same rationale as `BleedingWorldSpaceIcon.SetText`).

**Element names:** Not scan-confirmed.

---

### TurnOrderFactionSlot — partial, implement

**Inherits:** `InteractiveElement` → `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_InactiveMask` (`VisualElement`) — overlay tint shown when faction is inactive
- `m_Selected` (`VisualElement`) — selection highlight tint
- `m_InactiveIcon` (`VisualElement`) — inactive state icon tint

**Hook:** `Init(FactionTemplate _factionTemplate, int _leftToActActors, int _totalActors, bool _activeFaction)` — fires once per slot.

**Element names:** Not scan-confirmed.

---

### UnitsTurnBarSlot — partial, implement

**Inherits:** `BaseHoverButton, IDisposable`. Registry pattern (BaseHoverButton ultimately inherits InterfaceElement).

**Customisable fields:**
- `m_OverlayElement` (`VisualElement`) — the animated overlay tint (the game uses `GRAY_OVERLAY_COLOR` as its own constant; this can be overridden via inline style on the element)
- `m_Selected` (`VisualElement`) — selected unit highlight tint
- `m_PortraitElement` (`VisualElement`) — portrait tint

**Hook:** `Init(UITactical _screen)` for initial setup. Re-apply hook: `SetActor(Actor _actor)` — fires when the slot's unit changes, likely resets inline styles.

**Note:** `GRAY_OVERLAY_COLOR` is a `private static readonly Color` — it cannot be patched directly. The overlay colour is applied to `m_OverlayElement` via inline style at some point; patching `SetActor` and overwriting the inline style after the game sets it is the correct approach.

**Element names:** Not scan-confirmed.

---

### SelectedUnitPanel — partial, implement

**Inherits:** `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_Portrait` (`VisualElement`) — unit portrait tint
- `m_UnitWindowHeader` (`VisualElement`) — header background tint
- `m_ConditionLabel` (`Label`) — condition text font/colour
- `m_ActionPointsLabel` (`Label`) — AP label font/colour

**Hook:** `SetActor(Actor _actor, bool _actorChanged)` — fires on unit selection change. `Init()` fires once at scene load but without actor data; `SetActor` is the correct hook for content-populated customisation.

**Note:** `m_StatsContainer`, `m_PerksContainer`, `m_EmotionalStatesContainer` etc. are layout containers populated with `TacticalUnitInfoStat` children — customise those via a separate `TacticalUnitInfoStat` patch rather than targeting the containers here.

**Element names:** Not scan-confirmed.

---

### DelayedAbilityHUD — partial, hybrid patching

**Inherits:** `BaseHUD, IDisposable` → `InteractiveElement` → `InterfaceElement`. Registry pattern for the UIElement surface. Material surface handled separately.

**Customisable fields:**
- `m_ProgressElement` (`VisualElement`) — progress ring/bar fill tint via inline style
- `m_WorldSpaceMarkerMaterial` (`Material`) — world-space marker colour via `MaterialPropertyBlock` or `SetWorldSpaceMarkerColor(Color)`

**Hook:** `SetAbility(DelayedOffmapAbility _ability)` — fires on assignment. Re-apply: `SetProgressPct(float _pct)` — fires on each progress update and likely resets `m_ProgressElement` inline styles.

**Relationship to `ColorPositionMarkerDelayedAbility`:** The UIConfig field `ColorPositionMarkerDelayedAbility` (offset `0x608`) is the game's own source for the world-space marker colour — it feeds `SetWorldSpaceMarkerColor()` somewhere in the game's update logic. Wiring up that UIConfig field via `UnitCustomizer` (non-USS pattern) may be sufficient for the marker colour without a direct Material patch. Confirm by testing whether setting `ColorPositionMarkerDelayedAbility` on `UIConfig.Get()` changes the marker in-game before implementing the Material path.

---

### TacticalUnitInfoStat — minor, implement

**Inherits:** `InteractiveElement` → `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_ValueLabel` (`Label`) — stat value font/colour
- `m_Icon` (`VisualElement`) — stat icon tint

**Hook:** `Init(PropertyDisplayConfig _property, float _value)` — fires once per stat row on creation.

**Note:** Multiple `TacticalUnitInfoStat` instances are created dynamically inside `SelectedUnitPanel.m_StatsContainer`. Each fires its own `Init` — the patch will cover all of them automatically via the registry.

---

### StatusEffectIcon — minor, implement

**Inherits:** `InteractiveElement` → `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_StackCountLabel` (`Label`) — stack count font/colour

**Hook:** Two overloads — `Init(SkillTemplate _skillTemplate)` and `Init(Skill _skill)`. Patch both, or patch the base `InteractiveElement` init if both call a common base (confirm via scan).

**Note:** The icon image itself is set via USS class on the root element, not via a named child `VisualElement` — tinting the root element's `unityBackgroundImageTintColor` is possible but would tint the entire element including the stack label background. Scope font customisation to the label only unless a per-icon tint config is explicitly desired.

---

### TurnOrderPanel — minor, implement

**Inherits:** `InterfaceElement`. Registry pattern.

**Customisable fields:**
- `m_RoundNumberLabel` (`Label`) — round number font/colour

**Hook:** `UpdateFactions()` — no `Init` method present. This fires when faction data changes; it is the earliest reliable point where the panel has content. Use the `_scanned` flag pattern to fire `RunElementScan` once for UXML name confirmation.

---

### StructureHUD — free, scale config only

**Inherits:** `EntityHUD`. The existing `Patch_EntityHUD_InitBars` already fires for `StructureHUD` instances (the `TryCast<UnitHUD>` guard only skips `UnitHUD` subtypes, not `StructureHUD`). Bar colours and fonts from `EntityHUD` config already apply at no implementation cost.

**What is missing:** No `StructureHUDScale` config entry exists. Add one following the `EntityHUDScale` pattern in `HUDConfig.cs` and `UnitCustomizer.Apply()`. Verify in-game that structures with visible HUDs actually appear in tactical before prioritising this.

**Files:** `HUDConfig.cs`, `UnitCustomizer.cs`.

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
| `WorldSpaceIcon` | Confirmed empty — no fields at all; consistent with `SimpleWorldSpaceIcon` findings |
| `SkillBar` | Layout container wrapper only; no Color or Label fields |
| `ISkillBarElement` | Interface definition only |
