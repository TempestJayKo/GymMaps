using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GymMaps;

internal static class DeathSurfaces
{
    private static float nextReset;
    internal static bool Handle(KillPlayerOnCollision surface,Transform contact)
    {
        if(!surface || (!Runtime.OwnsMapObject(surface.gameObject) && !MapTools.ControlsDeathSurface(surface))) return false;
        var controller=PlayerManager.instance?.localPlayer?.Controller;
        // Race maps affect only the local player; non-player contacts must not call the
        // native handler, which dereferences a missing player component on these shapes.
        if(controller && contact && contact.IsChildOf(controller.transform)) ResetLocal();
        return true;
    }
    internal static void ResetLocal()
    {
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Gym" || Runtime.IsBusy || Time.realtimeSinceStartup<nextReset) return;
        var controller=PlayerManager.instance?.localPlayer?.Controller;
        var reset=controller?controller.GetComponentInChildren<PlayerResetSystem>(true):null;
        if(!reset || reset.currentResetRoutine!=null) return;
        nextReset=Time.realtimeSinceStartup+1.25f;
        MelonLogger.Msg("[KillFloor] Local player reached a death surface; native reset requested.");
        reset.ResetPlayerController();
    }
}

[HarmonyPatch(typeof(KillPlayerOnCollision),nameof(KillPlayerOnCollision.AttemptKillPlayer))]
internal static class MapDeathSurfacePatch
{
    private static bool Prefix(KillPlayerOnCollision __instance,Transform tr) => !DeathSurfaces.Handle(__instance,tr);
}
