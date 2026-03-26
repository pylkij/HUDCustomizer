using System;
using System.Collections.Generic;
using UnityEngine;

using Il2CppMovementVisualizer     = Il2CppMenace.Tactical.MovementVisualizer;
using Il2CppTargetAimVisualizer    = Il2CppMenace.Tactical.TargetAimVisualizer;
using Il2CppLineOfSightVisualizer  = Il2CppMenace.Tactical.LineOfSightVisualizer;

// =============================================================================
// VisualizerCustomizer
// Applies colour and parameter overrides to the three world-space 3D visualizers:
//   - MovementVisualizer    (path polyline: ReachableColor, UnreachableColor)
//   - TargetAimVisualizer   (aim spline:    OutOfRangeColor via MPB, float params,
//                                           material in-range colour via
//                                           MaterialPropertyBlock)
//   - LineOfSightVisualizer (LOS lines:     LineColor via Il2CppShapes.Line
//                                           ColorStart/ColorEnd on each child;
//                                           implemented via LOSResizePatch +
//                                           ApplyLineOfSightColor + TryApplyLineOfSight)
//
// TryApply() is called from HUDCustomizerPlugin.OnTacticalReady() and from
// OnUpdate() on hot-reload.  It enumerates all live MonoBehaviour instances of
// each type -- these are not UIElements and cannot use the _registry pattern.
//
// Config: HUDCustomizer.json -> "Visualizers" object.
// Each colour slot: { "Enabled": bool, "R": 0-255, "G": 0-255, "B": 0-255, "A": 0.0-1.0 }
// Each float slot:  numeric value, or the magic sentinel -1 to leave unchanged.
//
// Material colour for TargetAimVisualizer:
//   Both InRangeColor and OutOfRangeColor are applied via MaterialPropertyBlock
//   using confirmed shader properties "_UnlitColor" (base tint) and
//   "_EmissiveColor" (glow).  OutOfRangeColor is also written to the native
//   component field, but UpdateAim() ignores that field for out-of-range
//   rendering; the MPB write in ReapplyTargetAimVisualizerColors is what
//   actually takes effect.
//
// LineOfSightVisualizer:
//   Renderer type confirmed as Il2CppShapes.Line (Il2CppShapesRuntime.dll,
//   namespace Il2CppShapes).  Colour is written via ColorStart/ColorEnd only
//   in Resize(int) -- LOSResizePatch re-applies after every pool growth.
//   GetComponentsInChildren<T> is fatal for this type; indexed GetChild(i)
//   traversal is used instead.  Config slot: Visualizers.LineOfSight.LineColor.
// =============================================================================
public static class VisualizerCustomizer
{
    // Confirmed from material scan (HDRP/Unlit shader on 'Aiming' material):
    //   _UnlitColor    -- base line colour tint (standard colour, default white)
    //   _EmissiveColor -- HDR glow/bloom colour (values > 1 = brightness multiplier)
    //   _Color         -- legacy stub, HasProperty=true but writes have no effect
    private const string UnlitColorProperty    = "_UnlitColor";
    private const string EmissiveColorProperty = "_EmissiveColor";

    // =========================================================================
    // Apply
    // =========================================================================
    public static void TryApply()
    {
        ApplyMovementVisualizers();
        ApplyTargetAimVisualizers();
    }

    // -------------------------------------------------------------------------
    // MovementVisualizer
    // -------------------------------------------------------------------------
    private static void ApplyMovementVisualizers()
    {
        var cfg = HUDCustomizerPlugin.Config.Visualizers;
        var mv  = cfg.MovementVisualizer;

        // Early-out if nothing is configured.
        if (!mv.ReachableColor.Enabled && !mv.UnreachableColor.Enabled)
            return;

        try
        {
            var instances = UnityEngine.Object.FindObjectsOfType<Il2CppMovementVisualizer>();
            if (instances == null || instances.Length == 0)
            {
                HUDCustomizerPlugin.Debug("[VisualizerCustomizer] No MovementVisualizer instances found.");
                return;
            }

            foreach (var instance in instances)
            {
                if (instance == null) continue;
                try
                {
                    ApplyMovementVisualizer(instance, mv);
                }
                catch (Exception ex)
                {
                    HUDCustomizerPlugin.Log.Warning(
                        $"[VisualizerCustomizer] MovementVisualizer instance apply failed: {ex.Message}");
                }
            }

            HUDCustomizerPlugin.Debug(
                $"[VisualizerCustomizer] MovementVisualizer applied to {instances.Length} instance(s).");
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning(
                $"[VisualizerCustomizer] ApplyMovementVisualizers failed: {ex.Message}");
        }
    }

    private static void ApplyMovementVisualizer(
        Il2CppMovementVisualizer instance,
        MovementVisualizerConfig cfg)
    {
        if (cfg.ReachableColor.Enabled)
        {
            instance.ReachableColor = HUDCustomizerPlugin.ToColor(cfg.ReachableColor, "MovementVisualizer.ReachableColor");
        }
        if (cfg.UnreachableColor.Enabled)
        {
            instance.UnreachableColor = HUDCustomizerPlugin.ToColor(cfg.UnreachableColor, "MovementVisualizer.UnreachableColor");
        }
    }

    // -------------------------------------------------------------------------
    // TargetAimVisualizer
    // -------------------------------------------------------------------------
    private static void ApplyTargetAimVisualizers()
    {
        var cfg = HUDCustomizerPlugin.Config.Visualizers;
        var tv  = cfg.TargetAimVisualizer;

        bool anyColor = tv.InRangeColor.Enabled     ||
                        tv.InRangeEmissiveColor.Enabled ||
                        tv.EmissiveIntensity >= 0f ||
                        tv.OutOfRangeColor.Enabled;
        bool anyFloat = tv.AnimationScrollSpeed >= 0f ||
                        tv.Width                >= 0f ||
                        tv.MinimumHeight        >= 0f ||
                        tv.MaximumHeight        >= 0f ||
                        tv.DistanceToHeightScale >= 0f;

        if (!anyColor && !anyFloat)
            return;

        try
        {
            var instances = UnityEngine.Object.FindObjectsOfType<Il2CppTargetAimVisualizer>();
            if (instances == null || instances.Length == 0)
            {
                HUDCustomizerPlugin.Debug("[VisualizerCustomizer] No TargetAimVisualizer instances found.");
                return;
            }

            foreach (var instance in instances)
            {
                if (instance == null) continue;
                try
                {
                    ApplyTargetAimVisualizer(instance, tv);
                }
                catch (Exception ex)
                {
                    HUDCustomizerPlugin.Log.Warning(
                        $"[VisualizerCustomizer] TargetAimVisualizer instance apply failed: {ex.Message}");
                }
            }

            HUDCustomizerPlugin.Debug(
                $"[VisualizerCustomizer] TargetAimVisualizer applied to {instances.Length} instance(s).");
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning(
                $"[VisualizerCustomizer] ApplyTargetAimVisualizers failed: {ex.Message}");
        }
    }

    private static void ApplyTargetAimVisualizer(
        Il2CppTargetAimVisualizer instance,
        TargetAimVisualizerConfig cfg)
    {
        // In-range colour via MaterialPropertyBlock on the MeshRenderer.
        // The MeshRenderer is a private field (m_MeshRenderer) so we reach it
        // through the GameObject's component.  The renderer lives on a child
        // GameObject (m_MeshObject), so we search all child MeshRenderers.
        // isInRange: true -- no aim is active at apply time, default to in-range.
        if (cfg.InRangeColor.Enabled || cfg.InRangeEmissiveColor.Enabled || cfg.EmissiveIntensity >= 0f)
            TryApplyMaterialColor(instance.gameObject, cfg, isInRange: true);

        // Float parameters -- sentinel value -1 means "leave unchanged".
        if (cfg.AnimationScrollSpeed >= 0f)
            instance.AnimationScrollSpeed = cfg.AnimationScrollSpeed;
        if (cfg.Width >= 0f)
            instance.Width = cfg.Width;
        if (cfg.MinimumHeight >= 0f)
            instance.MinimumHeight = cfg.MinimumHeight;
        if (cfg.MaximumHeight >= 0f)
            instance.MaximumHeight = cfg.MaximumHeight;
        if (cfg.DistanceToHeightScale >= 0f)
            instance.DistanceToHeightScale = cfg.DistanceToHeightScale;

        // OutOfRangeColor: written to the native field so the component holds the
        // configured value, but UpdateAim() hardcodes white for the out-of-range
        // rendering path without reading this field.  The effective colour override
        // is applied via MPB in ReapplyTargetAimVisualizerColors (postfix on UpdateAim).
        if (cfg.OutOfRangeColor.Enabled)
        {
            instance.OutOfRangeColor = HUDCustomizerPlugin.ToColor(cfg.OutOfRangeColor, "TargetAimVisualizer.OutOfRangeColor");
            HUDCustomizerPlugin.Log.Msg($"[TargetAimViz] OutOfRangeColor written: {instance.OutOfRangeColor}");
        }
    }

    private static void TryApplyMaterialColor(GameObject root, TargetAimVisualizerConfig cfg, bool isInRange)
    {
        try
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (renderers == null || renderers.Length == 0)
            {
                HUDCustomizerPlugin.Debug(
                    "[VisualizerCustomizer] TargetAimVisualizer: no MeshRenderer found in children " +
                    "-- InRangeColor cannot be applied yet (mesh may not exist until UpdateAim is called).");
                return;
            }

            var block = new MaterialPropertyBlock();

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);

                // Gate the _UnlitColor MPB write on range state.
                // UpdateAim writes white (1,1,1,1) to material.SetColor for in-range, and
                // OutOfRangeColor for out-of-range.  Our MPB overrides the material, so we
                // must mirror the game's decision here rather than always stamping InRangeColor.
                if (isInRange)
                {
                    if (cfg.InRangeColor.Enabled)
                    {
                        var color = HUDCustomizerPlugin.ToColor(
                            cfg.InRangeColor, "TargetAimVisualizer.InRangeColor(_UnlitColor)");
                        block.SetColor(UnlitColorProperty, color);
                    }
                }
                else
                {
                    if (cfg.OutOfRangeColor.Enabled)
                    {
                        var color = HUDCustomizerPlugin.ToColor(
                            cfg.OutOfRangeColor, "TargetAimVisualizer.OutOfRangeColor(_UnlitColor)");
                        block.SetColor(UnlitColorProperty, color);
                    }
                }

                if (cfg.InRangeEmissiveColor.Enabled || cfg.EmissiveIntensity >= 0f)
                {
                    // Read the current emissive colour from the material as base,
                    // so we only change the components the user has configured.
                    Color emissive = renderer.material != null
                        ? renderer.material.GetColor(EmissiveColorProperty)
                        : Color.white;

                    if (cfg.InRangeEmissiveColor.Enabled)
                    {
                        // Override hue, preserve intensity from material unless
                        // EmissiveIntensity is also set.
                        float intensity = cfg.EmissiveIntensity >= 0f
                            ? cfg.EmissiveIntensity
                            : Mathf.Max(emissive.r, emissive.g, emissive.b);

                        var hue = HUDCustomizerPlugin.ToColor(
                            cfg.InRangeEmissiveColor,
                            "TargetAimVisualizer.InRangeEmissiveColor(_EmissiveColor)");

                        emissive = new Color(
                            hue.r * intensity,
                            hue.g * intensity,
                            hue.b * intensity,
                            hue.a);
                    }
                    else if (cfg.EmissiveIntensity >= 0f)
                    {
                        // Intensity-only override: scale the existing hue.
                        float maxChannel = Mathf.Max(emissive.r, emissive.g, emissive.b);
                        if (maxChannel > 0f)
                        {
                            float scale = cfg.EmissiveIntensity / maxChannel;
                            emissive = new Color(
                                emissive.r * scale,
                                emissive.g * scale,
                                emissive.b * scale,
                                emissive.a);
                        }
                    }

                    block.SetColor(EmissiveColorProperty, emissive);
                    HUDCustomizerPlugin.Debug(
                        $"[VisualizerCustomizer] TargetAimVisualizer._EmissiveColor set to {emissive}");
                }

                renderer.SetPropertyBlock(block);
                HUDCustomizerPlugin.Debug(
                    $"[VisualizerCustomizer] TargetAimVisualizer material colours applied to " +
                    $"MeshRenderer on '{renderer.gameObject.name}'.");
            }
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning(
                $"[VisualizerCustomizer] TryApplyMaterialColor failed: {ex.Message}");
        }
    }

    // =========================================================================
    // LineOfSightVisualizer
    // =========================================================================

    // Holds the last colour applied to LOS lines.
    // Written by TryApplyLineOfSight; read by LOSResizePatch in HUDCustomizer.cs.
    public static Color _currentLOSColor = Color.white;

    // Applies the given colour to every Il2CppShapes.Line child of the instance.
    // Children are grouped in sets of 3 (fade-in, solid, fade-out) identified by
    // (childIndex % 3).  Called from TryApplyLineOfSight and from LOSResizePatch
    // after every Resize() so the colour survives pool re-use.
    public static void ApplyLineOfSightColor(
        Il2CppLineOfSightVisualizer instance, Color color)
    {
        int childCount = instance.transform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = instance.transform.GetChild(i);
            if (child == null) continue;

            var line = child.gameObject.GetComponent<Il2CppShapes.Line>();
            if (line == null) continue;

            float r = color.r;
            float g = color.g;
            float b = color.b;
            float a = color.a;

            int posInGroup = i % 3;
            switch (posInGroup)
            {
                case 0: // fade-in: transparent -> opaque
                    line.ColorStart = new Color(r, g, b, 0f);
                    line.ColorEnd   = new Color(r, g, b, a);
                    break;
                case 1: // solid
                    line.ColorStart = new Color(r, g, b, a);
                    line.ColorEnd   = new Color(r, g, b, a);
                    break;
                case 2: // fade-out: opaque -> transparent
                    line.ColorStart = new Color(r, g, b, a);
                    line.ColorEnd   = new Color(r, g, b, 0f);
                    break;
            }
        }
    }

    // Finds all live LineOfSightVisualizer instances and applies the configured
    // line colour to each one.  Called from OnTacticalReady and hot-reload.
    public static void TryApplyLineOfSight(HUDCustomizerConfig cfg)
    {
        if (!cfg.Visualizers.LineOfSight.LineColor.Enabled) return;

        _currentLOSColor = HUDCustomizerPlugin.ToColor(
            cfg.Visualizers.LineOfSight.LineColor, "LineOfSight.LineColor");

        try
        {
            var instances = UnityEngine.Object.FindObjectsOfType<Il2CppLineOfSightVisualizer>();
            if (instances == null || instances.Length == 0)
            {
                HUDCustomizerPlugin.Debug(
                    "[VisualizerCustomizer] No LineOfSightVisualizer instances found.");
                return;
            }

            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i] == null) continue;
                try
                {
                    ApplyLineOfSightColor(instances[i], _currentLOSColor);
                }
                catch (Exception ex)
                {
                    HUDCustomizerPlugin.Log.Warning(
                        $"[VisualizerCustomizer] LineOfSightVisualizer instance apply failed: {ex.Message}");
                }
            }

            HUDCustomizerPlugin.Debug(
                $"[VisualizerCustomizer] LineOfSightVisualizer applied to {instances.Length} instance(s).");
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning(
                $"[VisualizerCustomizer] TryApplyLineOfSight failed: {ex.Message}");
        }
    }

    // Logs a summary line for the LineOfSight config block (called from LoadConfig).
    public static void LogLineOfSightSummary()
    {
        var e = HUDCustomizerPlugin.Config.Visualizers.LineOfSight.LineColor;
        if (e.Enabled)
            HUDCustomizerPlugin.Log.Msg(
                $"  [LineOfSight] LineColor=RGB({(int)e.R},{(int)e.G},{(int)e.B}) A({e.A:F2})");
        else
            HUDCustomizerPlugin.Log.Msg(
                "  [LineOfSight] LineColor: disabled (game default preserved)");
    }

    // =========================================================================
    // Re-apply after UpdateAim (called from patch in HUDCustomizer.cs)
    // UpdateAim rebuilds the spline mesh and rewrites material colours each call,
    // which resets any MPB overrides.  We re-apply here after every UpdateAim.
    //
    // Range state detection: UpdateAim writes to material.SetColor directly
    // (confirmed via Ghidra).  We read what it just wrote to detect state:
    //   in-range     -> game wrote white (1,1,1,1) to _UnlitColor
    //   out-of-range -> game hardcoded white too (OutOfRangeColor field is NOT
    //                   read by UpdateAim -- root cause confirmed via Ghidra).
    // For out-of-range we override _UnlitColor via MPB with the configured
    // OutOfRangeColor.  This is the fix that makes OutOfRangeColor functional.
    // =========================================================================
    internal static void ReapplyTargetAimVisualizerColors(Il2CppTargetAimVisualizer instance)
    {
        var cfg = HUDCustomizerPlugin.Config.Visualizers.TargetAimVisualizer;
        bool anyMaterial = cfg.InRangeColor.Enabled ||
                           cfg.InRangeEmissiveColor.Enabled ||
                           cfg.EmissiveIntensity >= 0f ||
                           cfg.OutOfRangeColor.Enabled;
        if (!anyMaterial) return;

        try
        {
            // Detect range state by reading what UpdateAim just wrote to the material.
            // The game calls renderer.GetMaterial().SetColor(...) directly (confirmed via
            // Ghidra: FUN_182978b10 = Renderer.GetMaterial, FUN_18296ac80 = Material.SetColor).
            // In-range: game writes white (0x3f800000 = 1.0f on all channels).
            // Out-of-range: game writes OutOfRangeColor from field offset 0x30.
            bool isInRange = true;
            var renderers = instance.gameObject.GetComponentsInChildren<MeshRenderer>();
            if (renderers != null && renderers.Length > 0)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || r.material == null) continue;
                    var gameColor = r.material.GetColor(UnlitColorProperty);
                    // White threshold: all channels > 0.99 means the game chose in-range.
                    isInRange = (gameColor.r > 0.99f && gameColor.g > 0.99f && gameColor.b > 0.99f);
                    break; // all renderers share the same material state; first non-null is sufficient
                }
            }

            TryApplyMaterialColor(instance.gameObject, cfg, isInRange);
        }
        catch (Exception ex)
        {
            HUDCustomizerPlugin.Log.Warning(
                $"[VisualizerCustomizer] ReapplyTargetAimVisualizerColors failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Logging
    // =========================================================================
    public static void LogSummary()
    {
        var cfg    = HUDCustomizerPlugin.Config.Visualizers;
        var active = new List<string>();

        void CheckColor(string label, TileHighlightEntry e)
        {
            if (e.Enabled) active.Add($"{label}=RGB({(int)e.R},{(int)e.G},{(int)e.B})");
        }
        void CheckFloat(string label, float v)
        {
            if (v >= 0f) active.Add($"{label}={v:F3}");
        }

        var mv = cfg.MovementVisualizer;
        CheckColor("Mvmt.Reachable",   mv.ReachableColor);
        CheckColor("Mvmt.Unreachable", mv.UnreachableColor);

        var tv = cfg.TargetAimVisualizer;
        CheckColor("Aim.OutOfRange",           tv.OutOfRangeColor);
        CheckColor("Aim.InRange(material)",    tv.InRangeColor);
        CheckColor("Aim.Emissive(hue)",        tv.InRangeEmissiveColor);
        CheckFloat("Aim.EmissiveIntensity",    tv.EmissiveIntensity);
        CheckFloat("Aim.AnimScrollSpeed",      tv.AnimationScrollSpeed);
        CheckFloat("Aim.Width",                tv.Width);
        CheckFloat("Aim.MinHeight",            tv.MinimumHeight);
        CheckFloat("Aim.MaxHeight",            tv.MaximumHeight);
        CheckFloat("Aim.DistToHeightScale",    tv.DistanceToHeightScale);

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [Visualizers] Overrides: none (all disabled / -1 -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [Visualizers] Overrides active ({active.Count}): {string.Join("  ", active)}");
    }
}
