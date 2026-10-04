using System;
using System.Collections.Generic;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Economy.Interactables;
using MelonLoader;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GymMaps;

internal static class FireworkSound
{
    private static readonly List<GameObject> playing=new();
    internal static void Play(GameObject marker)
    {
        AudioCall purchase=null;
        foreach(var market in Resources.FindObjectsOfTypeAll<GearMarket>())
            if(market && market.gameObject.scene.IsValid() && market.unlockPurchaseSFX)
            { purchase=market.unlockPurchaseSFX;break; }
        if(!purchase) throw new InvalidOperationException("The shop purchase sound asset is not loaded in this scene.");
        var selected=purchase.GetRandomClip();
        if(selected==null || !selected.Clip) throw new InvalidOperationException("The shop purchase sound asset has no playable clip.");
        playing.RemoveAll(go=>!go);
        var emitter=new GameObject("GymMaps.FireworkSound");playing.Add(emitter);
        try
        {
            emitter.transform.position=marker.transform.position;
            var audio=emitter.AddComponent<AudioSource>();
            audio.playOnAwake=false;audio.loop=false;audio.clip=selected.Clip;
            var settings=purchase.GeneralSettings;
            audio.outputAudioMixerGroup=settings.MixerGroup;
            audio.volume=settings.GetRandomVolume();audio.pitch=settings.GetRandomPitch();
            audio.spatialBlend=1;
            var spatial=purchase.SpatialSettings;
            audio.minDistance=spatial!=null?spatial.MinMaxDistance.x:2;
            audio.maxDistance=spatial!=null?spatial.MinMaxDistance.y:30;
            audio.Play();
            Object.Destroy(emitter,selected.Clip.length/Math.Max(.01f,Math.Abs(audio.pitch))+.25f);
            MelonLogger.Msg("[MapTrigger] play.sound.firework: shop unlockPurchaseSFX / "+selected.Clip.name+" at "+marker.name);
        }
        catch { if(emitter) Object.Destroy(emitter);throw; }
    }
    internal static void Clear()
    {
        foreach(var emitter in playing) if(emitter) Object.Destroy(emitter);
        playing.Clear();
    }
}
