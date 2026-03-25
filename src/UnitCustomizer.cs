using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

using Il2CppInterfaceElement = Il2CppMenace.UI.InterfaceElement;

// =============================================================================
// UnitCustomizer
// Responsible for all visual customisation of UnitHUD and EntityHUD elements:
//   - CSS scale and transform origin
//   - Bar fill, preview, and track background colours
//   - Badge sprite tint
//
// Element paths confirmed by scan:
//   root
//     [0] Pickable
//     [1] Container
//           [0] DetailsContainer
//                 [0] Bars
//                       [0] Suppression > [0] Pickable (track) > [1] Fill, [2] PreviewFill
//                       [1] Armor       > [0] Pickable (track) > [1] Fill, [2] PreviewFill
//                       [2] Hitpoints   > [0] Pickable (track) > [1] Fill, [2] PreviewFill
//           [1] ContainedBadge  (tinted via unityBackgroundImageTintColor)
// =============================================================================
public static class UnitCustomizer
{
    // Apply scale and colours to a unit or entity HUD element.
    public static void Apply(Il2CppInterfaceElement element, float scale)
    {
        if (element == null) return;
        ApplyScale(element, scale);
        ApplyColours(element);
    }

    // =========================================================================
    // Scale
    // =========================================================================
    private static void ApplyScale(Il2CppInterfaceElement element, float scale)
    {
        var style = element.style;
        if (style == null)
        {
            HUDCustomizerPlugin.Log.Warning(
                $"[UnitCustomizer] ApplyScale: style is null on {element.GetType().Name}");
            return;
        }

        style.scale = new StyleScale(new Scale(new Vector2(scale, scale)));
        style.transformOrigin = new StyleTransformOrigin(
            new TransformOrigin(
                Length.Percent(HUDCustomizerPlugin.Config.TransformOriginX),
                Length.Percent(HUDCustomizerPlugin.Config.TransformOriginY),
                0f));

        HUDCustomizerPlugin.Debug(
            $"  [Unit] ApplyScale -> {element.GetType().Name}  scale={scale:F2}  " +
            $"origin=({HUDCustomizerPlugin.Config.TransformOriginX:F0}%," +
            $"{HUDCustomizerPlugin.Config.TransformOriginY:F0}%)");
    }

    // =========================================================================
    // Colours
    // =========================================================================
    internal static void ApplyColours(Il2CppInterfaceElement element)
    {
        var cfg = HUDCustomizerPlugin.Config;

        // Badge tint
        SetTint(element.Q("ContainedBadge", (string)null),
                cfg.BadgeTintColor, "ContainedBadge.tint");

        // Health, armour, suppression bars
        ApplyBarColours(element, "Hitpoints",
            cfg.HitpointsFillColor, cfg.HitpointsPreviewColor, cfg.HitpointsTrackColor);
        ApplyBarColours(element, "Armor",
            cfg.ArmorFillColor,     cfg.ArmorPreviewColor,     cfg.ArmorTrackColor);
        ApplyBarColours(element, "Suppression",
            cfg.SuppressionFillColor, cfg.SuppressionPreviewColor, cfg.SuppressionTrackColor);
    }

    // Locates the named bar container and applies fill / preview / track colours.
    // barName is the confirmed element name: "Hitpoints", "Armor", "Suppression".
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

    // =========================================================================
    // Style setters
    // =========================================================================
    private static void SetBackground(VisualElement ve, string colorStr, string debugLabel)
    {
        if (ve == null || string.IsNullOrEmpty(colorStr)) return;
        if (!HUDCustomizerPlugin.TryParseColor(colorStr, out var color)) return;
        ve.style.backgroundColor = new StyleColor(color);
        HUDCustomizerPlugin.Debug($"  [Unit] SetBackground -> {debugLabel} = {color}");
    }

    private static void SetTint(VisualElement ve, string colorStr, string debugLabel)
    {
        if (ve == null || string.IsNullOrEmpty(colorStr)) return;
        if (!HUDCustomizerPlugin.TryParseColor(colorStr, out var color)) return;
        ve.style.unityBackgroundImageTintColor = new StyleColor(color);
        HUDCustomizerPlugin.Debug($"  [Unit] SetTint -> {debugLabel} = {color}");
    }


    // =========================================================================
    // Logging
    // =========================================================================
    public static void LogColourSummary()
    {
        var cfg    = HUDCustomizerPlugin.Config;
        var active = new System.Collections.Generic.List<string>();

        if (!string.IsNullOrEmpty(cfg.HitpointsFillColor))
            active.Add($"HP.Fill={cfg.HitpointsFillColor}");
        if (!string.IsNullOrEmpty(cfg.HitpointsPreviewColor))
            active.Add($"HP.Preview={cfg.HitpointsPreviewColor}");
        if (!string.IsNullOrEmpty(cfg.HitpointsTrackColor))
            active.Add($"HP.Track={cfg.HitpointsTrackColor}");
        if (!string.IsNullOrEmpty(cfg.ArmorFillColor))
            active.Add($"Armor.Fill={cfg.ArmorFillColor}");
        if (!string.IsNullOrEmpty(cfg.ArmorPreviewColor))
            active.Add($"Armor.Preview={cfg.ArmorPreviewColor}");
        if (!string.IsNullOrEmpty(cfg.ArmorTrackColor))
            active.Add($"Armor.Track={cfg.ArmorTrackColor}");
        if (!string.IsNullOrEmpty(cfg.SuppressionFillColor))
            active.Add($"Supp.Fill={cfg.SuppressionFillColor}");
        if (!string.IsNullOrEmpty(cfg.SuppressionPreviewColor))
            active.Add($"Supp.Preview={cfg.SuppressionPreviewColor}");
        if (!string.IsNullOrEmpty(cfg.SuppressionTrackColor))
            active.Add($"Supp.Track={cfg.SuppressionTrackColor}");
        if (!string.IsNullOrEmpty(cfg.BadgeTintColor))
            active.Add($"Badge.Tint={cfg.BadgeTintColor}");

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [Unit] Colour overrides: none (all empty -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [Unit] Colour overrides active ({active.Count}): " +
                string.Join("  ", active));
    }

    // =========================================================================
    // Faction health bar colours
    // Applied via UIConfig singleton at TacticalReady and on hot-reload.
    // These control health bar colours in the unit infobox shown when a unit is
    // selected by the player.  They do NOT affect the floating Entity HUD bars
    // above units -- those are patched directly via Patch_UnitHUD_SetActor /
    // Patch_UnitHUD_Show and styled via HitpointsFillColor / HitpointsPreviewColor.
    // =========================================================================
    public static void ApplyFactionHealthBarColors()
    {
        var uiConfig = Il2CppMenace.UI.UIConfig.Get();
        if (uiConfig == null)
        {
            HUDCustomizerPlugin.Log.Warning("[UnitCustomizer] ApplyFactionHealthBarColors: UIConfig.Get() returned null.");
            return;
        }

        var f = HUDCustomizerPlugin.Config.FactionHealthBarColors;

        if (f.HealthBarFillColorPlayerUnits.Enabled)    uiConfig.HealthBarFillColorPlayerUnits    = HUDCustomizerPlugin.ToColor(f.HealthBarFillColorPlayerUnits,    "HealthBarFillColorPlayerUnits");
        if (f.HealthBarPreviewColorPlayerUnits.Enabled) uiConfig.HealthBarPreviewColorPlayerUnits = HUDCustomizerPlugin.ToColor(f.HealthBarPreviewColorPlayerUnits, "HealthBarPreviewColorPlayerUnits");
        if (f.HealthBarFillColorAllies.Enabled)         uiConfig.HealthBarFillColorAllies         = HUDCustomizerPlugin.ToColor(f.HealthBarFillColorAllies,         "HealthBarFillColorAllies");
        if (f.HealthBarPreviewColorAllies.Enabled)      uiConfig.HealthBarPreviewColorAllies      = HUDCustomizerPlugin.ToColor(f.HealthBarPreviewColorAllies,      "HealthBarPreviewColorAllies");
        if (f.HealthBarFillColorEnemies.Enabled)        uiConfig.HealthBarFillColorEnemies        = HUDCustomizerPlugin.ToColor(f.HealthBarFillColorEnemies,        "HealthBarFillColorEnemies");
        if (f.HealthBarPreviewColorEnemies.Enabled)     uiConfig.HealthBarPreviewColorEnemies     = HUDCustomizerPlugin.ToColor(f.HealthBarPreviewColorEnemies,     "HealthBarPreviewColorEnemies");
        if (f.HealthBarSectionColorPlayerUnits.Enabled) uiConfig.HealthBarSectionColorPlayerUnits = HUDCustomizerPlugin.ToColor(f.HealthBarSectionColorPlayerUnits, "HealthBarSectionColorPlayerUnits");
        if (f.HealthBarSectionColorEnemies.Enabled)     uiConfig.HealthBarSectionColorEnemies     = HUDCustomizerPlugin.ToColor(f.HealthBarSectionColorEnemies,     "HealthBarSectionColorEnemies");

        HUDCustomizerPlugin.Debug("[UnitCustomizer] Faction health bar colours applied.");
    }

    public static void ApplyRarityColors()
    {
        var uiConfig = Il2CppMenace.UI.UIConfig.Get();
        if (uiConfig == null)
        {
            HUDCustomizerPlugin.Log.Warning("[UnitCustomizer] ApplyRarityColors: UIConfig.Get() returned null.");
            return;
        }

        var r = HUDCustomizerPlugin.Config.RarityColors;

        if (r.Common.Enabled)        uiConfig.ColorCommonRarity       = HUDCustomizerPlugin.ToColor(r.Common,        "ColorCommonRarity");
        if (r.CommonNamed.Enabled)   uiConfig.ColorCommonRarityNamed  = HUDCustomizerPlugin.ToColor(r.CommonNamed,   "ColorCommonRarityNamed");
        if (r.Uncommon.Enabled)      uiConfig.ColorUncommonRarity     = HUDCustomizerPlugin.ToColor(r.Uncommon,      "ColorUncommonRarity");
        if (r.UncommonNamed.Enabled) uiConfig.ColorUncommonRarityNamed= HUDCustomizerPlugin.ToColor(r.UncommonNamed, "ColorUncommonRarityNamed");
        if (r.Rare.Enabled)          uiConfig.ColorRareRarity         = HUDCustomizerPlugin.ToColor(r.Rare,          "ColorRareRarity");
        if (r.RareNamed.Enabled)     uiConfig.ColorRareRarityNamed    = HUDCustomizerPlugin.ToColor(r.RareNamed,     "ColorRareRarityNamed");
        if (r.ColorPositionMarkerDelayedAbility.Enabled) uiConfig.ColorPositionMarkerDelayedAbility = HUDCustomizerPlugin.ToColor(r.ColorPositionMarkerDelayedAbility, "ColorPositionMarkerDelayedAbility");

        // Read back from the live UIConfig instance to confirm writes landed.
        var uiConfigCheck = Il2CppMenace.UI.UIConfig.Get();
        if (uiConfigCheck != null)
        {
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorCommonRarity = {uiConfigCheck.ColorCommonRarity}");
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorCommonRarityNamed = {uiConfigCheck.ColorCommonRarityNamed}");
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorUncommonRarity = {uiConfigCheck.ColorUncommonRarity}");
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorUncommonRarityNamed = {uiConfigCheck.ColorUncommonRarityNamed}");
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorRareRarity = {uiConfigCheck.ColorRareRarity}");
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorRareRarityNamed = {uiConfigCheck.ColorRareRarityNamed}");
            HUDCustomizerPlugin.Log.Msg($"[RarityCheck] ColorPositionMarkerDelayedAbility = {uiConfigCheck.ColorPositionMarkerDelayedAbility}");
        }

        HUDCustomizerPlugin.Debug("[UnitCustomizer] Rarity colours applied.");
    }

    public static void LogFactionHealthBarSummary()
    {
        var f      = HUDCustomizerPlugin.Config.FactionHealthBarColors;
        var active = new List<string>();

        void Check(string label, TileHighlightEntry e)
        {
            if (e.Enabled) active.Add($"{label}=RGB({(int)e.R},{(int)e.G},{(int)e.B})");
        }

        Check("FillPlayer",    f.HealthBarFillColorPlayerUnits);
        Check("PreviewPlayer", f.HealthBarPreviewColorPlayerUnits);
        Check("FillAllies",    f.HealthBarFillColorAllies);
        Check("PreviewAllies", f.HealthBarPreviewColorAllies);
        Check("FillEnemies",   f.HealthBarFillColorEnemies);
        Check("PreviewEnemies",f.HealthBarPreviewColorEnemies);
        Check("SectionPlayer", f.HealthBarSectionColorPlayerUnits);
        Check("SectionEnemies",f.HealthBarSectionColorEnemies);

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [Unit] Faction health bar overrides: none (all disabled -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [Unit] Faction health bar overrides active ({active.Count}): {string.Join("  ", active)}");
    }

    public static void LogRarityColorSummary()
    {
        var r      = HUDCustomizerPlugin.Config.RarityColors;
        var active = new List<string>();

        void Check(string label, TileHighlightEntry e)
        {
            if (e.Enabled) active.Add($"{label}=RGB({(int)e.R},{(int)e.G},{(int)e.B})");
        }

        Check("Common",        r.Common);
        Check("CommonNamed",   r.CommonNamed);
        Check("Uncommon",      r.Uncommon);
        Check("UncommonNamed", r.UncommonNamed);
        Check("Rare",          r.Rare);
        Check("RareNamed",     r.RareNamed);
        Check("PosMarkerDelayedAbility", r.ColorPositionMarkerDelayedAbility);

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [Unit] Rarity colour overrides: none (all disabled -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [Unit] Rarity colour overrides active ({active.Count}): {string.Join("  ", active)}");
    }
}
