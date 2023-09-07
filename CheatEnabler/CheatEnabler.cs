﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CheatEnabler;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class CheatEnabler : BaseUnityPlugin
{
    public new static readonly BepInEx.Logging.ManualLogSource Logger =
        BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);

    private static bool _configWinInitialized = false;
    private static KeyboardShortcut _shortcut = KeyboardShortcut.Deserialize("H + LeftControl");
    private static UIConfigWindow _configWin;
    private static bool _sitiVeinsOnBirthPlanet = true;
    private static bool _fireIceOnBirthPlanet = false;
    private static bool _kimberliteOnBirthPlanet = false;
    private static bool _fractalOnBirthPlanet = false;
    private static bool _organicOnBirthPlanet = true;
    private static bool _opticalOnBirthPlanet = false;
    private static bool _spiniformOnBirthPlanet = false;
    private static bool _unipolarOnBirthPlanet = false;
    private static bool _flatBirthPlanet = true;
    private static bool _highLuminosityBirthStar = true;
    private static bool _terraformAnyway = false;
    private static string _unlockTechToMaximumLevel = "";
    private static readonly List<int> TechToUnlock = new();
    
    private static Harmony _windowPatch;
    private static Harmony _patch;

    private static bool _initialized;

    private void Awake()
    {
        DevShortcuts.Enabled = Config.Bind("General", "DevShortcuts", true, "enable DevMode shortcuts");
        AbnormalDisabler.Enabled = Config.Bind("General", "DisableAbnormalChecks", false,
            "disable all abnormal checks");
        ResourcePatch.InfiniteEnabled = Config.Bind("Planet", "AlwaysInfiniteResource", false,
            "always infinite natural resource");
        ResourcePatch.FastEnabled = Config.Bind("Planet", "FastMining", false,
            "super-fast mining speed");
        _unlockTechToMaximumLevel = Config.Bind("General", "UnlockTechToMaxLevel", _unlockTechToMaximumLevel,
            "Unlock listed tech to MaxLevel").Value;
        WaterPumperPatch.Enabled = Config.Bind("Planet", "WaterPumpAnywhere", false,
            "Can pump water anywhere (while water type is not None)");
        _sitiVeinsOnBirthPlanet = Config.Bind("Birth", "SiTiVeinsOnBirthPlanet", _sitiVeinsOnBirthPlanet,
            "Has Silicon/Titanium veins on birth planet").Value;
        _fireIceOnBirthPlanet = Config.Bind("Birth", "FireIceOnBirthPlanet", _fireIceOnBirthPlanet,
            "Fire ice on birth planet (You should enable Rare Veins first)").Value;
        _kimberliteOnBirthPlanet = Config.Bind("Birth", "KimberliteOnBirthPlanet", _kimberliteOnBirthPlanet,
            "Kimberlite on birth planet (You should enable Rare Veins first)").Value;
        _fractalOnBirthPlanet = Config.Bind("Birth", "FractalOnBirthPlanet", _fractalOnBirthPlanet,
            "Fractal silicon on birth planet (You should enable Rare Veins first)").Value;
        _organicOnBirthPlanet = Config.Bind("Birth", "OrganicOnBirthPlanet", _organicOnBirthPlanet,
            "Organic crystal on birth planet (You should enable Rare Veins first)").Value;
        _opticalOnBirthPlanet = Config.Bind("Birth", "OpticalOnBirthPlanet", _opticalOnBirthPlanet,
            "Optical grating crystal on birth planet (You should enable Rare Veins first)").Value;
        _spiniformOnBirthPlanet = Config.Bind("Birth", "SpiniformOnBirthPlanet", _spiniformOnBirthPlanet,
            "Spiniform stalagmite crystal on birth planet (You should enable Rare Veins first)").Value;
        _unipolarOnBirthPlanet = Config.Bind("Birth", "UnipolarOnBirthPlanet", _unipolarOnBirthPlanet,
            "Unipolar magnet on birth planet (You should enable Rare Veins first)").Value;
        _flatBirthPlanet = Config.Bind("Birth", "FlatBirthPlanet", _flatBirthPlanet,
            "Birth planet is solid flat (no water)").Value;
        _highLuminosityBirthStar = Config.Bind("Birth", "HighLuminosityBirthStar", _highLuminosityBirthStar,
            "Birth star has high luminosity").Value;
        _terraformAnyway = Config.Bind("General", "TerraformAnyway", _terraformAnyway,
            "Can do terraform without enough sands").Value;

        I18N.Init();
        I18N.Add("CheatEnabler Config", "CheatEnabler Config", "CheatEnabler设置");
        I18N.Add("General", "General", "常规");
        I18N.Add("Enable Dev Shortcuts", "Enable Dev Shortcuts", "启用开发模式快捷键");
        I18N.Add("Disable Abnormal Checks", "Disable Abnormal Checks", "关闭数据异常检查");
        I18N.Add("Planet", "Planet", "行星");
        I18N.Add("Infinite Natural Resources", "Infinite Natural Resources", "自然资源采集不消耗");
        I18N.Add("Fast Mining", "Fast Mining", "高速采集");
        I18N.Add("Pump Anywhere", "Pump Anywhere", "平地抽水");

        // UI Patch
        _windowPatch = Harmony.CreateAndPatchAll(typeof(UI.MyWindowManager.Patch));
        _patch = Harmony.CreateAndPatchAll(typeof(CheatEnabler));

        DevShortcuts.Init();
        AbnormalDisabler.Init();
        ResourcePatch.Init();

        foreach (var idstr in _unlockTechToMaximumLevel.Split(','))
        {
            if (int.TryParse(idstr, out var id))
            {
                TechToUnlock.Add(id);
            }
        }

        if (TechToUnlock.Count > 0)
        {
            Harmony.CreateAndPatchAll(typeof(UnlockTechOnGameStart));
        }

        WaterPumperPatch.Init();

        if (_sitiVeinsOnBirthPlanet || _fireIceOnBirthPlanet || _kimberliteOnBirthPlanet || _fractalOnBirthPlanet ||
            _organicOnBirthPlanet || _opticalOnBirthPlanet || _spiniformOnBirthPlanet || _unipolarOnBirthPlanet ||
            _flatBirthPlanet || _highLuminosityBirthStar)
        {
            Harmony.CreateAndPatchAll(typeof(BirthPlanetCheat));
        }

        if (_terraformAnyway)
        {
            Harmony.CreateAndPatchAll(typeof(TerraformAnyway));
        }
    }

    public void OnDestroy()
    {
        WaterPumperPatch.Uninit();
        ResourcePatch.Uninit();
        AbnormalDisabler.Uninit();
        DevShortcuts.Uninit();
        _patch?.UnpatchSelf();
        _windowPatch?.UnpatchSelf();
    }

    private void Update()
    {
        if (VFInput.inputing)
        {
            return;
        }
        
        if (_shortcut.IsDown())
            ShowConfigWindow();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(UIRoot), "_OnOpen")]
    public static void UIRoot__OnOpen_Postfix()
    {
        if (_initialized) return;
        var mainMenu = UIRoot.instance.uiMainMenu;
        var src = mainMenu.newGameButton.gameObject;
        var parent = src.transform.parent;
        var btn = Instantiate(src, parent);
        btn.name = "btn-cheatenabler-config";
        var btnConfig = btn.GetComponent<UIMainMenuButton>();
        btnConfig.text.text = "CheatEnabler Config";
        btnConfig.text.fontSize = btnConfig.text.fontSize * 7 / 8;
        I18N.OnInitialized += () =>
        {
            btnConfig.text.text = "CheatEnabler Config".Translate();
        };
        btnConfig.transform.SetParent(parent);
        var vec = ((RectTransform)mainMenu.exitButton.transform).anchoredPosition3D;
        var vec2 = ((RectTransform)mainMenu.creditsButton.transform).anchoredPosition3D;
        var transform1 = (RectTransform)btn.transform;
        transform1.anchoredPosition3D = new Vector3(vec.x, vec.y + (vec.y - vec2.y) * 2, vec.z);
        btnConfig.button.onClick.AddListener(ShowConfigWindow);
        _initialized = true;
    }

    private static void ShowConfigWindow()
    {
        if (!_configWinInitialized)
        {
            _configWinInitialized = true;
            _configWin = UIConfigWindow.CreateInstance();
        }

        if (_configWin.active)
        {
            _configWin._Close();
        }
        else
        {
            UIRoot.instance.uiGame.ShutPlayerInventory();
            _configWin.Open();
        }
    }

    private class UnlockTechOnGameStart
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameScenarioLogic), "NotifyOnGameBegin")]
        private static void UnlockTechPatch()
        {
            var history = GameMain.history;
            if (GameMain.mainPlayer == null || GameMain.mainPlayer.mecha == null)
            {
                return;
            }

            foreach (var currentTech in TechToUnlock)
            {
                UnlockTechRecursive(history, currentTech, currentTech == 3606 ? 7000 : 10000);
            }

            var techQueue = history.techQueue;
            if (techQueue == null || techQueue.Length == 0)
            {
                return;
            }

            history.VarifyTechQueue();
            if (history.currentTech > 0 && history.currentTech != techQueue[0])
            {
                history.AlterCurrentTech(techQueue[0]);
            }
        }

        private static void UnlockTechRecursive(GameHistoryData history, int currentTech, int maxLevel = 10000)
        {
            var techStates = history.techStates;
            if (techStates == null || !techStates.ContainsKey(currentTech))
            {
                return;
            }

            var techProto = LDB.techs.Select(currentTech);
            if (techProto == null)
            {
                return;
            }

            var value = techStates[currentTech];
            var maxLvl = Math.Min(maxLevel, value.maxLevel);
            if (value.unlocked)
            {
                return;
            }

            foreach (var preid in techProto.PreTechs)
            {
                UnlockTechRecursive(history, preid, maxLevel);
            }

            var techQueue = history.techQueue;
            if (techQueue != null)
            {
                for (var i = 0; i < techQueue.Length; i++)
                {
                    if (techQueue[i] == currentTech)
                    {
                        techQueue[i] = 0;
                    }
                }
            }

            if (value.curLevel < techProto.Level) value.curLevel = techProto.Level;
            while (value.curLevel <= maxLvl)
            {
                for (var j = 0; j < techProto.UnlockFunctions.Length; j++)
                {
                    history.UnlockTechFunction(techProto.UnlockFunctions[j], techProto.UnlockValues[j], value.curLevel);
                }

                value.curLevel++;
            }

            value.unlocked = maxLvl >= value.maxLevel;
            value.curLevel = value.unlocked ? maxLvl : maxLvl + 1;
            value.hashNeeded = techProto.GetHashNeeded(value.curLevel);
            value.hashUploaded = value.unlocked ? value.hashNeeded : 0;
            techStates[currentTech] = value;
        }
    }

    private class BirthPlanetCheat
    {
        [HarmonyPostfix, HarmonyPatch(typeof(VFPreload), "InvokeOnLoadWorkEnded")]
        private static void VFPreload_InvokeOnLoadWorkEnded_Postfix()
        {
            var theme = LDB.themes.Select(1);
            if (_flatBirthPlanet)
            {
                theme.Algos[0] = 2;
            }

            if (_sitiVeinsOnBirthPlanet)
            {
                theme.VeinSpot[2] = 2;
                theme.VeinSpot[3] = 2;
                theme.VeinCount[2] = 0.7f;
                theme.VeinCount[3] = 0.7f;
                theme.VeinOpacity[2] = 1f;
                theme.VeinOpacity[3] = 1f;
            }

            List<int> veins = new();
            List<float> settings = new();
            if (_fireIceOnBirthPlanet)
            {
                veins.Add(8);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            if (_kimberliteOnBirthPlanet)
            {
                veins.Add(9);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            if (_fractalOnBirthPlanet)
            {
                veins.Add(10);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            if (_organicOnBirthPlanet)
            {
                veins.Add(11);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            if (_opticalOnBirthPlanet)
            {
                veins.Add(12);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            if (_spiniformOnBirthPlanet)
            {
                veins.Add(13);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            if (_unipolarOnBirthPlanet)
            {
                veins.Add(14);
                settings.AddRange(new[] { 1f, 1f, 0.5f, 1f });
            }

            theme.RareVeins = veins.ToArray();
            theme.RareSettings = settings.ToArray();
            if (_highLuminosityBirthStar)
            {
                StarGen.specifyBirthStarMass = 100f;
            }
        }
    }

    private class TerraformAnyway
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(BuildTool_Reform), "ReformAction")]
        private static IEnumerable<CodeInstruction> BuildTool_Reform_ReformAction_Patch(
            IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var instr = list[i];
                yield return instr;
                if (instr.opcode == OpCodes.Callvirt &&
                    instr.OperandIs(AccessTools.Method(typeof(Player), "get_sandCount")))
                {
                    /* ldloc.s 6 */
                    i++;
                    instr = list[i];
                    yield return instr;
                    /* sub */
                    i++;
                    instr = list[i];
                    yield return instr;
                    /* ldc.i4.0 */
                    yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                    /* call Math.Max() */
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Math), "Max", new[] { typeof(int), typeof(int) }));
                    /* stloc.s 21 */
                    i++;
                    instr = list[i];
                    yield return instr;
                    /* skip 3 instructions:
                     *  ldloc.s 21
                     *  ldc.i4.0
                     *  blt (633)
                     */
                    i += 3;
                }
            }
        }
    }
}