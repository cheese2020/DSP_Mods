﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UXAssist.Common;

namespace UXAssist;

public static class LogisticsPatch
{
    public static ConfigEntry<bool> LogisticsCapacityTweaksEnabled;
    public static ConfigEntry<bool> AllowOverflowInLogisticsEnabled;
    public static ConfigEntry<bool> LogisticsConstrolPanelImprovementEnabled;

    public static void Init()
    {
        LogisticsCapacityTweaksEnabled.SettingChanged += (_, _) => LogisticsCapacityTweaks.Enable(LogisticsCapacityTweaksEnabled.Value);
        AllowOverflowInLogisticsEnabled.SettingChanged += (_, _) => AllowOverflowInLogistics.Enable(AllowOverflowInLogisticsEnabled.Value);
        LogisticsConstrolPanelImprovementEnabled.SettingChanged += (_, _) => LogisticsConstrolPanelImprovement.Enable(LogisticsConstrolPanelImprovementEnabled.Value);
        LogisticsCapacityTweaks.Enable(LogisticsCapacityTweaksEnabled.Value);
        AllowOverflowInLogistics.Enable(AllowOverflowInLogisticsEnabled.Value);
        LogisticsConstrolPanelImprovement.Enable(LogisticsConstrolPanelImprovementEnabled.Value);
    }

    public static void Uninit()
    {
        LogisticsCapacityTweaks.Enable(false);
        AllowOverflowInLogistics.Enable(false);
        LogisticsConstrolPanelImprovement.Enable(false);
    }

    public static void Start()
    {
        LogisticsInfoWindow.InitGUI();
    }

    public static void OnUpdate()
    {
        LogisticsInfoWindow.StationInfoWindowUpdate();
    }

    public static class LogisticsCapacityTweaks
    {
        private static Harmony _patch;

        public static void Enable(bool enable)
        {
            if (enable)
            {
                _patch ??= Harmony.CreateAndPatchAll(typeof(LogisticsCapacityTweaks));
                return;
            }

            _patch?.UnpatchSelf();
            _patch = null;
        }

        private static KeyCode _lastKey = KeyCode.None;
        private static long _nextKeyTick;
        private static bool _skipNextEvent;

        private static bool UpdateKeyPressed(KeyCode code)
        {
            if (!Input.GetKey(code))
                return false;
            if (code != _lastKey)
            {
                _lastKey = code;
                _nextKeyTick = GameMain.instance.timei + 30;
                return true;
            }

            var currTick = GameMain.instance.timei;
            if (_nextKeyTick > currTick) return false;
            _nextKeyTick = currTick + 4;
            return true;
        }

        public static void UpdateInput()
        {
            if (_lastKey != KeyCode.None && Input.GetKeyUp(_lastKey))
            {
                _lastKey = KeyCode.None;
            }

            if (VFInput.shift) return;
            var ctrl = VFInput.control;
            var alt = VFInput.alt;
            if (ctrl && alt) return;
            int delta;
            if (UpdateKeyPressed(KeyCode.LeftArrow))
            {
                if (ctrl)
                    delta = -100000;
                else if (alt)
                    delta = -1000;
                else
                    delta = -10;
            }
            else if (UpdateKeyPressed(KeyCode.RightArrow))
            {
                if (ctrl)
                    delta = 100000;
                else if (alt)
                    delta = 1000;
                else
                    delta = 10;
            }
            else if (UpdateKeyPressed(KeyCode.DownArrow))
            {
                if (ctrl)
                    delta = -1000000;
                else if (alt)
                    delta = -10000;
                else
                    delta = -100;
            }
            else if (UpdateKeyPressed(KeyCode.UpArrow))
            {
                if (ctrl)
                    delta = 1000000;
                else if (alt)
                    delta = 10000;
                else
                    delta = 100;
            }
            else
            {
                return;
            }

            var targets = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, targets);
            foreach (var target in targets)
            {
                var stationStorage = target.gameObject.GetComponentInParent<UIStationStorage>();
                if (stationStorage is null) continue;
                var station = stationStorage.station;
                ref var storage = ref station.storage[stationStorage.index];
                var oldMax = storage.max;
                var newMax = oldMax + delta;
                if (newMax < 0)
                {
                    newMax = 0;
                }
                else
                {
                    int itemCountMax;
                    if (AllowOverflowInLogisticsEnabled.Value)
                    {
                        itemCountMax = 90000000;
                    }
                    else
                    {
                        var modelProto = LDB.models.Select(stationStorage.stationWindow.factory.entityPool[station.entityId].modelIndex);
                        itemCountMax = 0;
                        if (modelProto != null)
                        {
                            itemCountMax = modelProto.prefabDesc.stationMaxItemCount;
                        }

                        itemCountMax += station.isStellar ? GameMain.history.remoteStationExtraStorage : GameMain.history.localStationExtraStorage;
                    }

                    if (newMax > itemCountMax)
                    {
                        newMax = itemCountMax;
                    }
                }

                storage.max = newMax;
                _skipNextEvent = oldMax / 100 != newMax / 100;
                break;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIStationStorage), nameof(UIStationStorage.OnMaxSliderValueChange))]
        private static bool UIStationStorage_OnMaxSliderValueChange_Prefix()
        {
            if (!_skipNextEvent) return true;
            _skipNextEvent = false;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.OnTechFunctionUnlocked))]
        private static bool PlanetTransport_OnTechFunctionUnlocked_Prefix(PlanetTransport __instance, int _funcId, double _valuelf, int _level)
        {
            switch (_funcId)
            {
                case 30:
                {
                    var stationPool = __instance.stationPool;
                    var factory = __instance.factory;
                    var history = GameMain.history;
                    for (var i = __instance.stationCursor - 1; i > 0; i--)
                    {
                        if (stationPool[i] == null || stationPool[i].id != i || (stationPool[i].isStellar && !stationPool[i].isCollector && !stationPool[i].isVeinCollector)) continue;
                        var modelIndex = factory.entityPool[stationPool[i].entityId].modelIndex;
                        var maxCount = LDB.models.Select(modelIndex).prefabDesc.stationMaxItemCount;
                        var oldMaxCount = maxCount + history.localStationExtraStorage - _valuelf;
                        var intOldMaxCount = (int)Math.Round(oldMaxCount);
                        var ratio = (maxCount + history.localStationExtraStorage) / oldMaxCount;
                        var storage = stationPool[i].storage;
                        for (var j = storage.Length - 1; j >= 0; j--)
                        {
                            if (storage[j].max + 10 < intOldMaxCount) continue;
                            storage[j].max = Mathf.RoundToInt((float)(storage[j].max * ratio / 50.0)) * 50;
                        }
                    }

                    break;
                }
                case 31:
                {
                    var stationPool = __instance.stationPool;
                    var factory = __instance.factory;
                    var history = GameMain.history;
                    for (var i = __instance.stationCursor - 1; i > 0; i--)
                    {
                        if (stationPool[i] == null || stationPool[i].id != i || !stationPool[i].isStellar || stationPool[i].isCollector || stationPool[i].isVeinCollector) continue;
                        var modelIndex = factory.entityPool[stationPool[i].entityId].modelIndex;
                        var maxCount = LDB.models.Select(modelIndex).prefabDesc.stationMaxItemCount;
                        var oldMaxCount = maxCount + history.remoteStationExtraStorage - _valuelf;
                        var intOldMaxCount = (int)Math.Round(oldMaxCount);
                        var ratio = (maxCount + history.remoteStationExtraStorage) / oldMaxCount;
                        var storage = stationPool[i].storage;
                        for (var j = storage.Length - 1; j >= 0; j--)
                        {
                            if (storage[j].max + 10 < intOldMaxCount) continue;
                            storage[j].max = Mathf.RoundToInt((float)(storage[j].max * ratio / 100.0)) * 100;
                        }
                    }

                    break;
                }
            }

            return false;
        }
    }

    private static class AllowOverflowInLogistics
    {
        private static Harmony _patch;

        public static void Enable(bool enable)
        {
            if (enable)
            {
                _patch ??= Harmony.CreateAndPatchAll(typeof(AllowOverflowInLogistics));
                return;
            }

            _patch?.UnpatchSelf();
            _patch = null;
        }

        // Do not check for overflow when try to send hand items into storages
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(UIStationStorage), nameof(UIStationStorage.OnItemIconMouseDown))]
        private static IEnumerable<CodeInstruction> UIStationStorage_OnItemIconMouseDown_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var matcher = new CodeMatcher(instructions, generator);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(LDB), nameof(LDB.items))),
                new CodeMatch(OpCodes.Ldarg_0)
            );
            var pos = matcher.Pos;
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldloc_S),
                new CodeMatch(OpCodes.Stloc_S)
            );
            var inst = matcher.InstructionAt(1).Clone();
            var pos2 = matcher.Pos + 2;
            matcher.Start().Advance(pos);
            var labels = matcher.Labels;
            matcher.RemoveInstructions(pos2 - pos).Insert(
                new CodeInstruction(OpCodes.Ldloc_1).WithLabels(labels),
                new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Player), nameof(Player.inhandItemCount))),
                inst
            );
            return matcher.InstructionEnumeration();
        }

        // Remove storage limit check
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.SetStationStorage))]
        private static IEnumerable<CodeInstruction> PlanetTransport_SetStationStorage_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var matcher = new CodeMatcher(instructions, generator);
            matcher.MatchForward(false,
                new CodeMatch(ci => ci.IsLdarg()),
                new CodeMatch(ci => ci.IsLdloc()),
                new CodeMatch(ci => ci.IsLdloc()),
                new CodeMatch(OpCodes.Add),
                new CodeMatch(ci => ci.Branches(out _)),
                new CodeMatch(ci => ci.IsLdloc()),
                new CodeMatch(ci => ci.IsLdloc()),
                new CodeMatch(OpCodes.Add),
                new CodeMatch(ci => ci.IsStarg())
            );
            var labels = matcher.Labels;
            matcher.RemoveInstructions(9).Labels.AddRange(labels);
            return matcher.InstructionEnumeration();
        }
    }

    private static class LogisticsConstrolPanelImprovement
    {
        private static Harmony _patch;

        public static void Enable(bool enable)
        {
            if (enable)
            {
                _patch ??= Harmony.CreateAndPatchAll(typeof(LogisticsConstrolPanelImprovement));
                return;
            }

            _patch?.UnpatchSelf();
            _patch = null;
        }

        private static int ItemIdHintUnderMouse()
        {
            List<RaycastResult> targets = [];
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };
            EventSystem.current.RaycastAll(pointer, targets);
            foreach (var target in targets)
            {
                var btn = target.gameObject.GetComponentInParent<UIButton>();
                if (btn?.tips is { itemId: > 0 })
                {
                    return btn.tips.itemId;
                }

                var repWin = target.gameObject.GetComponentInParent<UIReplicatorWindow>();
                if (repWin != null)
                {
                    var mouseRecipeIndex = repWin.mouseRecipeIndex;
                    var recipeProtoArray = repWin.recipeProtoArray;
                    if (mouseRecipeIndex < 0)
                    {
                        return 0;
                    }

                    var recipeProto = recipeProtoArray[mouseRecipeIndex];
                    return recipeProto != null ? recipeProto.Results[0] : 0;
                }

                var grid = target.gameObject.GetComponentInParent<UIStorageGrid>();
                if (grid != null)
                {
                    var storage = grid.storage;
                    if (storage == null) return 0;
                    var mouseOnX = grid.mouseOnX;
                    var mouseOnY = grid.mouseOnY;
                    if (mouseOnX < 0 || mouseOnY < 0) return 0;
                    var gridIndex = mouseOnX + mouseOnY * grid.colCount;
                    return storage.grids[gridIndex].itemId;
                }

                var productEntry = target.gameObject.GetComponentInParent<UIProductEntry>();
                if (productEntry == null) continue;
                if (!productEntry.productionStatWindow.isProductionTab) return 0;
                return productEntry.entryData?.itemId ?? 0;
            }

            return 0;
        }

        private static bool SetFilterItemId(UIControlPanelFilterPanel filterPanel, int itemId)
        {
            var filter = filterPanel.GetCurrentFilter();
            if (filter.itemsFilter is { Length: 1 } && filter.itemsFilter[0] == itemId) return false;
            filter.itemsFilter = [itemId];
            filterPanel.SetNewFilter(filter);
            return true;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(UIGame), nameof(UIGame.On_I_Switch))]
        private static IEnumerable<CodeInstruction> UIGame_On_I_Switch_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var matcher = new CodeMatcher(instructions, generator);
            matcher.End().MatchBack(false,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UIGame), nameof(UIGame.ShutAllFunctionWindow)))
            );
            if (matcher.IsInvalid)
            {
                UXAssist.Logger.LogWarning("Failed to patch UIGame.On_I_Switch()");
                return matcher.InstructionEnumeration();
            }

            var labels = matcher.Labels;
            matcher.Labels = null;
            matcher.Insert(
                new CodeInstruction(OpCodes.Ldarg_0).WithLabels(labels),
                Transpilers.EmitDelegate((UIGame uiGame) =>
                {
                    var itemId = ItemIdHintUnderMouse();
                    if (itemId <= 0) return;
                    SetFilterItemId(uiGame.controlPanelWindow.filterPanel, itemId);
                })
            );
            return matcher.InstructionEnumeration();
        }

        private static void OnStationEntryItemIconRightClick(UIControlPanelStationEntry stationEntry, int slot)
        {
            var storage = stationEntry.station?.storage;
            if (storage == null) return;
            var itemId = storage.Length > slot ? storage[slot].itemId : 0;
            var controlPanelWindow = UIRoot.instance?.uiGame?.controlPanelWindow;
            if (controlPanelWindow == null) return;
            var filterPanel = controlPanelWindow.filterPanel;
            if (filterPanel == null) return;
            if (!SetFilterItemId(filterPanel, itemId)) return;
            filterPanel.RefreshFilterUI();
            controlPanelWindow.DetermineFilterResults();
        }

        private static readonly Action<int>[] OnStationEntryItemIconRightClickActions = new Action<int>[5];

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIControlPanelStationEntry), nameof(UIControlPanelStationEntry._OnRegEvent))]
        private static void UIControlPanelStationEntry__OnRegEvent_Postfix(UIControlPanelStationEntry __instance)
        {
            OnStationEntryItemIconRightClickActions[0] = _ => OnStationEntryItemIconRightClick(__instance, 0);
            OnStationEntryItemIconRightClickActions[1] = _ => OnStationEntryItemIconRightClick(__instance, 1);
            OnStationEntryItemIconRightClickActions[2] = _ => OnStationEntryItemIconRightClick(__instance, 2);
            OnStationEntryItemIconRightClickActions[3] = _ => OnStationEntryItemIconRightClick(__instance, 3);
            OnStationEntryItemIconRightClickActions[4] = _ => OnStationEntryItemIconRightClick(__instance, 4);
            __instance.storageItem0.itemButton.onRightClick += OnStationEntryItemIconRightClickActions[0];
            __instance.storageItem1.itemButton.onRightClick += OnStationEntryItemIconRightClickActions[1];
            __instance.storageItem2.itemButton.onRightClick += OnStationEntryItemIconRightClickActions[2];
            __instance.storageItem3.itemButton.onRightClick += OnStationEntryItemIconRightClickActions[3];
            __instance.storageItem4.itemButton.onRightClick += OnStationEntryItemIconRightClickActions[4];
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIControlPanelStationEntry), nameof(UIControlPanelStationEntry._OnUnregEvent))]
        private static void UIControlPanelStationEntry__OnUnregEvent_Postfix(UIControlPanelStationEntry __instance)
        {
            __instance.storageItem0.itemButton.onRightClick -= OnStationEntryItemIconRightClickActions[0];
            __instance.storageItem1.itemButton.onRightClick -= OnStationEntryItemIconRightClickActions[1];
            __instance.storageItem2.itemButton.onRightClick -= OnStationEntryItemIconRightClickActions[2];
            __instance.storageItem3.itemButton.onRightClick -= OnStationEntryItemIconRightClickActions[3];
            __instance.storageItem4.itemButton.onRightClick -= OnStationEntryItemIconRightClickActions[4];
            for (var i = 0; i < 5; i++)
            {
                OnStationEntryItemIconRightClickActions[i] = null;
            }
        }
    }

    private static class LogisticsInfoWindow
    {
        private static int _maxCount;
        private static StationTip[] _stationtips = new StationTip[_maxCount];
        private static GameObject _stationTipRoot;
        private static GameObject _tipPrefab;
        private static readonly Color OrangeColor = new(224f / 255, 139f / 255, 93f / 255);
        private static readonly Color BlueColor = new(75f / 255, 172f / 255, 205f / 255);
        private static Sprite _leftsprite;
        private static Sprite _rightsprite;
        private static Sprite _flatsprite;

        public static void InitGUI()
        {
            _leftsprite = Util.LoadEmbeddedSprite("assets/icon/in.png");
            _rightsprite = Util.LoadEmbeddedSprite("assets/icon/out.png");
            _flatsprite = Util.LoadEmbeddedSprite("assets/icon/keep.png");
            _stationTipRoot = UnityEngine.Object.Instantiate(GameObject.Find("UI Root/Overlay Canvas/In Game/Scene UIs/Vein Marks"), GameObject.Find("UI Root/Overlay Canvas/In Game/Scene UIs").transform);
            _stationTipRoot.name = "stationTip";
            UnityEngine.Object.Destroy(_stationTipRoot.GetComponent<UIVeinDetail>());
            _tipPrefab = UnityEngine.Object.Instantiate(GameObject.Find("UI Root/Overlay Canvas/In Game/Scene UIs/Vein Marks/vein-tip-prefab"), _stationTipRoot.transform);
            _tipPrefab.name = "tipPrefab";
            _tipPrefab.GetComponent<Image>().sprite = GameObject.Find("UI Root/Overlay Canvas/In Game/Windows/Key Tips/tip-prefab").GetComponent<Image>().sprite;
            _tipPrefab.GetComponent<Image>().color = new Color(0, 0, 0, 0.8f);
            _tipPrefab.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 160f);
            _tipPrefab.GetComponent<Image>().enabled = true;
            _tipPrefab.transform.localPosition = new Vector3(200f, 800f, 0);
            _tipPrefab.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
            UnityEngine.Object.Destroy(_tipPrefab.GetComponent<UIVeinDetailNode>());
            var infoText = _tipPrefab.transform.Find("info-text").gameObject;
            for (var index = 0; index < 13; ++index)
            {
                var countText = UnityEngine.Object.Instantiate(infoText, Vector3.zero, Quaternion.identity, _tipPrefab.transform);
                countText.name = "countText" + index;
                float y = (-5 - 35 * index);
                countText.GetComponent<Text>().fontSize = index == 5 ? 15 : 19;
                countText.GetComponent<Text>().text = "99999";
                countText.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
                countText.GetComponent<RectTransform>().sizeDelta = new Vector2(95, 30);
                countText.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(70, y, 0);
                UnityEngine.Object.Destroy(countText.GetComponent<Shadow>());
                var itemIcon = UnityEngine.Object.Instantiate(_tipPrefab.transform.Find("icon").gameObject, new Vector3(0, 0, 0), Quaternion.identity, _tipPrefab.transform);
                itemIcon.name = "icon" + index;
                countText.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
                itemIcon.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                itemIcon.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                itemIcon.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                itemIcon.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(0, y, 0);
                var stateLocal = UnityEngine.Object.Instantiate(_tipPrefab.transform.Find("icon").gameObject, new Vector3(0, 0, 0), Quaternion.identity, _tipPrefab.transform);
                stateLocal.name = "iconLocal" + index;
                stateLocal.GetComponent<Image>().material = null;
                stateLocal.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 20);
                stateLocal.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                stateLocal.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                stateLocal.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                stateLocal.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(105, y, 0);
                var stateRemote = UnityEngine.Object.Instantiate(_tipPrefab.transform.Find("icon").gameObject, new Vector3(0, 0, 0), Quaternion.identity, _tipPrefab.transform);
                stateRemote.name = "iconRemote" + index;
                stateRemote.GetComponent<Image>().material = null;
                stateRemote.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 20);
                stateRemote.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                stateRemote.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                stateRemote.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                stateRemote.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(105, y - 15, 0);
            }
            for (var i = 0; i < 3; i++)
            {
                var iconText = UnityEngine.Object.Instantiate(GameObject.Find("UI Root/Overlay Canvas/In Game/Top Tips/Entity Briefs/brief-info-top/brief-info/content/icons/icon"), new Vector3(0, 0, 0), Quaternion.identity, _tipPrefab.transform);
                UnityEngine.Object.Destroy(iconText.transform.Find("count-text").gameObject);
                UnityEngine.Object.Destroy(iconText.transform.Find("bg").gameObject);
                UnityEngine.Object.Destroy(iconText.transform.Find("inc").gameObject);
                UnityEngine.Object.Destroy(iconText.GetComponent<UIIconCountInc>());

                iconText.name = "iconText" + i;
                iconText.GetComponent<RectTransform>().localScale = new Vector3(0.7f, 0.7f, 1);
                iconText.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                iconText.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                iconText.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                iconText.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(i * 30, -180, 0);
                var countText = UnityEngine.Object.Instantiate(infoText, Vector3.zero, Quaternion.identity, iconText.transform);
                UnityEngine.Object.Destroy(countText.GetComponent<Shadow>());

                countText.name = "countText";
                countText.GetComponent<Text>().fontSize = 22;
                countText.GetComponent<Text>().text = "100";
                countText.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
                countText.GetComponent<RectTransform>().sizeDelta = new Vector2(95, 30);
                countText.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().localPosition = new Vector3(-50, -20, 0);

                if (i == 2) continue;

                countText = UnityEngine.Object.Instantiate(infoText, Vector3.zero, Quaternion.identity, iconText.transform);
                UnityEngine.Object.Destroy(countText.GetComponent<Shadow>());

                countText.name = "countText2";
                countText.GetComponent<Text>().fontSize = 22;
                countText.GetComponent<Text>().text = "100";
                countText.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
                countText.GetComponent<RectTransform>().sizeDelta = new Vector2(95, 30);
                countText.GetComponent<RectTransform>().anchorMax = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
                countText.GetComponent<RectTransform>().localPosition = new Vector3(-50, 10, 0);
            }
            _tipPrefab.transform.Find("icon").gameObject.SetActive(false);
            _tipPrefab.SetActive(false);
            for (var i = 0; i < _maxCount; ++i)
            {
                var temptip = UnityEngine.Object.Instantiate(_tipPrefab, _stationTipRoot.transform);
                var tempstationtip = temptip.AddComponent<StationTip>();
                _stationtips[i] = tempstationtip;
            }
        }

        public class StationTip : MonoBehaviour
        {
            [FormerlySerializedAs("RectTransform")] public RectTransform rectTransform;
            private Transform[] _icons;
            private RectTransform[] _iconRectTransforms;
            private Transform[] _iconLocals;
            private Transform[] _iconRemotes;
            private Transform[] _countTexts;

            private Transform[] _iconTexts;
            private Text[] _countText;
            private Text[] _countText2;

            private GameObject _infoText;

            public void InitStationTip()
            {
                rectTransform = GetComponent<RectTransform>();
                _icons = new Transform[13];
                _iconLocals = new Transform[13];
                _iconRemotes = new Transform[13];
                _countTexts = new Transform[13];
                _iconTexts = new Transform[3];


                _countText = new Text[3];
                _countText2 = new Text[3];

                _iconRectTransforms = new RectTransform[13];
                _infoText = transform.Find("info-text").gameObject;
                for (var i = 0; i < 3; i++)
                {
                    _iconTexts[i] = transform.Find("iconText" + i);
                    _countText[i] = _iconTexts[i].Find("countText").GetComponent<Text>();
                    if (i != 2)
                    {
                        _countText2[i] = _iconTexts[i].Find("countText2").GetComponent<Text>();
                    }
                }

                for (var i = 0; i < 13; i++)
                {
                    _icons[i] = transform.Find("icon" + i);
                    _iconLocals[i] = transform.Find("iconLocal" + i);
                    _iconRemotes[i] = transform.Find("iconRemote" + i);
                    _countTexts[i] = transform.Find("countText" + i);
                    _iconRectTransforms[i] = _icons[i].GetComponent<RectTransform>();

                    _iconLocals[i].gameObject.SetActive(false);
                    _iconRemotes[i].gameObject.SetActive(false);
                }

                _infoText.SetActive(false);
            }

            public void SetStationName(string stationName, int index)
            {
                var lastIcon = _icons[index];
                var lastCountText = _countTexts[index];
                lastIcon.gameObject.SetActive(false);
                for (var i = index; i < 13; ++i)
                {
                    _iconLocals[i].gameObject.SetActive(false);
                    _iconRemotes[i].gameObject.SetActive(false);
                    _icons[i].gameObject.SetActive(false);
                    _countTexts[i].gameObject.SetActive(false);
                }

                if (!string.IsNullOrEmpty(stationName))
                {
                    var lastCountTextPosition = lastCountText.GetComponent<RectTransform>().anchoredPosition3D;
                    lastCountText.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(90, lastCountTextPosition.y, 0);
                    lastCountText.GetComponent<Text>().fontSize = 18;
                    lastCountText.GetComponent<Text>().text = stationName;
                    lastCountText.GetComponent<Text>().color = Color.white;
                    lastCountText.gameObject.SetActive(true);
                }
                else
                {
                    lastIcon.gameObject.SetActive(false);
                    lastCountText.gameObject.SetActive(false);
                }
            }

            public void SetItem(int itemId, int itemCount, int i, ELogisticStorage localLogic, ELogisticStorage remoteLogic, bool isStellarorCollector)
            {
                var icon = _icons[i];
                var iconPos = _iconRectTransforms[i].anchoredPosition3D;
                var iconLocal = _iconLocals[i];
                var iconRemote = _iconRemotes[i];
                var iconLocalImage = iconLocal.GetComponent<Image>();
                var iconRemoteImage = iconRemote.GetComponent<Image>();
                var countText = _countTexts[i];
                var countUIText = countText.GetComponent<Text>();
                if (itemId > 0)
                {
                    switch (localLogic)
                    {
                        case ELogisticStorage.Supply:
                            iconLocalImage.sprite = _rightsprite;
                            iconLocalImage.color = BlueColor;
                            countUIText.color = BlueColor;
                            break;
                        case ELogisticStorage.Demand:
                            iconLocalImage.sprite = _leftsprite;
                            iconLocalImage.color = OrangeColor;
                            countUIText.color = OrangeColor;
                            break;
                        case ELogisticStorage.None:
                            iconLocalImage.sprite = _flatsprite;
                            iconLocalImage.color = Color.gray;
                            countUIText.color = Color.gray;
                            break;
                    }

                    if (isStellarorCollector)
                    {
                        switch (remoteLogic)
                        {
                            case ELogisticStorage.Supply:
                                iconRemoteImage.sprite = _rightsprite;
                                iconRemoteImage.color = BlueColor;
                                break;
                            case ELogisticStorage.Demand:
                                iconRemoteImage.sprite = _leftsprite;
                                iconRemoteImage.color = OrangeColor;
                                break;
                            case ELogisticStorage.None:
                                iconRemoteImage.sprite = _flatsprite;
                                iconRemoteImage.color = Color.gray;
                                break;
                        }

                        iconRemote.gameObject.SetActive(true);
                    }

                    iconLocal.gameObject.SetActive(true);
                    countText.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(70, iconPos.y, 0);
                    if (isStellarorCollector)
                    {
                        iconLocal.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 20);
                        iconRemote.GetComponent<RectTransform>().sizeDelta = new Vector2(20, 20);
                        iconLocal.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(105, iconPos.y, 0);
                        iconRemote.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(105, iconPos.y - 15, 0);
                    }
                    else
                    {
                        iconRemote.gameObject.SetActive(false);
                        iconLocal.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(100, iconPos.y, 0);
                        iconLocal.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
                    }

                    icon.GetComponent<Image>().sprite = LDB.items.Select(itemId)?.iconSprite;
                    icon.gameObject.SetActive(true);
                    countUIText.text = itemCount.ToString("#,##0");
                }
                else
                {
                    iconLocal.gameObject.SetActive(false);
                    iconRemote.gameObject.SetActive(false);
                    icon.gameObject.SetActive(false);
                    countUIText.color = Color.white;
                    countUIText.text = "无";
                    var anchoredX = showStationInfoMode ? isStellarorCollector ? 70 : 90 : 70;

                    countUIText.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(anchoredX, iconPos.y, 0);
                }

                countText.gameObject.SetActive(true);
            }

            public void SetDroneShipWarp(int index, int itemId, int totalCount, int currentCount, int lastLine)
            {
                if (itemId == 0)
                {
                    _iconTexts[index].gameObject.SetActive(false);
                    return;
                }

                if (index < 2)
                {
                    _countText2[index].color = Color.white;
                    _countText2[index].text = currentCount.ToString();
                }

                _iconTexts[index].gameObject.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(index * 30, -30 - 30 * lastLine, 0);
                _iconTexts[index].GetComponent<Image>().sprite = LDB.items.Select(itemId).iconSprite;
                _countText[index].color = Color.white;
                _countText[index].text = totalCount.ToString();
                _iconTexts[index].gameObject.SetActive(true);
            }
        }

        public static void StationInfoWindowUpdate()
        {
            var pd = GameMain.localPlanet;
            var transport = pd?.factory?.transport;
            if (transport == null || transport.stationCursor == 0 || (UIGame.viewMode != EViewMode.Normal && UIGame.viewMode != EViewMode.Globe))
            {
                if (_stationTipRoot.activeSelf)
                {
                    _stationTipRoot.SetActive(false);
                }

                return;
            }

            _stationTipRoot.SetActive(true);
            var tipIndex = 0;
            var localPosition = GameCamera.main.transform.localPosition;
            var forward = GameCamera.main.transform.forward;
            var realRadius = pd.realRadius;

            foreach (var stationComponent in transport.stationPool)
            {
                if (stationComponent?.storage == null) continue;
                if (tipIndex == _maxCount)
                {
                    _maxCount++;
                    var temptip = UnityEngine.Object.Instantiate(_tipPrefab, _stationTipRoot.transform);
                    var tempstationtip = temptip.AddComponent<StationTip>();
                    tempstationtip.InitStationTip();
                    Array.Resize(ref _stationtips, _maxCount);
                    _stationtips[_maxCount - 1] = tempstationtip;
                }

                var isStellarorCollector = stationComponent.isStellar || stationComponent.isCollector;
                var position = pd.factory.entityPool[stationComponent.entityId].pos.normalized;
                var storageNum = Math.Min(stationComponent.storage.Length, 5);
                float tipWindowHeight = 40 * storageNum + 20;
                if (stationComponent.isCollector)
                {
                    storageNum = 2;
                    position *= realRadius + 35;
                    tipWindowHeight -= 20;
                }
                else if (stationComponent.isStellar)
                {
                    position *= realRadius + 20;
                }
                else if (stationComponent.isVeinCollector)
                    tipWindowHeight -= 20;
                else
                {
                    position *= realRadius + 15;
                }

                var stationtip = _stationtips[tipIndex];
                var vec = position - localPosition;
                var magnitude = vec.magnitude;
                if (magnitude < 1.0 || Vector3.Dot(forward, vec) < 1.0)
                    continue;
                if (!UIRoot.ScreenPointIntoRect(GameCamera.main.WorldToScreenPoint(position), _stationTipRoot.GetComponent<RectTransform>(), out var rectPoint))
                    continue;
                if (rectPoint.x is < -4096f or > 4096f || rectPoint.y is < -4096f or > 4096f)
                    continue;
                if (Phys.RayCastSphere(localPosition, vec / magnitude, magnitude, Vector3.zero, realRadius, out _))
                    continue;
                if (stationComponent.storage.Select(x => x.itemId).All(x => x == 0))
                    continue;
                tipIndex++;
                stationtip.gameObject.SetActive(true);
                rectPoint.x = Mathf.Round(rectPoint.x);
                rectPoint.y = Mathf.Round(rectPoint.y);
                stationtip.rectTransform.anchoredPosition = rectPoint;
                for (var i = 0; i < storageNum; ++i)
                {
                    var storage = stationComponent.storage[i];
                    stationtip.SetItem(storage.itemId, storage.count, i, storage.localLogic, storage.remoteLogic, /*ShowStationInfoMode.Value*/isStellarorCollector);
                }

                var lastLine = storageNum;
                var stationComponentName = pd.factory.ReadExtraInfoOnEntity(stationComponent.entityId);
                stationtip.SetStationName(stationComponentName, lastLine);
                if (!string.IsNullOrEmpty(stationComponentName))
                {
                    tipWindowHeight += 27;
                    lastLine++;
                }

                for (var i = 0; i < 3; ++i)
                {
                    if (stationComponent.isCollector || stationComponent.isVeinCollector || (i >= 1 && !stationComponent.isStellar))
                    {
                        stationtip.SetDroneShipWarp(i, 0, 0, 0, lastLine);
                        continue;
                    }

                    int itemId;
                    int totalCount;
                    var currentCount = 0;
                    if (i == 0)
                    {
                        itemId = 5001;
                        totalCount = stationComponent.idleDroneCount + stationComponent.workDroneCount;
                        currentCount = stationComponent.idleDroneCount;
                    }
                    else if (i == 1)
                    {
                        itemId = 5002;
                        totalCount = stationComponent.idleShipCount + stationComponent.workShipCount;
                        currentCount = stationComponent.idleShipCount;
                    }
                    else
                    {
                        itemId = 1210;
                        totalCount = stationComponent.warperCount;
                    }

                    stationtip.SetDroneShipWarp(i, itemId, totalCount, currentCount, lastLine);
                }

                float localScaleMultiple;
                if (magnitude < 50.0)
                    localScaleMultiple = 1.5f;
                else if (magnitude < 250.0)
                    localScaleMultiple = (float)(1.75 - magnitude * 0.005);
                else
                    localScaleMultiple = 0.5f;
                stationtip.transform.localScale = Vector3.one * localScaleMultiple;
                stationtip.rectTransform.sizeDelta = new Vector2(125f, tipWindowHeight);
            }

            for (var i = tipIndex; i < _maxCount; ++i)
                _stationtips[i].gameObject.SetActive(false);
        }
    }
}