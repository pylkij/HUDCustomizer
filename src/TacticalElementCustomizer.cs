using System;
using UnityEngine;
using UnityEngine.UIElements;

using Il2CppInterfaceElement = Il2CppMenace.UI.InterfaceElement;

// =============================================================================
// TacticalElementCustomizer
// Applies non-font tactical UI tint/style overrides introduced for Tier 2-4.
// =============================================================================
public static class TacticalElementCustomizer
{
    public static void Apply(Il2CppInterfaceElement element, string hudType)
    {
        if (element == null) return;

        switch (hudType)
        {
            case "SkillBarButton":
                ApplySkillBarButton(element);
                break;
            case "BaseSkillBarItemSlot":
                ApplyBaseSkillBarItemSlot(element);
                break;
            case "SimpleSkillBarButton":
                ApplySimpleSkillBarButton(element);
                break;
            case "TurnOrderFactionSlot":
                ApplyTurnOrderFactionSlot(element);
                break;
            case "UnitsTurnBarSlot":
                ApplyUnitsTurnBarSlot(element);
                break;
            case "SelectedUnitPanel":
                ApplySelectedUnitPanel(element);
                break;
            case "TacticalUnitInfoStat":
                ApplyTacticalUnitInfoStat(element);
                break;
            case "DelayedAbilityHUD":
                ApplyDelayedAbilityHUD(element);
                break;
            case "ObjectivesTracker":
                ApplyObjectivesTrackerProgressBar(element);
                break;
        }
    }

    private static void ApplySkillBarButton(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.SkillBarButton;
        SetTint(root.Q("SkillIcon", (string)null), cfg.SkillIconTint, "SkillBarButton.SkillIcon");
        SetTint(root.Q("SelectedOverlay", (string)null), cfg.SelectedOverlayTint, "SkillBarButton.SelectedOverlay");
        SetTint(root.Q("HoverOverlay", (string)null), cfg.HoverOverlayTint, "SkillBarButton.HoverOverlay");

        if (cfg.PreviewOpacity >= 0f)
        {
            var target = root.Q("Pickable", (string)null) ?? root;
            target.style.opacity = new StyleFloat(Mathf.Clamp(cfg.PreviewOpacity, 0f, 1f));
        }
    }

    private static void ApplyBaseSkillBarItemSlot(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.BaseSkillBarItemSlot;
        SetTint(root.Q("Background", (string)null), cfg.BackgroundTint, "BaseSkillBarItemSlot.Background");
        SetTint(root.Q("ItemIcon", (string)null), cfg.ItemIconTint, "BaseSkillBarItemSlot.ItemIcon");
        SetTint(root.Q("Cross", (string)null), cfg.CrossTint, "BaseSkillBarItemSlot.Cross");
    }

    private static void ApplySimpleSkillBarButton(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.SimpleSkillBarButton;
        SetTint(root.Q("Hover", (string)null), cfg.HoverTint, "SimpleSkillBarButton.Hover");
    }

    private static void ApplyTurnOrderFactionSlot(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.TurnOrderFactionSlot;
        SetTint(root.Q("InactiveMask", (string)null), cfg.InactiveMaskTint, "TurnOrderFactionSlot.InactiveMask");
        SetTint(root.Q("Selected", (string)null), cfg.SelectedTint, "TurnOrderFactionSlot.Selected");
        SetTint(root.Q("InactiveIcon", (string)null), cfg.InactiveIconTint, "TurnOrderFactionSlot.InactiveIcon");
    }

    private static void ApplyUnitsTurnBarSlot(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.UnitsTurnBarSlot;
        SetTint(root.Q("Overlay", (string)null), cfg.OverlayTint, "UnitsTurnBarSlot.Overlay");
        SetTint(root.Q("Selected", (string)null), cfg.SelectedTint, "UnitsTurnBarSlot.Selected");
        SetTint(root.Q("Portrait", (string)null), cfg.PortraitTint, "UnitsTurnBarSlot.Portrait");
    }

    private static void ApplySelectedUnitPanel(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.SelectedUnitPanel;
        SetTint(root.Q("Portrait", (string)null), cfg.PortraitTint, "SelectedUnitPanel.Portrait");
        SetTint(root.Q("UnitWindowHeader", (string)null), cfg.HeaderTint, "SelectedUnitPanel.UnitWindowHeader");
    }

    private static void ApplyTacticalUnitInfoStat(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.TacticalUnitInfoStat;
        SetTint(root.Q("Icon", (string)null), cfg.IconTint, "TacticalUnitInfoStat.Icon");
    }

    private static void ApplyDelayedAbilityHUD(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.TacticalUIStyles.DelayedAbilityHUD;
        SetTint(root.Q("Progress", (string)null), cfg.ProgressTint, "DelayedAbilityHUD.Progress");
    }

    private static void ApplyObjectivesTrackerProgressBar(Il2CppInterfaceElement root)
    {
        var cfg = HUDCustomizerPlugin.Config.ObjectivesTrackerProgressBar;
        if (string.IsNullOrEmpty(cfg.FillColor) &&
            string.IsNullOrEmpty(cfg.PreviewColor) &&
            string.IsNullOrEmpty(cfg.TrackColor))
            return;

        var bar = root.Q("ProgressBar", (string)null);
        var track = bar?.Q("Pickable", (string)null);
        var fill = track?.Q("Fill", (string)null);
        var preview = track?.Q("PreviewFill", (string)null);

        SetBackground(track, cfg.TrackColor, "ObjectivesTracker.ProgressBar.Pickable");
        SetBackground(fill, cfg.FillColor, "ObjectivesTracker.ProgressBar.Fill");
        SetBackground(preview, cfg.PreviewColor, "ObjectivesTracker.ProgressBar.PreviewFill");
    }

    private static void SetBackground(VisualElement ve, string color, string label)
    {
        if (ve == null || string.IsNullOrWhiteSpace(color)) return;
        if (!HUDCustomizerPlugin.TryParseColor(color, out var c))
        {
            HUDCustomizerPlugin.Log.Warning($"[TacticalElementCustomizer] Invalid color '{color}' for {label}");
            return;
        }
        ve.style.backgroundColor = new StyleColor(c);
    }

    private static void SetTint(VisualElement ve, TileHighlightEntry entry, string label)
    {
        if (ve == null || entry == null || !entry.Enabled) return;
        ve.style.unityBackgroundImageTintColor = new StyleColor(HUDCustomizerPlugin.ToColor(entry, label));
    }

    private static bool Enabled(TileHighlightEntry e) => e != null && e.Enabled;

    public static void LogSummary()
    {
        var c = HUDCustomizerPlugin.Config;
        var s = c.TacticalUIStyles;
        int enabledCount = 0;
        if (Enabled(s.SkillBarButton.SkillIconTint)) enabledCount++;
        if (Enabled(s.SkillBarButton.SelectedOverlayTint)) enabledCount++;
        if (Enabled(s.SkillBarButton.HoverOverlayTint)) enabledCount++;
        if (s.SkillBarButton.PreviewOpacity >= 0f) enabledCount++;
        if (Enabled(s.BaseSkillBarItemSlot.BackgroundTint)) enabledCount++;
        if (Enabled(s.BaseSkillBarItemSlot.ItemIconTint)) enabledCount++;
        if (Enabled(s.BaseSkillBarItemSlot.CrossTint)) enabledCount++;
        if (Enabled(s.SimpleSkillBarButton.HoverTint)) enabledCount++;
        if (Enabled(s.TurnOrderFactionSlot.InactiveMaskTint)) enabledCount++;
        if (Enabled(s.TurnOrderFactionSlot.SelectedTint)) enabledCount++;
        if (Enabled(s.TurnOrderFactionSlot.InactiveIconTint)) enabledCount++;
        if (Enabled(s.UnitsTurnBarSlot.OverlayTint)) enabledCount++;
        if (Enabled(s.UnitsTurnBarSlot.SelectedTint)) enabledCount++;
        if (Enabled(s.UnitsTurnBarSlot.PortraitTint)) enabledCount++;
        if (Enabled(s.SelectedUnitPanel.PortraitTint)) enabledCount++;
        if (Enabled(s.SelectedUnitPanel.HeaderTint)) enabledCount++;
        if (Enabled(s.TacticalUnitInfoStat.IconTint)) enabledCount++;
        if (Enabled(s.DelayedAbilityHUD.ProgressTint)) enabledCount++;
        if (!string.IsNullOrWhiteSpace(c.ObjectivesTrackerProgressBar.FillColor)) enabledCount++;
        if (!string.IsNullOrWhiteSpace(c.ObjectivesTrackerProgressBar.PreviewColor)) enabledCount++;
        if (!string.IsNullOrWhiteSpace(c.ObjectivesTrackerProgressBar.TrackColor)) enabledCount++;

        HUDCustomizerPlugin.Log.Msg($"  [TacticalUI] Overrides active: {enabledCount}");
    }
}
