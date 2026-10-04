using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppRUMBLE.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace GymMaps;

internal static class MapTools
{
    private sealed class MovedFloor { internal Transform Transform; internal Vector3 Position; }
    private static readonly List<MovedFloor> moved=new();
    private static GameObject created;
    private static Transform selected;
    private static float raiseAmount=1;
    internal static bool ControlsDeathSurface(KillPlayerOnCollision source) =>
        selected && source && selected.GetComponentInParent<KillPlayerOnCollision>()==source;
    internal static void CheckKillFloor()
    {
        if(!Runtime.GameplayAllowed) return;
        if(!selected || !selected.gameObject.activeInHierarchy) return;
        var collider=selected.GetComponent<Collider>();
        var death=selected.GetComponentInParent<KillPlayerOnCollision>();
        if(!collider || !collider.enabled || !death || !death.enabled) return;
        var body=LoadFeatures.PlayerBody();
        if(!body || !body.PhysicsRigidbody) return;
        var position=body.PhysicsRigidbody.position;
        var bounds=collider.bounds;
        float feet=body.FootCollider?body.FootCollider.bounds.min.y:position.y;
        // Falling through a thin collider between frames must still reach the death path.
        if(position.x>=bounds.min.x && position.x<=bounds.max.x && position.z>=bounds.min.z
            && position.z<=bounds.max.z && feet<=bounds.max.y)
            DeathSurfaces.ResetLocal();
    }
    internal static void SetRaiseAmount(float value)
    {
        if(float.IsFinite(value) && value>=.25f && value<=25) raiseAmount=value;
    }
    internal static void SetKillFloorHeight(float height)
    {
        if(!float.IsFinite(height)) throw new ArgumentException("Kill floor height must be finite.");
        if(SceneManager.GetActiveScene().name!="Gym") throw new InvalidOperationException("Kill floors are adjusted in the Gym.");
        if(!selected) selected=FindFloor();
        if(!selected) CreateFloor(height);
        else
        {
            var collider=selected.GetComponent<Collider>();
            if(!collider) throw new InvalidOperationException("Selected kill floor has no collider.");
            RememberFloor();
            selected.position+=Vector3.up*(height-collider.bounds.max.y);
            Physics.SyncTransforms();
        }
        Runtime.ReportTrigger($"Kill floor top set to Y={height:F2}.");
    }
    private static void RememberFloor()
    {
        if((!created || selected!=created.transform) && !moved.Any(f=>f.Transform==selected))
            moved.Add(new MovedFloor {Transform=selected,Position=selected.position});
    }
    private static void CreateFloor(float top)
    {
        Runtime.TryCourseBounds(out var course);
        var player=LoadFeatures.PlayerBody();
        Vector3 center=course.size!=Vector3.zero?course.center:(player && player.PhysicsRigidbody?player.PhysicsRigidbody.position:Vector3.zero);
        created=GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.name="CubeKillFloor(D)(I).GymMaps.Tools";created.layer=9;
        created.transform.localScale=new Vector3(Math.Max(200,course.size.x+40),1,Math.Max(200,course.size.z+40));
        created.transform.position=new Vector3(center.x,top-.5f,center.z);
        created.GetComponent<Renderer>().enabled=false;
        created.GetComponent<Collider>().isTrigger=true;
        created.AddComponent<KillPlayerOnCollision>().useLayermask=false;
        selected=created.transform;Physics.SyncTransforms();
    }
    internal static void RaiseKillFloor()
    {
        if(SceneManager.GetActiveScene().name!="Gym") { Runtime.ReportTrigger("Enter the Gym to adjust the kill floor.");return; }
        if(Runtime.IsBusy) { Runtime.ReportTrigger("Wait for the map to finish loading.");return; }
        try
        {
            if(!selected) selected=FindFloor();
            if(!selected)
            {
                Runtime.TryCourseBounds(out var course);
                var player=LoadFeatures.PlayerBody();
                Vector3 center=course.size!=Vector3.zero?course.center:(player && player.PhysicsRigidbody?player.PhysicsRigidbody.position:Vector3.zero);
                float top=course.size!=Vector3.zero?course.min.y-5:center.y-10;
                CreateFloor(top);
                Runtime.ReportTrigger($"Created kill floor below the course at Y={top:F2}.");
                return;
            }
            RememberFloor();
            selected.position+=Vector3.up*raiseAmount;
            Physics.SyncTransforms();
            Runtime.ReportTrigger($"Raised kill floor {raiseAmount:F2} units. Center Y={selected.position.y:F2}.");
        }
        catch(Exception ex) { MelonLogger.Warning("[Tools] Kill floor: "+ex.Message);Runtime.ReportTrigger("Kill floor adjustment failed: "+ex.Message); }
    }
    private static Transform FindFloor()
    {
        Transform best=null;float area=0;
        // A floor must be a broad, thin, horizontal death collider. Vertical walls are excluded.
        foreach(var kill in Object.FindObjectsOfType<KillPlayerOnCollision>())
        {
            if(!kill || !kill.enabled || !kill.gameObject.activeInHierarchy || !kill.gameObject.scene.IsValid()) continue;
            foreach(var col in kill.GetComponentsInChildren<Collider>())
            {
                if(!col.enabled || !col.gameObject.activeInHierarchy) continue;
                var size=col.bounds.size;
                if(size.x<10 || size.z<10 || size.y>Math.Min(size.x,size.z)*.2f) continue;
                float candidate=size.x*size.z;
                if(candidate>area) { area=candidate;best=col.transform; }
            }
        }
        return best;
    }
    internal static void Clear()
    {
        foreach(var floor in moved) if(floor.Transform) floor.Transform.position=floor.Position;
        moved.Clear();selected=null;
        if(created) {created.SetActive(false);Object.Destroy(created);}created=null;
        Physics.SyncTransforms();
    }
}
