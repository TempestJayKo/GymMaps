using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Social.Phone;
using MelonLoader;
using UnityEngine;

namespace GymMaps;

public sealed class CommandContext
{
    public string PrimitiveName { get; }
    public string MapName => Runtime.ActiveName;
    public GameObject Primitive { get; }
    internal CommandContext(GameObject primitive) { Primitive=primitive; PrimitiveName=primitive.name; }
}

// Commands are explicitly registered mod callbacks, not OS/shell commands.
public static class MapCommands
{
    internal static bool Enabled { get; private set; }
    internal static void SetEnabled(bool enabled) => Enabled=enabled;
    private static readonly Dictionary<string,Action<CommandContext,string>> handlers=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Stopwatch timer=new();
    public static void Register(string name,Action<CommandContext,string> callback)
    {
        if(string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[]{' ','(',')','='})>=0)
            throw new ArgumentException("Command name must be one word.");
        if(callback==null) throw new ArgumentNullException(nameof(callback));
        if(!handlers.TryAdd(name,callback)) throw new ArgumentException("Command already registered: "+name);
    }
    public static bool Unregister(string name) => handlers.Remove(name);
    private static string Name(string command) => command.Split(new[]{' '},2)[0];
    internal static void Validate(TriggerTags tags)
    {
        foreach(string command in tags.OnEnter.Concat(tags.OnLoad))
        {
            if(Name(command).Equals("interface.killfloor.height",StringComparison.OrdinalIgnoreCase) && !tags.Height.HasValue)
                throw new FormatException("interface.killfloor.height requires a (Height=NUMBER) tag.");
            if(TryMapButton(command,out var buttonMap)) { Runtime.ResolveMap(buttonMap);continue; }
            if(TryMapLoad(command,out var target,out _)) { Runtime.ResolveMap(target);continue; }
            if(GameSounds.IsCommand(command)) continue;
            if(!handlers.ContainsKey(Name(command))) throw new FormatException("Unknown map command: "+Name(command));
        }
    }
    internal static void Boot()
    {
        SetEnabled(false);
        Register("log",(c,args)=>MelonLogger.Msg("[MapTrigger] "+c.PrimitiveName+": "+args));
        Register("status",(c,args)=>Runtime.ReportTrigger(args));
        Register("timer.start",(c,args)=>{timer.Restart();Runtime.ReportTrigger("Timer started");});
        Register("timer.stop",(c,args)=>{timer.Stop();Runtime.ReportTrigger($"Finish: {timer.Elapsed.TotalSeconds:F3}s");});
        Register("timer.reset",(c,args)=>{timer.Reset();Runtime.ReportTrigger("Timer reset");});
        Register("structure.summon.cube",(c,args)=>LoadFeatures.SummonCube(c.Primitive));
        foreach(var pair in new Dictionary<string,string> { ["wall"]="Wall",["pillar"]="Pillar",["ball"]="Ball",["disk"]="Disc",["disc"]="Disc" })
        {
            string resource=pair.Value;
            Register("structure.summon."+pair.Key,(c,args)=>LoadFeatures.Summon(c.Primitive,resource));
        }
        Register("summon.asset.button.interface.teleport.gym",(c,args)=>AssetButtons.SpawnGym(c.Primitive));
        Register("interface.respawn.start",(c,args)=>RespawnPoint.Set(c.Primitive));
        Register("interface.killfloor.height",(c,args)=>MapTools.SetKillFloorHeight(TriggerTags.Parse(c.PrimitiveName).Height.Value));
        Register("play.sound.telephone",(c,args)=>TelephoneSound.Play(c.Primitive));
        Register("play.sound.firework",(c,args)=>FireworkSound.Play(c.Primitive));
    }
    internal static void ResetMap() { timer.Reset();TelephoneSound.Clear();FireworkSound.Clear();GameSounds.Clear();LoadFeatures.Clear();RespawnPoint.Clear();MapTools.Clear();AssetButtons.Clear(); }
    internal static void Shutdown() { handlers.Clear();ResetMap(); }
    private static bool TryMapLoad(string command,out string target,out bool append)
    {
        target=null;append=false;
        const string prefix="map.load.";
        if(!command.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) return false;
        int separator=command.LastIndexOf('.');
        if(separator<=prefix.Length) throw new FormatException("Map command needs a map name and replace/append: "+command);
        string mode=command.Substring(separator+1);
        append=mode.Equals("append",StringComparison.OrdinalIgnoreCase);
        if(!append && !mode.Equals("replace",StringComparison.OrdinalIgnoreCase)) throw new FormatException("Map command must end in .replace or .append: "+command);
        target=command.Substring(prefix.Length,separator-prefix.Length);
        return true;
    }
    private static bool TryMapButton(string command,out string mapName)
    {
        const string prefix="summon.asset.button.interface.teleport.map.";
        mapName=null;
        if(!command.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) return false;
        mapName=command.Substring(prefix.Length);
        if(string.IsNullOrWhiteSpace(mapName)) throw new FormatException("Map button requires a destination map name.");
        return true;
    }
    internal static void Run(GameObject primitive,string command)
    {
        if(!Enabled || !Runtime.GameplayAllowed) return;
        string name=Name(command),args=command.Length>name.Length?command.Substring(name.Length+1):"";
        try
        {
            if(GameSounds.IsCommand(command)) { GameSounds.Play(primitive,command);return; }
            if(TryMapButton(command,out var buttonMap)) { AssetButtons.SpawnMap(primitive,buttonMap);return; }
            if(TryMapLoad(command,out var target,out bool append)) { Runtime.Request(Runtime.ResolveMap(target),append);return; }
            if(handlers.TryGetValue(name,out var callback)) callback(new CommandContext(primitive),args);
        }
        catch(Exception ex) { MelonLogger.Warning("[MapTrigger] "+primitive.name+": "+ex.Message); }
    }
}

internal static class GameSounds
{
    private const string Prefix="play.sound.clip.";
    private static readonly List<GameObject> playing=new();
    // UTF-8 hex keeps every asset name intact while remaining safe in dot tags.
    private static string Token(string name) => "id-"+Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(name));
    internal static bool IsCommand(string command)
    {
        if(!command.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)) return false;
        Decode(command);
        return true;
    }
    private static string Decode(string command)
    {
        string token=command.Substring(Prefix.Length);
        if(!token.StartsWith("id-",StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Sound clip commands require an id from sound-templates.md.");
        try
        {
            string name=new System.Text.UTF8Encoding(false,true).GetString(Convert.FromHexString(token.Substring(3)));
            if(string.IsNullOrWhiteSpace(name)) throw new FormatException();
            return name;
        }
        catch(Exception ex) when(ex is FormatException || ex is System.Text.DecoderFallbackException)
        { throw new FormatException("Invalid sound clip id."); }
    }
    internal static void ExportCatalog()
    {
        try
        {
            var lines=new List<string> { "Loaded game audio clips. Enable trigger commands before use.",
                "Refresh this catalog after visiting scenes that load additional sounds." };
            foreach(string name in Resources.FindObjectsOfTypeAll<AudioClip>().Where(c=>c).Select(c=>c.name)
                .Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal))
            {
                lines.Add("Asset: "+name);
                lines.Add("CubeSound.OnTriggerEnter."+Prefix+Token(name));
                lines.Add("CubeSound.OnMapLoad."+Prefix+Token(name));
            }
            System.IO.Directory.CreateDirectory(Runtime.MapDirectory);
            // Markdown extension prevents the map scanner from treating this as a map.
            string path=System.IO.Path.Combine(Runtime.MapDirectory,"sound-templates.md");
            System.IO.File.WriteAllLines(path,lines);
            Runtime.ReportTrigger("Sound templates exported to "+path);
        }
        catch(Exception ex) { MelonLogger.Warning("Sound catalog: "+ex.Message); }
    }
    internal static void Play(GameObject marker,string command)
    {
        string name=Decode(command);
        AudioClip clip=null;
        foreach(var candidate in Resources.FindObjectsOfTypeAll<AudioClip>())
            if(candidate && candidate.name.Equals(name,StringComparison.Ordinal)) { clip=candidate;break; }
        if(!clip) clip=Resources.Load<AudioClip>(name);
        if(!clip) throw new InvalidOperationException("Sound asset is not loaded: "+name);
        playing.RemoveAll(go=>!go);
        var emitter=new GameObject("GymMaps.Sound."+name);
        playing.Add(emitter);
        try
        {
            emitter.transform.position=marker.transform.position;
            var audio=emitter.AddComponent<AudioSource>();
            audio.playOnAwake=false;audio.loop=false;audio.clip=clip;
            foreach(var original in Resources.FindObjectsOfTypeAll<AudioSource>())
                if(original && original!=audio && original.clip==clip)
                { audio.outputAudioMixerGroup=original.outputAudioMixerGroup;audio.volume=original.volume;break; }
            audio.pitch=1;audio.spatialBlend=1;audio.minDistance=2;audio.maxDistance=30;
            audio.Play();
            UnityEngine.Object.Destroy(emitter,clip.length+.25f);
        }
        catch { UnityEngine.Object.Destroy(emitter);throw; }
    }
    internal static void Clear()
    {
        foreach(var emitter in playing) if(emitter) UnityEngine.Object.Destroy(emitter);
        playing.Clear();
    }
}

internal static class TelephoneSound
{
    private static readonly List<GameObject> playing=new();
    internal static void Play(GameObject marker)
    {
        AudioSource original=null;
        foreach(var phone in Resources.FindObjectsOfTypeAll<PhoneHandler>())
            if(phone && phone.gameObject.scene.IsValid() && phone.notificationAudioSource && phone.notificationAudioSource.clip)
            { original=phone.notificationAudioSource;break; }
        if(!original) throw new InvalidOperationException("Telephone notification audio is not loaded in this scene.");
        playing.RemoveAll(go=>!go);
        var emitter=new GameObject("GymMaps.TelephoneSound");
        playing.Add(emitter);
        emitter.transform.position=marker.transform.position;
        var audio=emitter.AddComponent<AudioSource>();
        audio.playOnAwake=false;audio.loop=false;audio.clip=original.clip;
        audio.outputAudioMixerGroup=original.outputAudioMixerGroup;
        audio.volume=original.volume;audio.pitch=1;
        audio.spatialBlend=1;audio.minDistance=2;audio.maxDistance=30;
        audio.Play();
        UnityEngine.Object.Destroy(emitter,original.clip.length+0.25f);
        MelonLogger.Msg("[MapTrigger] play.sound.telephone: "+original.clip.name+" at "+marker.name);
    }
    internal static void Clear()
    {
        foreach(var go in playing) if(go) UnityEngine.Object.Destroy(go);
        playing.Clear();
    }
}

internal sealed class TrackedPrimitive
{
    public GameObject Object;
    public Bounds LocalBounds;
    public TriggerTags Tags;
    public EntryGate Gate=new();
    public Vector3 Previous;
    public bool HasPrevious;
    public bool Entered(Vector3 position,double now)
    {
        Vector3 point=Object.transform.InverseTransformPoint(position);
        bool inside=LocalBounds.Contains(point);
        bool crossed=HasPrevious && SegmentIntersects(Previous,point,LocalBounds);
        Previous=point;HasPrevious=true;
        return Gate.Step(inside,crossed,now,Tags.Once,Tags.Cooldown);
    }
    // Slab intersection also catches a fast player passing through a thin zone between frames.
    internal static bool SegmentIntersects(Vector3 a,Vector3 b,Bounds box)
    {
        float lo=0,hi=1;var min=box.min;var max=box.max;
        for(int i=0;i<3;i++)
        {
            float delta=b[i]-a[i];
            if(Math.Abs(delta)<1e-8f) { if(a[i]<min[i] || a[i]>max[i]) return false;continue; }
            float x=(min[i]-a[i])/delta,y=(max[i]-a[i])/delta;
            if(x>y) (x,y)=(y,x);
            lo=Math.Max(lo,x);hi=Math.Min(hi,y);if(lo>hi) return false;
        }
        return true;
    }
}

internal static class TriggerTracking
{
    private static List<TrackedPrimitive> current=new();
    private static int playerId;
    private static double nextErrorLog;
    internal static void Set(List<TrackedPrimitive> entries) { Clear();current=entries; }
    internal static void Clear() { current=new();playerId=0;MapCommands.ResetMap(); }
    internal static void Append(List<TrackedPrimitive> entries)
    { var combined=new List<TrackedPrimitive>(current);combined.AddRange(entries);current=combined; }
    internal static int Count => current.Count;
    internal static void Tick()
    {
        if(!Runtime.GameplayAllowed || !MapCommands.Enabled) { ResetTracking();return; }
        if(current.Count==0) return;
        try
        {
            var manager=PlayerManager.instance;
            var controller=manager?.localPlayer?.Controller;
            var physics=controller ? controller.GetComponentInChildren<PlayerPhysics>(true) : null;
            if(!physics || !physics.PhysicsRigidbody) { ResetTracking();return; }
            int id=controller.GetInstanceID();
            if(id!=playerId) { ResetTracking();playerId=id; }
            Vector3 position=physics.PhysicsRigidbody.position;
            double time=Time.realtimeSinceStartupAsDouble;
            var snapshot=current;
            foreach(var entry in snapshot)
            {
                if(Runtime.IsBusy || !ReferenceEquals(snapshot,current)) break;
                if(!entry.Object || !entry.Object.activeInHierarchy || !entry.Entered(position,time)) continue;
                foreach(string command in entry.Tags.OnEnter)
                {
                    if(Runtime.IsBusy || !ReferenceEquals(snapshot,current) || !entry.Object) break;
                    MapCommands.Run(entry.Object,command);
                }
            }
        }
        catch(Exception ex)
        {
            if(Time.realtimeSinceStartupAsDouble>=nextErrorLog)
            { nextErrorLog=Time.realtimeSinceStartupAsDouble+10;MelonLogger.Warning("[MapTrigger] "+ex.Message); }
        }
    }
    private static void ResetTracking()
    {
        if(playerId==0) return;
        foreach(var entry in current) { entry.Gate.ResetOccupancy();entry.HasPrevious=false; }
        playerId=0;
    }
}
