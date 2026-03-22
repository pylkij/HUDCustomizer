# CombatFlyoverText + HUDCustomizer Integration — AI Agent Working Brief

This file covers the seam between the two plugins. Read it when working on anything that touches `CombatFlyoverCustomizer.cs`, `CombatFlyoverSettings`, or the config surface of the flyover feature. It does not replace either plugin's own brief — read those first.

---

## What this integration does

`CombatFlyoverPlugin` previously held all its configurable values as `private const` fields. This integration moves those values into `HUDCustomizer.json` under a `CombatFlyover` section, giving users hot-reload control over colours, display duration, and the fade animation, plus a master enable/disable toggle.

The bridge is `CombatFlyoverCustomizer.cs`, a static class that lives in the **HUDCustomizer assembly**. It receives config values from HUDCustomizer on load and on every hot-reload, and exposes them to `CombatFlyoverPlugin` via public accessors.

---

## Assembly boundary

```
HUDCustomizer.dll
  └── CombatFlyoverCustomizer   (owns the config shape and bridge accessors)

CombatFlyoverText.dll
  └── CombatFlyoverPlugin       (consumes bridge accessors at runtime)
```

**The dependency arrow is one-way.** `CombatFlyoverPlugin` references `HUDCustomizer.dll`. `HUDCustomizer` has no reference to `CombatFlyoverText` — it cannot; it must compile and run correctly whether or not CombatFlyoverText is installed.

`CombatFlyoverPlugin.csproj` must include a reference to `HUDCustomizer.dll`:

```xml
<ItemGroup>
  <Reference Include="HUDCustomizer">
    <HintPath>..\HUDCustomizer\bin\Release\net6\HUDCustomizer.dll</HintPath>
  </Reference>
</ItemGroup>
```

Adjust the `HintPath` to match your local build output. `HUDCustomizer.dll` must be present in the Menace mod directory at runtime — if HUDCustomizer is not installed, `CombatFlyoverPlugin` will fail to load entirely (assembly resolution failure at init). Document this as a hard dependency for users.

---

## Load-order safety

HUDCustomizer and CombatFlyoverText are both loaded by Menace Modpack Loader. Load order between them is not guaranteed.

`CombatFlyoverCustomizer` is safe to call before `Apply()` has been called — every accessor returns a hardcoded fallback that exactly matches the original `private const` defaults in `CombatFlyoverPlugin`:

| Accessor | Fallback |
|---|---|
| `IsEnabled()` | `true` |
| `ColourHP()` | `"#FF4444"` |
| `ColourARM()` | `"#4488FF"` |
| `ColourAcc()` | `"#44CC44"` |
| `ExtraDisplaySeconds()` | `2.0f` |
| `FadeDurationScale()` | `2.0f` |

This means CombatFlyoverPlugin behaves identically to its pre-integration defaults until HUDCustomizer calls `Apply()` for the first time. In practice `Apply()` is called during `OnInitialize` → `LoadConfig()`, which fires before any tactical scene is entered, so the fallbacks are never visible to the user in normal play.

---

## Config ownership

`CombatFlyoverSettings` and the `CombatFlyover` property on `HUDCustomizerConfig` live in **`HUDConfig.cs`** in the HUDCustomizer project. The JSON block lives in `BuildDefaultConfig()` in the same file.

When adding, removing, or renaming a setting:

1. **`HUDConfig.cs`** — update `CombatFlyoverSettings` (add/remove/rename the property) and update the corresponding entry in `BuildDefaultConfig()`'s verbatim string. Follow the existing `CombatFlyover` block format exactly — the merge-fill system in `HUDCustomizer.cs` depends on the template structure.
2. **`CombatFlyoverCustomizer.cs`** — add/remove/rename the corresponding accessor and update its fallback default to match the new `BuildDefaultConfig()` value. Update `LogSummary()` to include the new field.
3. **`CombatFlyoverPlugin.cs`** — replace the old call site with the new accessor.
4. **`HUDCustomizer-AI.md`** — no changes needed unless the call sequence in `LoadConfig()` or `OnUpdate()` changes.
5. **This file** — update the accessor fallback table above.

Do not add config fields directly to `CombatFlyoverPlugin.cs`. All config shape lives in `HUDConfig.cs`. All bridge logic lives in `CombatFlyoverCustomizer.cs`.

---

## Hot-reload flow

Pressing F8 in a tactical scene triggers this sequence in `HUDCustomizer.cs`:

```
LoadConfig()
  → deserialises HUDCustomizer.json into HUDCustomizerConfig
  → CombatFlyoverCustomizer.Apply(Config.CombatFlyover)   ← pushes new values to bridge
  → CombatFlyoverCustomizer.LogSummary()                  ← logs current state
FontCustomizer.InvalidateCache()
ReapplyToLiveElements()
TileCustomizer.TryApply()
USSCustomizer.TryApply()
UnitCustomizer.ApplyFactionHealthBarColors()
VisualizerCustomizer.TryApply()
CombatFlyoverCustomizer.Apply(Config.CombatFlyover)        ← second call, in OnUpdate() block
```

`Apply()` is called twice: once inside `LoadConfig()` (which runs at init and at reload), and once directly in the `OnUpdate()` hot-reload block. This is intentional — `LoadConfig()` is also called at startup where `OnUpdate()` does not run, so both call sites are needed to cover all paths.

**Timing effect on in-flight elements:** colour changes take effect on the next flyover that fires after the reload. `ExtraDisplaySeconds` and `FadeDurationScale` take effect on elements that enter the queue after the reload — already-queued `DropdownText` instances retain the values that were live when they were enqueued. This is unavoidable given the memory-write approach and is correct behaviour.

---

## Where each concern lives

| Concern | File | Project |
|---|---|---|
| Config shape (`CombatFlyoverSettings` class) | `HUDConfig.cs` | HUDCustomizer |
| Default JSON values | `HUDConfig.cs` (`BuildDefaultConfig()`) | HUDCustomizer |
| Bridge accessors and `Apply()` | `CombatFlyoverCustomizer.cs` | HUDCustomizer |
| Summary logging | `CombatFlyoverCustomizer.cs` (`LogSummary()`) | HUDCustomizer |
| `Apply()` call sites | `HUDCustomizer.cs` (`LoadConfig()` + `OnUpdate()`) | HUDCustomizer |
| Runtime consumption of config values | `CombatFlyoverPlugin.cs` | CombatFlyoverText |
| Combat event patches and flyover display logic | `CombatFlyoverPlugin.cs` | CombatFlyoverText |

---

## Adding a new configurable value — checklist

- [ ] Add property to `CombatFlyoverSettings` in `HUDConfig.cs`
- [ ] Add entry to the `CombatFlyover` block in `BuildDefaultConfig()` in `HUDConfig.cs`
- [ ] Add accessor to `CombatFlyoverCustomizer.cs` with matching fallback default
- [ ] Add field to `LogSummary()` in `CombatFlyoverCustomizer.cs`
- [ ] Replace the hardcoded value in `CombatFlyoverPlugin.cs` with the new accessor call
- [ ] Update the accessor fallback table in this file

---

## Cross-references

- `FloatingCombatText-AI.md` — full CombatFlyoverPlugin brief: patch classes, event flow, aggregation state, timing constraints
- `HUDCustomizer-AI.md` — full HUDCustomizer brief: config system, hot-reload lifecycle, pattern reference for all customiser types
- `CONTRIBUTOR_README.md` — `LoadConfig()` summary call sequence (Section 3), hot-reload sequence (Section 6)
