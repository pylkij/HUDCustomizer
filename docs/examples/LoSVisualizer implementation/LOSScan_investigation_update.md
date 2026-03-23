# LineOfSightVisualizer — Scan Investigation Update

---

### 1. Confirmed facts from scan

- **The Shapes library IS in use — every child carries a `Shapes.Line` component, not `UnityEngine.LineRenderer`.** — `[LOSScan] child[0..20] comp[1] type='Shapes.Line'` (all 21 children)
- **21 child GameObjects exist in the pool at scan time**, all named `LineOfSightLine(Clone)`. — `[LOSScan] child[0]` through `[LOSScan] child[20]`
- **Each child carries exactly 4 components in a fixed layout**: `UnityEngine.Transform` (comp[0]), `Shapes.Line` (comp[1]), `UnityEngine.MeshFilter` (comp[2]), `UnityEngine.MeshRenderer` (comp[3]). — every `child[N] componentCount=4` block in the log (child[1] onward fully visible; child[0] comp[0] and comp[1] are in the truncated log section but the pattern is unbroken across all 21 children)
- **`GetComponentsInChildren<LineRenderer>(true)` throws a fatal type-initialiser exception** and does not return. — `[LOSScan] LOSSetVisiblePatch exception: The type initializer for 'MethodInfoStoreGeneric_GetComponentsInChildren_Public_Il2CppArrayBase_1_T_Boolean_0\`1' threw an exception.`
- **The earlier Ghidra finding that the renderers were "standard Unity LineRenderer components, not a Shapes library" was incorrect.** The runtime scan is definitive: `Shapes.Line` is present on every child, `UnityEngine.LineRenderer` is absent.
- **Steps (d), (e), and (f) did not complete** — the crash at `GetComponentsInChildren<LineRenderer>` aborted the postfix before the Component sweep ran. No colour data was collected.

---

### 2. Resize() loop answer

The scan found 21 child GameObjects at the moment `SetVisible` first fired, each containing exactly 1 `Shapes.Line`. Ghidra confirms `Resize()` loops 3 iterations. 21 is evenly divisible by 3 (21 ÷ 3 = 7), which is consistent with `Resize()` being called 7 times over the lifetime of the pool — each call instantiating 3 new `LineOfSightLine(Clone)` children. This points toward **(b): 3 child GameObjects with 1 renderer each, per Resize call**. However, this cannot be stated as confirmed from the scan alone: the scan captures pool state at one point in time, not the number of Resize calls, and a different call pattern (e.g. one call that instantiates 21 objects with an internal loop count that differs from the outer Ghidra loop) cannot be ruled out. To resolve this conclusively, an additional scan step is needed: patch `Resize()` itself as a postfix, log `instance.transform.childCount` immediately before and after each call, and log the delta. That would directly confirm how many children each Resize invocation adds.

---

### 3. GetComponentsInChildren\<LineRenderer\> viability

**CONFIRMED NOT VIABLE** — the call throws a fatal Il2CppInterop type-initialiser exception at runtime; no results are returned. Additionally, `UnityEngine.LineRenderer` is structurally absent from every child: each child carries `Shapes.Line`, making a `LineRenderer` query semantically wrong even if the exception were resolved.

---

### 4. Required changes to LineOfSightVisualizer-AI.md

CHANGE: **Renderer Discovery** — replace all `GetComponentsInChildren<LineRenderer>` calls with `GetComponentsInChildren<Shapes.Line>` (or the equivalent Il2Cpp interop type name for `Shapes.Line`); `LineRenderer` is not present and the generic method store for it throws at runtime.

CHANGE: **Colour Application** — the target properties for colour are on `Shapes.Line`, not `LineRenderer`; the property names (`Color`, `ColorEnd`, or equivalent) must be confirmed against the `Shapes.Line` API before implementation proceeds. The section currently specifies `startColor` / `endColor` which are `LineRenderer`-specific fields and do not exist on `Shapes.Line`.

CHANGE: **Prior Investigation Note / Ghidra Findings** — the recorded finding that Ghidra showed "standard Unity LineRenderer components, not a Shapes library" must be retracted; the runtime scan definitively shows `Shapes.Line` on every child.

---

### 5. Remaining unknowns

- **`Shapes.Line` Il2CppInterop type name** — the log reports the type as `Shapes.Line` via `GetIl2CppType().FullName`, but the compile-time Il2CppInterop proxy name (e.g. `Il2CppShapes.Line` or a different namespace) is not confirmed. Minimum additional log statement: in the scan postfix, after step (c), add `Log.Msg($"[LOSScan] Shapes.Line assembly: {comp.GetIl2CppType().Assembly.FullName}");` on the comp[1] of any child, and attempt `child.GetComponent<Il2CppShapes.Line>() != null` to confirm the compile-time binding resolves.

- **`Shapes.Line` colour property names** — no colour data was collected because the scan crashed before step (e). The property names and types used to set line colour on `Shapes.Line` are unknown. Minimum additional log statement: after resolving the type binding above, iterate the properties of the `Shapes.Line` instance via `GetIl2CppType().GetProperties()` and log each `PropertyInfo.Name` and `PropertyInfo.PropertyType.FullName`.

- **Default colour values** — scan crashed before any colour was read (Q4 from the investigation record is still open). Minimum additional log statement: once the correct colour property names are known, log their current values on each `Shapes.Line` instance in the same postfix, one line per child per property.

- **Resize() call count** — it is unknown how many times `Resize()` has been called to produce the 21-child pool at scan time, so the per-call yield of the 3-iteration loop cannot be confirmed. Minimum additional log statement: postfix patch on `Resize()` logging `childCount` before and after each invocation.
