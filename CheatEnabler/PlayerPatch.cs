﻿using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace CheatEnabler;

public static class PlayerPatch
{
    public static ConfigEntry<bool> InstantTeleportEnabled;
    public static ConfigEntry<bool> WarpWithoutSpaceWarpersEnabled;

    public static void Init()
    {
        InstantTeleportEnabled.SettingChanged += (_, _) => InstantTeleport.Enable(InstantTeleportEnabled.Value);
        WarpWithoutSpaceWarpersEnabled.SettingChanged += (_, _) => WarpWithoutSpaceWarpers.Enable(WarpWithoutSpaceWarpersEnabled.Value);
        InstantTeleport.Enable(InstantTeleportEnabled.Value);
        WarpWithoutSpaceWarpers.Enable(WarpWithoutSpaceWarpersEnabled.Value);
    }

    public static void Uninit()
    {
        InstantTeleport.Enable(false);
        WarpWithoutSpaceWarpers.Enable(false);
    }

    private static class InstantTeleport
    {
        private static Harmony _patch;

        public static void Enable(bool on)
        {
            if (on)
            {
                _patch ??= Harmony.CreateAndPatchAll(typeof(InstantTeleport));
            }
            else
            {
                _patch?.UnpatchSelf();
                _patch = null;
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(UIGlobemap), nameof(UIGlobemap._OnUpdate))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.DoRightClickFastTravel))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.OnFastTravelButtonClick))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.OnScreenClick))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.SandboxRightClickFastTravelLogic))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.StartFastTravelToPlanet))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.StartFastTravelToUPosition))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap.UpdateCursorView))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIStarmap._OnUpdate))]
        private static IEnumerable<CodeInstruction> UIGlobemap__OnUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var matcher = new CodeMatcher(instructions, generator);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(GameMain), nameof(GameMain.sandboxToolsEnabled)))
            );
            matcher.Repeat(cm => cm.SetAndAdvance(OpCodes.Ldc_I4_1, null));
            return matcher.InstructionEnumeration();
        }
    }

    private static class WarpWithoutSpaceWarpers
    {
        private static Harmony _patch;

        public static void Enable(bool on)
        {
            if (on)
            {
                _patch ??= Harmony.CreateAndPatchAll(typeof(WarpWithoutSpaceWarpers));
            }
            else
            {
                _patch?.UnpatchSelf();
                _patch = null;
            }
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mecha), nameof(Mecha.HasWarper))]
        private static bool Mecha_HasWarper_Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Mecha), nameof(Mecha.UseWarper))]
        private static void Mecha_UseWarper_Postfix(ref bool __result)
        {
            __result = true;
        }
    }
}
