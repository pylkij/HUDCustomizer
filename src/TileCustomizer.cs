using System.Collections.Generic;

using UnityEngine;

using Il2CppTileHighlighter = Il2CppMenace.Tactical.TileHighlighter;
using Il2CppColorOverrides  = Il2CppMenace.Tactical.TileHighlightColorOverrides;
using Il2CppColorOverride   = Il2CppMenace.Tools.ColorOverride;

// =============================================================================
// TileCustomizer
// Applies tile highlight colour overrides via TileHighlighter.SetColorOverrides.
//
// TileHighlighter is a singleton accessed via Exists()/Instance().
// TryApply() is called from HUDCustomizerPlugin.OnTacticalReady() and from
// OnUpdate() on hot-reload -- no Harmony patches needed.
//
// Config: HUDCustomizer.json → "TileHighlights" object.
// Each slot has { "Enabled": bool, "R": 0-255, "G": 0-255, "B": 0-255, "A": 0.0-1.0 }.
// Enabled = false leaves that slot at the game default.
//
// Confirmed slot names (from ConfigurableTileHighlightColours source):
//   FowOutlineColor, FowOutlineInnerGlowColor, FowUnwalkableColor
//   ObjectiveColor, ObjectiveGlowColor
//   SkillRangeColor, SkillRangeGlowColor
//   AoEFillColor, AoELineColor, SpecialAoETileColor
//   DelayedAoEFillColor, DelayedAoEOutlineColor, DelayedAoEInnerLineColor
//   EnemyViewColor, EnemyViewGlowColor
//   EnemySkillsColor, EnemySkillsGlowColor, EnemySkillsTintColor
//   MovementColor, MovementGlowColor, MovementTintColor
//   UnwalkableColor, UnplayableOutlineColor
// =============================================================================
public static class TileCustomizer
{
    // =========================================================================
    // Apply
    // =========================================================================
    public static void TryApply()
    {
        if (!Il2CppTileHighlighter.Exists())
        {
            HUDCustomizerPlugin.Debug("[Tile] TileHighlighter does not exist -- skipping.");
            return;
        }

        var highlighter = Il2CppTileHighlighter.Instance();
        if (highlighter == null)
        {
            HUDCustomizerPlugin.Log.Warning("[Tile] TryApply: Instance() returned null.");
            return;
        }

        var ov = highlighter.GetColorOverrides() ?? new Il2CppColorOverrides();
        var t  = HUDCustomizerPlugin.Config.TileHighlights;

        ov.FowOutlineColor          = Resolve(ov.FowOutlineColor,          t.FowOutlineColor,          "FowOutlineColor");
        ov.FowOutlineInnerGlowColor = Resolve(ov.FowOutlineInnerGlowColor, t.FowOutlineInnerGlowColor, "FowOutlineInnerGlowColor");
        ov.FowUnwalkableColor       = Resolve(ov.FowUnwalkableColor,       t.FowUnwalkableColor,       "FowUnwalkableColor");

        ov.ObjectiveColor           = Resolve(ov.ObjectiveColor,           t.ObjectiveColor,           "ObjectiveColor");
        ov.ObjectiveGlowColor       = Resolve(ov.ObjectiveGlowColor,       t.ObjectiveGlowColor,       "ObjectiveGlowColor");

        ov.SkillRangeColor          = Resolve(ov.SkillRangeColor,          t.SkillRangeColor,          "SkillRangeColor");
        ov.SkillRangeGlowColor      = Resolve(ov.SkillRangeGlowColor,      t.SkillRangeGlowColor,      "SkillRangeGlowColor");

        ov.AoEFillColor             = Resolve(ov.AoEFillColor,             t.AoEFillColor,             "AoEFillColor");
        ov.AoELineColor             = Resolve(ov.AoELineColor,             t.AoELineColor,             "AoELineColor");
        ov.SpecialAoETileColor      = Resolve(ov.SpecialAoETileColor,      t.SpecialAoETileColor,      "SpecialAoETileColor");

        ov.DelayedAoEFillColor      = Resolve(ov.DelayedAoEFillColor,      t.DelayedAoEFillColor,      "DelayedAoEFillColor");
        ov.DelayedAoEOutlineColor   = Resolve(ov.DelayedAoEOutlineColor,   t.DelayedAoEOutlineColor,   "DelayedAoEOutlineColor");
        ov.DelayedAoEInnerLineColor = Resolve(ov.DelayedAoEInnerLineColor, t.DelayedAoEInnerLineColor, "DelayedAoEInnerLineColor");

        ov.EnemyViewColor           = Resolve(ov.EnemyViewColor,           t.EnemyViewColor,           "EnemyViewColor");
        ov.EnemyViewGlowColor       = Resolve(ov.EnemyViewGlowColor,       t.EnemyViewGlowColor,       "EnemyViewGlowColor");

        ov.EnemySkillsColor         = Resolve(ov.EnemySkillsColor,         t.EnemySkillsColor,         "EnemySkillsColor");
        ov.EnemySkillsGlowColor     = Resolve(ov.EnemySkillsGlowColor,     t.EnemySkillsGlowColor,     "EnemySkillsGlowColor");
        ov.EnemySkillsTintColor     = Resolve(ov.EnemySkillsTintColor,     t.EnemySkillsTintColor,     "EnemySkillsTintColor");

        ov.MovementColor            = Resolve(ov.MovementColor,            t.MovementColor,            "MovementColor");
        ov.MovementGlowColor        = Resolve(ov.MovementGlowColor,        t.MovementGlowColor,        "MovementGlowColor");
        ov.MovementTintColor        = Resolve(ov.MovementTintColor,        t.MovementTintColor,        "MovementTintColor");

        ov.UnwalkableColor          = Resolve(ov.UnwalkableColor,          t.UnwalkableColor,          "UnwalkableColor");
        ov.UnplayableOutlineColor   = Resolve(ov.UnplayableOutlineColor,   t.UnplayableOutlineColor,   "UnplayableOutlineColor");

        highlighter.SetColorOverrides(ov);
        HUDCustomizerPlugin.Debug("[Tile] Colours applied.");
    }

    // Resolves a single slot: if the entry is disabled, clears the override flag
    // so the game reverts to its own default for that slot.
    private static Il2CppColorOverride Resolve(Il2CppColorOverride existing,
                                               TileHighlightEntry  entry,
                                               string              label)
    {
        if (!entry.Enabled)
        {
            // Clear the override so the game uses its own default colour.
            // Returning existing unchanged would leave Enabled=true from a prior
            // call, preventing reversion after a hot-reload that disables a slot.
            // existing.Color is preserved rather than zeroed because the game does
            // not read the Color field when Enabled = false -- TileHighlighter only
            // applies a slot's colour when its Enabled flag is set.  If that
            // assumption ever proves incorrect, replace existing.Color with
            // default(Color) here.
            HUDCustomizerPlugin.Debug($"  [Tile] CLEAR {label}");
            return new Il2CppColorOverride { Enabled = false, Color = existing.Color };
        }

        var color = new Color(entry.R / 255f, entry.G / 255f, entry.B / 255f, entry.A);
        HUDCustomizerPlugin.Debug(
            $"  [Tile] SET {label} -> RGB({(int)entry.R},{(int)entry.G},{(int)entry.B}) A({entry.A:F2})");
        return new Il2CppColorOverride { Enabled = true, Color = color };
    }

    // =========================================================================
    // Logging
    // =========================================================================
    public static void LogSummary()
    {
        var t      = HUDCustomizerPlugin.Config.TileHighlights;
        var active = new List<string>();

        void Check(string label, TileHighlightEntry e)
        {
            if (e.Enabled) active.Add($"{label}=RGB({(int)e.R},{(int)e.G},{(int)e.B})");
        }

        Check("FowOutline",          t.FowOutlineColor);
        Check("FowGlow",             t.FowOutlineInnerGlowColor);
        Check("FowUnwalkable",       t.FowUnwalkableColor);
        Check("Objective",           t.ObjectiveColor);
        Check("ObjectiveGlow",       t.ObjectiveGlowColor);
        Check("SkillRange",          t.SkillRangeColor);
        Check("SkillRangeGlow",      t.SkillRangeGlowColor);
        Check("AoEFill",             t.AoEFillColor);
        Check("AoELine",             t.AoELineColor);
        Check("SpecialAoE",          t.SpecialAoETileColor);
        Check("DelayedAoEFill",      t.DelayedAoEFillColor);
        Check("DelayedAoEOutline",   t.DelayedAoEOutlineColor);
        Check("DelayedAoEInnerLine", t.DelayedAoEInnerLineColor);
        Check("EnemyView",           t.EnemyViewColor);
        Check("EnemyViewGlow",       t.EnemyViewGlowColor);
        Check("EnemySkills",         t.EnemySkillsColor);
        Check("EnemySkillsGlow",     t.EnemySkillsGlowColor);
        Check("EnemySkillsTint",     t.EnemySkillsTintColor);
        Check("Movement",            t.MovementColor);
        Check("MovementGlow",        t.MovementGlowColor);
        Check("MovementTint",        t.MovementTintColor);
        Check("Unwalkable",          t.UnwalkableColor);
        Check("UnplayableOutline",   t.UnplayableOutlineColor);

        if (active.Count == 0)
            HUDCustomizerPlugin.Log.Msg(
                "  [Tile] Colour overrides: none (all disabled -- game defaults preserved)");
        else
            HUDCustomizerPlugin.Log.Msg(
                $"  [Tile] Colour overrides active ({active.Count}): {string.Join("  ", active)}");
    }
}
