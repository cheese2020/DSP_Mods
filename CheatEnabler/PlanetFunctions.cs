﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Random = UnityEngine.Random;

namespace CheatEnabler;
public static class PlanetFunctions
{
    /*
    private static Harmony _patch;
    private static PumpSource[] _pumpItemSources = new PumpSource[16];
    private static int _pumpItemSourcesLength = 16;

    private class PumpSource
    {
        public Tuple<int, float>[] Items;
        public float[] Progress;
    }

    public static void Init()
    {
        if (_patch != null) return;
        _patch = Harmony.CreateAndPatchAll(typeof(PlanetFunctions));
    }

    public static void Uninit()
    {
        _patch?.UnpatchSelf();
        _patch = null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMain), nameof(GameMain.Begin))]
    private static void GameMain_Begin_Postfix()
    {
        InitItemSources();
        var factories = GameMain.data.factories;
        for (var i = GameMain.data.factoryCount - 1; i >= 0; i--)
        {
            if (factories[i].index != i) continue;
            UpdatePumpItemSources(factories[i]);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.Init))]
    private static void PlanetFactory_Init_Postfix(PlanetFactory __instance)
    {
        const int waterItemId = 6006;
        __instance.planet.waterItemId = waterItemId;
        CheatEnabler.Logger.LogDebug($"Init PlanetFactory with waterItemId {waterItemId}");
        UpdatePumpItemSources(__instance);
    }

    private static void UpdatePumpItemSources(PlanetFactory factory)
    {
        var index = factory.index;
        var waterItemId = factory.planet.waterItemId;
        if (waterItemId == 0 || !ItemSources.TryGetValue(waterItemId, out var itemSource) || itemSource.From == null)
        {
            if (index < _pumpItemSourcesLength)
            {
                _pumpItemSources[index] = null;
            }
            return;
        }
        if (index >= _pumpItemSourcesLength)
        {
            var newLength = _pumpItemSourcesLength * 2;
            while (index >= newLength)
            {
                newLength *= 2;
            }
            var newPumpItemSources = new PumpSource[newLength];
            Array.Copy(_pumpItemSources, newPumpItemSources, _pumpItemSourcesLength);
            _pumpItemSources = newPumpItemSources;
            _pumpItemSourcesLength = newLength;
        }
        var pump = _pumpItemSources[index];
        if (pump == null)
        {
            pump = new PumpSource();
            _pumpItemSources[index] = pump;
        }

        var result = new Dictionary<int, float>();
        var extra = new Dictionary<int, float>();
        CalculateAllProductions(result, extra, waterItemId, 1f);
        foreach (var p in extra)
        {
            if (!result.TryGetValue(p.Key, out var cnt) || cnt < p.Value)
            {
                result[p.Key] = p.Value;
            }
        }

        var count = result.Count;
        var items = new Tuple<int, float>[count];
        var progress = new float[count];
        foreach (var p in result)
        {
            items[--count] = Tuple.Create(p.Key, p.Value);
        }
        pump.Items = items;
        pump.Progress = progress;
    }
    */
    
    public static void DismantleAll(bool toBag)
    {
        var player = GameMain.mainPlayer;
        if (player == null) return;
        var planet = GameMain.localPlanet;
        var factory = planet?.factory;
        if (factory == null) return;
        foreach (var etd in factory.entityPool)
        {
            var stationId = etd.stationId;
            if (stationId > 0)
            {
                var sc = GameMain.localPlanet.factory.transport.stationPool[stationId];
                if (toBag)
                {
                    for (var i = 0; i < sc.storage.Length; i++)
                    {
                        var package = player.TryAddItemToPackage(sc.storage[i].itemId, sc.storage[i].count, 0, true, etd.id);
                        UIItemup.Up(sc.storage[i].itemId, package);
                    }
                }
                sc.storage = new StationStore[sc.storage.Length];
                sc.needs = new int[sc.needs.Length];
            }
            if (toBag)
            {
                player.controller.actionBuild.DoDismantleObject(etd.id);
            }
            else
            {
                factory.RemoveEntityWithComponents(etd.id);
            }
        }
    }

    public static void BuryAllVeins(bool bury)
    {
        var planet = GameMain.localPlanet;
        var factory = planet?.factory;
        if (factory == null) return;
        var physics = planet.physics;
        var height = bury ? planet.realRadius - 50f : planet.realRadius + 0.07f;
        var array = factory.veinPool;
        var num = factory.veinCursor;
        for (var m = 1; m < num; m++)
        {
            var pos = array[m].pos;
            var colliderId = array[m].colliderId;
            var colliderData = physics.GetColliderData(colliderId);
            var vector = colliderData.pos.normalized * (height + 0.4f);
            physics.colChunks[colliderId >> 20].colliderPool[colliderId & 0xFFFFF].pos = vector;
            array[m].pos = pos.normalized * height;
            var quaternion = Maths.SphericalRotation(array[m].pos, Random.value * 360f);
            physics.SetPlanetPhysicsColliderDirty();
            GameMain.gpuiManager.AlterModel(array[m].modelIndex, array[m].modelId, m, array[m].pos, quaternion, false);
        }
        GameMain.gpuiManager.SyncAllGPUBuffer();
    }

    public static void RecreatePlanet(bool revertReform)
    {
        var player = GameMain.mainPlayer;
        if (player == null) return;
        var planet = GameMain.localPlanet;
        var factory = planet?.factory;
        if (factory == null) return;
        //planet.data = new PlanetRawData(planet.precision);
        //planet.data.CalcVerts();
        for (var id = factory.entityCursor - 1; id > 0; id--)
        {
            var ed = factory.entityPool[id];
            if (ed.id != id) continue;
            if (ed.colliderId != 0)
            {
                planet.physics.RemoveLinkedColliderData(ed.colliderId);
                planet.physics.NotifyObjectRemove(EObjectType.Entity, ed.id);
            }

            if (ed.modelId != 0)
            {
                GameMain.gpuiManager.RemoveModel(ed.modelIndex, ed.modelId);
            }

            if (ed.mmblockId != 0)
            {
                factory.blockContainer.RemoveMiniBlock(ed.mmblockId);
            }

            if (ed.audioId != 0)
            {
                if (planet.audio != null)
                {
                    planet.audio.RemoveAudioData(ed.audioId);
                }
            }
        }

        var stationPool = factory.transport?.stationPool;
        if (stationPool != null)
        {
            foreach (var sc in stationPool)
            {
                if (sc == null || sc.id <= 0) continue;
                sc.storage = new StationStore[sc.storage.Length];
                sc.needs = new int[sc.needs.Length];
                int protoId = factory.entityPool[sc.entityId].protoId;
                factory.DismantleFinally(player, sc.entityId, ref protoId);
            }
        }

        var gameScenario = GameMain.gameScenario;
        if (gameScenario != null)
        {
            var genPool = factory.powerSystem?.genPool;
            if (genPool != null)
            {
                foreach (var pgc in genPool)
                {
                    if (pgc.id <= 0) continue;
                    int protoId = factory.entityPool[pgc.entityId].protoId;
                    gameScenario.achievementLogic.NotifyBeforeDismantleEntity(planet.id, protoId, pgc.entityId);
                    gameScenario.NotifyOnDismantleEntity(planet.id, protoId, pgc.entityId);
                }
            }
        }

        if (revertReform)
        {
            factory.PlanetReformRevert();
        }

        planet.UnloadFactory();
        var index = factory.index;
        var warningSystem = GameMain.data.warningSystem;
        var warningPool = warningSystem.warningPool;
        for (var i = warningSystem.warningCursor - 1; i > 0; i--)
        {
            if (warningPool[i].id == i && warningPool[i].factoryId == index)
                warningSystem.RemoveWarningData(warningPool[i].id);
        }
        factory.entityCursor = 1;
        factory.entityRecycleCursor = 0;
        factory.entityCapacity = 0;
        factory.SetEntityCapacity(1024);
        factory.prebuildCursor = 1;
        factory.prebuildRecycleCursor = 0;
        factory.prebuildCapacity = 0;
        factory.SetPrebuildCapacity(256);
        factory.cargoContainer = new CargoContainer();
        factory.cargoTraffic = new CargoTraffic(planet);
        factory.blockContainer = new MiniBlockContainer();
        factory.factoryStorage = new FactoryStorage(planet);
        factory.powerSystem = new PowerSystem(planet);
        factory.factorySystem = new FactorySystem(planet);
        factory.transport = new PlanetTransport(GameMain.data, planet);
        factory.transport.Init();
        factory.digitalSystem = new DigitalSystem(planet);
        //GameMain.data.statistics.production.CreateFactoryStat(index);
        planet.LoadFactory();
        while (!planet.factoryLoaded)
        {
            PlanetModelingManager.Update();
            Thread.Sleep(0);
        }
    }
}