# CombatFlyoverText — AI Agent Working Brief

This file is written for AI agents performing implementation work on the CombatFlyoverText mod. Read this before writing any code. It supersedes all previous versions.

If your work touches configurable values (colours, timing, the enable toggle) or the relationship with HUDCustomizer, also read `CombatFlyoverIntegration-AI.md` before proceeding.

---

## What this mod does

Intercepts tactical combat events and displays aggregated combat results as `DropdownText` flyover elements above the attacker unit in the tactical scene — using the same UI component the game uses for "AP increased!" and skill result notifications.

Flyovers are aggregated per skill use. A 24-shot salvo produces one set of flyovers, not 24.

**Displayed stats (all shown on the attacker's HUD):**
- Theoretical accuracy (e.g. `~65%`) — shown immediately on action commit
- Damage taken (HP delta, summed across all hits) — shown after skill resolves
- Armour damage taken (e.g. `-33 ARM`) — shown after skill resolves
- Actual accuracy (e.g. `75%`) — shown after skill resolves

**Design decisions:**
- All flyovers are routed to the attacker's HUD regardless of which unit the stat relates to. This keeps all information in one place.
- Shots ratio (`18/24`) was removed — actual accuracy percentage conveys the same information more concisely.
- Theoretical accuracy fires at `InvokeOnSkillUse` time (before shots land) so it contextualises the action as it happens.

---

## The game and mod environment

- **Game:** MENACE by Overhype Studios
- **Engine:** Unity 6 (6000.0.63f1), Il2Cpp, .NET 6
- **Loader:** Menace Modpack Loader — mods implement `IModpackPlugin`
- **Il2Cpp interop:** Il2CppInterop via MelonLoader v0.7.2

---

## Critical: Il2Cpp namespace prefix

All game types in the compiled interop assembly are prefixed with `Il2Cpp`. The dummy DLL stubs use bare `Menace.*` namespaces — **these are for reference only and do not reflect compile-time type names**.

| Reference form (dummy DLL) | Compile-time form (interop assembly) |
|---|---|
| `Menace.Tactical.TacticalManager` | `Il2CppMenace.Tactical.TacticalManager` |
| `Menace.UI.UITacticalHUD` | `Il2CppMenace.UI.UITacticalHUD` |
| `Menace.UI.Tactical.UnitHUD` | `Il2CppMenace.UI.Tactical.UnitHUD` |
| `Menace.UI.Tactical.DropdownText` | `Il2CppMenace.UI.Tactical.DropdownText` |
| `Menace.States.TacticalState` | `Il2CppMenace.States.TacticalState` |
| `Menace.SDK.*` | `Menace.SDK.*` — managed assembly, no prefix |
| `Menace.ModpackLoader.*` | `Menace.ModpackLoader.*` — managed assembly, no prefix |
| `UnityEngine.UIElements.*` | `UnityEngine.UIElements.*` — Unity engine assembly, no prefix |
| `UnityEngine.UIElements.Experimental.*` | `UnityEngine.UIElements.Experimental.*` — no prefix |

**Dummy DLL virtual addresses are unreliable.** The `VA` values in `[Address]` attributes do not match the current `GameAssembly.dll`. Do not use them for Ghidra navigation. Always get current VAs from `dump.cs`.

**Generic Unity types are not accessible via compile-time `typeof`.** Use `TargetMethod()` with runtime reflection for generic types like `ValueAnimation<float>`. See the `ValueAnimationDurationPatch` pattern below.

---

## Plugin structure

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
using Menace.ModpackLoader;
using Menace.SDK;
using Il2CppMenace.States;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI.Tactical;

public class CombatFlyoverPlugin : IModpackPlugin
{
    private static MelonLogger.Instance Log;

    public void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
    {
        Log = logger;
        harmony.PatchAll(typeof(HudRegistryPatch));
        // ... register all patch classes individually
    }

    public void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _hudRegistry.Clear();
        _extendedDropdowns.Clear();
        _activeDropdownElements.Clear();
    }

    public void OnUpdate() { }
}
```

### Key lifecycle facts

- `OnInitialize` fires once at mod load, before any scene. Do not access game singletons here.
- Harmony patches: `harmony.PatchAll(typeof(YourPatchClass))` — always pass a type, never bare `harmony.PatchAll()`.
- `OnSceneLoaded` must clear all registry and tracking sets to prevent stale pointer references across tactical sessions.

---

## Critical: SDK Initialize methods are broken on this build

`TacticalEventHooks.Initialize(harmony)` and `Intercept.Initialize(harmony)` both **fail silently** on this game version. Both methods search for game types via `GetType("Menace.Tactical.TacticalManager")` — but the actual interop assembly uses `Il2CppMenace.*` names, so the lookup returns null and no patches are applied. The loader's own calls fail identically.

**Do not call `TacticalEventHooks.Initialize` or `Intercept.Initialize`.** Do not subscribe to `TacticalEventHooks.*` or `Intercept.*` events expecting them to fire — they won't.

**Replacement:** Apply all Harmony patches directly using `typeof(Il2CppMenace.*)` at compile time. See the patch section below.

---

## Confirmed: UnitHUD registry

`UITacticalHUD.m_HUDList` is `private readonly` with no generated accessor. `UnitHUD.m_Actor` is `private` — use `UnitHUD.GetActor()` (confirmed public method) instead.

The only way to obtain a `UnitHUD` for a given actor is to patch `UITacticalHUD.AddActor`, which returns the `UnitHUD` for a given `Actor` as `__result`.

```csharp
private static readonly Dictionary<IntPtr, UnitHUD> _hudRegistry = new();

[HarmonyPatch(typeof(Il2CppMenace.UI.UITacticalHUD), nameof(Il2CppMenace.UI.UITacticalHUD.AddActor))]
private static class HudRegistryPatch
{
    [HarmonyPostfix]
    private static void Postfix(Il2CppMenace.Tactical.Actor _actor, UnitHUD __result)
    {
        try
        {
            if (_actor == null || __result == null) return;
            _hudRegistry[_actor.Pointer] = __result;
        }
        catch (Exception ex) { Log?.Warning($"[CombatFlyover] HudRegistryPatch: {ex.Message}"); }
    }
}
```

Clear `_hudRegistry` in `OnSceneLoaded` to prevent stale references across tactical sessions.

---

## Confirmed: how to display a flyover

```csharp
private static void ShowFlyover(UnitHUD hud, string text)
{
    try { hud.ShowDropDownText(text, null, false); }
    catch (Exception ex) { Log.Warning($"[CombatFlyover] ShowFlyover: {ex.Message}"); }
}
```

- `_icon`: pass `null` — confirmed safe
- `_translate`: pass `false` for literal strings

**`ShowDropDownText` does not check `IsVisible()` internally.** It enqueues text unconditionally. Text plays when `UnitHUD.OnUpdate` drains `m_QueuedTexts`.

**All flyovers are routed to the attacker's HUD** — both per-target stats (HP, ARM) and per-attacker stats (accuracy). Do not route to `hud` from the target registry.

---

## KNOWN LIMITATION: ShowDropDownText fails on killed units

**This is a confirmed and fully diagnosed limitation, not a bug to fix with timing.**

When a unit is killed:

1. All `InvokeOnDamageReceived` calls fire — accumulators are fully populated
2. `UITacticalHUD.RemoveEntity` fires — detaches the HUD's `VisualElement` from the UIToolkit panel tree. `IsVisible()` still returns `true` (m_IsVisible is not reset), but the element is no longer rendered
3. ~1.5 seconds later: `InvokeOnAfterSkillUse` fires

`ShowDropDownText` called at any point after step 2 enqueues text that is never displayed because `UnitHUD.OnUpdate` no longer processes the queue for panel-detached elements.

**Resolution requires a custom world-space UI element** that does not depend on `UnitHUD` panel attachment. See open items.

---

## Confirmed: DropdownText lifecycle and duration mechanics

**Confirmed from Ghidra decompilation of `UnitHUD.OnUpdate` (current VA: `0x1807EECA0`).**

`DropdownText` has no update loop of its own. All lifecycle management happens in `UnitHUD.OnUpdate`:

```
First tick (StartTime == 0.0):
  - Write StartTime = Time.realtimeSinceStartup
  - Start fade animation: ValueAnimation<float> with durationMs=1500, opacity sin(t*π)
  - Set m_NextAnimationTime = realtimeSinceStartup + 0.35

Later ticks (StartTime != 0.0):
  - If realtimeSinceStartup - StartTime >= 1.5: remove element
  - Otherwise: return (element still alive)
```

**Key confirmed facts:**
- `StartTime` is at offset `0x4B0` on `DropdownText` — confirmed from dummy DLL and Ghidra
- `StartTime` is written by `UnitHUD.OnUpdate` on the element's **first tick**, NOT by `DropdownText.Init`
- The time function used is `Time.realtimeSinceStartup`, NOT `Time.time`
- Default display duration is **1.5 seconds**, hardcoded
- The fade animation uses a sine curve: `opacity = sin(t * π) * 1.2` clamped to 1.0, so the element fades in and out over the animation duration
- The fade animation duration is **1500ms** (`0x5dc`), matching the 1.5s lifetime
- `m_QueuedTexts` is a `Queue<DropdownText>` at offset `0x578` on `UnitHUD`
- `m_NextAnimationTime` is at offset `0x58C` on `UnitHUD` — gates when the next queued element is dequeued (every 0.35s)
- **Do NOT modify `m_NextAnimationTime`** — it is shared across the entire queue and modifications accumulate incorrectly across multiple elements

**These two offsets are extracted to named constants in `CombatFlyoverPlugin.cs`:**
```csharp
private const int Offset_DropdownText_StartTime = 0x4B0;
private const int Offset_UnitHUD_QueuedTexts    = 0x578;
```

Note: `m_NextAnimationTime` (offset `0x58C`) is documented here for reference but is **not present as a constant in the plugin** — it must not be modified (see note above).

**After a game update**, verify with:
```powershell
.\find_type.ps1 -Dump .\dump.cs -Search "DropdownText"
.\find_type.ps1 -Dump .\dump.cs -Search "UnitHUD"
```
Read the `[FieldOffset]` values for `StartTime` and `m_QueuedTexts`. Update the two constants and recompile — no other code changes needed.

**DropdownText class hierarchy:**
`DropdownText` → `InterfaceElement` → `VisualElement`

`InterfaceElement` is just a thin UXML loader wrapper. `DropdownText` IS a `VisualElement` — its `Pointer` is the `VisualElement` pointer. There is no separate `visualElement` property to access.

---

## Confirmed: DropdownText duration extension approach

Two patches work in concert to extend display duration:

### 1. StartTime extension (UnitHudOnUpdatePatch)

After `UnitHUD.OnUpdate` runs, walk the full `m_QueuedTexts` circular buffer. For any element whose `StartTime` was just written (non-zero, not yet in `_extendedDropdowns`), add `ExtraDisplaySeconds` to it. This delays the `>= 1.5s` expiry check.

Queue circular buffer layout (Il2Cpp .NET `Queue<T>`):
```
Queue object:
  +0x10  T[] _array    — pointer to backing array
  +0x18  int _head     — index of next element to dequeue
  +0x1C  int _tail     — index where next enqueue writes
  +0x20  int _size     — number of live elements

Array object:
  +0x18  int length    — capacity (not count)
  +0x20  first element — 8 bytes per pointer (x64)

Valid elements: indices [(head + i) % capacity] for i in [0, size)
```

**Critical:** always use `(head + i) % capacity` — do NOT iterate from index 0. The circular buffer head can be anywhere in the array.

### 2. Fade animation duration scaling (ValueAnimationDurationPatch)

`DropdownText.Init` registers the `DropdownText` pointer in `_activeDropdownElements`. When `ValueAnimation<float>.set_durationMs` is called for an animation whose owner is a registered `DropdownText`, multiply the duration by `FadeDurationScale`. This makes the sine-curve fade visually slower.

`ValueAnimation<float>` is a Unity engine generic type with no stable compile-time interop name. Use `TargetMethod()` with runtime reflection:

```csharp
[HarmonyPatch]
private static class ValueAnimationDurationPatch
{
    static System.Reflection.MethodBase TargetMethod()
    {
        var type = typeof(UnityEngine.UIElements.Experimental.ITransitionAnimations)
            .Assembly
            .GetType("UnityEngine.UIElements.Experimental.ValueAnimation`1")
            ?.MakeGenericType(typeof(float));
        return type?.GetProperty("durationMs")?.GetSetMethod();
    }

    [HarmonyPrefix]
    private static void Prefix(object __instance, ref int value)
    {
        if (FadeDurationScale <= 1f) return;
        var ownerProp = __instance.GetType().GetProperty("owner",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        var owner = ownerProp?.GetValue(__instance)
            as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
        if (owner == null) return;
        if (!_activeDropdownElements.Contains(owner.Pointer)) return;
        value = (int)(value * FadeDurationScale);
    }
}
```

**Current tuning constants:**
- `ExtraDisplaySeconds = 2.0f` — adds 2s to the 1.5s default lifetime (total: 3.5s per element)
- `FadeDurationScale = 2.0f` — doubles the fade from 1500ms to 3000ms
- Total visible time per element: `(1500ms × 2) + (2000ms) = 5000ms`
- Inter-element gap: `ExtraDisplaySeconds + 1.5s = 3.5s` — this is inherent to the sequential queue; it cannot be reduced without also reducing display duration

---

## Confirmed: event patches

All patches use `typeof(Il2CppMenace.*)` directly. No SDK initialization required.

### Patch: skill window open

```csharp
[HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager),
    nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnSkillUse))]
private static class SkillUsePatch
{
    [HarmonyPostfix]
    private static void Postfix(Actor _actor, Skill _skill, Tile _targetTile)
    {
        try { OnSkillUsed(...); }
        catch (Exception ex) { Log?.Warning($"[CombatFlyover] SkillUsePatch: {ex.Message}"); }
    }
}
```

**Note:** `_theoreticalAccuracy` must NOT be reset at the top of `OnSkillUsed`. It holds the `GetHitchance` value from the targeting phase and must be readable at `OnSkillUsed` time to show the immediate flyover. Reset it in the `finally` block of `OnSkillCompleted` instead.

### Patch: skill window close / flush flyovers

```csharp
[HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager),
    nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnAfterSkillUse))]
private static class AfterSkillUsePatch
{
    [HarmonyPostfix]
    private static void Postfix(Skill _skill)
    {
        try { OnSkillCompleted(...); }
        catch (Exception ex) { Log?.Warning($"[CombatFlyover] AfterSkillUsePatch: {ex.Message}"); }
    }
}
```

### Patch: shot hit — accumulate damage

Real signature: `InvokeOnDamageReceived(Entity _entity, Entity _attacker, Skill _skill, DamageInfo _damageInfo)`

Use `_damageInfo.Damage` (confirmed public `int` field) for absolute HP damage per hit. This correctly excludes armour-absorbed hits (where `Damage == 0`).

### Patch: shot miss

Real signature: `InvokeOnAttackMissed(Entity _entity, Actor _attacker, Skill _skill)`
Note: `_entity` is the **target**, `_attacker` is the attacker.

### Patch: armour changed

Real signature: `InvokeOnArmorChanged(Entity _entity, float _armorDurability, int _armor, int _animationDurationInMs)`

**Use `_armor` (the `int` parameter), not `_armorDurability` (the `float`).** `_armorDurability` is a 0–1 ratio; `_armor` is the absolute post-hit durability total matching `Entity.m_ArmorDurability`. Using `_armorDurability` produces a scale mismatch with the snapshot taken via `GetArmorDurability()`.

The armour snapshot is taken in `OnDamageReceived` (not here) — see armour snapshot strategy in the aggregation state section. `OnArmorChanged` only needs to check that a snapshot exists (`_armorSnapshot.ContainsKey(actor)`) and update `_armorLast`.

`GetArmorDurability()` returns TOTAL durability across ALL armour elements (e.g. 6 elements × 40 = 240). Normalise by `GetOriginalElementCount()` to display on the per-element scale the player sees in tooltips.

### Patch: hit chance (theoretical accuracy)

**Status: confirmed working.** `GetHitchance` fires repeatedly during targeting. `FinalValue` is on the 0–100 scale (not 0–1).

```csharp
[HarmonyPatch(typeof(Il2CppMenace.Tactical.Skills.Skill),
    nameof(Il2CppMenace.Tactical.Skills.Skill.GetHitchance))]
private static class GetHitchancePatch
{
    private static System.Reflection.FieldInfo _finalValueField;

    [HarmonyPostfix]
    private static void Postfix(object __result)
    {
        // HitChance is a VALUE TYPE — Harmony boxes it as object.
        // Must read FinalValue via reflection from the boxed struct.
        if (_finalValueField == null)
            _finalValueField = __result?.GetType().GetField("FinalValue",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
        float finalValue = (float)_finalValueField.GetValue(__result);
        _theoreticalAccuracy = finalValue; // always overwrite — last value before commit is correct
    }
}
```

`HitChance` is a **value type (struct)** — do NOT attempt to cast `__result` to `Il2CppObjectBase`. Use reflection only.

### HUD snapshot pattern

Snapshot the attacker's `UnitHUD` at `InvokeOnSkillUse` time. `UITacticalHUD.RemoveEntity` fires during skill resolution (before `InvokeOnAfterSkillUse`), so live registry lookups at flush time may miss killed units.

```csharp
private static UnitHUD _attackerHUDSnapshot;

// In OnSkillUsed:
_attackerHUDSnapshot = null;
_hudRegistry.TryGetValue(user, out _attackerHUDSnapshot);
```

Only the attacker's HUD is snapshotted. All flyovers (including per-target stats) are routed to the attacker's HUD, so no target HUD snapshots are needed.

---

## Event ordering — confirmed from logs

Within a single skill use:

```
InvokeOnSkillUse             — reset accumulators, snapshot attacker HUD, show ~theo% immediately
  per shot:
    InvokeOnDamageReceived   — accumulate damage; take lazy armour snapshot on first hit
    InvokeOnArmorChanged     — record post-hit armour value (fires after OnDamageReceived, confirmed)
  InvokeOnAttackMissed       — accumulate misses (fires instead of Damage+Armor for missed shots)
UITacticalHUD.RemoveEntity   — fires for killed units (~1.5s before AfterSkillUse)
    (death animation plays)
InvokeOnAfterSkillUse        — flush and display flyovers
```

**Critical ordering confirmed:** `InvokeOnDamageReceived` always fires immediately before `InvokeOnArmorChanged` for the same hit. This is what makes the lazy armour snapshot approach correct — `GetArmorDurability()` called in `OnDamageReceived` returns the pre-hit value.

---

## Aggregation state

```csharp
private static IntPtr _currentAttacker = IntPtr.Zero;
private static float  _theoreticalAccuracy = -1f;  // reset in finally block of OnSkillCompleted
private static readonly Dictionary<IntPtr, int> _hpDamage     = new(); // target → HP lost
private static readonly Dictionary<IntPtr, int> _shotsHit     = new(); // target → hits
private static readonly Dictionary<IntPtr, int> _shotsFired   = new(); // target → total shots
private static readonly HashSet<IntPtr>         _armorChanged = new();
private static readonly Dictionary<IntPtr, int> _armorSnapshot = new(); // entity → durability before first hit
private static readonly Dictionary<IntPtr, int> _armorLast     = new(); // entity → durability after last hit
private static UnitHUD _attackerHUDSnapshot;

// DropdownText duration tracking
private static readonly HashSet<IntPtr> _extendedDropdowns = new();     // already had StartTime extended
private static readonly HashSet<IntPtr> _activeDropdownElements = new(); // registered DropdownText pointers
```

Reset all accumulation state at `InvokeOnSkillUse`. Clear all (including `_attackerHUDSnapshot`) in a `finally` block in `OnSkillCompleted`. Clear `_hudRegistry`, `_extendedDropdowns`, and `_activeDropdownElements` in `OnSceneLoaded`.

**Armour snapshot strategy:** snapshots are taken **lazily** in `OnDamageReceived` on the first hit per entity per skill window. `OnDamageReceived` is confirmed to fire before `OnArmorChanged` for every hit (verified from log event ordering), so `GetArmorDurability()` called in `OnDamageReceived` returns the pre-hit value. Only the first hit per entity writes the snapshot — subsequent hits only update `_armorLast`.

**Per-target flush loop:** iterates over the union of `_hpDamage.Keys` and `_armorChanged` to ensure targets that took only armour damage (all shots `Damage == 0`) are not skipped:
```csharp
var allTargets = new HashSet<IntPtr>(_hpDamage.Keys);
allTargets.UnionWith(_armorChanged);
foreach (var target in allTargets) { ... }
```

---

## Display format reference

| Stat | Format | Shown on | Timing | Status |
|---|---|---|---|---|
| Theoretical accuracy | `~65%` | Attacker | At skill commit | ✓ working |
| HP damage | `-24 HP` | Attacker | After skill resolves | ✓ working (live units only) |
| Armour damage | `-33 ARM` | Attacker | After skill resolves | ✓ working (live units only) |
| Actual accuracy | `75%` | Attacker | After skill resolves | ✓ working |

For percentages: use `(int)Math.Round(value)` if the game returns 0–100 scale (confirmed for `FinalValue`). Use `(int)Math.Round(100.0 * value)` for 0–1 fractions. The `> 1f` check distinguishes scale at runtime.

Colour helper: `private static string Coloured(string text, string hex) => $"<color={hex}>{text}</color>";`

Colours are defined as named constants at the top of `CombatFlyoverPlugin.cs`:

```csharp
private const string ColourHPDamage     = "#FF4444"; // red
private const string ColourArmourDamage = "#4488FF"; // blue
private const string ColourAccuracy     = "#44CC44"; // green
```

**Unity Rich Text colour constraints:** only `#RRGGBB`, `#RRGGBBAA`, and a limited set of named colours are supported. 3-digit hex, `rgb()`, `hsl()`, and most CSS named colours are not supported and will render as white or be ignored. Supported named colours: aqua, black, blue, brown, cyan, darkblue, fuchsia, green, grey, lightblue, lime, magenta, maroon, navy, olive, orange, purple, red, silver, teal, white, yellow.

---

## Debug logging

A `DebugLogging` constant gates verbose per-event log lines:

```csharp
private const bool DebugLogging = false;
```

Set to `true` for development to restore full per-shot, per-hit, and per-event logging. Set to `false` for normal use.

**Always-on log lines** (fire regardless of `DebugLogging`):
- Startup: `OnInitialize started`, `All patches applied`, `OnInitialize complete`
- `OnSceneLoaded` scene name and index
- Per-skill result: `{EntityName}: hp=X`
- Per-skill result: `{EntityName}: armorDelta=X elementCount=Y normalisedDelta=Z (before=A after=B)`
- Per-skill result: `Actual accuracy: totalHit=X totalFired=Y`
- Error/warning lines: snapshot failures, `GetOriginalElementCount` failures, all `Log.Warning` calls

---

## Entity name helper

```csharp
private static string EntityName(IntPtr entityPtr)
{
    try
    {
        var entity = new Il2CppMenace.Tactical.Entity(entityPtr);
        var name = entity.DebugName;
        if (!string.IsNullOrEmpty(name)) return name;
    }
    catch { }
    return $"0x{entityPtr:X}";
}
```

`Entity.DebugName` returns the entity's template name (e.g. `enemy.alien_01_small_spiderling`, `enemy.pirate_veteran_scavengers_t2`). This is a template identifier, not a localised display name, but is human-readable and useful for cross-referencing game data files. Falls back to the hex pointer if unavailable.

---

## Open items

### 1. Kill-case flyover display

Custom world-space UI element required. `ShowDropDownText` cannot work after `RemoveEntity`. The attacker-HUD routing partially mitigates this for attacker stats, but target stats (HP, ARM) are still lost when the target dies.

### 2. Armour damage display on unarmoured units

`InvokeOnArmorChanged` only fires when armour is present. Units with no armour produce no ARM flyover, which is correct behaviour.

---

## Il2Cpp constraints

1. **Do not patch `MonoBehaviour.OnEnable`** — stripped from interop assembly
2. **Do not use `foreach` on Il2Cpp lists** — use `for (int i = 0; i < list.Count; i++)`
3. **Always pass two arguments to `element.Q()`** — `element.Q("Name", (string)null)`
4. **Use `TryCast<T>()` for conditional Il2Cpp type checks**, `Cast<T>()` only when certain
5. **Every Harmony patch must have a try/catch** — patch failure must never crash the game
6. **`harmony.PatchAll(typeof(YourPatchClass))`** — never bare `harmony.PatchAll()`
7. **Do not access singletons in `OnInitialize`** — use `GameState.TacticalReady`
8. **Always include `using System;` and `using System.Collections.Generic;`**
9. **Generic Unity engine types cannot be referenced via compile-time `typeof` in Harmony attributes** — use `[HarmonyPatch]` with a `TargetMethod()` returning a runtime-reflected `MethodBase`
10. **Do not read `computedStyle`, `resolvedStyle`, or walk the panel tree** — live animated collections throw during enumeration and panel tree walks crash the game (CTD confirmed)
11. **`Queue<T>` in Il2Cpp uses a circular buffer** — always offset reads by `_head % capacity`, never iterate from index 0

---

## SDK utilities (Menace.SDK)

- `GameObj` — wrapper around a native pointer: `ReadObj`, `ReadInt`, `ReadBool`, `GetName`
- `GameState` — scene awareness, `TacticalReady` event, `RunDelayed`, `RunWhen`
- `TacticalState` — singleton: `TacticalState.Get()` → `GetUI()` → `GetHUD()`
- `TacticalEventHooks` — **non-functional on this build** (Initialize fails silently)
- `Intercept` — **non-functional on this build** (Initialize fails silently)

---

## Known type inventory — Il2CppMenace.UI.Tactical

`BaseHUD`, `BaseSkillBarItemSlot`, `BleedingWorldSpaceIcon`, `DelayedAbilityHUD`, `DropdownText`, `EntityHUD`, `ISkillBarElement`, `MissionInfoPanel`, `MovementHUD`, `ObjectiveHUD`, `ObjectivesTracker`, `OffmapAbilityButton`, `SelectedUnitPanel`, `SimpleSkillBarButton`, `SimpleWorldSpaceIcon`, `SkillBar`, `SkillBarButton`, `SkillBarSlotAccessory`, `SkillBarSlotWeapon`, `SkillUsesBar`, `StatusEffectIcon`, `StructureHUD`, `TacticalBarkPanel`, `TacticalUnitInfoStat`, `TurnOrderFactionSlot`, `TurnOrderPanel`, `UnitBadge`, `UnitHUD`, `UnitsTurnBar`, `UnitsTurnBarSlot`, `WorldSpaceIcon`

## Dump.cs investigation utilities

Two PowerShell scripts are stored in `HUDCustomizer/Dev/`:

- **`find_type.ps1`** — prints the full block for a type including all fields and method RVAs
- **`find_namespace.ps1`** — for every matching line, prints the nearest namespace and class declaration above it; use to find correct fully-qualified names and whether types carry the `Il2Cpp` prefix

Usage (run from the `Dev/` directory with `dump.cs` copied alongside):
```powershell
.ind_type.ps1 -Dump .\dump.cs -Search "UnitHUD"
.ind_namespace.ps1 -Dump .\dump.cs -Search "set_durationMs"
```

If you get an execution policy error: `powershell -ExecutionPolicy Bypass -File .ind_type.ps1 -Dump .\dump.cs -Search "UnitHUD"`

Always use current `dump.cs` RVAs for Ghidra navigation — never use dummy DLL VAs.
