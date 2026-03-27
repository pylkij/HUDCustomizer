using System.IO;
using System.Text.Json;

// =============================================================================
// HUDConfig
// Config data classes and file I/O helpers for HUDCustomizer.
//
// Classes defined here:
//   HUDCustomizerConfig          -- top-level config object (deserialized from JSON)
//   FontSettings                 -- font/size/color override for a single element group
//   TileHighlightEntry           -- single enabled/RGBA slot (shared by Tile, USS, Faction)
//   TacticalUIStylesConfig       -- scan-driven tactical UI tint/opacity overrides
//   TileHighlightsConfig         -- all tile highlight colour slots
//   USSColorsConfig              -- all USS global theme colour slots
//   FactionHealthBarColorsConfig -- faction-specific health bar colour slots
//   CombatFlyoverSettings        -- settings for the CombatFlyoverText plugin integration
//   HUDConfig (static)           -- ConfigDir, ConfigPath, JsonOpts, BuildDefaultConfig()
// =============================================================================

public class HUDCustomizerConfig
{
    // Schema version -- incremented when a breaking/targeted migration is needed.
    // Currently used only for logging; the merge-fill strategy handles additive
    // changes without requiring explicit version checks.
    public int    ConfigVersion  { get; set; } = 1;

    public bool   DebugLogging  { get; set; } = false;
    public bool   EnableScans   { get; set; } = false;
    public string ReloadKey     { get; set; } = "F8";

    // Unit HUD scale ----------------------------------------------------------
    // Multiplier applied to unit HUDs (badge, health bar, icons).
    public float UnitHUDScale     { get; set; } = 1.0f;
    // Multiplier applied to non-unit entity HUDs (vehicles, emplacements, etc.).
    public float EntityHUDScale   { get; set; } = 1.0f;
    // Multiplier applied to StructureHUD instances (inherits EntityHUD).
    // Kept separate from EntityHUDScale for independent structure tuning.
    public float StructureHUDScale { get; set; } = 1.0f;
    // Transform origin: which point stays fixed when scaling.
    // Values are percentages of element width/height.
    // X: 50 = horizontal centre.
    // Y: 100 = bottom edge (HUD grows upward), 50 = centre, 0 = top edge.
    public float TransformOriginX { get; set; } = 50f;
    public float TransformOriginY { get; set; } = 100f;

    // Spent unit HUD opacity -----------------------------------------------
    // Controls the opacity of a UnitHUD element after that unit has used its
    // turn (i.e. is "spent").  The game's default is 0.5 (50% opacity).
    // Range: 0.0 (invisible) to 1.0 (fully opaque).
    // Set to -1 to leave unchanged (game default of 0.5 is preserved).
    public float SpentUnitHUDOpacity { get; set; } = -1f;

    // Unit / entity HUD colours -----------------------------------------------
    // Format: "R, G, B" or "R, G, B, A"  (R/G/B = 0-255, A = 0.0-1.0)
    // Set any value to "" to leave that element unchanged (game default).
    //
    // HitpointsFillColor and HitpointsPreviewColor are GLOBAL settings sourced
    // from UIConfig via FactionHealthBarColors below.  The per-element values
    // here apply inline styles after bar init and take precedence on individually
    // patched HUDs (UnitHUD, EntityHUD).
    //
    // Game defaults observed in scan:
    //   Hitpoints fill:          145, 156, 100   (olive-green) [global: see FactionHealthBarColors]
    //   Hitpoints preview:       186, 226, 105   (light green) [global: see FactionHealthBarColors]
    //   Hitpoints track bg:      0, 0, 0, 0.33   (dark semi-transparent)
    //   Armor fill:              116, 116, 116   (mid-grey)
    //   Armor preview:           89, 89, 89      (dark grey)
    //   Armor track bg:          0, 0, 0, 0.33   (dark semi-transparent)
    //   Suppression fill:        205, 183, 107   (tan)
    //   Suppression preview:     243, 217, 127   (light tan)
    //   Suppression track bg:    51, 51, 51, 0.70 (dark semi-transparent)
    //   Badge tint:              255, 255, 255   (white = no tint)
    public string HitpointsFillColor      { get; set; } = "";
    public string HitpointsPreviewColor   { get; set; } = "";
    public string HitpointsTrackColor     { get; set; } = "";
    public string ArmorFillColor          { get; set; } = "";
    public string ArmorPreviewColor       { get; set; } = "";
    public string ArmorTrackColor         { get; set; } = "";
    public string SuppressionFillColor    { get; set; } = "";
    public string SuppressionPreviewColor { get; set; } = "";
    public string SuppressionTrackColor   { get; set; } = "";
    public string BadgeTintColor          { get; set; } = "";

    // Faction-specific health bar colours (global -- via UIConfig singleton).
    // These affect the health bar colours shown in the unit infobox when a unit
    // is selected by the player.  They do NOT affect the floating Entity HUD bars
    // shown above units in the tactical scene -- use HitpointsFillColor /
    // HitpointsPreviewColor above for those.
    public FactionHealthBarColorsConfig FactionHealthBarColors { get; set; } = new();
    public RarityColorsConfig RarityColors { get; set; } = new();

    // Font settings -----------------------------------------------------------
    // Each FontSettings entry has a Font name and a Size (0 = unchanged).
    // Global is applied to all text elements first; per-element entries override it.
    // Available fonts confirmed in scan:
    //   Jura-Regular, Jura-Bold, OCRAStd, Inconsolata-SemiBold, NotInter-Regular
    //   NotoSansJP-Regular, NotoSansKR-Regular, NotoSansSC-Regular, NotoSansTC-Regular
    // Set Font to "" and Size to 0 to leave unchanged (game default via USS).
    public FontSettings Global { get; set; } = new();

    // UnitHUD / EntityHUD text elements (confirmed in scan, fontSize defaults in parens)
    public FontSettings UnitBarLabel      { get; set; } = new(); // bar Label / DarkLabel (14)

    // ObjectivesTracker text elements (confirmed in scan)
    public FontSettings ObjTrackerHeadline     { get; set; } = new(); // ObjectivesTrackerHeadline (12)
    public FontSettings ObjTrackerPoints       { get; set; } = new(); // Points (16)
    public FontSettings ObjTrackerDescription  { get; set; } = new(); // Description (10)
    public FontSettings ObjTrackerLabel        { get; set; } = new(); // Label / DarkLabel (14)
    public FontSettings ObjSecondaryHeadline   { get; set; } = new(); // SecondaryObjectivesHeadline (12)
    public FontSettings ObjRewardPoints        { get; set; } = new(); // RewardPoints (16)

    // MissionInfoPanel text elements (confirmed in scan)
    public FontSettings MissionName            { get; set; } = new(); // MissionName (12)
    public FontSettings MissionDuration        { get; set; } = new(); // MissionDuration (12)

    // ObjectiveHUD text elements (confirmed by scan)
    public FontSettings ObjectiveNameLabel     { get; set; } = new(); // ObjectiveName
    public FontSettings ObjectiveStateLabel    { get; set; } = new(); // ObjectiveState

    // MovementHUD text elements (confirmed by scan)
    //   CostLabel    -- movement AP cost (fontSize=16)
    //   ActionLabel  -- action type label (fontSize=14)
    public FontSettings MovementCostLabel   { get; set; } = new();
    public FontSettings MovementActionLabel { get; set; } = new();

    // BleedingWorldSpaceIcon text elements (element name confirmed by scan: "TextElement")
    public FontSettings BleedingIconText    { get; set; } = new(); // m_TextElement

    // DropdownText -- flyover text shown above units on skill/status application
    // (e.g. "AP increased!", suppression, taking command). Element name confirmed
    // by scan: Label (fontSize=14, USS class font-headline).
    public FontSettings DropdownText        { get; set; } = new(); // Label (fontSize=14)

    // SkillBarButton text elements
    public FontSettings SkillBarActionPointsLabel { get; set; } = new(); // ActionPointsLabel
    public FontSettings SkillBarUsesLabel         { get; set; } = new(); // UsesLabel
    public FontSettings SkillBarHotkeyLabel       { get; set; } = new(); // HotkeyLabel

    // SimpleSkillBarButton text elements
    public FontSettings SimpleSkillBarLabel       { get; set; } = new(); // Label
    public FontSettings SimpleSkillBarHotkeyLabel { get; set; } = new(); // HotkeyLabel

    // SkillBarSlotWeapon text elements
    public FontSettings SkillBarSlotWeaponNameLabel { get; set; } = new(); // NameLabel

    // SelectedUnitPanel text elements
    public FontSettings SelectedUnitConditionLabel    { get; set; } = new(); // ConditionLabel
    public FontSettings SelectedUnitActionPointsLabel { get; set; } = new(); // ActionPointsLabel

    // TacticalUnitInfoStat text elements
    public FontSettings TacticalUnitInfoValueLabel { get; set; } = new(); // Value

    // Turn order / status text elements
    public FontSettings TurnOrderPanelRoundNumberLabel { get; set; } = new(); // RoundNumber
    public FontSettings StatusEffectIconStackCountLabel { get; set; } = new(); // StackCount

    // ObjectivesTracker progress bar colours (same Fill / PreviewFill / Pickable pattern
    // as UnitHUD/EntityHUD bars; string format matches existing bar colour settings).
    public ObjectivesTrackerProgressBarColorsConfig ObjectivesTrackerProgressBar { get; set; } = new();

    // Tactical UI element tint/style overrides for systems under active implementation.
    public TacticalUIStylesConfig TacticalUIStyles { get; set; } = new();

    // Tile highlight colours
    public TileHighlightsConfig TileHighlights { get; set; } = new();

    // USS global theme colour overrides
    public USSColorsConfig USSColors { get; set; } = new();

    // Visualizer colour and parameter overrides
    public VisualizersConfig Visualizers { get; set; } = new();

    // CombatFlyoverText plugin integration
    // Controls flyover display behaviour when the CombatFlyoverText mod is loaded.
    public CombatFlyoverSettings CombatFlyover { get; set; } = new();
}

// Holds font overrides for a single text element group.
// Font:  asset name string, "" = unchanged.
// Size:  float, 0 = unchanged.
// Color: "R,G,B" or "R,G,B,A" (R/G/B = 0-255, A = 0.0-1.0), "" = unchanged.
public class FontSettings
{
    public string Font  { get; set; } = "";
    public float  Size  { get; set; } = 0f;
    public string Color { get; set; } = "";
}

// Controls a single tile highlight colour slot.
// Enabled: false = leave this slot unchanged (game default).
// R/G/B:   0-255 integers.
// A:       0.0-1.0 float.
public class TileHighlightEntry
{
    public bool  Enabled { get; set; } = false;
    public float R       { get; set; } = 255f;
    public float G       { get; set; } = 255f;
    public float B       { get; set; } = 255f;
    public float A       { get; set; } = 1f;
}

// ObjectivesTracker progress bar inline colour overrides.
// Uses the same string format as existing UnitHUD/EntityHUD bar colours:
// "R, G, B" or "R, G, B, A". Empty string leaves unchanged.
public class ObjectivesTrackerProgressBarColorsConfig
{
    public string FillColor    { get; set; } = "";
    public string PreviewColor { get; set; } = "";
    public string TrackColor   { get; set; } = "";
}

public class SkillBarButtonStyleConfig
{
    public TileHighlightEntry SkillIconTint       { get; set; } = new();
    public TileHighlightEntry SelectedOverlayTint { get; set; } = new();
    public TileHighlightEntry HoverOverlayTint    { get; set; } = new();
    // -1 = leave game animation-driven preview opacity unchanged.
    public float PreviewOpacity { get; set; } = -1f;
}

public class BaseSkillBarItemSlotStyleConfig
{
    public TileHighlightEntry BackgroundTint { get; set; } = new();
    public TileHighlightEntry ItemIconTint   { get; set; } = new();
    public TileHighlightEntry CrossTint      { get; set; } = new();
}

public class SimpleSkillBarButtonStyleConfig
{
    public TileHighlightEntry HoverTint { get; set; } = new();
}

public class TurnOrderFactionSlotStyleConfig
{
    public TileHighlightEntry InactiveMaskTint { get; set; } = new();
    public TileHighlightEntry SelectedTint     { get; set; } = new();
    public TileHighlightEntry InactiveIconTint { get; set; } = new();
}

public class UnitsTurnBarSlotStyleConfig
{
    public TileHighlightEntry OverlayTint  { get; set; } = new();
    public TileHighlightEntry SelectedTint { get; set; } = new();
    public TileHighlightEntry PortraitTint { get; set; } = new();
}

public class SelectedUnitPanelStyleConfig
{
    public TileHighlightEntry PortraitTint { get; set; } = new();
    public TileHighlightEntry HeaderTint   { get; set; } = new();
}

public class TacticalUnitInfoStatStyleConfig
{
    public TileHighlightEntry IconTint { get; set; } = new();
}

public class DelayedAbilityHUDStyleConfig
{
    public TileHighlightEntry ProgressTint { get; set; } = new();
}

public class TacticalUIStylesConfig
{
    public SkillBarButtonStyleConfig       SkillBarButton       { get; set; } = new();
    public BaseSkillBarItemSlotStyleConfig BaseSkillBarItemSlot { get; set; } = new();
    public SimpleSkillBarButtonStyleConfig SimpleSkillBarButton { get; set; } = new();
    public TurnOrderFactionSlotStyleConfig TurnOrderFactionSlot { get; set; } = new();
    public UnitsTurnBarSlotStyleConfig     UnitsTurnBarSlot     { get; set; } = new();
    public SelectedUnitPanelStyleConfig    SelectedUnitPanel    { get; set; } = new();
    public TacticalUnitInfoStatStyleConfig TacticalUnitInfoStat { get; set; } = new();
    public DelayedAbilityHUDStyleConfig    DelayedAbilityHUD    { get; set; } = new();
}

// Tile highlight colour overrides.
// Each slot maps directly to a field on TileHighlightColorOverrides.
// Set Enabled = true and adjust R/G/B/A to override that slot.
public class TileHighlightsConfig
{
    // Fog of War
    public TileHighlightEntry FowOutlineColor          { get; set; } = new();
    public TileHighlightEntry FowOutlineInnerGlowColor { get; set; } = new();
    public TileHighlightEntry FowUnwalkableColor       { get; set; } = new();
    // Objective
    public TileHighlightEntry ObjectiveColor           { get; set; } = new();
    public TileHighlightEntry ObjectiveGlowColor       { get; set; } = new();
    // Skill Range
    public TileHighlightEntry SkillRangeColor          { get; set; } = new();
    public TileHighlightEntry SkillRangeGlowColor      { get; set; } = new();
    // AoE
    public TileHighlightEntry AoEFillColor             { get; set; } = new();
    public TileHighlightEntry AoELineColor             { get; set; } = new();
    public TileHighlightEntry SpecialAoETileColor      { get; set; } = new();
    // Delayed AoE
    public TileHighlightEntry DelayedAoEFillColor      { get; set; } = new();
    public TileHighlightEntry DelayedAoEOutlineColor   { get; set; } = new();
    public TileHighlightEntry DelayedAoEInnerLineColor { get; set; } = new();
    // Enemy View
    public TileHighlightEntry EnemyViewColor           { get; set; } = new();
    public TileHighlightEntry EnemyViewGlowColor       { get; set; } = new();
    // Enemy Skills
    public TileHighlightEntry EnemySkillsColor         { get; set; } = new();
    public TileHighlightEntry EnemySkillsGlowColor     { get; set; } = new();
    public TileHighlightEntry EnemySkillsTintColor     { get; set; } = new();
    // Movement
    public TileHighlightEntry MovementColor            { get; set; } = new();
    public TileHighlightEntry MovementGlowColor        { get; set; } = new();
    public TileHighlightEntry MovementTintColor        { get; set; } = new();
    // Unwalkable
    public TileHighlightEntry UnwalkableColor          { get; set; } = new();
    public TileHighlightEntry UnplayableOutlineColor   { get; set; } = new();
}

// USS theme colour overrides.
// Each slot maps directly to a public Color field on UIConfig (confirmed in UIConfig.cs).
// Set Enabled = true and adjust R/G/B/A to override that slot.
// Game defaults from UIConfig scan are documented in HUDCustomizer.json.
// Includes 23 general USS fields plus 5 mission state fields
// (ColorMissionPlayable/Locked/Played/PlayedArrow/Unplayable -- all carry [UssColor]).
public class USSColorsConfig
{
    // General text / UI
    public TileHighlightEntry ColorNormal              { get; set; } = new();
    public TileHighlightEntry ColorBright              { get; set; } = new();
    public TileHighlightEntry ColorNormalTransparent   { get; set; } = new();
    // Interactive elements
    public TileHighlightEntry ColorInteract            { get; set; } = new();
    public TileHighlightEntry ColorInteractDark        { get; set; } = new();
    public TileHighlightEntry ColorInteractHover       { get; set; } = new();
    public TileHighlightEntry ColorInteractSelected    { get; set; } = new();
    public TileHighlightEntry ColorInteractSelectedText { get; set; } = new();
    // Disabled states
    public TileHighlightEntry ColorDisabled            { get; set; } = new();
    public TileHighlightEntry ColorDisabledHover       { get; set; } = new();
    // Tooltip comparison
    public TileHighlightEntry ColorTooltipBetter       { get; set; } = new();
    public TileHighlightEntry ColorTooltipWorse        { get; set; } = new();
    public TileHighlightEntry ColorTooltipNormal       { get; set; } = new();
    // Status
    public TileHighlightEntry ColorPositive            { get; set; } = new();
    public TileHighlightEntry ColorNegative            { get; set; } = new();
    public TileHighlightEntry ColorWarning             { get; set; } = new();
    // Backgrounds / chrome
    public TileHighlightEntry ColorDarkBg              { get; set; } = new();
    public TileHighlightEntry ColorWindowCorner        { get; set; } = new();
    public TileHighlightEntry ColorTopBar              { get; set; } = new();
    public TileHighlightEntry ColorTopBarDark          { get; set; } = new();
    // Progress bars
    public TileHighlightEntry ColorProgressBarNormal   { get; set; } = new();
    public TileHighlightEntry ColorProgressBarBright   { get; set; } = new();
    // Misc
    public TileHighlightEntry ColorEmptySlotIcon       { get; set; } = new();
    // Mission state colours
    public TileHighlightEntry ColorMissionPlayable     { get; set; } = new() { R = 168f, G = 152f, B = 103f, A = 1f };
    public TileHighlightEntry ColorMissionLocked       { get; set; } = new() { R = 168f, G = 152f, B = 103f, A = 1f };
    public TileHighlightEntry ColorMissionPlayed       { get; set; } = new() { R = 113f, G = 102f, B = 69f,  A = 1f };
    public TileHighlightEntry ColorMissionPlayedArrow  { get; set; } = new() { R = 75f,  G = 67f,  B = 44f,  A = 0.5f };
    public TileHighlightEntry ColorMissionUnplayable   { get; set; } = new() { R = 115f, G = 115f, B = 115f, A = 1f };
}

// Faction-specific health bar colour overrides.
// Each slot maps directly to a public Color field on UIConfig (confirmed in UIConfig.cs).
// These control the health bar colours in the unit infobox when a unit is selected
// by the player.  They do NOT affect the floating Entity HUD bars above units -- those
// are controlled by the HitpointsFillColor / HitpointsPreviewColor string settings in
// HUDCustomizerConfig.
public class FactionHealthBarColorsConfig
{
    // Player units
    public TileHighlightEntry HealthBarFillColorPlayerUnits    { get; set; } = new();
    public TileHighlightEntry HealthBarPreviewColorPlayerUnits { get; set; } = new();
    // Allies
    public TileHighlightEntry HealthBarFillColorAllies         { get; set; } = new();
    public TileHighlightEntry HealthBarPreviewColorAllies      { get; set; } = new();
    // Enemies
    public TileHighlightEntry HealthBarFillColorEnemies        { get; set; } = new();
    public TileHighlightEntry HealthBarPreviewColorEnemies     { get; set; } = new();
    // Section colours (armor bar segmentation)
    public TileHighlightEntry HealthBarSectionColorPlayerUnits { get; set; } = new();
    public TileHighlightEntry HealthBarSectionColorEnemies     { get; set; } = new();
}

// Rarity colour overrides.
// Each slot maps directly to a public Color field on UIConfig.
// Set Enabled = true and adjust R/G/B/A to override that slot.
public class RarityColorsConfig
{
    public TileHighlightEntry Common        { get; set; } = new() { R = 116f, G = 108f, B = 75f,  A = 1f };
    public TileHighlightEntry CommonNamed   { get; set; } = new() { R = 216f, G = 232f, B = 203f, A = 1f };
    public TileHighlightEntry Uncommon      { get; set; } = new() { R = 61f,  G = 117f, B = 136f, A = 1f };
    public TileHighlightEntry UncommonNamed { get; set; } = new() { R = 185f, G = 208f, B = 214f, A = 1f };
    public TileHighlightEntry Rare          { get; set; } = new() { R = 189f, G = 49f,  B = 49f,  A = 1f };
    public TileHighlightEntry RareNamed     { get; set; } = new() { R = 252f, G = 241f, B = 240f, A = 1f };

    // Misc UIConfig colour
    public TileHighlightEntry ColorPositionMarkerDelayedAbility { get; set; } = new() { R = 0f, G = 255f, B = 255f, A = 1f };
}

// ---------------------------------------------------------------------------
// MovementVisualizerConfig
// Colour overrides for the 3D movement path drawn in the tactical scene.
// Exposed fields (from MovementVisualizer.cs source):
//   ReachableColor   -- polyline and dot colour for reachable path segments
//   UnreachableColor -- polyline colour for segments beyond the unit's AP range
// ---------------------------------------------------------------------------
public class MovementVisualizerConfig
{
    public TileHighlightEntry ReachableColor   { get; set; } = new();
    public TileHighlightEntry UnreachableColor { get; set; } = new();
}

// ---------------------------------------------------------------------------
// TargetAimVisualizerConfig
// Colour and parameter overrides for the 3D aim spline drawn when targeting.
// Exposed fields (from TargetAimVisualizer.cs source + material scan):
//
//   OutOfRangeColor      -- colour applied when the target is out of range.
//                           UpdateAim() does not read the native field for
//                           out-of-range rendering (root cause confirmed via
//                           Ghidra); the override is applied via MPB in the
//                           Patch_TargetAimVisualizer_UpdateAim postfix.
//
//   InRangeColor         -- sets shader property '_UnlitColor' on the aim
//                           mesh material (HDRP/Unlit).  Controls the base
//                           tint of the line texture.  Default: white (1,1,1).
//
//   InRangeEmissiveColor -- sets shader property '_EmissiveColor' (HDR).
//                           Controls the bloom/glow colour.  The R/G/B fields
//                           set the hue (0-255); EmissiveIntensity is a float
//                           multiplier for brightness (game default ~15).
//                           Set EmissiveIntensity = -1 to leave intensity
//                           unchanged when only overriding the hue.
//
//   AnimationScrollSpeed -- UV scroll speed of the animated texture (float)
//   Width                -- world-space width of the spline mesh (float)
//   MinimumHeight        -- minimum arc height above terrain (float)
//   MaximumHeight        -- maximum arc height above terrain (float)
//   DistanceToHeightScale -- scales arc height with distance to target (float)
//
// Float sentinel: -1 means "leave unchanged" (game default preserved).
// ---------------------------------------------------------------------------
public class TargetAimVisualizerConfig
{
    // Colours
    public TileHighlightEntry OutOfRangeColor      { get; set; } = new();
    public TileHighlightEntry InRangeColor         { get; set; } = new();
    public TileHighlightEntry InRangeEmissiveColor { get; set; } = new();

    // Emissive intensity multiplier for InRangeEmissiveColor.
    // -1 = leave unchanged. Game default is ~15 (produces visible bloom).
    // Set to 0 to disable bloom entirely. Values above 1 increase brightness.
    public float EmissiveIntensity { get; set; } = -1f;

    // Float parameters -- use -1 to leave unchanged (game default).
    public float AnimationScrollSpeed  { get; set; } = -1f;
    public float Width                 { get; set; } = -1f;
    public float MinimumHeight         { get; set; } = -1f;
    public float MaximumHeight         { get; set; } = -1f;
    public float DistanceToHeightScale { get; set; } = -1f;
}

// ---------------------------------------------------------------------------
// LineOfSightVisualizerConfig
// Colour overrides for the LOS lines drawn during the LineOfSightVisualizer.
// Each visible LOS line is composed of 3 Il2CppShapes.Line segments:
//   [0] fade-in  -- ColorStart=(R,G,B,0) ColorEnd=(R,G,B,A)
//   [1] solid    -- ColorStart=(R,G,B,A) ColorEnd=(R,G,B,A)
//   [2] fade-out -- ColorStart=(R,G,B,A) ColorEnd=(R,G,B,0)
// Colour is applied (and re-applied after every Resize) via LOSResizePatch.
// ---------------------------------------------------------------------------
public class LineOfSightVisualizerConfig
{
    public TileHighlightEntry LineColor { get; set; } = new();
}

// ---------------------------------------------------------------------------
// VisualizersConfig
// Top-level wrapper for all visualizer sub-configs.
// ---------------------------------------------------------------------------
public class VisualizersConfig
{
    public MovementVisualizerConfig      MovementVisualizer  { get; set; } = new();
    public TargetAimVisualizerConfig     TargetAimVisualizer { get; set; } = new();
    public LineOfSightVisualizerConfig   LineOfSight         { get; set; } = new();
}

// ---------------------------------------------------------------------------
// CombatFlyoverSettings
// Controls the CombatFlyoverText plugin when it is loaded alongside
// HUDCustomizer.  All values are read at load time and on every hot-reload.
//
// Enabled:             master toggle -- false disables all flyover display
//                      and duration extension patches.
//
// Colour fields:       Unity Rich Text hex format (#RRGGBB or #RRGGBBAA).
//                      Only the following formats are accepted by Unity:
//                        #RRGGBB / #RRGGBBAA (6- or 8-digit hex)
//                        named colours: aqua, black, blue, brown, cyan,
//                        darkblue, fuchsia, green, grey, lightblue, lime,
//                        magenta, maroon, navy, olive, orange, purple, red,
//                        silver, teal, white, yellow
//                      3-digit hex, rgb(), and most CSS names are NOT supported
//                      and will render white or be silently ignored.
//
// ExtraDisplaySeconds: seconds added to the game's default 1.5s display
//                      window per flyover element.
//
// FadeDurationScale:   multiplier applied to the 1500ms fade animation.
//                      At the defaults (ExtraDisplaySeconds=2, FadeDurationScale=2),
//                      total visible time = (1500ms * 2) + 2000ms = 5000ms.
// ---------------------------------------------------------------------------
public class CombatFlyoverSettings
{
    public bool   Enabled             { get; set; } = true;
    public string ColourHPDamage      { get; set; } = "#FF4444";
    public string ColourArmourDamage  { get; set; } = "#4488FF";
    public string ColourAccuracy      { get; set; } = "#44CC44";
    public float  ExtraDisplaySeconds { get; set; } = 2.0f;
    public float  FadeDurationScale   { get; set; } = 2.0f;
}

// =============================================================================
// HUDConfig
// Static helpers for config file I/O.
// BuildDefaultConfig() returns the annotated JSON written on first run.
// Called from HUDCustomizerPlugin.LoadConfig().
// =============================================================================
public static class HUDConfig
{
    // =========================================================================
    // Config file paths and serializer options
    // Consumed by HUDCustomizerPlugin.LoadConfig() and by the patches that
    // need to locate the config file.
    // =========================================================================
    public static readonly string ConfigDir  = Path.Combine("Mods", "HUDCustomizer");
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "HUDCustomizer.json");

    public static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    public static string BuildDefaultConfig()
    {
        return
@"{
  // ==========================================================================
  // HUDCustomizer config  (Mods/HUDCustomizer/HUDCustomizer.json)
  // Hot-reload: press ReloadKey in a tactical scene to apply changes instantly.
  // ==========================================================================

  // Schema version -- do not edit. Used by the mod to track config migrations.
  ""ConfigVersion"": 1,

  // --- Unit HUD scale -------------------------------------------------------
  // Multiplier applied to unit / entity HUDs. 1.0 = original size.
  ""UnitHUDScale"":     1.0,
  ""EntityHUDScale"":   1.0,
  ""StructureHUDScale"": 1.0,

  // Transform origin: which point of the element stays fixed during scaling.
  // X: 50 = horizontal centre.
  // Y: 100 = bottom edge (HUD grows upward), 50 = centre, 0 = top edge.
  ""TransformOriginX"": 50.0,
  ""TransformOriginY"": 100.0,

  // --- Spent unit HUD opacity -----------------------------------------------
  // Controls the opacity of a UnitHUD after its unit has used its turn.
  // The game's default is 0.5 (50% opacity -- units appear dimmed when spent).
  // Range: 0.0 (invisible) to 1.0 (fully opaque, no dimming at all).
  // Set to -1 to leave unchanged (game default of 0.5 is preserved).
  ""SpentUnitHUDOpacity"": -1.0,

  // --- Unit / entity HUD colours -------------------------------------------
  // Format: ""R, G, B"" or ""R, G, B, A""  (R/G/B = 0-255 integers, A = 0.0-1.0 float)
  // Set any value to """" to leave that element unchanged (game default).
  //
  // HitpointsFillColor and HitpointsPreviewColor are GLOBAL settings sourced
  // from UIConfig via FactionHealthBarColors below.  The per-element values
  // here apply inline styles after bar init and take precedence on individually
  // patched HUDs (UnitHUD, EntityHUD).
  //
  // Game defaults observed in scan:
  //   Hitpoints fill:         145, 156, 100         (olive-green) [global: see FactionHealthBarColors]
  //   Hitpoints preview:      186, 226, 105         (light green) [global: see FactionHealthBarColors]
  //   Hitpoints track bg:     0, 0, 0, 0.33         (dark semi-transparent)
  //   Armor fill:             116, 116, 116         (mid-grey)
  //   Armor preview:          89, 89, 89            (dark grey)
  //   Armor track bg:         0, 0, 0, 0.33         (dark semi-transparent)
  //   Suppression fill:       205, 183, 107         (tan)
  //   Suppression preview:    243, 217, 127         (light tan)
  //   Suppression track bg:   51, 51, 51, 0.70      (dark semi-transparent)
  //   Badge tint:             255, 255, 255         (white = no tint)
  ""HitpointsFillColor"":      """",
  ""HitpointsPreviewColor"":   """",
  ""HitpointsTrackColor"":     """",
  ""ArmorFillColor"":          """",
  ""ArmorPreviewColor"":       """",
  ""ArmorTrackColor"":         """",
  ""SuppressionFillColor"":    """",
  ""SuppressionPreviewColor"": """",
  ""SuppressionTrackColor"":   """",
  ""BadgeTintColor"":          """",

  // Faction health bar colours (global -- set via UIConfig singleton).
  // These control the health bar colours in the unit infobox shown when a unit
  // is selected by the player.  They do NOT affect the floating Entity HUD bars
  // above units -- use HitpointsFillColor / HitpointsPreviewColor above for those.
  // R/G/B: 0-255 integers. A: 0.0-1.0 float.
  ""FactionHealthBarColors"": {
    // Player units
    ""HealthBarFillColorPlayerUnits"":    { ""Enabled"": false, ""R"": 145, ""G"": 156, ""B"": 100, ""A"": 1.0 },  // default: RGB(145, 156, 100)
    ""HealthBarPreviewColorPlayerUnits"": { ""Enabled"": false, ""R"": 186, ""G"": 226, ""B"": 105, ""A"": 1.0 },  // default: RGB(186, 226, 105)
    // Allies
    ""HealthBarFillColorAllies"":         { ""Enabled"": false, ""R"": 138, ""G"": 151, ""B"": 161, ""A"": 1.0 },  // default: RGB(138, 151, 161)
    ""HealthBarPreviewColorAllies"":      { ""Enabled"": false, ""R"": 184, ""G"": 199, ""B"": 211, ""A"": 1.0 },  // default: RGB(184, 199, 211)
    // Enemies
    ""HealthBarFillColorEnemies"":        { ""Enabled"": false, ""R"": 204, ""G"": 104, ""B"": 106, ""A"": 1.0 },  // default: RGB(204, 104, 106)
    ""HealthBarPreviewColorEnemies"":     { ""Enabled"": false, ""R"": 240, ""G"":  75, ""B"":  75, ""A"": 1.0 },  // default: RGB(240, 75, 75)
    // Section colours (armor bar segmentation)
    ""HealthBarSectionColorPlayerUnits"": { ""Enabled"": false, ""R"":  95, ""G"": 114, ""B"":  35, ""A"": 1.0 },  // default: RGB(95, 114, 35)
    ""HealthBarSectionColorEnemies"":     { ""Enabled"": false, ""R"": 172, ""G"":  44, ""B"":  45, ""A"": 1.0 }   // default: RGB(172, 44, 45)
  },

  // --- Rarity colours -------------------------------------------------------
  // These map to public Color fields on UIConfig (confirmed in UIConfig.cs).
  // R/G/B: 0-255 integers. A: 0.0-1.0 float.
  ""RarityColors"": {
    ""Common"":        { ""Enabled"": false, ""R"": 116, ""G"": 108, ""B"": 75,  ""A"": 1.0 },  // default: RGB(116, 108, 75)
    ""CommonNamed"":   { ""Enabled"": false, ""R"": 216, ""G"": 232, ""B"": 203, ""A"": 1.0 },  // default: RGB(216, 232, 203)
    ""Uncommon"":      { ""Enabled"": false, ""R"": 61,  ""G"": 117, ""B"": 136, ""A"": 1.0 },  // default: RGB(61, 117, 136)
    ""UncommonNamed"": { ""Enabled"": false, ""R"": 185, ""G"": 208, ""B"": 214, ""A"": 1.0 },  // default: RGB(185, 208, 214)
    ""Rare"":          { ""Enabled"": false, ""R"": 189, ""G"": 49,  ""B"": 49,  ""A"": 1.0 },  // default: RGB(189, 49, 49)
    ""RareNamed"":     { ""Enabled"": false, ""R"": 252, ""G"": 241, ""B"": 240, ""A"": 1.0 },  // default: RGB(252, 241, 240)
    // Misc UIConfig colour
    ""ColorPositionMarkerDelayedAbility"": { ""Enabled"": false, ""R"": 0, ""G"": 255, ""B"": 255, ""A"": 1.0 }  // default: unknown (cyan placeholder)
  },

  // --- Font -----------------------------------------------------------------
  // Each section has ""Font"" (asset name, """" = unchanged) and ""Size"" (float, 0 = unchanged).
  // Global is applied first; per-element entries override it where set.
  //
  // Available fonts confirmed in scan:
  //   Jura-Regular, Jura-Bold, OCRAStd, Inconsolata-SemiBold, NotInter-Regular
  //   NotoSansJP-Regular, NotoSansKR-Regular, NotoSansSC-Regular, NotoSansTC-Regular
  //
  // Game default font sizes observed in scan:
  //   Bar labels (Label/DarkLabel):             14
  //   Tracker headline / Mission labels:        12
  //   Objective description:                    10
  //   Points / RewardPoints:                    16
  ""Global"":                { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // UnitHUD / EntityHUD
  ""UnitBarLabel"":          { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // ObjectivesTracker
  ""ObjTrackerHeadline"":    { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""ObjTrackerPoints"":      { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""ObjTrackerDescription"": { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""ObjTrackerLabel"":       { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""ObjSecondaryHeadline"":  { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""ObjRewardPoints"":       { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // MissionInfoPanel
  ""MissionName"":           { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""MissionDuration"":       { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // ObjectiveHUD (confirmed by scan)
  ""ObjectiveNameLabel"":    { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""ObjectiveStateLabel"":   { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // MovementHUD
  //   CostLabel (fontSize=16): AP cost display
  //   ActionLabel (fontSize=14): action type label
  ""MovementCostLabel"":     { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""MovementActionLabel"":   { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // BleedingWorldSpaceIcon (element name confirmed by scan: ""TextElement"")
  ""BleedingIconText"":      { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // DropdownText -- flyover text shown above units on skill/status application
  // (e.g. ""AP increased!"", suppression, Taking Command). Element name: Label (fontSize=14).
  ""DropdownText"":          { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // SkillBarButton
  ""SkillBarActionPointsLabel"": { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""SkillBarUsesLabel"":         { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""SkillBarHotkeyLabel"":       { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // SimpleSkillBarButton
  ""SimpleSkillBarLabel"":       { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""SimpleSkillBarHotkeyLabel"": { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // SkillBarSlotWeapon
  ""SkillBarSlotWeaponNameLabel"": { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // SelectedUnitPanel
  ""SelectedUnitConditionLabel"":    { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""SelectedUnitActionPointsLabel"": { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // TacticalUnitInfoStat
  ""TacticalUnitInfoValueLabel"": { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // Turn order / status
  ""TurnOrderPanelRoundNumberLabel"":  { ""Font"": """", ""Size"": 0, ""Color"": """" },
  ""StatusEffectIconStackCountLabel"": { ""Font"": """", ""Size"": 0, ""Color"": """" },

  // ObjectivesTracker progress bar colours
  // Uses the same format as Unit/Entity HUD bar colours: ""R, G, B"" or ""R, G, B, A""
  // Leave empty strings to keep game defaults.
  ""ObjectivesTrackerProgressBar"": {
    ""FillColor"": """",
    ""PreviewColor"": """",
    ""TrackColor"": """"
  },

  // Tactical UI style/tint overrides (scan-driven additions in progress).
  // Each tint slot uses { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }.
  // Set Enabled=true to apply.
  ""TacticalUIStyles"": {
    ""SkillBarButton"": {
      ""SkillIconTint"":       { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""SelectedOverlayTint"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""HoverOverlayTint"":    { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""PreviewOpacity"":      -1.0
    },
    ""BaseSkillBarItemSlot"": {
      ""BackgroundTint"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""ItemIconTint"":   { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""CrossTint"":      { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    },
    ""SimpleSkillBarButton"": {
      ""HoverTint"":      { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    },
    ""TurnOrderFactionSlot"": {
      ""InactiveMaskTint"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""SelectedTint"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""InactiveIconTint"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    },
    ""UnitsTurnBarSlot"": {
      ""OverlayTint"":      { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""SelectedTint"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""PortraitTint"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    },
    ""SelectedUnitPanel"": {
      ""PortraitTint"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
      ""HeaderTint"":       { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    },
    ""TacticalUnitInfoStat"": {
      ""IconTint"":         { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    },
    ""DelayedAbilityHUD"": {
      ""ProgressTint"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    }
  },

  // --- Tile highlight colours -----------------------------------------------
  // Each slot: { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
  // Set ""Enabled"": true and adjust R/G/B/A to override that slot.
  // Set ""Enabled"": false to leave that slot unchanged (game default).
  // R/G/B: 0-255 integers. A: 0.0-1.0 float.
  ""TileHighlights"": {
    // Fog of War
    ""FowOutlineColor"":          { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""FowOutlineInnerGlowColor"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""FowUnwalkableColor"":       { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Objective
    ""ObjectiveColor"":           { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""ObjectiveGlowColor"":       { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Skill Range
    ""SkillRangeColor"":          { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""SkillRangeGlowColor"":      { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // AoE
    ""AoEFillColor"":             { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""AoELineColor"":             { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""SpecialAoETileColor"":      { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Delayed AoE
    ""DelayedAoEFillColor"":      { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""DelayedAoEOutlineColor"":   { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""DelayedAoEInnerLineColor"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Enemy View
    ""EnemyViewColor"":           { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""EnemyViewGlowColor"":       { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Enemy Skills
    ""EnemySkillsColor"":         { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""EnemySkillsGlowColor"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""EnemySkillsTintColor"":     { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Movement
    ""MovementColor"":            { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""MovementGlowColor"":        { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""MovementTintColor"":        { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    // Unwalkable
    ""UnwalkableColor"":          { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
    ""UnplayableOutlineColor"":   { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
  },


  // --- USS global theme colours -----------------------------------------------
  // These map to public Color fields on UIConfig (confirmed in UIConfig.cs).
  // Setting Enabled = true overrides the value game-wide -- affects ALL UI screens.
  // R/G/B: 0-255 integers. A: 0.0-1.0 float.
  ""USSColors"": {
    // General text
        ""ColorNormal"": { ""Enabled"": false, ""R"": 225, ""G"": 225, ""B"": 225, ""A"": 1.0 },  // default: RGB(225, 225, 225) A(1.00)
        ""ColorBright"": { ""Enabled"": false, ""R"": 255, ""G"": 214, ""B"": 127, ""A"": 1.0 },  // default: RGB(255, 214, 127) A(1.00)
        ""ColorNormalTransparent"": { ""Enabled"": false, ""R"": 225, ""G"": 225, ""B"": 225, ""A"": 0.07 },  // default: RGB(225, 225, 225) A(0.07)
    // Interactive elements
        ""ColorInteract"": { ""Enabled"": false, ""R"": 187, ""G"": 175, ""B"": 149, ""A"": 1.0 },  // default: RGB(187, 175, 149) A(1.00)
        ""ColorInteractDark"": { ""Enabled"": false, ""R"": 122, ""G"": 115, ""B"": 98, ""A"": 1.0 },  // default: RGB(122, 115, 98) A(1.00)
        ""ColorInteractHover"": { ""Enabled"": false, ""R"": 238, ""G"": 226, ""B"": 189, ""A"": 1.0 },  // default: RGB(238, 226, 189) A(1.00)
        ""ColorInteractSelected"": { ""Enabled"": false, ""R"": 215, ""G"": 192, ""B"": 116, ""A"": 1.0 },  // default: RGB(215, 192, 116) A(1.00)
        ""ColorInteractSelectedText"": { ""Enabled"": false, ""R"": 0, ""G"": 0, ""B"": 0, ""A"": 1.0 },  // default: RGB(0, 0, 0) A(1.00)
    // Disabled states
        ""ColorDisabled"": { ""Enabled"": false, ""R"": 72, ""G"": 72, ""B"": 72, ""A"": 1.0 },  // default: RGB(72, 72, 72) A(1.00)
        ""ColorDisabledHover"": { ""Enabled"": false, ""R"": 199, ""G"": 199, ""B"": 199, ""A"": 1.0 },  // default: RGB(199, 199, 199) A(1.00)
    // Tooltip comparison
        ""ColorTooltipBetter"": { ""Enabled"": false, ""R"": 0, ""G"": 184, ""B"": 0, ""A"": 1.0 },  // default: RGB(0, 184, 0) A(1.00)
        ""ColorTooltipWorse"": { ""Enabled"": false, ""R"": 229, ""G"": 0, ""B"": 0, ""A"": 1.0 },  // default: RGB(229, 0, 0) A(1.00)
        ""ColorTooltipNormal"": { ""Enabled"": false, ""R"": 225, ""G"": 225, ""B"": 225, ""A"": 1.0 },  // default: RGB(225, 225, 225) A(1.00)
    // Status
        ""ColorPositive"": { ""Enabled"": false, ""R"": 72, ""G"": 191, ""B"": 147, ""A"": 1.0 },  // default: RGB(72, 191, 147) A(1.00)
        ""ColorNegative"": { ""Enabled"": false, ""R"": 180, ""G"": 67, ""B"": 65, ""A"": 1.0 },  // default: RGB(180, 67, 65) A(1.00)
        ""ColorWarning"": { ""Enabled"": false, ""R"": 255, ""G"": 50, ""B"": 50, ""A"": 1.0 },  // default: RGB(255, 50, 50) A(1.00)
    // Backgrounds / chrome
        ""ColorDarkBg"": { ""Enabled"": false, ""R"": 22, ""G"": 25, ""B"": 24, ""A"": 1.0 },  // default: RGB(22, 25, 24) A(1.00)
        ""ColorWindowCorner"": { ""Enabled"": false, ""R"": 233, ""G"": 212, ""B"": 111, ""A"": 1.0 },  // default: RGB(233, 212, 111) A(1.00)
        ""ColorTopBar"": { ""Enabled"": false, ""R"": 225, ""G"": 225, ""B"": 225, ""A"": 1.0 },  // default: RGB(225, 225, 225) A(1.00)
        ""ColorTopBarDark"": { ""Enabled"": false, ""R"": 160, ""G"": 171, ""B"": 163, ""A"": 1.0 },  // default: RGB(160, 171, 163) A(1.00)
    // Progress bars
        ""ColorProgressBarNormal"": { ""Enabled"": false, ""R"": 225, ""G"": 225, ""B"": 225, ""A"": 1.0 },  // default: RGB(225, 225, 225) A(1.00)
        ""ColorProgressBarBright"": { ""Enabled"": false, ""R"": 232, ""G"": 205, ""B"": 124, ""A"": 1.0 },  // default: RGB(232, 205, 124) A(1.00)
    // Misc
        ""ColorEmptySlotIcon"": { ""Enabled"": false, ""R"": 65, ""G"": 86, ""B"": 90, ""A"": 1.0 },  // default: RGB(65, 86, 90) A(1.00)
    // Mission state colours
        ""ColorMissionPlayable"":    { ""Enabled"": false, ""R"": 168, ""G"": 152, ""B"": 103, ""A"": 1.0 },
        ""ColorMissionLocked"":      { ""Enabled"": false, ""R"": 168, ""G"": 152, ""B"": 103, ""A"": 1.0 },
        ""ColorMissionPlayed"":      { ""Enabled"": false, ""R"": 113, ""G"": 102, ""B"": 69,  ""A"": 1.0 },
        ""ColorMissionPlayedArrow"": { ""Enabled"": false, ""R"": 75,  ""G"": 67,  ""B"": 44,  ""A"": 0.5 },
        ""ColorMissionUnplayable"":  { ""Enabled"": false, ""R"": 115, ""G"": 115, ""B"": 115, ""A"": 1.0 }
  },

   // --- Visualizer colours and parameters ------------------------------------
   // MovementVisualizer: colours for the 3D path drawn during movement planning.
   //   ReachableColor   -- path segments within the unit's AP range
   //   UnreachableColor -- path segments beyond AP range (dimmed/red by default)
   // TargetAimVisualizer: colours and params for the 3D spline drawn when aiming.
   //   OutOfRangeColor       -- colour when target is out of range
   //   InRangeColor          -- in-range line colour (applied via MaterialPropertyBlock)
   //                           NOTE: requires shader property ""_Color""; may have no
   //                           effect if the shader uses a different property name.
   //   AnimationScrollSpeed  -- UV scroll speed of animated texture (-1 = unchanged)
   //   Width                 -- world-space line width in metres (-1 = unchanged)
   //   MinimumHeight         -- minimum arc height above terrain (-1 = unchanged)
   //   MaximumHeight         -- maximum arc height above terrain (-1 = unchanged)
   //   DistanceToHeightScale -- arc height scale with distance (-1 = unchanged)
   // LineOfSightVisualizer: colour for the LOS lines drawn during targeting.
   //   LineColor -- applied to all Il2CppShapes.Line children via ColorStart/ColorEnd.
   //                Each LOS line is 3 Shapes.Line segments: fade-in, solid, fade-out.
   //                Colour is re-applied after every Resize() call via LOSResizePatch.
   ""Visualizers"": {
     ""MovementVisualizer"": {
       ""ReachableColor"":   { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },
       ""UnreachableColor"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
     },
    ""TargetAimVisualizer"": {
      // OutOfRangeColor: applied via MaterialPropertyBlock in the UpdateAim postfix.
      // UpdateAim() does not read the native field; the MPB override is the effective fix.
      ""OutOfRangeColor"":         { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },

      // InRangeColor: sets '_UnlitColor' on the HDRP/Unlit aim material.
      // Controls the base tint of the line. Default: white (no tint).
      ""InRangeColor"":            { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },

      // InRangeEmissiveColor: sets '_EmissiveColor' hue on the aim material.
      // Controls the bloom/glow colour. R/G/B set the hue (0-255).
      // Use EmissiveIntensity to control brightness independently.
      // Default material emissive: ~RGB(224, 224, 224) at intensity ~15.
      ""InRangeEmissiveColor"":    { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 },

      // EmissiveIntensity: HDR brightness multiplier for InRangeEmissiveColor.
      // -1 = leave unchanged. 0 = no bloom. ~15 = game default visible bloom.
      // Can be set independently of InRangeEmissiveColor to only change brightness.
      ""EmissiveIntensity"":       -1.0,

      ""AnimationScrollSpeed"":    -1.0,
      ""Width"":                   -1.0,
      ""MinimumHeight"":           -1.0,
      ""MaximumHeight"":           -1.0,
      ""DistanceToHeightScale"":   -1.0
    },
    ""LineOfSight"": {
      ""LineColor"": { ""Enabled"": false, ""R"": 255, ""G"": 255, ""B"": 255, ""A"": 1.0 }
    }
   },

  // --- Combat Flyover Text --------------------------------------------------
  // Requires the CombatFlyoverText mod to be installed alongside HUDCustomizer.
  // If CombatFlyoverText is not loaded these settings are read but have no effect.
  //
  // Enabled: set to false to disable all flyover display and duration extension.
  //
  // Colour values use Unity Rich Text hex format.
  // Supported: #RRGGBB, #RRGGBBAA, and a limited set of named colours:
  //   aqua, black, blue, brown, cyan, darkblue, fuchsia, green, grey,
  //   lightblue, lime, magenta, maroon, navy, olive, orange, purple, red,
  //   silver, teal, white, yellow
  // NOT supported: 3-digit hex, rgb(), hsl(), most CSS named colours.
  // Unsupported values render as white or are silently ignored by Unity.
  //
  // ExtraDisplaySeconds: additional seconds added to the game's default 1.5s
  //   display window.  Set to 0 to use the game's default duration.
  //
  // FadeDurationScale: multiplier on the game's default 1500ms fade animation.
  //   At defaults (ExtraDisplaySeconds=2.0, FadeDurationScale=2.0):
  //   total visible time = (1500ms * 2.0) + 2000ms = 5000ms per flyover.
  ""CombatFlyover"": {
    ""Enabled"":             true,
    ""ColourHPDamage"":      ""#FF4444"",
    ""ColourArmourDamage"":  ""#4488FF"",
    ""ColourAccuracy"":      ""#44CC44"",
    ""ExtraDisplaySeconds"": 2.0,
    ""FadeDurationScale"":   2.0
  },

  // --- Misc -----------------------------------------------------------------
  ""ReloadKey"":    ""F8"",
  ""DebugLogging"": false,

  // EnableScans: set to true to run discovery scans at startup.
  // Scans dump element trees, font assets, and UIConfig colour values to the
  // MelonLoader log.  Each scan fires at most once per session.
  // Intended for development only -- leave false in normal use.
  ""EnableScans"":  false
}";
    }
}
