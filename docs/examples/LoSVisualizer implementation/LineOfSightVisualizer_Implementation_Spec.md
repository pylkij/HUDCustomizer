# LineOfSightVisualizer — Colour Customisation Implementation Spec
**Status:** Ready for implementation  
**Built from:** Runtime scan (LOSScanPlugin), dump.cs extraction, Ghidra decompilation of Resize / Update / SetVisible  
**DLL binding verified:** `Il2CppShapesRuntime.dll` — .NET metadata confirmed (`Il2CppShapes.Line`, `ColorStart`, `ColorEnd`, `ColorMode`)  
**No content inherited from prior LineOfSightVisualizer-AI.md — see Section 7 for reconciliation**

---

## 1. Confirmed Facts

### 1.1 Renderer type
Every pooled child `LineOfSightLine(Clone)` carries exactly one `Shapes.Line` component (Il2CppInterop proxy: `Il2CppShapes.Line`). There are no `UnityEngine.LineRenderer` components anywhere in the hierarchy. The earlier Ghidra finding that stated otherwise was incorrect — retracted.

### 1.2 Pool structure
`LineOfSightVisualizer` holds:
```
private List<Line[]> m_Lines; // offset 0x30
```
Each `Line[]` contains exactly **3** `Shapes.Line` references, corresponding to 3 child GameObjects. `Resize(int _n)` grows `m_Lines` until it contains `_n` entries; each call appends one new `Line[]` of 3. Resize never removes entries.

At the time of the scan 21 child GameObjects existed, consistent with 7 Resize calls × 3 children per call.

### 1.3 Colour properties on `Shapes.Line`
Source: `Line.txt` (dump.cs) and `rva_summary.txt`.

| Property | Field offset | set_ VA | Notes |
|---|---|---|---|
| `Color` | inherited (ShapeRenderer) | `0x181CA4950` | Uniform / solid colour. `get_Color` and `get_ColorStart` share the same getter RVA (`0x181C9BD80`) — they are aliases. |
| `ColorStart` | inherited (ShapeRenderer) | `0x181CA48C0` | Alias for `Color`; controls the start-point colour in gradient mode. |
| `ColorEnd` | `0xA8` | `0x181CA4790` | Controls the end-point colour; distinct field. |
| `ColorMode` | `0xA4` | `0x181CA4820` | `Line.LineColorMode` enum. **Not written by Resize.** Inherited from prefab. Do not write. |

### 1.4 Colour write location
Colour is written **only in `Resize()`**. `Update()` and `SetVisible()` make zero calls to any colour setter. This was confirmed by scanning the Ghidra decompilation of all three methods for the setter VAs listed above.

### 1.5 Colour initialisation pattern (from Resize decompilation)
Resize reads `ColorStart` once from the **prefab's** `Shapes.Line` component, then applies this pattern to every new `Line[]`:

| Array index | `set_ColorStart` alpha | `set_ColorEnd` alpha | Visual role |
|---|---|---|---|
| `Line[0]` | **0** (transparent) | A (from prefab) | Fade in |
| `Line[1]` | A (from prefab) | A (from prefab) | Solid |
| `Line[2]` | A (from prefab) | **0** (transparent) | Fade out |

RGB is identical across all three lines and all four colour writes. Only alpha differs.

### 1.6 SetVisible safety
`SetVisible(bool)` iterates `m_Lines` and calls `SetActive(bool)` on each `Line`'s `GameObject`. It writes `m_IsVisible` (offset `0x40`). It does not write colour. It is safe to use as a patch target without risk of overwriting applied colours.

### 1.7 Update safety
`Update()` writes `set_Start` and `set_End` on each active `Line` (positions only) and calls `SetActive`. It does not write colour. Applied colours will not be overwritten by Update.

---

## 2. Il2CppInterop Type Binding

**Status: Confirmed.** Verified against `Il2CppShapesRuntime.dll` via .NET metadata table inspection (TypeDef + MethodDef + string heap parse).

| Item | Confirmed value |
|---|---|
| **DLL filename** | `Il2CppShapesRuntime.dll` (not `Il2CppShapes.dll`) |
| **Namespace** | `Il2CppShapes` |
| **Class name** | `Line` |
| **Full compile-time type** | `Il2CppShapes.Line` |
| **TypeDef index** | [5] in the assembly's TypeDef table |

### Property confirmation on `Il2CppShapes.Line`

| Property | get_ present | set_ present | Type |
|---|---|---|---|
| `ColorStart` | ✅ | ✅ | `Color` |
| `ColorEnd` | ✅ | ✅ | `Color` |
| `ColorMode` | ✅ | ✅ | `LineColorMode` |

`ColorMode` typing confirmed via `NativeMethodInfoPtr_set_ColorMode_Public_set_Void_LineColorMode_0` — matches the spec's instruction not to write this property. `NativeFieldInfoPtr_colorStart` backing field also confirmed present.

### Critical naming distinction

The **DLL filename** is `Il2CppShapesRuntime.dll`, but the **namespace declared inside it** is `Il2CppShapes`. These are different strings. The `.csproj` reference must use the filename; the C# type reference uses the namespace:

```xml
<!-- .csproj -->
<Reference Include="Il2CppShapesRuntime" />
```
```csharp
// C# — namespace from inside the assembly, not the filename
Il2CppShapes.Line line = child.gameObject.GetComponent<Il2CppShapes.Line>();
```

No further DLL verification is required before first compile.

---

## 3. Colour Application Strategy

### 3.1 Patch target
**Postfix on `Resize(int _n)`** — VA `0x180692CE0`.

Rationale: Colour is only written in Resize. A Resize postfix fires immediately after the game writes its prefab-derived colours, giving us a guaranteed window to override them. Because Update and SetVisible never touch colour, overrides applied here persist for the lifetime of the pooled Lines.

### 3.2 Per-group colour application
For every `Line[]` group, apply using the same fade pattern the game uses — only substituting our custom RGB and preserving the game's alpha attenuation at the endpoints:

```
Line[0].set_ColorStart( customR, customG, customB, 0 )       // fade in start: transparent
Line[0].set_ColorEnd(   customR, customG, customB, customA ) // fade in end:   opaque

Line[1].set_ColorStart( customR, customG, customB, customA ) // solid start
Line[1].set_ColorEnd(   customR, customG, customB, customA ) // solid end

Line[2].set_ColorStart( customR, customG, customB, customA ) // fade out start
Line[2].set_ColorEnd(   customR, customG, customB, 0 )       // fade out end:  transparent
```

### 3.3 Accessing Lines — traversal path
**Do not use `GetComponentsInChildren<T>`** — confirmed to throw a fatal Il2CppInterop type-initialiser exception at runtime for `Shapes.Line` (and previously for `LineRenderer`).

**Use indexed child traversal:**
```csharp
for (int i = 0; i < __instance.transform.childCount; i++)
{
    var child = __instance.transform.GetChild(i);
    var line = child.gameObject.GetComponent<Il2CppShapes.Line>();
    if (line == null) continue;
    // position within group:
    int posInGroup = i % 3;
    ApplyColourToLine(line, posInGroup, customColor);
}
```
`GetComponent<T>` on a specific `GameObject` does not share the failing code path of `GetComponentsInChildren<T>`.

### 3.4 Re-application on colour change
Because colour is never overwritten by the game after Resize, a single helper method can re-colour all existing pooled Lines at any time:
```
void ApplyColorToAll(LineOfSightVisualizer instance, Color color)
```
Call this: from the Resize postfix (covers new groups), and whenever the user changes the colour setting (covers the full pool retroactively).

---

## 4. Harmony Patch Specification

### 4.1 Patch class
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
    { ... }
}
```
`Resize` is `private` — `AccessTools.Method` is required (same pattern as the confirmed-working `LOSSetVisiblePatch`).

### 4.2 Postfix body requirements
- Wrap entirely in try/catch; log exceptions as Warning.
- Call `ApplyColorToAll(__instance, _currentColor)` where `_currentColor` is the user-configured colour.
- Do not call `GetComponentsInChildren<T>` for any type.
- Use indexed for loops over `transform.childCount` — never foreach on Il2Cpp collections.

---

## 5. Remaining Unknowns

| Unknown | Impact | Resolution |
|---|---|---|
| ~~Exact dummy DLL name for `Il2CppShapes.Line`~~ | ~~Compile-time — will fail loudly if wrong~~ | **Resolved** — DLL is `Il2CppShapesRuntime.dll`, namespace is `Il2CppShapes`, class is `Line`. See Section 2. |
| `Line.LineColorMode` enum values | None — we do not write `ColorMode` | Not needed |
| Default prefab colour values | None — we override with user config | Not needed |
| Number of Resize() calls at scene load | None — we re-apply to all children regardless | Not needed |

---

## 6. Constraints (carry forward from project)
- All game types use `Il2CppMenace.*` / `Il2CppShapes.*` prefixes at compile time.
- `harmony.PatchAll(typeof(PatchClass))` always — never bare `harmony.PatchAll()`.
- Every Harmony patch body wrapped in try/catch.
- No foreach on Il2Cpp lists — indexed for loops only.
- Do not call `TacticalEventHooks.Initialize` or `Intercept.Initialize`.
- Do not access game singletons in `OnInitialize`.

---

## 7. Reconciliation with Prior LineOfSightVisualizer-AI.md

The following statements in the prior spec are now known to be incorrect and must be replaced:

| Prior claim | Correct finding | Source |
|---|---|---|
| Renderers are `UnityEngine.LineRenderer` | Renderers are `Shapes.Line` | Runtime scan (LOSScanPlugin) |
| Colour applied via `startColor` / `endColor` | Colour applied via `ColorStart` / `ColorEnd` | dump.cs Line.txt |
| `GetComponentsInChildren<LineRenderer>` is the traversal path | `GetComponentsInChildren<T>` throws at runtime; use indexed `GetChild(i).GetComponent<T>()` | Runtime scan exception log |
| Pool contains `LineRenderer` components | Pool contains `Shapes.Line` components, 3 per `Line[]` per `m_Lines` entry | Resize decompilation |
| Colour write path unknown | Colour only written in `Resize()`, never in `Update()` or `SetVisible()` | Ghidra decompilation of all three methods |

All other sections of the prior spec (scene lifecycle, patch registration pattern, OnInitialize constraints) are assumed unaffected and should be reviewed for consistency with this spec before merging.
