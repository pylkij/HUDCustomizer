using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using Menace.ModpackLoader;
using Menace.SDK;

using Il2CppUnitHUD           = Il2CppMenace.UI.Tactical.UnitHUD;
using Il2CppEntityHUD         = Il2CppMenace.UI.Tactical.EntityHUD;
using Il2CppObjectiveHUD      = Il2CppMenace.UI.Tactical.ObjectiveHUD;
using Il2CppObjectivesTracker = Il2CppMenace.UI.Tactical.ObjectivesTracker;
using Il2CppMissionInfoPanel  = Il2CppMenace.UI.Tactical.MissionInfoPanel;
using Il2CppMovementHUD            = Il2CppMenace.UI.Tactical.MovementHUD;
using Il2CppBleedingWorldSpaceIcon  = Il2CppMenace.UI.Tactical.BleedingWorldSpaceIcon;
using Il2CppSimpleWorldSpaceIcon    = Il2CppMenace.UI.Tactical.SimpleWorldSpaceIcon;
using Il2CppWorldSpaceIcon          = Il2CppMenace.UI.Tactical.WorldSpaceIcon;
using Il2CppInterfaceElement        = Il2CppMenace.UI.InterfaceElement;
using Il2CppMovementVisualizer    = Il2CppMenace.Tactical.MovementVisualizer;
using Il2CppTargetAimVisualizer   = Il2CppMenace.Tactical.TargetAimVisualizer;
using Il2CppLineOfSightVisualizer = Il2CppMenace.Tactical.LineOfSightVisualizer;
using Il2CppDropdownText          = Il2CppMenace.UI.Tactical.DropdownText;
using Il2CppSkillBarButton        = Il2CppMenace.UI.Tactical.SkillBarButton;
using Il2CppBaseSkillBarItemSlot  = Il2CppMenace.UI.Tactical.BaseSkillBarItemSlot;
using Il2CppSimpleSkillBarButton  = Il2CppMenace.UI.Tactical.SimpleSkillBarButton;
using Il2CppTurnOrderFactionSlot  = Il2CppMenace.UI.Tactical.TurnOrderFactionSlot;
using Il2CppUnitsTurnBarSlot      = Il2CppMenace.UI.Tactical.UnitsTurnBarSlot;
using Il2CppSelectedUnitPanel     = Il2CppMenace.UI.Tactical.SelectedUnitPanel;
using Il2CppTacticalUnitInfoStat  = Il2CppMenace.UI.Tactical.TacticalUnitInfoStat;
using Il2CppTurnOrderPanel        = Il2CppMenace.UI.Tactical.TurnOrderPanel;
using Il2CppStatusEffectIcon      = Il2CppMenace.UI.Tactical.StatusEffectIcon;
using Il2CppDelayedAbilityHUD     = Il2CppMenace.UI.Tactical.DelayedAbilityHUD;
using Il2CppStructureHUD          = Il2CppMenace.UI.Tactical.StructureHUD;

// =============================================================================
// HUDCustomizerPlugin -- entry point, lifecycle, patches, and helpers.
// Partial class; other parts live in UnitCustomizer.cs and FontCustomizer.cs.
// Config data classes live in HUDConfig.cs.
// =============================================================================
public partial class HUDCustomizerPlugin : IModpackPlugin
{
    internal static MelonLogger.Instance Log;
    public   static HUDCustomizerPlugin  Instance { get; private set; }

    public static HUDCustomizerConfig Config { get; private set; } = new();

    // ConfigDir, ConfigPath, and JsonOpts have moved to HUDConfig.cs.
    internal static string               ConfigDir  => HUDConfig.ConfigDir;
    internal static string               ConfigPath => HUDConfig.ConfigPath;
    internal static JsonSerializerOptions JsonOpts  => HUDConfig.JsonOpts;

    private KeyCode _reloadKey = KeyCode.F8;

    // =========================================================================
    // Live element registry
    // Populated by each patch postfix so hot-reload can re-apply without
    // needing to search the scene.  Keyed by native pointer (stable per
    // element lifetime).  Value stores the element, its hud type string, and
    // the scale value that was last used (unit vs entity).
    // =========================================================================
    private static readonly Dictionary<IntPtr, (Il2CppInterfaceElement el, string hudType)>
        _registry = new();

    internal static void Register(Il2CppInterfaceElement el, string hudType)
    {
        if (el == null) return;
        _registry[el.Pointer] = (el, hudType);
    }

    internal static void ReapplyToLiveElements()
    {
        int count = 0;
        var dead = new List<IntPtr>();
        foreach (var kvp in _registry)
        {
            var (el, hudType) = kvp.Value;
            try
            {
                // A null pointer means the native object was destroyed.
                if (el.Pointer == IntPtr.Zero) { dead.Add(kvp.Key); continue; }

                // Scale only applies to unit/entity HUDs -- other types (ObjectivesTracker,
                // MissionInfoPanel, ObjectiveHUD) have no scale config and must not be scaled.
                switch (hudType)
                {
                    case "UnitHUD":
                        UnitCustomizer.Apply(el, Config.UnitHUDScale);
                        // Re-apply spent opacity on hot-reload.
                        // SetOpacity is transition-only so the patch won't fire until the
                        // next turn change. We push the override immediately here instead.
                        // Any opacity value other than 1.0 means the unit is currently spent
                        // (the game only ever sets 0.5 or 1.0 via SetOpacity).
                        if (Config.SpentUnitHUDOpacity >= 0f &&
                            !Mathf.Approximately(el.style.opacity.value, 1.0f))
                        {
                            el.style.opacity = new StyleFloat(
                                Mathf.Clamp(Config.SpentUnitHUDOpacity, 0f, 1f));
                        }
                        break;
                    case "EntityHUD":
                        UnitCustomizer.Apply(el, Config.EntityHUDScale); break;
                    case "StructureHUD":
                        UnitCustomizer.Apply(el, Config.StructureHUDScale); break;
                    case "DropdownText":
                        // No bar or badge -- FontCustomizer handles everything.
                        break;
                    default:
                        // Non-unit HUD types (ObjectivesTracker, MissionInfoPanel,
                        // ObjectiveHUD, etc.) have no bars or badge -- UnitCustomizer
                        // has nothing to apply to them.
                        break;
                }

                TacticalElementCustomizer.Apply(el, hudType);
                FontCustomizer.Apply(el, hudType);
                count++;
            }
            catch { dead.Add(kvp.Key); }
        }
        foreach (var ptr in dead) _registry.Remove(ptr);
        Log.Msg($"[HUDCustomizer] Hot-reload: {count} live element(s) updated ({dead.Count} stale removed).");
    }

    // IModpackPlugin ----------------------------------------------------------
    public void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
    {
        Instance = this;
        Log      = logger;

        RestoreConfigFromUserData();
        LoadConfig();
        Log.Msg("HUDCustomizer loaded.");

        GameState.TacticalReady += OnTacticalReady;

        RegisterPatches(harmony);
    }

    public void OnSceneLoaded(int buildIndex, string sceneName)
    {
        Debug($"OnSceneLoaded: '{sceneName}' (index {buildIndex})");

        if (sceneName == "Strategy")
            MelonCoroutines.Start(ApplyUSSAfterDelay(0.5f));
    }

    private static IEnumerator ApplyUSSAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Debug("[HUDCustomizer] Strategy scene delay elapsed -- applying USS colours.");
        USSCustomizer.TryApply();
    }

    // GameState event ---------------------------------------------------------
    private void OnTacticalReady()
    {
        Debug("TacticalReady -- HUDCustomizer active.");
        FontCustomizer.OnTacticalReady();
        TileCustomizer.TryApply();
        USSCustomizer.TryApply();
        UnitCustomizer.ApplyFactionHealthBarColors();
        UnitCustomizer.ApplyRarityColors();
        VisualizerCustomizer.TryApply();
        VisualizerCustomizer.TryApplyLineOfSight(Config);
        Scans.RunUIConfigScan();  // fires after TryApply() -- UIConfig.Get() is warm by this point
    }

    // Per-frame ---------------------------------------------------------------
    public void OnUpdate()
    {
        if (Input.GetKeyDown(_reloadKey))
        {
            Log.Msg($"[{_reloadKey}] Hot-reload triggered.");
            LoadConfig();
            FontCustomizer.InvalidateCache();
            ReapplyToLiveElements();
            TileCustomizer.TryApply();
            USSCustomizer.TryApply();
            UnitCustomizer.ApplyFactionHealthBarColors();
            UnitCustomizer.ApplyRarityColors();
            VisualizerCustomizer.TryApply();
            VisualizerCustomizer.TryApplyLineOfSight(Config);
            CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
        }
    }

    // =========================================================================
    // Config I/O
    // BuildDefaultConfig() has moved to HUDConfig.cs (HUDConfig.BuildDefaultConfig()).
    // =========================================================================
    private void LoadConfig()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);

            if (!File.Exists(ConfigPath))
            {
                Log.Msg($"No config found -- writing defaults to {ConfigPath}");
                File.WriteAllText(ConfigPath, HUDConfig.BuildDefaultConfig());
            }
            else
            {
                // Merge: fill any keys missing from the user's file with defaults,
                // then write back so the file is always complete after an update.
                string merged = MergeWithDefaults(File.ReadAllText(ConfigPath));
                File.WriteAllText(ConfigPath, merged);
            }

            string json = File.ReadAllText(ConfigPath);
            Config = JsonSerializer.Deserialize<HUDCustomizerConfig>(json, JsonOpts)
                     ?? new HUDCustomizerConfig();

            if (!Enum.TryParse(Config.ReloadKey, out _reloadKey))
            {
                Log.Warning($"Unknown ReloadKey '{Config.ReloadKey}' -- defaulting to F8.");
                _reloadKey = KeyCode.F8;
            }

            Log.Msg($"Config loaded (v{Config.ConfigVersion}).  " +
                    $"UnitScale={Config.UnitHUDScale:F2}  " +
                    $"EntityScale={Config.EntityHUDScale:F2}  " +
                    $"Origin=({Config.TransformOriginX:F0}%,{Config.TransformOriginY:F0}%)  " +
                    $"ReloadKey={_reloadKey}");

            UnitCustomizer.LogColourSummary();
            FontCustomizer.LogFontSummary();
            TileCustomizer.LogSummary();
            USSCustomizer.LogSummary();
            UnitCustomizer.LogFactionHealthBarSummary();
            UnitCustomizer.LogRarityColorSummary();
            TacticalElementCustomizer.LogSummary();
            VisualizerCustomizer.LogSummary();
            VisualizerCustomizer.LogLineOfSightSummary();
            LogSpentOpacitySummary();
            CombatFlyoverCustomizer.Apply(Config.CombatFlyover);
            CombatFlyoverCustomizer.LogSummary();

            BackupConfig();
        }
        catch (JsonException ex)
        {
            // JSON is structurally broken -- do NOT overwrite the file.
            // The user's settings are preserved on disk; defaults are used for this session only.
            // Log the exact position so the user can find and fix the problem themselves.
            string location = (ex.LineNumber.HasValue && ex.BytePositionInLine.HasValue)
                ? $"line {ex.LineNumber.Value + 1}, column {ex.BytePositionInLine.Value + 1}"
                : "unknown position";

            Log.Error($"[HUDCustomizer] Config has a JSON syntax error at {location} -- file NOT overwritten.");
            Log.Error($"  --> {ex.Message}");
            Log.Error($"  --> Fix the error in {ConfigPath} and hot-reload with {_reloadKey}.");
            Log.Warning("[HUDCustomizer] Running with defaults for this session. Your settings are intact on disk.");

            Config = new HUDCustomizerConfig();
        }
        catch (Exception ex)
        {
            Log.Error($"LoadConfig failed unexpectedly: {ex.Message}");
            Config = new HUDCustomizerConfig();
        }
    }

    // =========================================================================
    // UserData backup and restore
    // BackupConfig() copies the current config to UserData after every successful
    // load (initial and hot-reload).  RestoreConfigFromUserData() runs once at
    // OnInitialize before LoadConfig() -- if the mod directory config is missing
    // but a UserData backup exists, it is copied back so LoadConfig() finds it.
    // =========================================================================
    private static readonly string UserDataDir  = Path.Combine("UserData", "TacticalHUDCustomizer");
    private static readonly string UserDataPath = Path.Combine(UserDataDir, "HUDCustomizer.json");

    private static void BackupConfig()
    {
        try
        {
            Directory.CreateDirectory(UserDataDir);
            File.Copy(ConfigPath, UserDataPath, overwrite: true);
            Debug($"[Backup] Config backed up to {UserDataPath}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[HUDCustomizer] Config backup to UserData failed: {ex.Message}");
        }
    }

    private static void RestoreConfigFromUserData()
    {
        try
        {
            if (File.Exists(ConfigPath)) return;
            if (!File.Exists(UserDataPath)) return;

            Directory.CreateDirectory(ConfigDir);
            File.Copy(UserDataPath, ConfigPath, overwrite: false);
            Log.Msg($"[HUDCustomizer] Config restored from UserData backup ({UserDataPath}).");
        }
        catch (Exception ex)
        {
            Log.Warning($"[HUDCustomizer] Config restore from UserData failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Option 3 merge: BuildDefaultConfig() is the output template.
    // User values are extracted into a path-keyed dictionary and substituted
    // into the template line by line, preserving all comments and formatting.
    //
    // Key paths use dot-notation matching the JSON structure, e.g.:
    //   "UnitHUDScale"
    //   "FactionHealthBarColors.HealthBarFillColorPlayerUnits.R"
    //
    // Only scalar leaf values are substituted; object boundaries are tracked
    // via a path stack driven by indentation cues in the template.
    // =========================================================================
    private static string MergeWithDefaults(string userJson)
    {
        var docOptions = new JsonDocumentOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        using var userDoc = JsonDocument.Parse(userJson, docOptions);

        // Flatten the user document into path -> raw JSON value string.
        var userValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(userDoc.RootElement, "", userValues);

        // Walk the template line by line, substituting user values for scalars.
        var template  = HUDConfig.BuildDefaultConfig();
        var output    = new System.Text.StringBuilder(template.Length);
        var pathStack = new System.Collections.Generic.Stack<string>();
        pathStack.Push("");

        foreach (var line in template.Split('\n'))
        {
            output.AppendLine(SubstituteLine(line, pathStack, userValues));
        }

        return output.ToString();
    }

    // Recursively flattens a JsonElement into dot-path -> raw value pairs.
    // Only leaf (non-object) values are recorded; object nodes are traversed.
    private static void FlattenJson(JsonElement el, string path,
                                     Dictionary<string, string> result)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                string childPath = path.Length == 0 ? prop.Name : $"{path}.{prop.Name}";
                FlattenJson(prop.Value, childPath, result);
            }
        }
        else
        {
            result[path] = el.GetRawText();
        }
    }

    // Given one line from the template, detects a "Key": value assignment and
    // substitutes the user's value if one exists for the resolved path.
    // Updates pathStack to track the current object nesting level.
    //
    // Stack discipline:
    //   PUSH  when the line is  "Key": {          (multi-line object open)
    //   POP   when the line is  }  or  },         (standalone closing brace only)
    //   NEITHER for inline objects: "Key": { ... } on one line -- these are
    //           never pushed so they must never trigger a pop.
    private static string SubstituteLine(string line,
                                          System.Collections.Generic.Stack<string> pathStack,
                                          Dictionary<string, string> userValues)
    {
        string trimmedLine = line.Trim();

        // Only pop for a standalone closing brace -- never for inline objects.
        // A line like  "Foo": { "Enabled": false, ... }  contains a closing brace
        // but was never pushed, so popping here would corrupt the stack.
        if ((trimmedLine == "}" || trimmedLine == "},") && pathStack.Count > 1)
            pathStack.Pop();

        // Match:  "Key": <anything>
        var match = System.Text.RegularExpressions.Regex.Match(
            line, @"^\s*\""([^\""]+)\""\s*:\s*(.+)$");

        if (!match.Success) return line;

        string key      = match.Groups[1].Value;
        string rest     = match.Groups[2].Value.TrimEnd();
        string fullPath = pathStack.Count > 0 && pathStack.Peek().Length > 0
                          ? $"{pathStack.Peek()}.{key}"
                          : key;

        // Multi-line object open ("Key": {) -- push scope, no substitution.
        if (rest.TrimEnd(',').Trim() == "{")
        {
            pathStack.Push(fullPath);
            return line;
        }

        // Inline object ("Key": { "A": x, "B": y }) -- substitute each leaf
        // individually using the flattened user values, rebuild the line.
        // The object is never pushed onto the stack.
        var inlineMatch = System.Text.RegularExpressions.Regex.Match(
            rest, @"^\{(.+)\}(,?)(\s*//.*)?$");
        if (inlineMatch.Success)
        {
            string inner           = inlineMatch.Groups[1].Value;
            string trailingComma   = inlineMatch.Groups[2].Value;
            string trailingComment = inlineMatch.Groups[3].Value;
            bool   anyChanged      = false;

            var innerPairs = System.Text.RegularExpressions.Regex.Matches(
                inner, @"\""([^\""]+)\""\s*:\s*([^,}]+)");
            var pairList = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match p in innerPairs)
            {
                string subKey      = p.Groups[1].Value;
                string subDefault  = p.Groups[2].Value.Trim();
                string subPath     = $"{fullPath}.{subKey}";
                if (userValues.TryGetValue(subPath, out string subUserRaw))
                {
                    pairList.Add($" \"{subKey}\": {subUserRaw}");
                    if (subUserRaw != subDefault) anyChanged = true;
                }
                else
                {
                    pairList.Add($" \"{subKey}\": {subDefault}");
                    Debug($"[MergeConfig] Adding missing key '{subPath}' with default value.");
                }
            }

            if (anyChanged)
                Debug($"[MergeConfig] Restoring user values for inline object '{fullPath}'.");

            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            return $"{indent}\"{key}\": {{{string.Join(",", pairList)} }}{trailingComma}{trailingComment}";
        }

        // Scalar -- substitute if the user has a value for this path.
        if (userValues.TryGetValue(fullPath, out string userRaw))
        {
            Debug($"[MergeConfig] Restoring user value for '{fullPath}': {userRaw}");

            string trimmedRest   = rest.TrimEnd(',');
            int    commentIdx    = trimmedRest.IndexOf("//");
            string inlineComment = commentIdx >= 0 ? "  " + trimmedRest.Substring(commentIdx) : "";
            bool   hadComma      = rest.TrimEnd().EndsWith(",") ||
                                   (commentIdx >= 0 && trimmedRest.Substring(0, commentIdx).TrimEnd().EndsWith(","));
            string comma         = hadComma ? "," : "";

            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            return $"{indent}\"{key}\": {userRaw}{comma}{inlineComment}";
        }

        // Key absent in user file -- keep template default, log insertion.
        Debug($"[MergeConfig] Adding missing key '{fullPath}' with default value.");
        return line;
    }

    // =========================================================================
    // Shared helpers
    // =========================================================================

    internal static void Debug(string msg)
    {
        if (Config?.DebugLogging == true)
            Log?.Msg($"[DBG] {msg}");
    }

    // Parses "R,G,B" or "R,G,B,A" where R/G/B are 0-255 integers and A is a
    // 0.0-1.0 float.  Returns false and logs a warning if the string is malformed.
    internal static bool TryParseColor(string s, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var parts = s.Split(',');
        if (parts.Length < 3)
        {
            Log.Warning($"[HUDCustomizer] TryParseColor: expected R,G,B or R,G,B,A " +
                        $"(e.g. '255,128,0' or '255,128,0,0.5'), got '{s}'");
            return false;
        }

        if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float r) ||
            !float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float g) ||
            !float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float b))
        {
            Log.Warning($"[HUDCustomizer] TryParseColor: could not parse R,G,B from '{s}'");
            return false;
        }

        if (r < 0f || r > 255f || g < 0f || g > 255f || b < 0f || b > 255f)
        {
            Log.Warning($"[HUDCustomizer] TryParseColor: R,G,B values must be 0-255 -- " +
                        $"got ({r},{g},{b}) in '{s}' -- clamping.");
            r = Mathf.Clamp(r, 0f, 255f);
            g = Mathf.Clamp(g, 0f, 255f);
            b = Mathf.Clamp(b, 0f, 255f);
        }

        float a = 1f;
        if (parts.Length >= 4 &&
            !float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out a))
        {
            Log.Warning($"[HUDCustomizer] TryParseColor: could not parse A from '{s}'");
            return false;
        }

        if (a < 0f || a > 1f)
        {
            Log.Warning($"[HUDCustomizer] TryParseColor: A must be 0.0-1.0 -- " +
                        $"got ({a}) in '{s}' -- clamping.");
            a = Mathf.Clamp(a, 0f, 1f);
        }

        color = new Color(r / 255f, g / 255f, b / 255f, a);
        return true;
    }

    // Converts a TileHighlightEntry to a Unity Color and logs the assignment.
    // Il2Cpp interop exposes Color fields as properties (no ref support), so the
    // caller checks Enabled, then assigns the return value to the property.
    internal static Color ToColor(TileHighlightEntry entry, string label)
    {
        Debug($"  [Color] SET {label} -> RGB({(int)entry.R},{(int)entry.G},{(int)entry.B}) A({entry.A:F2})");
        return new Color(entry.R / 255f, entry.G / 255f, entry.B / 255f, entry.A);
    }

    internal static void LogSpentOpacitySummary()
    {
        var v = Config.SpentUnitHUDOpacity;
        if (v < 0f)
            Log.Msg("  [SpentOpacity] SpentUnitHUDOpacity: unchanged (game default 0.5 preserved)");
        else
            Log.Msg($"  [SpentOpacity] SpentUnitHUDOpacity: {Mathf.Clamp(v, 0f, 1f):F2}");
    }

    // GetClasses helper: VisualElement.GetClasses() returns an Il2Cpp-wrapped
    // IEnumerable<string> whose enumerator does not satisfy the C# foreach
    // contract.  This drains it safely into a plain List<string>.
    internal static List<string> GetClasses(VisualElement ve)
    {
        var result = new List<string>();
        try
        {
            var enumerableObj = ve.GetClasses();
            if (enumerableObj == null) return result;
            var getEnum = enumerableObj.GetType()
                .GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance);
            if (getEnum == null) return result;
            var enumerator = getEnum.Invoke(enumerableObj, null);
            var moveNext   = enumerator.GetType()
                .GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
            var current    = enumerator.GetType()
                .GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
            if (moveNext == null || current == null) return result;
            while ((bool)moveNext.Invoke(enumerator, null))
            {
                var val = current.GetValue(enumerator);
                if (val != null) result.Add(val.ToString());
            }
        }
        catch { }
        return result;
    }
}

// =============================================================================
// Patches
// Registered by HUDCustomizerPlugin.OnInitialize via RegisterPatches().
// Each patch casts to Il2CppInterfaceElement then delegates to the appropriate
// customiser.  The font scan and element scans fire once per type on first use.
// =============================================================================
public partial class HUDCustomizerPlugin
{
    private void RegisterPatches(HarmonyLib.Harmony harmony)
    {
        harmony.PatchAll(typeof(Patch_UnitHUD_SetActor));
        harmony.PatchAll(typeof(Patch_UnitHUD_Show));
        harmony.PatchAll(typeof(Patch_EntityHUD_InitBars));
        harmony.PatchAll(typeof(Patch_ObjectiveHUD_SetObjective));
        harmony.PatchAll(typeof(Patch_ObjectivesTracker_Init));
        harmony.PatchAll(typeof(Patch_MissionInfoPanel_Init));
        harmony.PatchAll(typeof(Patch_UnitHUD_SetOpacity));
        harmony.PatchAll(typeof(Patch_MovementHUD_SetDestination));
        harmony.PatchAll(typeof(Patch_BleedingWorldSpaceIcon_SetText));
        harmony.PatchAll(typeof(Patch_MovementVisualizer_ShowPath));
        harmony.PatchAll(typeof(Patch_TargetAimVisualizer_UpdateAim));
        harmony.PatchAll(typeof(LOSResizePatch));
        harmony.PatchAll(typeof(Patch_DropdownText_Init));
        harmony.PatchAll(typeof(Patch_StructureHUD_Init));
        // Permanent tactical routing patches (Step 5).
        harmony.PatchAll(typeof(Patch_SkillBarButton_Init));
        harmony.PatchAll(typeof(Patch_SkillBarButton_Show));
        harmony.PatchAll(typeof(Patch_BaseSkillBarItemSlot_Init));
        harmony.PatchAll(typeof(Patch_SimpleSkillBarButton_SetText));
        harmony.PatchAll(typeof(Patch_TurnOrderFactionSlot_Init));
        harmony.PatchAll(typeof(Patch_UnitsTurnBarSlot_Init));
        harmony.PatchAll(typeof(Patch_UnitsTurnBarSlot_SetActor));
        harmony.PatchAll(typeof(Patch_SelectedUnitPanel_SetActor));
        harmony.PatchAll(typeof(Patch_TacticalUnitInfoStat_Init));
        harmony.PatchAll(typeof(Patch_TurnOrderPanel_UpdateFactions));
        harmony.PatchAll(typeof(Patch_StatusEffectIcon_InitSkillTemplate));
        harmony.PatchAll(typeof(Patch_StatusEffectIcon_InitSkill));
        harmony.PatchAll(typeof(Patch_DelayedAbilityHUD_SetAbility));
        harmony.PatchAll(typeof(Patch_DelayedAbilityHUD_SetProgressPct));

        Debug("Harmony patches registered.");
    }

    // Unit HUD -- SetActor is the primary init point.
    [HarmonyPatch(typeof(Il2CppUnitHUD), nameof(Il2CppUnitHUD.SetActor))]
    private static class Patch_UnitHUD_SetActor
    {
        private static void Postfix(Il2CppUnitHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                UnitCustomizer.Apply(el, Config.UnitHUDScale);
                FontCustomizer.Apply(el, "UnitHUD");
                Register(el, "UnitHUD");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_UnitHUD_SetActor: {ex}"); }
        }
    }

    // Unit HUD -- Show re-applies in case the game resets inline styles.
    [HarmonyPatch(typeof(Il2CppUnitHUD), nameof(Il2CppUnitHUD.Show))]
    private static class Patch_UnitHUD_Show
    {
        private static void Postfix(Il2CppUnitHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                UnitCustomizer.Apply(el, Config.UnitHUDScale);
                FontCustomizer.Apply(el, "UnitHUD");
                Register(el, "UnitHUD");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_UnitHUD_Show: {ex}"); }
        }
    }

    // Entity HUD -- InitBars; UnitHUD instances are skipped (handled above).
    [HarmonyPatch(typeof(Il2CppEntityHUD), "InitBars")]
    private static class Patch_EntityHUD_InitBars
    {
        private static void Postfix(Il2CppEntityHUD __instance)
        {
            if (__instance.TryCast<Il2CppUnitHUD>() != null) return;
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                UnitCustomizer.Apply(el, Config.EntityHUDScale);
                FontCustomizer.Apply(el, "EntityHUD");
                Register(el, "EntityHUD");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_EntityHUD_InitBars: {ex}"); }
        }
    }

    // ObjectiveHUD -- SetObjective is the per-objective init point.
    // Scan fires once on first instance; placeholder until implementation added.
    // DELETE the Scans call once ObjectiveHUD customisation is implemented.
    [HarmonyPatch(typeof(Il2CppObjectiveHUD), nameof(Il2CppObjectiveHUD.SetObjective))]
    private static class Patch_ObjectiveHUD_SetObjective
    {
        private static bool _scanned = false;
        private static void Postfix(Il2CppObjectiveHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                FontCustomizer.Apply(el, "ObjectiveHUD");
                Register(el, "ObjectiveHUD");
                if (!_scanned) { _scanned = true; Scans.RunElementScan(el, "ObjectiveHUD"); }
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_ObjectiveHUD_SetObjective: {ex}"); }
        }
    }

    // ObjectivesTracker -- Init is the single init point.
    // DELETE the Scans call once ObjectivesTracker customisation is implemented.
    [HarmonyPatch(typeof(Il2CppObjectivesTracker), nameof(Il2CppObjectivesTracker.Init))]
    private static class Patch_ObjectivesTracker_Init
    {
        private static bool _scanned = false;
        private static void Postfix(Il2CppObjectivesTracker __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "ObjectivesTracker");
                FontCustomizer.Apply(el, "ObjectivesTracker");
                Register(el, "ObjectivesTracker");
                if (!_scanned) { _scanned = true; Scans.RunElementScan(el, "ObjectivesTracker"); }
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_ObjectivesTracker_Init: {ex}"); }
        }
    }

    // MissionInfoPanel -- Init is the single init point.
    // DELETE the Scans call once MissionInfoPanel customisation is implemented.
    [HarmonyPatch(typeof(Il2CppMissionInfoPanel), nameof(Il2CppMissionInfoPanel.Init))]
    private static class Patch_MissionInfoPanel_Init
    {
        private static bool _scanned = false;
        private static void Postfix(Il2CppMissionInfoPanel __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                FontCustomizer.Apply(el, "MissionInfoPanel");
                Register(el, "MissionInfoPanel");
                if (!_scanned) { _scanned = true; Scans.RunElementScan(el, "MissionInfoPanel"); }
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_MissionInfoPanel_Init: {ex}"); }
        }
    }

    // UnitHUD.SetOpacity(float _opacity) is the single method the game calls to
    // dim or restore a unit HUD (confirmed in UnitHUD.cs source).  OnUpdate
    // delegates to it; it is only called on state transitions, not every frame.
    // Confirmed values from scan log:
    //   Spent (turn ended): _opacity = 0.5
    //   Active (turn start): _opacity = 1.0
    // We postfix here and substitute our configured value when the game passes 0.5,
    // leaving active-unit restores (1.0) untouched so game state logic is respected.
    // This is a private method -- Harmony resolves it by name string.
    [HarmonyPatch(typeof(Il2CppUnitHUD), "SetOpacity")]
    private static class Patch_UnitHUD_SetOpacity
    {
        private static void Postfix(Il2CppUnitHUD __instance, float _opacity)
        {
            try
            {
                var cfg = HUDCustomizerPlugin.Config;
                if (cfg.SpentUnitHUDOpacity < 0f) return;
                // Only intercept the spent-dim call (0.5). The active-restore call
                // (1.0) must reach the element unmodified so units un-dim correctly.
                if (!Mathf.Approximately(_opacity, 0.5f)) return;

                var el = __instance.Cast<Il2CppInterfaceElement>();
                float clamped = Mathf.Clamp(cfg.SpentUnitHUDOpacity, 0f, 1f);
                el.style.opacity = new StyleFloat(clamped);
                HUDCustomizerPlugin.Debug(
                    $"  [SpentOpacity] SET ptr={__instance.Pointer:X}  opacity={clamped:F2}");
            }
            catch (Exception ex) { Log.Warning($"[HUDCustomizer] Patch_UnitHUD_SetOpacity: {ex.Message}"); }
        }
    }

    // MovementHUD -- SetDestination fires on each movement target selection.
    // Confirmed elements: CostLabel (fontSize=16), ActionLabel (fontSize=14).
    [HarmonyPatch(typeof(Il2CppMovementHUD), nameof(Il2CppMovementHUD.SetDestination))]
    private static class Patch_MovementHUD_SetDestination
    {
        private static void Postfix(Il2CppMovementHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                FontCustomizer.Apply(el, "MovementHUD");
                Register(el, "MovementHUD");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_MovementHUD_SetDestination: {ex}"); }
        }
    }

    // MovementVisualizer -- ShowPath is called each time a movement path is
    // displayed.  We re-apply colour overrides here so they survive path updates.
    // ShowPath signature (from MovementVisualizer.cs):
    //   public void ShowPath(IReadOnlyCollection<Vector3> _path, int _disableAtIndex,
    //                        Tile _destTile, int _cost)
    [HarmonyPatch(typeof(Il2CppMovementVisualizer), nameof(Il2CppMovementVisualizer.ShowPath))]
    private static class Patch_MovementVisualizer_ShowPath
    {
        private static void Postfix(Il2CppMovementVisualizer __instance)
        {
            try
            {
                var mv = HUDCustomizerPlugin.Config.Visualizers.MovementVisualizer;
                if (!mv.ReachableColor.Enabled && !mv.UnreachableColor.Enabled) return;

                if (mv.ReachableColor.Enabled)
                    __instance.ReachableColor = HUDCustomizerPlugin.ToColor(
                        mv.ReachableColor, "MovementVisualizer.ReachableColor");

                if (mv.UnreachableColor.Enabled)
                    __instance.UnreachableColor = HUDCustomizerPlugin.ToColor(
                        mv.UnreachableColor, "MovementVisualizer.UnreachableColor");
            }
            catch (Exception ex)
            {
                Log.Error($"[HUDCustomizer] Patch_MovementVisualizer_ShowPath: {ex}");
            }
        }
    }

    // TargetAimVisualizer -- UpdateAim rebuilds the spline mesh each call,
    // which resets the MeshRenderer's MaterialPropertyBlock.  We re-apply
    // both InRangeColor and OutOfRangeColor via MPB after each rebuild.
    // OutOfRangeColor root cause: UpdateAim() hardcodes white for the out-of-range
    // path without reading the native OutOfRangeColor field (confirmed via Ghidra).
    // The effective fix is the MPB write in ReapplyTargetAimVisualizerColors.
    // UpdateAim signature (from TargetAimVisualizer.cs):
    //   public void UpdateAim(Vector3 _origin, Vector3 _target, Skill _skill)
    [HarmonyPatch(typeof(Il2CppTargetAimVisualizer), nameof(Il2CppTargetAimVisualizer.UpdateAim))]
    private static class Patch_TargetAimVisualizer_UpdateAim
    {
        private static void Postfix(Il2CppTargetAimVisualizer __instance)
        {
            try
            {
                Scans.RunTargetAimMaterialScan(__instance);
                VisualizerCustomizer.ReapplyTargetAimVisualizerColors(__instance);
            }
            catch (Exception ex)
            {
                Log.Error($"[HUDCustomizer] Patch_TargetAimVisualizer_UpdateAim: {ex}");
            }
        }
    }

    // LineOfSightVisualizer -- Resize(int _n) allocates/re-colours the Shapes Line
    // pool.  We postfix here to re-apply our configured colour immediately after
    // each resize so it survives pool expansion and colour resets.
    [HarmonyPatch]
    private static class LOSResizePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(Il2CppLineOfSightVisualizer),
                "Resize",
                new[] { typeof(int) });

        [HarmonyPostfix]
        private static void Postfix(Il2CppLineOfSightVisualizer __instance)
        {
            try
            {
                if (!HUDCustomizerPlugin.Config.Visualizers.LineOfSight.LineColor.Enabled) return;
                VisualizerCustomizer.ApplyLineOfSightColor(__instance, VisualizerCustomizer._currentLOSColor);
            }
            catch (Exception ex) { Log.Error($"[LOSResizePatch] {ex}"); }
        }
    }

    // BleedingWorldSpaceIcon -- SetText is called on creation and each text update
    // (confirmed in BleedingWorldSpaceIcon.cs). Has m_TextElement (Label) and
    // m_Icon (VisualElement). UpdateAnimation runs a pulse every frame so position/
    // opacity may be overwritten, but text colour via style.color should survive.
    // Element name confirmed by scan: "TextElement". QueryAndSet replaced with SetFont(el.Q(...)).
    [HarmonyPatch(typeof(Il2CppBleedingWorldSpaceIcon), nameof(Il2CppBleedingWorldSpaceIcon.SetText))]
    private static class Patch_BleedingWorldSpaceIcon_SetText
    {
        private static void Postfix(Il2CppBleedingWorldSpaceIcon __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                FontCustomizer.Apply(el, "BleedingWorldSpaceIcon");
                Register(el, "BleedingWorldSpaceIcon");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_BleedingWorldSpaceIcon_SetText: {ex}"); }
        }
    }

    // DropdownText -- flyover text shown above units on skill/status application
    // (e.g. "AP increased!", suppression, Taking Command).
    // Init(String _text, Sprite _icon) is the correct hook: fires once on creation
    // with text already set, confirmed by scan (text='AP increased!', element=Label).
    [HarmonyPatch(typeof(Il2CppDropdownText), nameof(Il2CppDropdownText.Init))]
    private static class Patch_DropdownText_Init
    {
        private static bool _scanned = false;
        private static void Postfix(Il2CppDropdownText __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                FontCustomizer.Apply(el, "DropdownText");
                Register(el, "DropdownText");
                if (!_scanned) { _scanned = true; Scans.RunElementScan(el, "DropdownText"); }
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_DropdownText_Init: {ex}"); }
        }
    }

    // StructureHUD -- Init(Structure) is the dedicated setup hook for structure HUDs.
    // This enables independent StructureHUDScale while preserving existing EntityHUD
    // styling patterns (bars/fonts) for structures.
    [HarmonyPatch(typeof(Il2CppStructureHUD), nameof(Il2CppStructureHUD.Init))]
    private static class Patch_StructureHUD_Init
    {
        private static void Postfix(Il2CppStructureHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                UnitCustomizer.Apply(el, Config.StructureHUDScale);
                FontCustomizer.Apply(el, "StructureHUD");
                Register(el, "StructureHUD");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_StructureHUD_Init: {ex}"); }
        }
    }

    // ---------------------------------------------------------------------
    // Permanent tactical routing patches (Step 5).
    // These hooks route new tactical element types into the live registry and
    // FontCustomizer pipeline so hot-reload can re-apply consistently.
    // ---------------------------------------------------------------------

    [HarmonyPatch(typeof(Il2CppSkillBarButton), nameof(Il2CppSkillBarButton.Init))]
    private static class Patch_SkillBarButton_Init
    {
        private static void Postfix(Il2CppSkillBarButton __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "SkillBarButton");
                FontCustomizer.Apply(el, "SkillBarButton");
                Register(el, "SkillBarButton");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_SkillBarButton_Init: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppSkillBarButton), nameof(Il2CppSkillBarButton.Show))]
    private static class Patch_SkillBarButton_Show
    {
        private static void Postfix(Il2CppSkillBarButton __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "SkillBarButton");
                FontCustomizer.Apply(el, "SkillBarButton");
                Register(el, "SkillBarButton");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_SkillBarButton_Show: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppBaseSkillBarItemSlot), nameof(Il2CppBaseSkillBarItemSlot.Init))]
    private static class Patch_BaseSkillBarItemSlot_Init
    {
        private static void Postfix(Il2CppBaseSkillBarItemSlot __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "BaseSkillBarItemSlot");
                FontCustomizer.Apply(el, "BaseSkillBarItemSlot");
                Register(el, "BaseSkillBarItemSlot");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_BaseSkillBarItemSlot_Init: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppSimpleSkillBarButton), nameof(Il2CppSimpleSkillBarButton.SetText))]
    private static class Patch_SimpleSkillBarButton_SetText
    {
        private static void Postfix(Il2CppSimpleSkillBarButton __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "SimpleSkillBarButton");
                FontCustomizer.Apply(el, "SimpleSkillBarButton");
                Register(el, "SimpleSkillBarButton");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_SimpleSkillBarButton_SetText: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppTurnOrderFactionSlot), nameof(Il2CppTurnOrderFactionSlot.Init))]
    private static class Patch_TurnOrderFactionSlot_Init
    {
        private static void Postfix(Il2CppTurnOrderFactionSlot __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "TurnOrderFactionSlot");
                FontCustomizer.Apply(el, "TurnOrderFactionSlot");
                Register(el, "TurnOrderFactionSlot");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_TurnOrderFactionSlot_Init: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppUnitsTurnBarSlot), nameof(Il2CppUnitsTurnBarSlot.Init))]
    private static class Patch_UnitsTurnBarSlot_Init
    {
        private static void Postfix(Il2CppUnitsTurnBarSlot __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "UnitsTurnBarSlot");
                FontCustomizer.Apply(el, "UnitsTurnBarSlot");
                Register(el, "UnitsTurnBarSlot");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_UnitsTurnBarSlot_Init: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppUnitsTurnBarSlot), nameof(Il2CppUnitsTurnBarSlot.SetActor))]
    private static class Patch_UnitsTurnBarSlot_SetActor
    {
        private static void Postfix(Il2CppUnitsTurnBarSlot __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "UnitsTurnBarSlot");
                FontCustomizer.Apply(el, "UnitsTurnBarSlot");
                Register(el, "UnitsTurnBarSlot");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_UnitsTurnBarSlot_SetActor: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppSelectedUnitPanel), nameof(Il2CppSelectedUnitPanel.SetActor))]
    private static class Patch_SelectedUnitPanel_SetActor
    {
        private static void Postfix(Il2CppSelectedUnitPanel __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "SelectedUnitPanel");
                FontCustomizer.Apply(el, "SelectedUnitPanel");
                Register(el, "SelectedUnitPanel");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_SelectedUnitPanel_SetActor: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppTacticalUnitInfoStat), nameof(Il2CppTacticalUnitInfoStat.Init))]
    private static class Patch_TacticalUnitInfoStat_Init
    {
        private static void Postfix(Il2CppTacticalUnitInfoStat __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "TacticalUnitInfoStat");
                FontCustomizer.Apply(el, "TacticalUnitInfoStat");
                Register(el, "TacticalUnitInfoStat");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_TacticalUnitInfoStat_Init: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppTurnOrderPanel), nameof(Il2CppTurnOrderPanel.UpdateFactions))]
    private static class Patch_TurnOrderPanel_UpdateFactions
    {
        private static void Postfix(Il2CppTurnOrderPanel __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "TurnOrderPanel");
                FontCustomizer.Apply(el, "TurnOrderPanel");
                Register(el, "TurnOrderPanel");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_TurnOrderPanel_UpdateFactions: {ex}"); }
        }
    }

    [HarmonyPatch]
    private static class Patch_StatusEffectIcon_InitSkillTemplate
    {
        static MethodBase TargetMethod()
        {
            foreach (var m in typeof(Il2CppStatusEffectIcon).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "Init") continue;
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType.Name == "SkillTemplate")
                    return m;
            }
            throw new MissingMethodException("StatusEffectIcon.Init(SkillTemplate) not found.");
        }

        [HarmonyPostfix]
        private static void Postfix(Il2CppStatusEffectIcon __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "StatusEffectIcon");
                FontCustomizer.Apply(el, "StatusEffectIcon");
                Register(el, "StatusEffectIcon");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_StatusEffectIcon_InitSkillTemplate: {ex}"); }
        }
    }

    [HarmonyPatch]
    private static class Patch_StatusEffectIcon_InitSkill
    {
        static MethodBase TargetMethod()
        {
            foreach (var m in typeof(Il2CppStatusEffectIcon).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "Init") continue;
                var p = m.GetParameters();
                if (p.Length == 1 && p[0].ParameterType.Name == "Skill")
                    return m;
            }
            throw new MissingMethodException("StatusEffectIcon.Init(Skill) not found.");
        }

        [HarmonyPostfix]
        private static void Postfix(Il2CppStatusEffectIcon __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "StatusEffectIcon");
                FontCustomizer.Apply(el, "StatusEffectIcon");
                Register(el, "StatusEffectIcon");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_StatusEffectIcon_InitSkill: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppDelayedAbilityHUD), nameof(Il2CppDelayedAbilityHUD.SetAbility))]
    private static class Patch_DelayedAbilityHUD_SetAbility
    {
        private static void Postfix(Il2CppDelayedAbilityHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "DelayedAbilityHUD");
                FontCustomizer.Apply(el, "DelayedAbilityHUD");
                Register(el, "DelayedAbilityHUD");
                Scans.RunDelayedAbilityHUDMarkerScan(__instance, "SetAbility");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_DelayedAbilityHUD_SetAbility: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(Il2CppDelayedAbilityHUD), nameof(Il2CppDelayedAbilityHUD.SetProgressPct))]
    private static class Patch_DelayedAbilityHUD_SetProgressPct
    {
        private static void Postfix(Il2CppDelayedAbilityHUD __instance)
        {
            try
            {
                var el = __instance.Cast<Il2CppInterfaceElement>();
                TacticalElementCustomizer.Apply(el, "DelayedAbilityHUD");
                FontCustomizer.Apply(el, "DelayedAbilityHUD");
                Register(el, "DelayedAbilityHUD");
                Scans.RunDelayedAbilityHUDMarkerScan(__instance, "SetProgressPct");
            }
            catch (Exception ex) { Log.Error($"[HUDCustomizer] Patch_DelayedAbilityHUD_SetProgressPct: {ex}"); }
        }
    }

}
