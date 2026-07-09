using System;
using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using HarmonyLib;

namespace MapLootEditorLite.Client
{
    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.FindStationaryWeapon))]
    public static class StationaryWeaponPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string id, ref StationaryWeapon __result, GameWorld __instance)
        {
            // If the weapon was not found in the scene, try to spawn it on-demand
            if (__result == null && RuntimeInteractiveObjectSpawner.CustomStationaryWeapons.ContainsKey(id))
            {
                Plugin.Log.LogInfo($"GameWorld.FindStationaryWeapon('{id}') returned null, attempting on-demand spawn.");
                __result = RuntimeInteractiveObjectSpawner.SpawnStationaryWeaponOnDemand(id);
                
                if (__result != null)
                {
                    // Register the spawned weapon with the game world
                    __instance.RegisterLoot<StationaryWeapon>(__result);
                    Plugin.Log.LogInfo($"Registered on-demand spawned stationary weapon '{id}' with GameWorld.");
                }
            }
        }
    }
}
