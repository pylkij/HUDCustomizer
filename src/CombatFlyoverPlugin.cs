using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
using Menace.ModpackLoader;
using Menace.SDK;
using Il2CppMenace.States;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI.Tactical;

// TacticalEventHooks.Initialize and Intercept.Initialize both fail on this build
// because they resolve types via gameAssembly.GetType("Menace.*") but the interop
// assembly exposes them as "Il2CppMenace.*". We bypass both and apply our own
// direct Harmony patches using typeof(Il2CppMenace.*) instead.

public class CombatFlyoverPlugin : IModpackPlugin
{
    private static MelonLogger.Instance Log;

    // Set to true to enable verbose per-event logging for development.
    // Set to false for normal use — only final result lines will be logged.
    private const bool DebugLogging = false;

    // HUD registry — populated by HudRegistryPatch on UITacticalHUD.AddActor.
    private static readonly Dictionary<IntPtr, UnitHUD> _hudRegistry = new();

    // Aggregation state — reset on each skill use.
    private static IntPtr _currentAttacker;
    private static float  _theoreticalAccuracy = -1f;
    private static readonly Dictionary<IntPtr, int>    _hpDamage     = new();
    private static readonly Dictionary<IntPtr, int>    _shotsHit     = new();
    private static readonly Dictionary<IntPtr, int>    _shotsFired   = new();
    private static readonly HashSet<IntPtr>            _armorChanged = new();

    // Armour tracking — snapshot taken lazily in OnDamageReceived on the first hit per
    // entity per skill window (confirmed to fire before OnArmorChanged). This means only
    // entities that are actually hit are snapshotted, eliminating the upfront full-map loop.
    // Delta at flush = snapshot - lastSeen = total armour durability lost this skill.
    private static readonly Dictionary<IntPtr, int> _armorSnapshot = new(); // entity → durability before skill
    private static readonly Dictionary<IntPtr, int> _armorLast     = new(); // entity → durability after last hit

    // Attacker HUD snapshot taken at OnSkillUsed — insulates flyover display from unit
    // death recycling the UnitHUD back to the pool before OnSkillCompleted fires.
    private static UnitHUD _attackerHUDSnapshot;

    // -------------------------------------------------------------------------
    // DropdownText duration extension
    //
    // Confirmed from Ghidra decompilation of UnitHUD.OnUpdate (VA 0x1807EECA0):
    //
    //   if (*(float *)(lVar6 + 0x4b0) != 0.0) {          // if StartTime already set:
    //       fVar8 = FUN_1829b1320(0);                     //   realtimeSinceStartup
    //       if (1.5 <= fVar8 - *(float *)(lVar6 + 0x4b0)) // if age >= 1.5s:
    //           FUN_182b4ee90(param_1, lVar6, 0);         //   remove element
    //       return;
    //   }
    //   uVar9 = FUN_1829b1320(0);                         // else: first tick —
    //   *(unsigned4 *)(lVar6 + 0x4b0) = uVar9;           //   write StartTime = now
    //   ... kick off fade animation (0x5dc = 1500ms) ...
    //
    // StartTime (offset 0x4B0) is written by OnUpdate on the element's FIRST tick,
    // not by Init. The expiry check is: realtimeSinceStartup - StartTime >= 1.5.
    // FUN_1829b1320 is Time.realtimeSinceStartup (not Time.time).
    //
    // Strategy: after OnUpdate runs, find any DropdownText in m_QueuedTexts
    // (Queue<DropdownText> at offset 0x578) whose StartTime was just written this
    // frame (i.e. non-zero and not yet in our extended set). Add ExtraDisplaySeconds
    // to it. Track extended pointers so we only adjust each element once.
    // -------------------------------------------------------------------------

    // How many additional seconds to display each DropdownText beyond the game's
    // default 1.5s. Controlled by HUDCustomizer.json CombatFlyover.ExtraDisplaySeconds.
    //
    // Multiplier applied to the DropdownText fade animation duration.
    // The game uses 1500ms (0x5dc). Controlled by HUDCustomizer.json CombatFlyover.FadeDurationScale.
    // Total visible time per element = (1500ms * FadeDurationScale) + (ExtraDisplaySeconds * 1000ms)
    // At defaults: (1500 * 2.0) + (2000) = 5000ms total, 3000ms of which is the fade.
    //
    // Colour values for flyover text. Unity Rich Text <color> tags.
    // Controlled by HUDCustomizer.json CombatFlyover.Colour* fields.
    // Accepted formats: #RRGGBB, #RRGGBBAA, or a limited set of named colours.
    // See CombatFlyoverSettings in HUDConfig.cs for the full constraint list.



    // Pointers of DropdownText instances whose StartTime we have already extended.
    // Cleared in OnSceneLoaded to prevent unbounded growth across tactical sessions.
    private static readonly HashSet<IntPtr> _extendedDropdowns = new();

    // Pointers of live DropdownText instances (which ARE VisualElements — DropdownText
    // extends InterfaceElement which extends VisualElement directly). Populated in
    // DropdownTextInitPatch, used to scope ValueAnimationDurationPatch to only
    // DropdownText animations. Cleared in OnSceneLoaded.
    private static readonly HashSet<IntPtr> _activeDropdownElements = new();

    // -------------------------------------------------------------------------
    // Memory offsets — update these from dump.cs after a game update.
    // Run: find_type.ps1 -Dump .\dump.cs -Search "UnitHUD"
    //      find_type.ps1 -Dump .\dump.cs -Search "DropdownText"
    // and read the [FieldOffset] values for the fields below.
    // -------------------------------------------------------------------------

    // DropdownText fields
    private const int Offset_DropdownText_StartTime    = 0x4B0; // DropdownText.StartTime

    // UnitHUD fields
    private const int Offset_UnitHUD_QueuedTexts       = 0x578; // UnitHUD.m_QueuedTexts (Queue<DropdownText>)

    // -------------------------------------------------------------------------
    // IModpackPlugin
    // -------------------------------------------------------------------------

    public void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
    {
        Log = logger;
        Log.Msg("[CombatFlyover] OnInitialize started.");

        harmony.PatchAll(typeof(HudRegistryPatch));
        harmony.PatchAll(typeof(SkillUsePatch));
        harmony.PatchAll(typeof(AfterSkillUsePatch));
        harmony.PatchAll(typeof(DamageReceivedPatch));
        harmony.PatchAll(typeof(AttackMissedPatch));
        harmony.PatchAll(typeof(ArmorChangedPatch));
        harmony.PatchAll(typeof(GetHitchancePatch));
        harmony.PatchAll(typeof(DropdownTextInitPatch));
        harmony.PatchAll(typeof(ValueAnimationDurationPatch));
        harmony.PatchAll(typeof(UnitHudOnUpdatePatch));
        Log.Msg("[CombatFlyover] All patches applied.");

        Log.Msg("[CombatFlyover] OnInitialize complete.");
    }

    public void OnSceneLoaded(int buildIndex, string sceneName)
    {
        Log.Msg($"[CombatFlyover] OnSceneLoaded: scene='{sceneName}' index={buildIndex}. Clearing HUD registry ({_hudRegistry.Count} entries).");
        _hudRegistry.Clear();
        _extendedDropdowns.Clear();
        _activeDropdownElements.Clear();
    }

    public void OnUpdate() { }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void ShowFlyover(UnitHUD hud, string text)
    {
        try
        {
            if (DebugLogging) Log.Msg($"[CombatFlyover] ShowFlyover: '{text}'");
            hud.ShowDropDownText(text, null, false);
        }
        catch (Exception ex) { Log.Warning($"[CombatFlyover] ShowDropDownText threw: {ex.Message}"); }
    }

    // Wraps text in a Unity UI Toolkit rich text colour tag.
    // If ShowDropDownText does not honour rich text, the tags will render as
    // literal characters — remove wrapping and use the UnitHUD label colour
    // approach instead (requires patching DropdownText directly).
    private static string Coloured(string text, string hex) => $"<color={hex}>{text}</color>";

    // Returns a human-readable label for an entity, falling back to the pointer if unavailable.
    private static string EntityName(IntPtr entityPtr)
    {
        try
        {
            var entity = new Il2CppMenace.Tactical.Entity(entityPtr);
            var name = entity.DebugName;
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }
        return $"0x{entityPtr:X}";
    }

    // -------------------------------------------------------------------------
    // Combat event handlers — called from Harmony postfixes below
    // -------------------------------------------------------------------------

    internal static void OnSkillUsed(IntPtr user, IntPtr skill, IntPtr target)
    {
        if (!CombatFlyoverCustomizer.IsEnabled()) return;

        _currentAttacker = user;
        _hpDamage.Clear();
        _shotsHit.Clear();
        _shotsFired.Clear();
        _armorChanged.Clear();
        _armorSnapshot.Clear();
        _armorLast.Clear();

        // Snapshot HUD references now while all units are alive.
        // InvokeOnAfterSkillUse can fire after a unit's HUD has been recycled.
        _attackerHUDSnapshot = null;
        _hudRegistry.TryGetValue(user, out _attackerHUDSnapshot);

        // Show theoretical accuracy immediately on skill commitment, before shots land.
        // _theoreticalAccuracy holds the value from GetHitchance during targeting and
        // has not been reset yet — it belongs to this skill use.
        if (_attackerHUDSnapshot != null && _theoreticalAccuracy >= 0f)
        {
            int theoPct = _theoreticalAccuracy > 1f
                ? (int)Math.Round(_theoreticalAccuracy)
                : (int)Math.Round(100.0 * _theoreticalAccuracy);
            ShowFlyover(_attackerHUDSnapshot, Coloured($"<i>~{theoPct}%</i>", CombatFlyoverCustomizer.ColourAcc()));
            if (DebugLogging) Log.Msg($"[CombatFlyover] OnSkillUsed: showing theoretical accuracy ~{theoPct}% immediately.");
        }

        if (DebugLogging) Log.Msg($"[CombatFlyover] OnSkillUsed: user=0x{user:X} skill=0x{skill:X} target=0x{target:X}. Registry has {_hudRegistry.Count} actors.");
    }

    internal static void OnDamageReceived(IntPtr target, IntPtr attacker, IntPtr skill, int damage)
    {
        // Accumulate absolute HP damage from DamageInfo.Damage.
        if (damage > 0)
            _hpDamage[target] = _hpDamage.GetValueOrDefault(target, 0) + damage;

        _shotsHit[target]   = _shotsHit.GetValueOrDefault(target, 0) + 1;
        _shotsFired[target] = _shotsFired.GetValueOrDefault(target, 0) + 1;

        // Lazy armour snapshot: OnDamageReceived fires before OnArmorChanged (confirmed
        // from log event ordering), so GetArmorDurability() here returns the pre-hit value.
        // Only snapshot once per entity per skill window — the first hit gives the baseline.
        if (!_armorSnapshot.ContainsKey(target))
        {
            try
            {
                var entity = new Il2CppMenace.Tactical.Entity(target);
                _armorSnapshot[target] = entity.GetArmorDurability();
                if (DebugLogging) Log.Msg($"[CombatFlyover] OnDamageReceived: lazy armour snapshot for 0x{target:X} = {_armorSnapshot[target]}.");
            }
            catch (Exception ex)
            {
                Log.Msg($"[CombatFlyover] OnDamageReceived: armour snapshot failed for 0x{target:X}: {ex.Message}");
            }
        }

        if (DebugLogging) Log.Msg($"[CombatFlyover] OnDamageReceived: target=0x{target:X} damage={damage} shotsHit={_shotsHit[target]} shotsFired={_shotsFired[target]}.");
    }

    internal static void OnAttackMissed(IntPtr attacker, IntPtr target)
    {
        _shotsFired[target] = _shotsFired.GetValueOrDefault(target, 0) + 1;
        if (DebugLogging) Log.Msg($"[CombatFlyover] OnAttackMissed: attacker=0x{attacker:X} target=0x{target:X} shotsFired={_shotsFired[target]}.");
    }

    internal static void OnArmorChanged(IntPtr actor, int currentArmor)
    {
        // Snapshot is taken in OnDamageReceived (confirmed to fire before OnArmorChanged).
        // If no snapshot exists, this entity received no tracked damage event — skip it.
        if (!_armorSnapshot.ContainsKey(actor))
        {
            if (DebugLogging) Log.Msg($"[CombatFlyover] OnArmorChanged: actor=0x{actor:X} skipped — no damage-event snapshot.");
            return;
        }
        _armorLast[actor] = currentArmor;
        _armorChanged.Add(actor);
        if (DebugLogging) Log.Msg($"[CombatFlyover] OnArmorChanged: actor=0x{actor:X} currentDurability={currentArmor} snapshot={_armorSnapshot[actor]}.");
    }

    internal static void OnSkillCompleted(IntPtr skill)
    {
        if (DebugLogging) Log.Msg($"[CombatFlyover] OnSkillCompleted: skill=0x{skill:X}. Targets with HP data: {_hpDamage.Count}. Attacker: 0x{_currentAttacker:X}. Registry: {_hudRegistry.Count} actors.");
        try
        {
            if (!CombatFlyoverCustomizer.IsEnabled()) return; // finally still clears accumulators

            // All flyovers display on the attacker's HUD regardless of which unit the
            // stat relates to. This keeps all information in one place and avoids the
            // player having to track multiple elements across the battlefield.
            var attackerHUD = _attackerHUDSnapshot;
            if (DebugLogging) Log.Msg($"[CombatFlyover]   Attacker 0x{_currentAttacker:X}: hudFound={attackerHUD != null}.");
            if (attackerHUD == null) return;

            // --- Per-target flyovers (routed to attacker HUD) ---
            // Union of HP-damage targets and armour-changed targets so that pure-armour
            // hits (damage=0 every shot) are not silently skipped.
            var allTargets = new HashSet<IntPtr>(_hpDamage.Keys);
            allTargets.UnionWith(_armorChanged);
            foreach (var target in allTargets)
            {
                int hp    = _hpDamage.GetValueOrDefault(target, 0);
                int hit   = _shotsHit.GetValueOrDefault(target, 0);
                int fired = _shotsFired.GetValueOrDefault(target, 0);

                if (DebugLogging) Log.Msg($"[CombatFlyover]   Target 0x{target:X}: hp={hp} hit={hit} fired={fired}.");

                if (hp > 0)
                {
                    Log.Msg($"[CombatFlyover]   {EntityName(target)}: hp={hp}.");
                    ShowFlyover(attackerHUD, Coloured($"<b>-{hp} HP</b>", CombatFlyoverCustomizer.ColourHP()));
                }

                // Armour damage — delta between pre-skill snapshot and post-last-hit value.
                // Both GetArmorDurability() (snapshot) and _armor (event param) are absolute
                // integer durability totals across ALL elements, confirmed from Entity.cs fields
                // m_ArmorDurability / m_ArmorDurabilityMax and InvokeOnArmorChanged dump signature.
                // e.g. 6 elements x 40 per element = 240 total. After 5 elements are
                // stripped: 1 x 40 = 40 remaining, delta = 200.
                // The game's tooltip shows per-element armour (40), so we normalise the
                // delta by element count to display on the same scale the player knows.
                // e.g. 200 total delta / 6 elements = ~33 per element displayed as "-33 ARM".
                if (_armorSnapshot.TryGetValue(target, out int armorBefore) &&
                    _armorLast.TryGetValue(target, out int armorAfter))
                {
                    int armorDelta = armorBefore - armorAfter;
                    if (armorDelta > 0)
                    {
                        int elementCount = 1;
                        try
                        {
                            var entity = new Il2CppMenace.Tactical.Entity(target);
                            int raw = entity.GetOriginalElementCount();
                            if (raw > 0) elementCount = raw;
                        }
                        catch (Exception ex)
                        {
                            Log.Msg($"[CombatFlyover]   Target 0x{target:X}: GetOriginalElementCount failed: {ex.Message} — using 1.");
                        }

                        int normalisedDelta = (int)Math.Round((double)armorDelta / elementCount);
                        Log.Msg($"[CombatFlyover]   {EntityName(target)}: armorDelta={armorDelta} elementCount={elementCount} normalisedDelta={normalisedDelta} (before={armorBefore} after={armorAfter}).");
                        ShowFlyover(attackerHUD, Coloured($"<b>-{normalisedDelta} ARM</b>", CombatFlyoverCustomizer.ColourARM()));
                    }
                    else
                    {
                        if (DebugLogging) Log.Msg($"[CombatFlyover]   Target 0x{target:X}: armorDelta={armorDelta} (before={armorBefore} after={armorAfter}) — no ARM flyover.");
                    }
                }
                else if (_armorSnapshot.TryGetValue(target, out int snapOnly))
                {
                    if (DebugLogging) Log.Msg($"[CombatFlyover]   Target 0x{target:X}: snapshot={snapOnly} but InvokeOnArmorChanged never fired — no ARM flyover.");
                }
            }

            // --- Attacker flyovers ---
            // Theoretical accuracy was already shown in OnSkillUsed at action commit time.
            // Actual accuracy (X%) is derived from total hits/fired across all targets.
            // The shots ratio (hit/fired) is omitted — actual accuracy covers the same
            // information more concisely.
            int totalHit   = 0;
            int totalFired = 0;
            foreach (var kv in _shotsHit)   totalHit   += kv.Value;
            foreach (var kv in _shotsFired) totalFired += kv.Value;

            Log.Msg($"[CombatFlyover]   Actual accuracy: totalHit={totalHit} totalFired={totalFired}.");
            if (totalFired > 0)
            {
                int actualPct = (int)Math.Round(100.0 * totalHit / totalFired);
                ShowFlyover(attackerHUD, Coloured($"<b>{actualPct}%</b>", CombatFlyoverCustomizer.ColourAcc()));
            }
        }
        catch (Exception ex) { Log.Warning($"[CombatFlyover] OnSkillCompleted exception: {ex.Message}"); }
        finally
        {
            _hpDamage.Clear();
            _shotsHit.Clear();
            _shotsFired.Clear();
            _armorChanged.Clear();
            _armorSnapshot.Clear();
            _armorLast.Clear();
            _attackerHUDSnapshot = null;
            _theoreticalAccuracy = -1f;
            _currentAttacker = IntPtr.Zero;
        }
    }

    internal static void OnGetHitChance(float finalChance)
    {
        // Always overwrite — GetHitchance is called repeatedly during targeting as the
        // player moves the cursor. The last value seen before InvokeOnSkillUse is the
        // one displayed to the player when they confirmed the action, which is what we want.
        if (DebugLogging) Log.Msg($"[CombatFlyover] OnGetHitChance: FinalChance={finalChance} (previous={_theoreticalAccuracy}).");
        _theoreticalAccuracy = finalChance;
    }

    // -------------------------------------------------------------------------
    // Harmony patches — direct on Il2CppMenace types, no SDK Initialize needed
    // -------------------------------------------------------------------------

    // UITacticalHUD.AddActor — registers UnitHUD per actor pointer.
    [HarmonyPatch(typeof(Il2CppMenace.UI.UITacticalHUD), nameof(Il2CppMenace.UI.UITacticalHUD.AddActor))]
    private static class HudRegistryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Actor _actor, UnitHUD __result)
        {
            try
            {
                if (_actor == null || __result == null)
                {
                    if (DebugLogging) Log?.Msg($"[CombatFlyover] HudRegistryPatch: skipped — actor null={_actor == null} result null={__result == null}.");
                    return;
                }
                _hudRegistry[_actor.Pointer] = __result;
                if (DebugLogging) Log?.Msg($"[CombatFlyover] HudRegistryPatch: registered actor=0x{_actor.Pointer:X}. Registry now has {_hudRegistry.Count} actors.");
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] HudRegistryPatch exception: {ex.Message}"); }
        }
    }

    // TacticalManager.InvokeOnSkillUse — skill window open.
    [HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager), nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnSkillUse))]
    private static class SkillUsePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Actor _actor, Skill _skill, Tile _targetTile)
        {
            try
            {
                var user  = _actor != null ? _actor.Pointer : IntPtr.Zero;
                var skill = _skill != null ? _skill.Pointer : IntPtr.Zero;
                var tile  = _targetTile != null ? _targetTile.Pointer : IntPtr.Zero;
                OnSkillUsed(user, skill, tile);
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] SkillUsePatch exception: {ex.Message}"); }
        }
    }

    // TacticalManager.InvokeOnAfterSkillUse — skill window close, flush flyovers.
    [HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager), nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnAfterSkillUse))]
    private static class AfterSkillUsePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Skill _skill)
        {
            try
            {
                var skill = _skill != null ? _skill.Pointer : IntPtr.Zero;
                OnSkillCompleted(skill);
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] AfterSkillUsePatch exception: {ex.Message}"); }
        }
    }

    // TacticalManager.InvokeOnDamageReceived — shot hit, accumulate damage.
    [HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager), nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnDamageReceived))]
    private static class DamageReceivedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Entity _entity, Entity _attacker, Skill _skill, DamageInfo _damageInfo)
        {
            try
            {
                var target   = _entity   != null ? _entity.Pointer   : IntPtr.Zero;
                var attacker = _attacker != null ? _attacker.Pointer  : IntPtr.Zero;
                var skill    = _skill    != null ? _skill.Pointer     : IntPtr.Zero;
                var damage   = _damageInfo != null ? _damageInfo.Damage : 0;
                OnDamageReceived(target, attacker, skill, damage);
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] DamageReceivedPatch exception: {ex.Message}"); }
        }
    }

    // TacticalManager.InvokeOnAttackMissed — shot missed, count fired only.
    [HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager), nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnAttackMissed))]
    private static class AttackMissedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Entity _entity, Actor _attacker, Skill _skill)
        {
            try
            {
                var target   = _entity   != null ? _entity.Pointer   : IntPtr.Zero;
                var attacker = _attacker != null ? _attacker.Pointer  : IntPtr.Zero;
                OnAttackMissed(attacker, target);
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] AttackMissedPatch exception: {ex.Message}"); }
        }
    }

    // TacticalManager.InvokeOnArmorChanged — accumulate armour delta per target.
    // _armor is the current integer armour value post-hit. We track first and last
    // values seen per entity per skill window; delta = first - last at flush time.
    [HarmonyPatch(typeof(Il2CppMenace.Tactical.TacticalManager), nameof(Il2CppMenace.Tactical.TacticalManager.InvokeOnArmorChanged))]
    private static class ArmorChangedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Entity _entity, float _armorDurability, int _armor, int _animationDurationInMs)
        {
            try
            {
                var actor = _entity != null ? _entity.Pointer : IntPtr.Zero;
                if (actor != IntPtr.Zero) OnArmorChanged(actor, _armor);
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] ArmorChangedPatch exception: {ex.Message}"); }
        }
    }

    // Skill.GetHitchance — capture theoretical accuracy.
    //
    // HitChance is a VALUE TYPE (struct) — IsValueType=True, not an Il2CppObjectBase.
    // Harmony boxes it as `object __result`. Reading via Il2CppObjectBase always fails.
    // Must use reflection to read the field from the boxed struct.
    //
    // Confirmed field layout from dump (field name is FinalValue, not FinalChance):
    //   Single FinalValue    — final hit chance on 0–100 scale
    //   Single Accuracy
    //   Single CoverMult
    //   Single DefenseMult
    //   Boolean AlwaysHits
    //   Boolean IncludeDropoff
    //   Single AccuracyDropoff
    //
    // FinalValue is on the 0–100 scale. The existing >1f scale-detection in OnSkillCompleted
    // handles this correctly (rounds directly rather than multiplying by 100).
    [HarmonyPatch(typeof(Il2CppMenace.Tactical.Skills.Skill), nameof(Il2CppMenace.Tactical.Skills.Skill.GetHitchance))]
    private static class GetHitchancePatch
    {
        private static System.Reflection.FieldInfo _finalValueField;

        [HarmonyPostfix]
        private static void Postfix(object __result)
        {
            try
            {
                if (__result == null) return;

                // Cache the FieldInfo on first call.
                if (_finalValueField == null)
                    _finalValueField = __result.GetType().GetField("FinalValue",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                if (_finalValueField == null)
                {
                    Log?.Warning("[CombatFlyover] GetHitchancePatch: FinalValue field not found on HitChance struct.");
                    return;
                }

                float finalValue = (float)_finalValueField.GetValue(__result);
                if (DebugLogging) Log?.Msg($"[CombatFlyover] GetHitchancePatch: FinalValue={finalValue}");
                OnGetHitChance(finalValue);
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] GetHitchancePatch exception: {ex.Message}"); }
        }
    }

    // DropdownText.Init — register this DropdownText pointer in _activeDropdownElements.
    // DropdownText extends InterfaceElement which extends VisualElement, so the
    // DropdownText pointer IS the VisualElement pointer. ValueAnimation<float>.owner
    // will hold this same pointer when the fade animation is started in UnitHUD.OnUpdate.
    [HarmonyPatch(typeof(Il2CppMenace.UI.Tactical.DropdownText),
        nameof(Il2CppMenace.UI.Tactical.DropdownText.Init))]
    private static class DropdownTextInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppMenace.UI.Tactical.DropdownText __instance)
        {
            try
            {
                _activeDropdownElements.Add(__instance.Pointer);
                if (DebugLogging) Log?.Msg($"[CombatFlyover] DropdownTextInitPatch: registered 0x{__instance.Pointer:X}");
            }
            catch (Exception ex)
            {
                Log?.Warning($"[CombatFlyover] DropdownTextInitPatch exception: {ex.Message}");
            }
        }
    }

    // ValueAnimation<float>.set_durationMs — scale fade duration for DropdownText animations.
    //
    // The game calls set_durationMs(1500) when starting the DropdownText fade in
    // UnitHUD.OnUpdate. We intercept and multiply by FadeDurationScale, but only when
    // the animation's owner is a known DropdownText instance. This leaves all other
    // ValueAnimation<float> usage (e.g. MissionResultUIScreen) completely unaffected.
    //
    // DropdownText IS a VisualElement (via InterfaceElement), so the owner pointer
    // will equal the DropdownText.Pointer registered in DropdownTextInitPatch.
    //
    // The type is resolved at runtime via reflection because the Il2Cpp interop assembly
    // does not expose ValueAnimation<float> under a predictable compile-time name.
    // TargetMethod() locates the setter once at patch application time.
    [HarmonyPatch]
    private static class ValueAnimationDurationPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var type = typeof(UnityEngine.UIElements.Experimental.ITransitionAnimations)
                .Assembly
                .GetType("UnityEngine.UIElements.Experimental.ValueAnimation`1")
                ?.MakeGenericType(typeof(float));

            if (type == null)
            {
                Log?.Warning("[CombatFlyover] ValueAnimationDurationPatch: could not find ValueAnimation<float> type — patch not applied.");
                return null;
            }

            var method = type.GetProperty("durationMs")?.GetSetMethod();
            if (method == null)
                Log?.Warning("[CombatFlyover] ValueAnimationDurationPatch: could not find set_durationMs — patch not applied.");
            return method;
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance, ref int value)
        {
            try
            {
                if (!CombatFlyoverCustomizer.IsEnabled()) return;
                if (CombatFlyoverCustomizer.FadeDurationScale() <= 1f) return;

                // Read the owner property via reflection to get the VisualElement pointer.
                var ownerProp = __instance.GetType().GetProperty("owner",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (ownerProp == null) return;

                var owner = ownerProp.GetValue(__instance) as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (owner == null) return;
                if (!_activeDropdownElements.Contains(owner.Pointer)) return;

                int scaled = (int)(value * CombatFlyoverCustomizer.FadeDurationScale());
                if (DebugLogging) Log?.Msg($"[CombatFlyover] ValueAnimationDurationPatch: scaling durationMs {value} → {scaled} for 0x{owner.Pointer:X}");
                value = scaled;
            }
            catch (Exception ex)
            {
                Log?.Warning($"[CombatFlyover] ValueAnimationDurationPatch exception: {ex.Message}");
            }
        }
    }

    // UnitHUD.OnUpdate — extend DropdownText display duration.
    //
    // The game's expiry logic (confirmed from Ghidra, VA 0x1807EECA0):
    //   First tick:  StartTime == 0.0  → write StartTime = Time.realtimeSinceStartup
    //   Later ticks: StartTime != 0.0  → if (realtimeSinceStartup - StartTime) >= 1.5, remove
    //
    // Strategy: walk the full queue each OnUpdate. For any element whose StartTime was
    // just written this frame (non-zero, not yet in _extendedDropdowns), add
    // ExtraDisplaySeconds to it. This makes each element display for
    // (1.5 + ExtraDisplaySeconds) seconds before expiring.
    //
    // Inter-element timing: the queue drains one element at a time. The next element
    // dequeues only after the current one is removed, so elements appear sequentially
    // with (1.5 + ExtraDisplaySeconds) between them. This is the correct behaviour
    // for the goal of longer per-element visibility — each flyover is fully readable
    // before the next appears.
    //
    // Queue<T> internal layout (Il2Cpp / .NET):
    //   +0x10  T[] _array   (pointer to backing array object)
    //   +0x18  int _head    (index of next element to dequeue)
    //   +0x1C  int _tail    (index where next enqueue writes)
    //   +0x20  int _size    (number of live elements)
    //
    // Backing array (Il2Cpp array object):
    //   +0x18  int length   (capacity)
    //   +0x20  first element (8 bytes per pointer, x64)
    //
    // Valid elements: indices [_head .. (_head + _size - 1)] % capacity.
    [HarmonyPatch(typeof(Il2CppMenace.UI.Tactical.UnitHUD),
        nameof(Il2CppMenace.UI.Tactical.UnitHUD.OnUpdate))]
    private static class UnitHudOnUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Il2CppMenace.UI.Tactical.UnitHUD __instance)
        {
            try
            {
                if (!CombatFlyoverCustomizer.IsEnabled()) return;
                if (CombatFlyoverCustomizer.ExtraDisplaySeconds() <= 0f) return;

                var queuePtr = Marshal.ReadIntPtr(__instance.Pointer + Offset_UnitHUD_QueuedTexts);
                if (queuePtr == IntPtr.Zero) return;

                var arrayPtr = Marshal.ReadIntPtr(queuePtr + 0x10);
                if (arrayPtr == IntPtr.Zero) return;

                int head     = Marshal.ReadInt32(queuePtr + 0x18);
                int count    = Marshal.ReadInt32(queuePtr + 0x20);
                int capacity = Marshal.ReadInt32(arrayPtr + 0x18);
                if (count <= 0 || capacity <= 0) return;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        int index = (head + i) % capacity;
                        var elemPtr = Marshal.ReadIntPtr(arrayPtr + 0x20 + index * 8);
                        if (elemPtr == IntPtr.Zero) continue;
                        if (_extendedDropdowns.Contains(elemPtr)) continue;

                        float startTime = Marshal.PtrToStructure<float>(elemPtr + Offset_DropdownText_StartTime);
                        if (startTime == 0f) continue; // not yet activated, skip

                        float extended = startTime + CombatFlyoverCustomizer.ExtraDisplaySeconds();
                        Marshal.StructureToPtr(extended, elemPtr + Offset_DropdownText_StartTime, false);
                        _extendedDropdowns.Add(elemPtr);

                        if (DebugLogging) Log?.Msg($"[CombatFlyover] UnitHudOnUpdatePatch: extended DropdownText 0x{elemPtr:X} StartTime {startTime:F3} → {extended:F3}");
                    }
                    catch (Exception ex)
                    {
                        Log?.Warning($"[CombatFlyover] UnitHudOnUpdatePatch: element[{i}] threw: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) { Log?.Warning($"[CombatFlyover] UnitHudOnUpdatePatch exception: {ex.Message}"); }
        }
    }
}
