using System.Collections.Generic;

using UnityEngine;

// =============================================================================
// USSCustomizer
// Applies USS global theme colour overrides via UIConfig.Get().
//
// UIConfig is a static singleton (confirmed in UIConfig.cs / DataTemplate.cs).
// Setting a Color field on the UIConfig instance changes the USS custom property
// game-wide -- this affects ALL UI screens, not just tactical HUDs.
//
// TryApply() is called from HUDCustomizerPlugin.OnTacticalReady() and from
// OnUpdate() on hot-reload -- no Harmony patches needed.
//
// Config: HUDCustomizer.json -> "USSColors" object.
// Each slot: { "Enabled": bool, "R": 0-255, "G": 0-255, "B": 0-255, "A": 0.0-1.0 }
// Enabled = false leaves that slot at the game default.
//
// Confirmed field list from UIConfig.cs source file (28 slots total):
//   23 general USS theme fields (ColorNormal .. ColorEmptySlotIcon)
//    5 mission state fields: ColorMissionPlayable, ColorMissionLocked,
//      ColorMissionPlayed, ColorMissionPlayedArrow, ColorMissionUnplayable
//      (all carry [UssColor] -- confirmed in dump.cs; implemented here via TryApply())
// Game default values confirmed by UIConfig scan.
// =============================================================================
public static class USSCustomizer
{
    // =========================================================================
    // Apply
    // =========================================================================
    public static void TryApply()
    {
        var uiConfig = Il2CppMenace.UI.UIConfig.Get();
        if (uiConfig == null)
        {
            HUDCustomizerPlugin.Log.Warning("[USSCustomizer] UIConfig.Get() returned null -- skipping.");
            return;
        }

        var c = HUDCustomizerPlugin.Config.USSColors;

        if (c.ColorNormal.Enabled)               uiConfig.ColorNormal               = HUDCustomizerPlugin.ToColor(c.ColorNormal,               "ColorNormal");
        if (c.ColorBright.Enabled)               uiConfig.ColorBright               = HUDCustomizerPlugin.ToColor(c.ColorBright,               "ColorBright");
        if (c.ColorNormalTransparent.Enabled)    uiConfig.ColorNormalTransparent    = HUDCustomizerPlugin.ToColor(c.ColorNormalTransparent,    "ColorNormalTransparent");
        if (c.ColorInteract.Enabled)             uiConfig.ColorInteract             = HUDCustomizerPlugin.ToColor(c.ColorInteract,             "ColorInteract");
        if (c.ColorInteractDark.Enabled)         uiConfig.ColorInteractDark         = HUDCustomizerPlugin.ToColor(c.ColorInteractDark,         "ColorInteractDark");
        if (c.ColorInteractHover.Enabled)        uiConfig.ColorInteractHover        = HUDCustomizerPlugin.ToColor(c.ColorInteractHover,        "ColorInteractHover");
        if (c.ColorInteractSelected.Enabled)     uiConfig.ColorInteractSelected     = HUDCustomizerPlugin.ToColor(c.ColorInteractSelected,     "ColorInteractSelected");
        if (c.ColorInteractSelectedText.Enabled) uiConfig.ColorInteractSelectedText = HUDCustomizerPlugin.ToColor(c.ColorInteractSelectedText, "ColorInteractSelectedText");
        if (c.ColorDisabled.Enabled)             uiConfig.ColorDisabled             = HUDCustomizerPlugin.ToColor(c.ColorDisabled,             "ColorDisabled");
        if (c.ColorDisabledHover.Enabled)        uiConfig.ColorDisabledHover        = HUDCustomizerPlugin.ToColor(c.ColorDisabledHover,        "ColorDisabledHover");
        if (c.ColorTooltipBetter.Enabled)        uiConfig.ColorTooltipBetter        = HUDCustomizerPlugin.ToColor(c.ColorTooltipBetter,        "ColorTooltipBetter");
        if (c.ColorTooltipWorse.Enabled)         uiConfig.ColorTooltipWorse         = HUDCustomizerPlugin.ToColor(c.ColorTooltipWorse,         "ColorTooltipWorse");
        if (c.ColorTooltipNormal.Enabled)        uiConfig.ColorTooltipNormal        = HUDCustomizerPlugin.ToColor(c.ColorTooltipNormal,        "ColorTooltipNormal");
        if (c.ColorPositive.Enabled)             uiConfig.ColorPositive             = HUDCustomizerPlugin.ToColor(c.ColorPositive,             "ColorPositive");
        if (c.ColorNegative.Enabled)             uiConfig.ColorNegative             = HUDCustomizerPlugin.ToColor(c.ColorNegative,             "ColorNegative");
        if (c.ColorWarning.Enabled)              uiConfig.ColorWarning              = HUDCustomizerPlugin.ToColor(c.ColorWarning,              "ColorWarning");
        if (c.ColorDarkBg.Enabled)               uiConfig.ColorDarkBg               = HUDCustomizerPlugin.ToColor(c.ColorDarkBg,               "ColorDarkBg");
        if (c.ColorWindowCorner.Enabled)         uiConfig.ColorWindowCorner         = HUDCustomizerPlugin.ToColor(c.ColorWindowCorner,         "ColorWindowCorner");
        if (c.ColorTopBar.Enabled)               uiConfig.ColorTopBar               = HUDCustomizerPlugin.ToColor(c.ColorTopBar,               "ColorTopBar");
        if (c.ColorTopBarDark.Enabled)           uiConfig.ColorTopBarDark           = HUDCustomizerPlugin.ToColor(c.ColorTopBarDark,           "ColorTopBarDark");
        if (c.ColorProgressBarNormal.Enabled)    uiConfig.ColorProgressBarNormal    = HUDCustomizerPlugin.ToColor(c.ColorProgressBarNormal,    "ColorProgressBarNormal");
        if (c.ColorProgressBarBright.Enabled)    uiConfig.ColorProgressBarBright    = HUDCustomizerPlugin.ToColor(c.ColorProgressBarBright,    "ColorProgressBarBright");
        if (c.ColorEmptySlotIcon.Enabled)        uiConfig.ColorEmptySlotIcon        = HUDCustomizerPlugin.ToColor(c.ColorEmptySlotIcon,        "ColorEmptySlotIcon");

        // Mission state colours
        if (c.ColorMissionPlayable.Enabled)    uiConfig.ColorMissionPlayable    = HUDCustomizerPlugin.ToColor(c.ColorMissionPlayable,    "ColorMissionPlayable");
        if (c.ColorMissionLocked.Enabled)      uiConfig.ColorMissionLocked      = HUDCustomizerPlugin.ToColor(c.ColorMissionLocked,      "ColorMissionLocked");
        if (c.ColorMissionPlayed.Enabled)      uiConfig.ColorMissionPlayed      = HUDCustomizerPlugin.ToColor(c.ColorMissionPlayed,      "ColorMissionPlayed");
        if (c.ColorMissionPlayedArrow.Enabled) uiConfig.ColorMissionPlayedArrow = HUDCustomizerPlugin.ToColor(c.ColorMissionPlayedArrow, "ColorMissionPlayedArrow");
        if (c.ColorMissionUnplayable.Enabled)  uiConfig.ColorMissionUnplayable  = HUDCustomizerPlugin.ToColor(c.ColorMissionUnplayable,  "ColorMissionUnplayable");

        HUDCustomizerPlugin.Debug("[USSCustomizer] USS colours applied.");
    }

    // =========================================================================
    // Logging
    // =========================================================================
    public static void LogSummary()
    {
        var c      = HUDCustomizerPlugin.Config.USSColors;
        var active = new List<string>();

        void Check(string label, TileHighlightEntry e)
        {
            if (e.Enabled) active.Add($"{label}=RGB({(int)e.R},{(int)e.G},{(int)e.B})");
        }

        Check("ColorNormal",               c.ColorNormal);
        Check("ColorBright",               c.ColorBright);
        Check("ColorNormalTransparent",    c.ColorNormalTransparent);
        Check("ColorInteract",             c.ColorInteract);
        Check("ColorInteractDark",         c.ColorInteractDark);
        Check("ColorInteractHover",        c.ColorInteractHover);
        Check("ColorInteractSelected",     c.ColorInteractSelected);
        Check("ColorInteractSelectedText", c.ColorInteractSelectedText);
        Check("ColorDisabled",             c.ColorDisabled);
        Check("ColorDisabledHover",        c.ColorDisabledHover);
        Check("ColorTooltipBetter",        c.ColorTooltipBetter);
        Check("ColorTooltipWorse",         c.ColorTooltipWorse);
        Check("ColorTooltipNormal",        c.ColorTooltipNormal);
        Check("ColorPositive",             c.ColorPositive);
        Check("ColorNegative",             c.ColorNegative);
        Check("ColorWarning",              c.ColorWarning);
        Check("ColorDarkBg",               c.ColorDarkBg);
        Check("ColorWindowCorner",         c.ColorWindowCorner);
        Check("ColorTopBar",               c.ColorTopBar);
        Check("ColorTopBarDark",           c.ColorTopBarDark);
        Check("ColorProgressBarNormal",    c.ColorProgressBarNormal);
        Check("ColorProgressBarBright",    c.ColorProgressBarBright);
        Check("ColorEmptySlotIcon",        c.ColorEmptySlotIcon);
        Check("ColorMissionPlayable",    c.ColorMissionPlayable);
        Check("ColorMissionLocked",      c.ColorMissionLocked);
        Check("ColorMissionPlayed",      c.ColorMissionPlayed);
        Check("ColorMissionPlayedArrow", c.ColorMissionPlayedArrow);
        Check("ColorMissionUnplayable",  c.ColorMissionUnplayable);

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [USS] Colour overrides: none (all disabled -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [USS] Colour overrides active ({active.Count}): {string.Join("  ", active)}");
    }
}
