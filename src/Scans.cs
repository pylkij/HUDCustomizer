using System;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

using Il2CppInterfaceElement = Il2CppMenace.UI.InterfaceElement;
using Il2CppTargetAimVisualizer   = Il2CppMenace.Tactical.TargetAimVisualizer;

// =============================================================================
// Scans
// Discovery-only scans used during development to inspect element trees,
// font assets, and UIConfig colour values.
//
// All scans are gated on HUDCustomizerConfig.EnableScans = true.
// Each scan fires at most once per session regardless of hot-reload.
// Output is always written via Log.Msg() -- no dependency on DebugLogging.
//
// To add a new scan:
//   1. Add a private static bool _<name>Scanned = false; flag.
//   2. Add a public static void Run<Name>Scan(...) method that checks the
//      gate (CheckGate()) and the flag before doing any work.
//   3. Call it from the appropriate patch postfix or lifecycle method in
//      HUDCustomizer.cs.
//
// Scans that are no longer needed should be deleted from this file, and
// their call-sites in HUDCustomizer.cs and FontCustomizer.cs removed.
// =============================================================================
public static class Scans
{
    // =========================================================================
    // Gate
    // All public entry points call this first.  Returns false (and logs nothing)
    // when EnableScans is off so callers can early-out cleanly.
    // =========================================================================
    private static bool CheckGate()
    {
        return HUDCustomizerPlugin.Config?.EnableScans == true;
    }

    // =========================================================================
    // UIConfig scan
    // Dumps all Color fields from UIConfig.Get() to the log.
    // Fires once per session at TacticalReady (called from
    // HUDCustomizerPlugin.OnTacticalReady).
    // DELETE once all useful UIConfig fields have been identified and implemented.
    // =========================================================================
    private static bool _uiConfigScanned = false;

    public static void RunUIConfigScan()
    {
        HUDCustomizerPlugin.Log.Msg("[UIConfigScan] CheckGate result: " + CheckGate());
        if (!CheckGate()) return;
        if (_uiConfigScanned) return;
        _uiConfigScanned = true;

        try
        {
            var uiConfig = Il2CppMenace.UI.UIConfig.Get();
            if (uiConfig == null)
            {
                HUDCustomizerPlugin.Log.Msg("[UIConfigScan] UIConfig.Get() returned null.");
                return;
            }

            void Dump(string name, Color c) =>
                HUDCustomizerPlugin.Log.Msg(
                    $"[UIConfigScan]   {name} = " +
                    $"RGB({(int)(c.r * 255)}, {(int)(c.g * 255)}, {(int)(c.b * 255)}) A({c.a:F2})");

            HUDCustomizerPlugin.Log.Msg("=== HUDCustomizer UIConfig Scan ===");

            HUDCustomizerPlugin.Log.Msg("[UIConfigScan] -- USS theme colours --");
            Dump("ColorNormal",                      uiConfig.ColorNormal);
            Dump("ColorBright",                      uiConfig.ColorBright);
            Dump("ColorNormalTransparent",            uiConfig.ColorNormalTransparent);
            Dump("ColorInteract",                     uiConfig.ColorInteract);
            Dump("ColorInteractDark",                 uiConfig.ColorInteractDark);
            Dump("ColorInteractHover",                uiConfig.ColorInteractHover);
            Dump("ColorInteractSelected",             uiConfig.ColorInteractSelected);
            Dump("ColorInteractSelectedText",         uiConfig.ColorInteractSelectedText);
            Dump("ColorDisabled",                     uiConfig.ColorDisabled);
            Dump("ColorDisabledHover",                uiConfig.ColorDisabledHover);
            Dump("ColorTooltipBetter",                uiConfig.ColorTooltipBetter);
            Dump("ColorTooltipWorse",                 uiConfig.ColorTooltipWorse);
            Dump("ColorTooltipNormal",                uiConfig.ColorTooltipNormal);
            Dump("ColorPositive",                     uiConfig.ColorPositive);
            Dump("ColorNegative",                     uiConfig.ColorNegative);
            Dump("ColorWarning",                      uiConfig.ColorWarning);
            Dump("ColorDarkBg",                       uiConfig.ColorDarkBg);
            Dump("ColorWindowCorner",                 uiConfig.ColorWindowCorner);
            Dump("ColorTopBar",                       uiConfig.ColorTopBar);
            Dump("ColorTopBarDark",                   uiConfig.ColorTopBarDark);
            Dump("ColorProgressBarNormal",            uiConfig.ColorProgressBarNormal);
            Dump("ColorProgressBarBright",            uiConfig.ColorProgressBarBright);
            Dump("ColorEmptySlotIcon",                uiConfig.ColorEmptySlotIcon);

            HUDCustomizerPlugin.Log.Msg("[UIConfigScan] -- Rarity colours --");
            Dump("ColorCommonRarity",                 uiConfig.ColorCommonRarity);
            Dump("ColorCommonRarityNamed",            uiConfig.ColorCommonRarityNamed);
            Dump("ColorUncommonRarity",               uiConfig.ColorUncommonRarity);
            Dump("ColorUncommonRarityNamed",          uiConfig.ColorUncommonRarityNamed);
            Dump("ColorRareRarity",                   uiConfig.ColorRareRarity);
            Dump("ColorRareRarityNamed",              uiConfig.ColorRareRarityNamed);

            HUDCustomizerPlugin.Log.Msg("[UIConfigScan] -- Health bar colours by faction --");
            Dump("HealthBarFillColorPlayerUnits",     uiConfig.HealthBarFillColorPlayerUnits);
            Dump("HealthBarPreviewColorPlayerUnits",  uiConfig.HealthBarPreviewColorPlayerUnits);
            Dump("HealthBarFillColorAllies",          uiConfig.HealthBarFillColorAllies);
            Dump("HealthBarPreviewColorAllies",       uiConfig.HealthBarPreviewColorAllies);
            Dump("HealthBarFillColorEnemies",         uiConfig.HealthBarFillColorEnemies);
            Dump("HealthBarPreviewColorEnemies",      uiConfig.HealthBarPreviewColorEnemies);
            Dump("HealthBarSectionColorPlayerUnits",  uiConfig.HealthBarSectionColorPlayerUnits);
            Dump("HealthBarSectionColorEnemies",      uiConfig.HealthBarSectionColorEnemies);

            HUDCustomizerPlugin.Log.Msg("[UIConfigScan] -- Mission colours --");
            Dump("ColorMissionPlayable",              uiConfig.ColorMissionPlayable);
            Dump("ColorMissionLocked",                uiConfig.ColorMissionLocked);
            Dump("ColorMissionPlayed",                uiConfig.ColorMissionPlayed);
            Dump("ColorMissionPlayedArrow",           uiConfig.ColorMissionPlayedArrow);
            Dump("ColorMissionUnplayable",            uiConfig.ColorMissionUnplayable);

            HUDCustomizerPlugin.Log.Msg("[UIConfigScan] -- Misc --");
            Dump("ColorPositionMarkerDelayedAbility", uiConfig.ColorPositionMarkerDelayedAbility);

            HUDCustomizerPlugin.Log.Msg("=== End UIConfig Scan ===");
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning($"[UIConfigScan] Exception: {ex.Message}");
        }
    }

    // =========================================================================
    // Font scan
    // Dumps IStyle font-related properties and all loaded Font asset names.
    // Fires once per session at TacticalReady (called from
    // FontCustomizer.OnTacticalReady).
    // DELETE once font implementation is confirmed working.
    // =========================================================================
    private static bool _fontScanned = false;

    public static void RunFontScan(IReadOnlyDictionary<string, Font> fontCache)
    {
        if (!CheckGate()) return;
        if (_fontScanned) return;
        _fontScanned = true;

        HUDCustomizerPlugin.Log.Msg("=== HUDCustomizer Font Scan ===");
        try
        {
            HUDCustomizerPlugin.Log.Msg("[FontScan] IStyle font-related properties:");
            foreach (var prop in typeof(IStyle).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                var n = prop.Name.ToLowerInvariant();
                if (n.Contains("font") || n.Contains("letterspacing")
                    || n.Contains("wordspacing") || n.Contains("textalign")
                    || n.Contains("textoverflow"))
                    HUDCustomizerPlugin.Log.Msg(
                        $"[FontScan]   {prop.PropertyType.Name} {prop.Name}");
            }

            HUDCustomizerPlugin.Log.Msg("[FontScan] Game Font assets:");
            foreach (var kv in fontCache)
                HUDCustomizerPlugin.Log.Msg($"[FontScan]   '{kv.Key}'");

            // NOTE: OS font enumeration is intentionally omitted.
            // Font.CreateDynamicFontFromOSFont is stripped in this Il2Cpp build so
            // OS fonts cannot currently be loaded at runtime.  This block is kept
            // as a reference for potential future custom font loading support
            // (e.g. via AssetBundle or an alternative loading path).
            //
            // HUDCustomizerPlugin.Log.Msg("[FontScan] OS installed fonts:");
            // try
            // {
            //     var osNames = Font.GetOSInstalledFontNames();
            //     foreach (var n in osNames)
            //         HUDCustomizerPlugin.Log.Msg($"[FontScan]   '{n}'");
            // }
            // catch (Exception osEx)
            // {
            //     HUDCustomizerPlugin.Log.Warning($"[FontScan] Could not enumerate OS fonts: {osEx.Message}");
            // }

            HUDCustomizerPlugin.Log.Msg("[FontScan] FontAsset / TMP_FontAsset types:");
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name != "FontAsset" && t.Name != "TMP_FontAsset") continue;
                    HUDCustomizerPlugin.Log.Msg(
                        $"[FontScan]   {t.FullName} in {asm.GetName().Name}");
                    try
                    {
                        var findAll = typeof(Resources).GetMethod(
                            "FindObjectsOfTypeAll",
                            BindingFlags.Public | BindingFlags.Static,
                            null, new[] { typeof(Type) }, null);
                        var results = findAll?.Invoke(null, new object[] { t })
                            as UnityEngine.Object[];
                        if (results != null)
                            foreach (var r in results)
                                HUDCustomizerPlugin.Log.Msg($"[FontScan]     '{r.name}'");
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Error($"[FontScan] Exception: {ex.Message}");
        }
        HUDCustomizerPlugin.Log.Msg("=== End Font Scan ===");
    }

    // =========================================================================
    // Element scan
    // Dumps the child element tree and all text elements of a given HUD element.
    // Called from patch postfixes that carry a // DELETE comment, once per
    // concrete type per session.
    // DELETE per call-site once that HUD's element structure is confirmed.
    // =========================================================================
    public static void RunElementScan(Il2CppInterfaceElement element, string label)
    {
        if (!CheckGate()) return;
        RunElementScanCore(element, label);
    }

    // Ungated variant -- used by scans that bypass EnableScans intentionally.
    public static void RunElementScanUngated(Il2CppInterfaceElement element, string label)
    {
        RunElementScanCore(element, label);
    }

    private static void RunElementScanCore(Il2CppInterfaceElement element, string label)
    {
        HUDCustomizerPlugin.Log.Msg($"=== HUDCustomizer Element Scan [{label}] ===");
        try
        {
            HUDCustomizerPlugin.Log.Msg($"[ElemScan/{label}] Child tree:");
            WalkFull(element, label, depth: 0, maxDepth: 5);

            HUDCustomizerPlugin.Log.Msg($"[ElemScan/{label}] Text elements:");
            WalkTextElements(element, label);
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Error($"[ElemScan/{label}] Exception: {ex.Message}");
        }
        HUDCustomizerPlugin.Log.Msg($"=== End Element Scan [{label}] ===");
    }

    // =========================================================================
    // TargetAimVisualizer material property scan
    // Dumps all shader properties on every material found on MeshRenderers that
    // are children of the TargetAimVisualizer GameObject.
    //
    // Confirmed findings:
    //   _UnlitColor    -- base line colour tint; hardcoded in VisualizerCustomizer
    //                     as UnlitColorProperty. Writes via MPB are effective.
    //   _EmissiveColor -- HDR glow/bloom colour; hardcoded as EmissiveColorProperty.
    //   _Color         -- legacy stub; HasProperty=true but writes have no effect.
    //
    // Called from Patch_TargetAimVisualizer_UpdateAim in HUDCustomizer.cs
    // (fires once, gated on EnableScans).
    //
    // DELETE once no longer needed for development reference.
    // =========================================================================
    private static bool _targetAimMaterialScanned = false;

    public static void RunTargetAimMaterialScan(Il2CppTargetAimVisualizer instance)
    {
        if (!CheckGate()) return;
        if (_targetAimMaterialScanned) return;
        _targetAimMaterialScanned = true;

        HUDCustomizerPlugin.Log.Msg("=== HUDCustomizer TargetAimVisualizer Material Scan ===");
        try
        {
            var renderers = instance.gameObject.GetComponentsInChildren<MeshRenderer>();
            if (renderers == null || renderers.Length == 0)
            {
                HUDCustomizerPlugin.Log.Msg("[AimMatScan] No MeshRenderers found in children.");
                HUDCustomizerPlugin.Log.Msg("=== End TargetAimVisualizer Material Scan ===");
                return;
            }

            foreach (var r in renderers)
            {
                if (r == null) continue;
                HUDCustomizerPlugin.Log.Msg(
                    $"[AimMatScan] MeshRenderer: '{r.gameObject.name}'");

                var mat = r.material;
                if (mat == null)
                {
                    HUDCustomizerPlugin.Log.Msg("[AimMatScan]   material: null");
                    continue;
                }

                HUDCustomizerPlugin.Log.Msg(
                    $"[AimMatScan]   material name: '{mat.name}'");
                HUDCustomizerPlugin.Log.Msg(
                    $"[AimMatScan]   shader name:   '{mat.shader?.name ?? "null"}'");

                // Dump every shader property via Shader.GetPropertyCount / GetPropertyName.
                var shader = mat.shader;
                if (shader == null)
                {
                    HUDCustomizerPlugin.Log.Msg("[AimMatScan]   shader: null");
                    continue;
                }

                int count = shader.GetPropertyCount();
                HUDCustomizerPlugin.Log.Msg(
                    $"[AimMatScan]   shader property count: {count}");

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var propName = shader.GetPropertyName(i);
                        var propType = shader.GetPropertyType(i);
                        string currentVal = "";
                        try
                        {
                            // Read the current value from the material for context.
                            switch (propType)
                            {
                                case UnityEngine.Rendering.ShaderPropertyType.Color:
                                    currentVal = $" = {mat.GetColor(propName)}";
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Float:
                                case UnityEngine.Rendering.ShaderPropertyType.Range:
                                    currentVal = $" = {mat.GetFloat(propName):F3}";
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                    currentVal = $" = '{mat.GetTexture(propName)?.name ?? "null"}'";
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                    currentVal = $" = {mat.GetVector(propName)}";
                                    break;
                            }
                        }
                        catch { }

                        HUDCustomizerPlugin.Log.Msg(
                            $"[AimMatScan]     [{i}] {propType,-10} '{propName}'{currentVal}");
                    }
                    catch (Exception propEx)
                    {
                        HUDCustomizerPlugin.Log.Msg(
                            $"[AimMatScan]     [{i}] <error reading property: {propEx.Message}>");
                    }
                }

                // Also check if HasProperty for common colour names:
                string[] candidates = {
                    "_Color", "_BaseColor", "_TintColor", "_MainColor",
                    "_EmissionColor", "_LineColor", "_FillColor", "_AlbedoColor"
                };
                HUDCustomizerPlugin.Log.Msg("[AimMatScan]   HasProperty check for common colour names:");
                foreach (var c in candidates)
                {
                    bool has = mat.HasProperty(c);
                    HUDCustomizerPlugin.Log.Msg($"[AimMatScan]     HasProperty('{c}') = {has}");
                }
            }
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning($"[AimMatScan] Exception: {ex.Message}");
        }
        HUDCustomizerPlugin.Log.Msg("=== End TargetAimVisualizer Material Scan ===");
    }

    // =========================================================================
    // SetOpacity scan
    // Tracks the resolved opacity of each UnitHUD root element and logs
    // whenever it changes.  Per-instance change detection keeps log output sparse.
    //
    // Confirmed findings:
    //   - Spent value: 0.5 (applied to root element once per turn-end)
    //   - Active restore value: 1.0
    //   - Opacity is applied to the root element, not a child
    //
    // DELETE once spent-opacity value and element target are confirmed.
    // =========================================================================
    private static readonly Dictionary<IntPtr, float> _lastOpacity = new();

    public static void RunOpacityChangeScan(Il2CppMenace.UI.Tactical.UnitHUD instance)
    {
        if (!CheckGate()) return;

        try
        {
            var el = instance.Cast<Il2CppMenace.UI.InterfaceElement>();
            if (el == null || el.Pointer == IntPtr.Zero) return;

            // Read resolved opacity on the root element.
            float opacity = -1f;
            try { opacity = el.resolvedStyle.opacity; } catch { return; }

            if (_lastOpacity.TryGetValue(el.Pointer, out float last) && last == opacity) return;
            _lastOpacity[el.Pointer] = opacity;

            // Also sample the pickable child so we can see if opacity is applied
            // there instead of (or in addition to) the root.
            float pickableOpacity = -1f;
            try
            {
                var pickable = instance.GetPickableElement();
                if (pickable != null) pickableOpacity = pickable.resolvedStyle.opacity;
            }
            catch { }

            HUDCustomizerPlugin.Log.Msg(
                $"[OpacityChangeScan] ptr=0x{el.Pointer:X}  " +
                $"root.opacity={opacity:F4}  " +
                $"pickable.opacity={pickableOpacity:F4}");
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning($"[OpacityChangeScan] Exception: {ex.Message}");
        }
    }

    // =========================================================================
    // Walk helpers (shared by RunElementScan and RunWorldSpaceIconScan)
    // =========================================================================
    private static void WalkFull(VisualElement ve, string label, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        var indent = new string(' ', depth * 2);
        for (int i = 0; i < ve.childCount; i++)
        {
            var child = ve.ElementAt(i);
            if (child == null) continue;
            var classes = string.Join(" ", HUDCustomizerPlugin.GetClasses(child));
            string col = "";
            try
            {
                var rs = child.resolvedStyle;
                if (rs != null)
                    col = $"  bg={rs.backgroundColor}  tint={rs.unityBackgroundImageTintColor}";
            }
            catch { }
            HUDCustomizerPlugin.Log.Msg(
                $"[ElemScan/{label}] {indent}[{i}] {child.GetType().Name}  " +
                $"name='{child.name}'  classes=[{classes}]{col}");
            WalkFull(child, label, depth + 1, maxDepth);
        }
    }

    private static void WalkTextElements(VisualElement ve, string label)
    {
        for (int i = 0; i < ve.childCount; i++)
        {
            var child = ve.ElementAt(i);
            if (child == null) continue;

            var classes = HUDCustomizerPlugin.GetClasses(child);
            if (classes.Contains("unity-text-element"))
            {
                string fontInfo = "";
                try
                {
                    var rs = child.resolvedStyle;
                    if (rs != null)
                    {
                        fontInfo = $"  fontSize={rs.fontSize}";
                        var fp = rs.GetType().GetProperty("unityFont",
                            BindingFlags.Public | BindingFlags.Instance);
                        fontInfo += $"  font='{fp?.GetValue(rs) ?? "null"}'";
                        var dp = rs.GetType().GetProperty("unityFontDefinition",
                            BindingFlags.Public | BindingFlags.Instance);
                        fontInfo += $"  fontDef='{dp?.GetValue(rs) ?? "null"}'";
                    }
                }
                catch { }
                HUDCustomizerPlugin.Log.Msg(
                    $"[ElemScan/{label}]   text  name='{child.name}'  " +
                    $"classes=[{string.Join(" ", classes)}]{fontInfo}");
            }

            WalkTextElements(child, label);
        }
    }
}
