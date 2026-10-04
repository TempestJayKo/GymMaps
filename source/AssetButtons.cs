using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using RumbleModdingAPI.RMAPI;
using UnityEngine;
using UnityEngine.Events;
using Object=UnityEngine.Object;

namespace GymMaps;

internal static class AssetButtons
{
    private sealed class OwnedButton
    {
        internal GameObject Object;
        internal UnityAction Callback;
    }
    private static readonly List<OwnedButton> owned=new();
    private static bool returning;
    internal static void SpawnGym(GameObject marker) => Spawn(marker,"TeleportGym",RequestGym);
    internal static void SpawnMap(GameObject marker,string mapName) =>
        Spawn(marker,"TeleportMap."+mapName,source=>RequestMap(source,mapName));
    private static void Spawn(GameObject marker,string purpose,Action<GameObject> pressed)
    {
        if(!Create.newButtonGameObject) throw new InvalidOperationException("RumbleModdingAPI's native button asset is not ready.");
        // The API template is inactive; configure its events before enabling the clone.
        var clone=Object.Instantiate(Create.newButtonGameObject);
        try
        {
            clone.SetActive(false);
            clone.name="GymMaps.Button."+purpose+"."+marker.name;
            clone.transform.SetParent(marker.transform.parent,false);
            clone.transform.SetPositionAndRotation(marker.transform.position,marker.transform.rotation);
            var button=clone.GetComponentInChildren<InteractionButton>(true);
            if(!button) throw new InvalidOperationException("Native button asset has no InteractionButton.");
            button.onPressed=new UnityEvent();
            button.onPressedByPlayer=new UnityEvent<Player>();
            button.onToggledByPlayerController=new UnityEvent<bool,PlayerController>();
            button.isToggleButton=false;button.useLongPress=false;button.disableButtonAfterInit=false;
            button.pressInterval=.75f;button.enabled=true;
            UnityAction action=(UnityAction)(Action)(()=>{ if(Runtime.GameplayAllowed && MapCommands.Enabled) pressed(clone); });
            button.onPressed.AddListener(action);
            var entry=new OwnedButton { Object=clone,Callback=action };
            owned.Add(entry);
            clone.SetActive(true);
            MelonLogger.Msg("[MapLoad] Spawned "+purpose+" button at "+marker.name);
        }
        catch { if(clone) Object.Destroy(clone);throw; }
    }
    private static void RequestMap(GameObject source,string mapName)
    {
        if(returning || Runtime.IsBusy || !source || !source.activeInHierarchy) return;
        returning=true;
        MelonCoroutines.Start(LoadMap(source,mapName));
    }
    private static IEnumerator LoadMap(GameObject source,string mapName)
    {
        yield return null;
        try
        {
            if(Runtime.GameplayAllowed && MapCommands.Enabled && source && source.activeInHierarchy && !Runtime.IsBusy)
                Runtime.Request(mapName,false,true);
        }
        catch(Exception ex)
        {
            MelonLogger.Warning("[MapButton] "+ex.Message);
            Runtime.ReportTrigger("Map button failed: "+ex.Message);
        }
        finally { returning=false; }
    }
    private static void RequestGym(GameObject source)
    {
        if(returning || Runtime.IsBusy || !source || !source.activeInHierarchy) return;
        returning=true;
        MelonCoroutines.Start(ReturnGym(source));
    }
    private static IEnumerator ReturnGym(GameObject source)
    {
        // Leave the native press event before unloading and destroying its button.
        yield return null;
        try
        {
            if(!Runtime.GameplayAllowed || !MapCommands.Enabled || !source || !source.activeInHierarchy || Runtime.IsBusy) yield break;
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Gym")
                throw new InvalidOperationException("Gym teleport buttons are available on Gym custom maps.");
            var manager=PlayerManager.instance;
            var controller=manager?.localPlayer?.Controller;
            var handler=manager?.activeSpawnPointHandler;
            if(!controller || !handler) throw new InvalidOperationException("Gym player or spawn-point handler unavailable.");
            var movement=controller.GetComponentInChildren<PlayerMovement>(true);
            var physics=controller.GetComponentInChildren<PlayerPhysics>(true);
            if(!movement) throw new InvalidOperationException("Player movement subsystem unavailable.");
            var spawn=handler.GetDesiredSpawnPoint(SpawnPointHandler.SpawnPointType.Startup,out bool found);
            if(!found || spawn==null) throw new InvalidOperationException("Gym startup spawn point unavailable.");
            var position=spawn.Position;var rotation=spawn.Rotation;
            var features=movement.ActiveFeatures;
            if(physics) physics.ClearAllVelocities(PlayerPhysics.RigidbodyTarget.Both);
            movement.RepositionWithFade(position,rotation,.25f,.1f,true,features,true);
            Runtime.Unload();
            Runtime.ReportTrigger("Returned to the Gym start.");
        }
        catch(Exception ex)
        {
            MelonLogger.Warning("[GymButton] "+ex.Message);
            Runtime.ReportTrigger("Gym teleport failed: "+ex.Message);
        }
        finally { returning=false; }
    }
    internal static void Clear()
    {
        foreach(var entry in owned)
            if(entry.Object) {entry.Object.SetActive(false);Object.Destroy(entry.Object);}
        owned.Clear();
    }
}
