// =============================================================================
// CombatFlyoverCustomizer
// Bridge between HUDCustomizer's config system and the CombatFlyoverText plugin.
//
// Responsibilities:
//   - Receives a fresh CombatFlyoverSettings instance from LoadConfig() and from
//     the hot-reload path in OnUpdate() via Apply().
//   - Exposes the current settings to CombatFlyoverPlugin via public accessors.
//   - Logs a config summary line via LogSummary(), called from the LoadConfig()
//     summary block alongside all other customiser summaries.
//
// Dependency direction:
//   CombatFlyoverPlugin (CombatFlyoverText assembly)
//       → references HUDCustomizer assembly
//       → calls CombatFlyoverCustomizer accessors at runtime.
//   HUDCustomizer assembly has NO reference to CombatFlyoverText.
//   CombatFlyoverPlugin.csproj must add a project reference to HUDCustomizer.dll.
//
// Load-order safety:
//   All accessors return hardcoded fallback defaults when _cfg is null, so
//   CombatFlyoverPlugin behaves identically to its pre-integration defaults
//   if it initialises before HUDCustomizer has called Apply() for the first time.
// =============================================================================

public static class CombatFlyoverCustomizer
{
    private static CombatFlyoverSettings _cfg;

    // =========================================================================
    // Called by HUDCustomizerPlugin.LoadConfig() immediately after deserialising
    // the config, and by the hot-reload block in OnUpdate() after every reload.
    // =========================================================================
    public static void Apply(CombatFlyoverSettings cfg)
    {
        _cfg = cfg;
    }

    // =========================================================================
    // Accessors -- called by CombatFlyoverPlugin at runtime.
    // Fallback defaults match the original hardcoded values in CombatFlyoverPlugin
    // so behaviour is unchanged if Apply() has not yet been called.
    // =========================================================================

    /// <summary>
    /// Master toggle. Returns false when the user sets Enabled = false in config.
    /// CombatFlyoverPlugin guards all display logic and duration-extension patches
    /// behind this check so the feature can be toggled without a game restart.
    /// </summary>
    public static bool IsEnabled() => _cfg?.Enabled ?? true;

    /// <summary>Unity Rich Text hex colour for HP damage flyovers (e.g. "-24 HP").</summary>
    public static string ColourHP()  => _cfg?.ColourHPDamage     ?? "#FF4444";

    /// <summary>Unity Rich Text hex colour for armour damage flyovers (e.g. "-33 ARM").</summary>
    public static string ColourARM() => _cfg?.ColourArmourDamage  ?? "#4488FF";

    /// <summary>Unity Rich Text hex colour for accuracy flyovers (e.g. "~65%" and "75%").</summary>
    public static string ColourAcc() => _cfg?.ColourAccuracy      ?? "#44CC44";

    /// <summary>
    /// Additional seconds added to the game's default 1.5s DropdownText display window.
    /// Consumed by UnitHudOnUpdatePatch to extend StartTime on newly-activated elements.
    /// </summary>
    public static float ExtraDisplaySeconds() => _cfg?.ExtraDisplaySeconds ?? 2.0f;

    /// <summary>
    /// Multiplier applied to the game's default 1500ms DropdownText fade animation.
    /// Consumed by ValueAnimationDurationPatch to scale set_durationMs calls.
    /// </summary>
    public static float FadeDurationScale() => _cfg?.FadeDurationScale ?? 2.0f;

    // =========================================================================
    // Called from the LoadConfig() summary block in HUDCustomizer.cs.
    // Appended after LogSpentOpacitySummary() -- see the summary call sequence
    // in HUDCustomizer-AI.md.
    // =========================================================================
    public static void LogSummary()
    {
        if (_cfg == null)
        {
            HUDCustomizerPlugin.Log.Msg(
                "  [CombatFlyover] Config not yet applied (CombatFlyoverText may not be loaded).");
            return;
        }

        if (!_cfg.Enabled)
        {
            HUDCustomizerPlugin.Log.Msg("  [CombatFlyover] Disabled.");
            return;
        }

        HUDCustomizerPlugin.Log.Msg(
            $"  [CombatFlyover] Enabled  " +
            $"HP={_cfg.ColourHPDamage}  " +
            $"ARM={_cfg.ColourArmourDamage}  " +
            $"Acc={_cfg.ColourAccuracy}  " +
            $"ExtraSec={_cfg.ExtraDisplaySeconds:F1}  " +
            $"FadeScale={_cfg.FadeDurationScale:F1}");
    }
}
