## Context (carry forward)
- Project: HUDCustomizer — MelonLoader mod for MENACE (Il2Cpp, Unity 6, .NET 6)
- Plugin loaded via Menace Modpack Loader; all game types prefixed Il2CppMenace.* or Il2CppShapes.*
- Renderer confirmed: Il2CppShapes.Line (NOT UnityEngine.LineRenderer — that finding was retracted)
- DLL: Il2CppShapesRuntime.dll — namespace inside is Il2CppShapes, class is Line
- Colour properties: ColorStart (set_ VA 0x181CA48C0), ColorEnd (set_ VA 0x181CA4790) — both on Il2CppShapes.Line
- Colour is ONLY written in Resize(int _n) — never in Update() or SetVisible(). Confirmed by Ghidra decompilation.
- Pool structure: m_Lines is List<Line[]>, each Line[] has exactly 3 Shapes.Line entries (fade-in, solid, fade-out)
- GetComponentsInChildren<T> throws a fatal Il2CppInterop type-initialiser exception — NEVER use it
- Use indexed GetChild(i).GetComponent<Il2CppShapes.Line>() traversal only
- Existing customisers for reference: VisualizerCustomizer.cs (MovementVisualizer, TargetAimVisualizer patterns)
- Config I/O: HUDConfig.cs BuildDefaultConfig() is a single verbatim C# string literal — do not split it
- Hot-reload sequence: LoadConfig() → FontCustomizer.InvalidateCache() → ReapplyToLiveElements() → TileCustomizer.TryApply() → USSCustomizer.TryApply() → UnitCustomizer.ApplyFactionHealthBarColors()
- LoadConfig() summary call block is in HUDCustomizer.cs — insert new summary call at the end of that block

## Hard constraints — NEVER violate
- NEVER use foreach on Il2Cpp lists — indexed for loops only
- NEVER call GetComponentsInChildren<T> for any type
- NEVER call TacticalEventHooks.Initialize or Intercept.Initialize
- NEVER access game singletons in OnInitialize
- NEVER patch MonoBehaviour.OnEnable
- NEVER use is/as for Il2Cpp types — use .Cast<T>() or .TryCast<T>()
- NEVER omit second argument to element.Q() — always write element.Q("Name", (string)null)
- NEVER call ToColor() without checking entry.Enabled first
- NEVER split BuildDefaultConfig() string or use string concatenation — it MUST remain a single verbatim literal
- Every Harmony patch postfix MUST be wrapped in try/catch; catch block calls Log.Error(...) with the patch class name
- Every new patch class MUST be registered with harmony.PatchAll(typeof(YourPatchClass)) in RegisterPatches()
- Do not write ColorMode — the enum value is inherited from the prefab and must not be changed

## Task
Implement LineOfSightVisualizer colour customisation. Make only the changes listed below. Do not refactor existing code, add unrelated features, or modify files not mentioned.

### 1. HUDConfig.cs
Add a `LineOfSightSettings` class and property following the exact same structure as the existing `VisualizerSettings` (MovementVisualizer / TargetAimVisualizer):
- One `ColorEntry LineColor` field with default RGB matching the game's prefab colour (use white: 255,255,255 if unknown — the value will be overridden by user config)
- Add `LineOfSightSettings LineOfSight { get; set; }` property to `HUDCustomizerConfig`
- Add `"LineOfSight": { "LineColor": { "Enabled": false, "R": 255, "G": 255, "B": 255, "A": 255 } }` to `BuildDefaultConfig()` inside the single verbatim string literal — place it adjacent to the existing Visualizer block

### 2. VisualizerCustomizer.cs (or equivalent customiser file)
Add a `static void ApplyLineOfSightColor(LineOfSightVisualizer instance, Color color)` method that:
- Iterates `instance.transform.childCount` with an indexed for loop
- Gets each child via `instance.transform.GetChild(i)`
- Gets the `Il2CppShapes.Line` component via `child.gameObject.GetComponent<Il2CppShapes.Line>()`
- Skips children where the component is null
- Applies colour using the exact fade pattern below (i % 3 determines position in group):
  - posInGroup == 0: ColorStart=(R,G,B,0), ColorEnd=(R,G,B,A)   // fade in
  - posInGroup == 1: ColorStart=(R,G,B,A), ColorEnd=(R,G,B,A)   // solid
  - posInGroup == 2: ColorStart=(R,G,B,A), ColorEnd=(R,G,B,0)   // fade out

Add a static `_currentLOSColor` field (Color) to hold the last-applied colour.

Add a `static void TryApplyLineOfSight(HUDCustomizerConfig cfg)` method that:
- Guards: if (!cfg.LineOfSight.LineColor.Enabled) return
- Stores `_currentLOSColor = ToColor(cfg.LineOfSight.LineColor, "LineOfSight.LineColor")`
- Calls ApplyLineOfSightColor on every live LineOfSightVisualizer instance (use `UnityEngine.Object.FindObjectsOfType<Il2CppMenace.Tactical.LineOfSightVisualizer>()` with an indexed for loop — no foreach)

Add a `static void LogLineOfSightSummary()` method following the existing `LogSummary()` pattern.

### 3. HUDCustomizer.cs — Harmony patch
Add a new inner patch class `LOSResizePatch`:

```csharp
[HarmonyPatch]
private static class LOSResizePatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method(
            typeof(Il2CppMenace.Tactical.LineOfSightVisualizer),
            "Resize",
            new[] { typeof(int) });

    [HarmonyPostfix]
    private static void Postfix(Il2CppMenace.Tactical.LineOfSightVisualizer __instance)
    {
        try
        {
            if (!_config.LineOfSight.LineColor.Enabled) return;
            VisualizerCustomizer.ApplyLineOfSightColor(__instance, VisualizerCustomizer._currentLOSColor);
        }
        catch (Exception ex) { Log.Error($"[LOSResizePatch] {ex}"); }
    }
}
```

Register it in `RegisterPatches()`:
```csharp
harmony.PatchAll(typeof(LOSResizePatch));
```

### 4. HUDCustomizer.cs — hot-reload
In the hot-reload sequence, add a call to `VisualizerCustomizer.TryApplyLineOfSight(_config)` immediately after the existing `VisualizerCustomizer` call.

### 5. HUDCustomizer.cs — LoadConfig() summary block
Add at the end of the summary call block:
```csharp
VisualizerCustomizer.LogLineOfSightSummary();
```

## Stop conditions
- Stop after completing all 6 steps above.
- Stop and ask before modifying any file not listed above.
- Stop and ask before adding any NuGet package or new dependency.
- After each file is modified, output: ✅ [filename] — [one-line summary of what changed]

## Success criteria
- Project compiles without errors or warnings
- New `LineOfSight` block appears in regenerated HUDCustomizer.json with Enabled: false default
- Enabling `LineColor` in the config and pressing F8 in a tactical mission recolours all visible LineOfSight lines
- LoadConfig() log includes a LineOfSight summary line
- No errors or warnings in the MelonLoader log during normal play