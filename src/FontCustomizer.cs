using System;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

using Il2CppInterfaceElement = Il2CppMenace.UI.InterfaceElement;

// =============================================================================
// FontCustomizer
// Applies per-element font and font-size overrides to HUD text elements.
//
// Config structure (HUDCustomizer.json):
//   Each entry is a FontSettings { Font: "name", Size: float }.
//   Global is applied first as a fallback; per-element entries override it.
//   Font = "" and Size = 0 both mean "leave unchanged".
//
// Confirmed element names from scans:
//
//   UnitHUD / EntityHUD
//     Label, DarkLabel              -- bar value labels (default size 14)
//
//   ObjectivesTracker
//     ObjectivesTrackerHeadline     -- section heading (default size 12)
//     Points                        -- objective point value (default size 16)
//     Description                   -- objective description text (default size 10)
//     Label, DarkLabel              -- progress bar labels (default size 14)
//     SecondaryObjectivesHeadline   -- secondary section heading (default size 12)
//     RewardPoints                  -- reward point total (default size 16)
//
//   MissionInfoPanel
//     MissionName                   -- mission title (default size 12)
//     MissionDuration               -- mission timer (default size 12)
//
//   ObjectiveHUD  (confirmed by scan)
//     ObjectiveName                 -- objective name label
//     ObjectiveState                -- objective state label
//
// Font assets available (confirmed via Resources.FindObjectsOfTypeAll):
//   Jura-Regular, Jura-Bold, OCRAStd, Inconsolata-SemiBold, NotInter-Regular
//   NotoSansJP-Regular, NotoSansKR-Regular, NotoSansSC-Regular, NotoSansTC-Regular
// =============================================================================
public static class FontCustomizer
{
    // Font asset cache: name -> Font.  Populated once at TacticalReady,
    // cleared on hot-reload.
    private static readonly Dictionary<string, Font> _fontCache
        = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
    private static bool _cacheValid = false;

    // Called from HUDCustomizerPlugin.OnTacticalReady().
    public static void OnTacticalReady()
    {
        BuildFontCache();
        Scans.RunFontScan(_fontCache);
    }

    // Clears the cache so the next Apply() call re-resolves.
    // Called from HUDCustomizerPlugin.OnUpdate() on hot-reload.
    public static void InvalidateCache()
    {
        _fontCache.Clear();
        _cacheValid = false;
    }

    // =========================================================================
    // Font cache
    // Populated once at TacticalReady from game-bundled assets via
    // Resources.FindObjectsOfTypeAll<Font>().  Cleared and rebuilt on hot-reload.
    //
    // OS font loading via Font.CreateDynamicFontFromOSFont is not supported --
    // the method is stripped in this Il2Cpp build.  Only the game-bundled font
    // names listed in the config comments are valid.
    // =========================================================================
    private static void BuildFontCache()
    {
        if (_cacheValid) return;
        _cacheValid = true;
        _fontCache.Clear();

        // 1. Game-bundled fonts
        var all = Resources.FindObjectsOfTypeAll<Font>();
        foreach (var f in all)
            if (!string.IsNullOrEmpty(f.name))
                _fontCache[f.name] = f;

        HUDCustomizerPlugin.Debug(
            $"  [Font] Cache built: {_fontCache.Count} game font(s) loaded.");
    }

    // Returns the Font for a FontSettings entry, or null if Font is empty.
    // Looks up the name in the game-bundled asset cache (exact match).
    // Note: Font.CreateDynamicFontFromOSFont is stripped in this Il2Cpp build
    // and cannot be used.  Use one of the game-bundled font names from the scan.
    private static Font Resolve(FontSettings s)
    {
        if (s == null || string.IsNullOrEmpty(s.Font)) return null;
        if (!_cacheValid) BuildFontCache();

        if (_fontCache.TryGetValue(s.Font, out var f)) return f;

        HUDCustomizerPlugin.Log.Warning(
            $"[FontCustomizer] Font '{s.Font}' not found. " +
            $"Available game fonts: {string.Join(", ", _fontCache.Keys)}. " +
            $"Check the font scan log for the full list of names.");
        return null;
    }


    // =========================================================================
    // Apply
    // Dispatches to the correct per-HUD method based on element type name.
    // Called from every patch postfix.
    // =========================================================================
    // hudType is the original Il2Cpp concrete type name supplied by the patch,
    // e.g. "UnitHUD", "ObjectivesTracker".  This is used instead of
    // element.GetType().Name because casting to Il2CppInterfaceElement loses the
    // concrete type information at runtime.
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
            case "DropdownText":
                ApplyDropdownText(element);
                break;
            default:
                // Unknown type -- apply global only as best-effort.
                ApplyGlobalFallback(element);
                break;
        }
    }

    // =========================================================================
    // Per-HUD application
    // Each method resolves its FontSettings entries (with Global as fallback)
    // and applies them to named elements via Q().
    // =========================================================================

    private static void ApplyUnitHUD(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        // Label and DarkLabel appear inside each bar's DarkLabelClip / Pickable.
        // Q() finds the first match anywhere in the subtree.
        SetFont(el.Q("Label",     (string)null), Merge(cfg.Global, cfg.UnitBarLabel), "UnitHUD.Label");
        SetFont(el.Q("DarkLabel", (string)null), Merge(cfg.Global, cfg.UnitBarLabel), "UnitHUD.DarkLabel");
    }

    private static void ApplyObjectivesTracker(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;

        SetFont(el.Q("ObjectivesTrackerHeadline",   (string)null),
                Merge(cfg.Global, cfg.ObjTrackerHeadline),    "ObjTracker.Headline");
        SetFont(el.Q("SecondaryObjectivesHeadline", (string)null),
                Merge(cfg.Global, cfg.ObjSecondaryHeadline),  "ObjTracker.SecondaryHeadline");
        SetFont(el.Q("RewardPoints",                (string)null),
                Merge(cfg.Global, cfg.ObjRewardPoints),       "ObjTracker.RewardPoints");
        SetFont(el.Q("Label",                       (string)null),
                Merge(cfg.Global, cfg.ObjTrackerLabel),       "ObjTracker.Label");
        SetFont(el.Q("DarkLabel",                   (string)null),
                Merge(cfg.Global, cfg.ObjTrackerLabel),       "ObjTracker.DarkLabel");

        // Points and Description repeat per objective entry -- apply to all.
        SetFontAll(el, "Points",      Merge(cfg.Global, cfg.ObjTrackerPoints),      "ObjTracker.Points");
        SetFontAll(el, "Description", Merge(cfg.Global, cfg.ObjTrackerDescription), "ObjTracker.Description");
    }

    private static void ApplyMissionInfoPanel(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        SetFont(el.Q("MissionName",     (string)null),
                Merge(cfg.Global, cfg.MissionName),     "MissionInfo.Name");
        SetFont(el.Q("MissionDuration", (string)null),
                Merge(cfg.Global, cfg.MissionDuration), "MissionInfo.Duration");
    }

    private static void ApplyObjectiveHUD(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        // Element names confirmed by scan: "ObjectiveName" and "ObjectiveState".
        // (Source file fields m_NameLabel / m_StateLabel do not match the UXML
        // name attributes -- UIToolkit uses the full camel-case name, not the
        // stripped m_ form.)
        QueryAndSet(el, "ObjectiveName",  Merge(cfg.Global, cfg.ObjectiveNameLabel),  "ObjectiveHUD.ObjectiveName");
        QueryAndSet(el, "ObjectiveState", Merge(cfg.Global, cfg.ObjectiveStateLabel), "ObjectiveHUD.ObjectiveState");
    }

    private static void ApplyMovementHUD(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        // Confirmed by scan: CostLabel (fontSize=16), ActionLabel (fontSize=14).
        SetFont(el.Q("CostLabel",   (string)null),
                Merge(cfg.Global, cfg.MovementCostLabel),   "MovementHUD.CostLabel");
        SetFont(el.Q("ActionLabel", (string)null),
                Merge(cfg.Global, cfg.MovementActionLabel), "MovementHUD.ActionLabel");
    }

    private static void ApplyBleedingWorldSpaceIcon(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        SetFont(el.Q("TextElement", (string)null), Merge(cfg.Global, cfg.BleedingIconText), "BleedingIconText");
    }

    private static void ApplyDropdownText(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        // Element name confirmed by scan: "Label" (fontSize=14, USS class font-headline).
        // USS class font-headline means font is inherited from the global USS theme;
        // setting an inline style here takes precedence over it.
        SetFont(el.Q("Label", (string)null), Merge(cfg.Global, cfg.DropdownText), "DropdownText.Label");
    }

    // Fallback: applies Global settings to every unity-text-element in the tree.
    private static void ApplyGlobalFallback(Il2CppInterfaceElement el)
    {
        var cfg = HUDCustomizerPlugin.Config;
        if (string.IsNullOrEmpty(cfg.Global.Font) && cfg.Global.Size <= 0f && string.IsNullOrEmpty(cfg.Global.Color)) return;
        SetFontAll(el, null, Merge(cfg.Global, null), $"{el.GetType().Name}.Global");
    }

    // =========================================================================
    // Merge helper
    // Returns a resolved (Font, Size) pair where the per-element entry takes
    // precedence over Global, and Global fills in any unset values.
    // =========================================================================
    private static (Font font, float size, string color) Merge(FontSettings global, FontSettings specific)
    {
        Font   font  = Resolve(specific) ?? Resolve(global);
        float  size  = (specific?.Size  > 0f)                    ? specific.Size
                     : (global?.Size    > 0f)                    ? global.Size  : 0f;
        string color = !string.IsNullOrEmpty(specific?.Color)    ? specific.Color
                     : !string.IsNullOrEmpty(global?.Color)      ? global.Color : "";
        return (font, size, color);
    }

    // =========================================================================
    // Style setters
    // =========================================================================

    // Queries for elementName, warns if it is not found and settings are
    // non-empty (indicating the user has configured something for this slot),
    // then delegates to SetFont.  Use this instead of SetFont(el.Q(...), ...)
    // for element names that are unconfirmed or may silently fail to resolve.
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

    // Applies a (font, size, color) tuple to a single VisualElement.
    private static void SetFont(VisualElement ve, (Font font, float size, string color) settings,
                                string debugLabel)
    {
        if (ve == null) return;
        var (font, size, color) = settings;

        if (font != null)
        {
            ve.style.unityFontDefinition =
                new StyleFontDefinition(FontDefinition.FromFont(font));
            HUDCustomizerPlugin.Debug(
                $"  [Font] {debugLabel}  font='{font.name}'");
        }

        if (size > 0f)
        {
            ve.style.fontSize = new StyleLength(size);
            HUDCustomizerPlugin.Debug(
                $"  [Font] {debugLabel}  size={size}");
        }

        if (!string.IsNullOrEmpty(color) &&
            HUDCustomizerPlugin.TryParseColor(color, out var col))
        {
            ve.style.color = new StyleColor(col);
            HUDCustomizerPlugin.Debug(
                $"  [Font] {debugLabel}  color={col}");
        }
    }

    // Applies settings to every element matching elementName (or all
    // unity-text-elements if elementName is null) anywhere in the subtree.
    private static void SetFontAll(VisualElement root, string elementName,
                                   (Font font, float size, string color) settings,
                                   string debugLabel)
    {
        if (settings.font == null && settings.size <= 0f && string.IsNullOrEmpty(settings.color)) return;
        WalkAndSet(root, elementName, settings, debugLabel);
    }

    private static void WalkAndSet(VisualElement ve, string targetName,
                                   (Font font, float size, string color) settings,
                                   string debugLabel)
    {
        for (int i = 0; i < ve.childCount; i++)
        {
            var child = ve.ElementAt(i);
            if (child == null) continue;

            bool matches = targetName == null
                ? HUDCustomizerPlugin.GetClasses(child).Contains("unity-text-element")
                : child.name == targetName;

            if (matches) SetFont(child, settings, debugLabel);

            // Recursion always continues into children even after a match.
            // For targetName == null (global fallback) this is necessary since
            // unity-text-elements can be nested.  For named targeting this means
            // all elements sharing the same name in the subtree are styled, which
            // is consistent behaviour -- do not add an early 'continue' here.
            WalkAndSet(child, targetName, settings, debugLabel);
        }
    }

    // =========================================================================
    // Logging
    // =========================================================================
    public static void LogFontSummary()
    {
        var cfg    = HUDCustomizerPlugin.Config;
        var active = new List<string>();

        void Check(string label, FontSettings s)
        {
            if (s == null) return;
            if (!string.IsNullOrEmpty(s.Font))  active.Add($"{label}.Font={s.Font}");
            if (s.Size > 0f)                    active.Add($"{label}.Size={s.Size}");
            if (!string.IsNullOrEmpty(s.Color)) active.Add($"{label}.Color={s.Color}");
        }

        Check("Global",               cfg.Global);
        Check("UnitBarLabel",         cfg.UnitBarLabel);
        Check("ObjTrackerHeadline",   cfg.ObjTrackerHeadline);
        Check("ObjTrackerPoints",     cfg.ObjTrackerPoints);
        Check("ObjTrackerDesc",       cfg.ObjTrackerDescription);
        Check("ObjTrackerLabel",      cfg.ObjTrackerLabel);
        Check("ObjSecHeadline",       cfg.ObjSecondaryHeadline);
        Check("ObjRewardPoints",      cfg.ObjRewardPoints);
        Check("MissionName",          cfg.MissionName);
        Check("MissionDuration",      cfg.MissionDuration);
        Check("ObjHUDName",           cfg.ObjectiveNameLabel);
        Check("ObjHUDState",          cfg.ObjectiveStateLabel);
        Check("MovementCostLabel",    cfg.MovementCostLabel);
        Check("MovementActionLabel",  cfg.MovementActionLabel);
        Check("BleedingIconText",     cfg.BleedingIconText);
        Check("DropdownText",         cfg.DropdownText);

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [Font] Overrides: none (all empty -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [Font] Overrides active ({active.Count}): {string.Join("  ", active)}");
    }

}
