using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Utilities;
using MelonLoader;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GymMaps;

internal static class LoadFeatures
{
    internal static bool Enabled { get; private set; }
    private static GameObject platform;
    private static Material platformMaterial;
    private static readonly Dictionary<int,Structure> structures=new();
    internal static void SetEnabled(bool enabled)
    {
        Enabled=enabled;
        if(!enabled) ClearPlatform();
    }
    internal static PlayerPhysics PlayerBody()
    {
        var controller=PlayerManager.instance?.localPlayer?.Controller;
        return controller ? controller.GetComponentInChildren<PlayerPhysics>(true) : null;
    }
    private static bool Nearest(Vector3 origin,float distance,out RaycastHit nearest)
    {
        nearest=default;float best=float.PositiveInfinity;
        var controller=PlayerManager.instance?.localPlayer?.Controller;
        foreach(var hit in Physics.RaycastAll(origin,Vector3.down,distance,~0,QueryTriggerInteraction.Ignore))
        {
            if(!hit.collider || (controller && hit.collider.transform.IsChildOf(controller.transform))) continue;
            if(hit.distance<best) { nearest=hit;best=hit.distance; }
        }
        return !float.IsPositiveInfinity(best);
    }
    internal static void CheckSafetyOnce()
    {
        if(!Enabled || !Runtime.GameplayAllowed) return;
        try
        {
            var body=PlayerBody();
            if(!body || !body.PhysicsRigidbody) { MelonLogger.Warning("[SafetyPlatform] Player body unavailable; check skipped for this load.");return; }
            Vector3 feet=body.PhysicsRigidbody.position;
            bool found=false;float bottom=float.PositiveInfinity;
            foreach(var col in body.PhysicsRigidbody.GetComponentsInChildren<Collider>())
                if(col.enabled && !col.isTrigger && col.attachedRigidbody==body.PhysicsRigidbody)
                { bottom=Math.Min(bottom,col.bounds.min.y);found=true; }
            if(!found) { MelonLogger.Warning("[SafetyPlatform] Body collider unavailable; check skipped.");return; }
            feet.y=bottom;
            CheckSafetyAt(feet);
        }
        catch(Exception ex) { MelonLogger.Warning("[SafetyPlatform] "+ex.Message); }
    }
    internal static void CheckSafetyAt(Vector3 feet)
    {
        if(!Enabled || !Runtime.GameplayAllowed) return;
        Physics.SyncTransforms();
        if(Nearest(feet+Vector3.up*.1f,.6f,out var hit) && hit.normal.y>.3f && !hit.collider.GetComponentInParent<KillPlayerOnCollision>())
        { MelonLogger.Msg("[SafetyPlatform] Supporting ground found; no platform needed.");return; }
        ClearPlatform();
        platform=GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name="GymMaps.SafetyPlatform";platform.layer=9;
        platform.transform.localScale=new Vector3(4,.25f,4);
        platform.transform.position=feet-Vector3.up*.175f;
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(shader)
        {
            platformMaterial=new Material(shader) {color=new Color(.35f,.8f,1)};
            platform.GetComponent<Renderer>().sharedMaterial=platformMaterial;
        }
        Physics.SyncTransforms();
        MelonLogger.Msg("[SafetyPlatform] Created once beneath player after map load.");
    }
    internal static void SummonCube(GameObject marker) => Summon(marker,"RockCube");
    internal static void Summon(GameObject marker,string resourceName)
    {
        if(!Runtime.GameplayAllowed) return;
        Physics.SyncTransforms();
        if(!Nearest(marker.transform.position,100,out var hit) || hit.collider.gameObject.layer!=9
            || hit.normal.y<.5f || hit.collider.GetComponentInParent<KillPlayerOnCollision>()
            || hit.collider.GetComponentInParent<KillStructureOnImpact>() || hit.collider.GetComponentInParent<Structure>())
        { MelonLogger.Msg("[MapLoad] "+marker.name+": skipped; no summonable ground directly below.");return; }
        var pool=PoolManager.instance;
        if(!pool) throw new InvalidOperationException("Structure pool unavailable.");
        Structure prefab=null;
        foreach(var candidate in pool.GetAllStructurePrefabsFromPool())
            if(candidate && string.Equals(candidate.ResourceName,resourceName,StringComparison.OrdinalIgnoreCase))
            { prefab=candidate;break; }
        if(!prefab) throw new InvalidOperationException("Gameplay "+resourceName+" prefab unavailable in structure pool.");
        var pooled=pool.GetPooledObject(prefab.ResourceName,hit.point,marker.transform.rotation);
        var cube=pooled ? pooled.TryCast<Structure>() : null;
        if(!cube)
        {
            if(pooled) pooled.ReturnToPool();
            throw new InvalidOperationException("Pool did not return a gameplay Structure.");
        }
        structures[cube.GetInstanceID()]=cube;
        try
        {
            cube.transform.SetPositionAndRotation(hit.point,marker.transform.rotation);
            Physics.SyncTransforms();
            // World collider bounds account for the complete marker rotation, including tilt.
            if(!cube.rigidBody) cube.rigidBody=cube.GetComponentInChildren<Rigidbody>(true);
            if(!cube.collider) cube.collider=cube.GetComponentsInChildren<Collider>(true)
                .FirstOrDefault(c=>c && !c.isTrigger && c.attachedRigidbody==cube.rigidBody);
            if(!cube.collider) throw new InvalidOperationException("Structure collider unavailable.");
            var position=cube.transform.position;
            position.y+=hit.point.y-cube.collider.bounds.min.y+.02f;
            cube.transform.SetPositionAndRotation(position,marker.transform.rotation);
            if(cube.rigidBody)
            {
                cube.rigidBody.position=position;cube.rigidBody.rotation=marker.transform.rotation;
                cube.rigidBody.velocity=Vector3.zero;cube.rigidBody.angularVelocity=Vector3.zero;
            }
            cube.CurrentPhysicsState=Structure.PhysicsState.Free;
            Physics.SyncTransforms();
            MelonLogger.Msg("[MapLoad] Summoned "+prefab.ResourceName+" for "+marker.name);
        }
        catch
        {
            structures.Remove(cube.GetInstanceID());cube.ReturnToPool();throw;
        }
    }
    // A reused pool instance belongs to its new caller, so never clean it up as our old cube.
    internal static void Forget(Structure cube) { if(cube) structures.Remove(cube.GetInstanceID()); }
    private static void ClearPlatform()
    {
        if(platform) {platform.SetActive(false);Object.Destroy(platform);} platform=null;
        if(platformMaterial) Object.Destroy(platformMaterial);platformMaterial=null;
    }
    internal static void Clear()
    {
        ClearPlatform();
        var owned=new List<Structure>(structures.Values);structures.Clear();
        foreach(var cube in owned)
            try {if(cube && !cube.IsInPool) cube.ReturnToPool();}
            catch(Exception ex) {MelonLogger.Warning("[MapLoad] Structure cleanup: "+ex.Message);}
    }
}

[HarmonyPatch(typeof(Structure),nameof(Structure.OnFetchFromPool))]
internal static class CubeLeasePatch
{
    private static void Prefix(Structure __instance) => LoadFeatures.Forget(__instance);
}

