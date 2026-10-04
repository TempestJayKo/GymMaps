using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GymMaps;

internal static class RespawnPoint
{
    private static GameObject marker;
    [ThreadStatic] internal static bool InLocalReset;
    internal static void Set(GameObject source)
    {
        marker=source;
        MelonLogger.Msg("[MapLoad] Respawn start: "+source.name+" at "+source.transform.position);
    }
    internal static bool Available => marker && marker.activeInHierarchy && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Gym";
    internal static bool IsLocal(PlayerResetSystem reset)
    {
        var controller=PlayerManager.instance?.localPlayer?.Controller;
        return reset && controller && reset.ParentController==controller;
    }
    internal static SpawnPoint Create(SpawnPointHandler.SpawnPointType type)
    {
        // Player spawn points use upright facing; marker pitch/roll are not a body tilt.
        return new SpawnPoint(-1,marker.transform.position,Quaternion.Euler(0,marker.transform.eulerAngles.y,0),type);
    }
    internal static SpawnPoint Redirect(SpawnPoint original) =>
        new SpawnPoint(original.SpawnIndexNo,marker.transform.position,
            Quaternion.Euler(0,marker.transform.eulerAngles.y,0),original.PointType);
    internal static void Clear() { marker=null;InLocalReset=false; }
    internal static void TeleportLocal()
    {
        if(!Available) throw new InvalidOperationException("Destination map has no active respawn start marker.");
        var controller=PlayerManager.instance?.localPlayer?.Controller;
        var movement=controller?controller.GetComponentInChildren<PlayerMovement>(true):null;
        var physics=controller?controller.GetComponentInChildren<PlayerPhysics>(true):null;
        if(!movement || !physics) throw new InvalidOperationException("Player movement or physics is not ready.");
        var point=Create(SpawnPointHandler.SpawnPointType.Startup);
        physics.ClearAllVelocities(PlayerPhysics.RigidbodyTarget.Both);
        movement.RepositionWithFade(point.Position,point.Rotation,.25f,.1f,true,movement.ActiveFeatures,true);
        MelonLogger.Msg("[MapButton] Teleporting to map start: "+marker.name);
    }
}

// Override only a local player's native reset routine, preserving its fade, health reset and RPC flow.
[HarmonyPatch(typeof(PlayerResetSystem._ResetRoutine_d__26),nameof(PlayerResetSystem._ResetRoutine_d__26.MoveNext))]
internal static class RespawnRoutinePatch
{
    internal sealed class ResetScope
    {
        internal bool PreviousScope;
        internal SpawnPointHandler Handler;
        internal Il2CppReferenceArray<SpawnPoint> OriginalPoints;
        internal bool Restored;
        internal void Restore()
        {
            if(Restored) return;
            Restored=true;
            if(Handler) Handler.points=OriginalPoints;
            RespawnPoint.InLocalReset=PreviousScope;
        }
    }
    private static void Prefix(PlayerResetSystem._ResetRoutine_d__26 __instance,out ResetScope __state)
    {
        __state=new ResetScope {PreviousScope=RespawnPoint.InLocalReset};
        RespawnPoint.InLocalReset=RespawnPoint.Available && RespawnPoint.IsLocal(__instance.__4__this);
        if(!RespawnPoint.InLocalReset) return;
        try
        {
            // Native IL2CPP selection may be inlined: redirect the actual data it reads,
            // only for this local reset step, then restore the Gym table before yielding.
            var handler=PlayerManager.instance?.activeSpawnPointHandler;
            var original=handler?handler.points:null;
            if(original!=null && original.Length>0)
            {
                var redirected=new Il2CppReferenceArray<SpawnPoint>(original.Length);
                for(int i=0;i<original.Length;i++)
                    redirected[i]=original[i]!=null?RespawnPoint.Redirect(original[i]):null;
                __state.Handler=handler;__state.OriginalPoints=original;
                handler.points=redirected;
            }
            if(__instance._spawnPoint_5__2!=null)
                __instance._spawnPoint_5__2=RespawnPoint.Redirect(__instance._spawnPoint_5__2);
            MelonLogger.Msg("[MapRespawn] Native local reset redirected to course start.");
        }
        catch { __state.Restore();throw; }
    }
    private static void Postfix(ResetScope __state) => __state?.Restore();
    private static Exception Finalizer(Exception __exception,ResetScope __state)
    { __state?.Restore();return __exception; }
}


