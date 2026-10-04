using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppRUMBLE.Utilities;
using MelonLoader;
using MelonLoader.Utils;
using UIFramework;
using UIFramework.Models;
using UIFramework.UiExtensions;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(GymMaps.Mod), "GymMaps", "1.5.0", "Colli", null)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("UIFramework", "RumbleModdingAPI")]

namespace GymMaps;

public sealed class Mod : MelonMod
{
    public override void OnInitializeMelon() => Runtime.Boot(this);
    public override void OnDeinitializeMelon() => Runtime.Shutdown();
    public override void OnSceneWasUnloaded(int buildIndex,string sceneName)
    {
        if(sceneName=="Gym") Runtime.SuspendForScene();
    }
}

// Runtime entries take effect immediately, without UIFramework's preferences Save step.
public sealed class MenuEntry : DataEntryModelBase
{
    private readonly string id, title, description;
    private readonly IUiExtension extension;
    public MenuEntry(CategoryModelBase category, string id, string title, string description,
        object value, IUiExtension extension) : base(category)
    { this.id=id; this.title=title; this.description=description; ModelBoxedValue=value; this.extension=extension; }
    public override string Identifier => id;
    public override string DisplayName => title;
    public override string Description => description;
    public override object ModelBoxedValue { get; protected set; }
    public override IUiExtension UiExtension => extension;
    public void Set(object value) => ModelBoxedValue=value;
}
public sealed class ActionDescriptor : DefaultValidator, IButtonDescriptor
{
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string ButtonText { get; set; }
    public Action Handler { get; set; }
}

public static class Runtime
{
    private static bool started, busy, wasInGym;
    private static int generation;
    private static readonly List<string> loadedFiles=new();
    internal static bool IsBusy => busy;
    private static GameObject activeMap;
    private static List<Material> mapMaterials = new();
    private static MelonModel menu;
    private static MenuEntry selection, statusEntry;
    private static DynamicDropdownDescriptor dropdown;
    private static List<string> files = new();
    public static string ActiveName { get; private set; } = "None";
    public static string Status { get; private set; } = "Ready";
    public static int ActiveCount => activeMap ? activeMap.transform.childCount : 0;
    public static string MapDirectory => Path.Combine(MelonEnvironment.GameRootDirectory, "UserData", "GymMaps");
    private const string RootName = "GymMaps.ActiveMap";

    public static void Boot(MelonBase owner)
    {
        if (started) return;
        LoadFeatures.SetEnabled(false);
        MapCommands.Boot();
        menu=UI.RegisterMelon(owner);
        menu._displayName="GymMaps";
        var category=new EmptyCategory("Maps", "Maps", menu);
        menu.AddModelCategory(category);
        dropdown=new DynamicDropdownDescriptor();
        selection=new MenuEntry(category,"SelectedMap","Map","Choose a map, then press Load.","",dropdown);
        statusEntry=new MenuEntry(category,"Status","Status","Map actions are available in the Gym.",Status,
            new TextInputBehaviorDescriptor { IsReadOnly=true });
        category.AddEntry(selection);
        AddButton(category,"Load","Load selected map",LoadSelected);
        AddButton(category,"Refresh","Refresh map list",Refresh);
        AddButton(category,"Unload","Unload custom map",Unload);
        category.AddEntry(statusEntry, new MenuEntry(category,"Folder","Map folder",
            "Add MapCreator .txt maps here, then press Refresh.",MapDirectory,new TextInputBehaviorDescriptor { IsReadOnly=true }));
        var safety=new EmptyCategory("Tools", "Tools", menu);
        menu.AddModelCategory(safety);
        safety.AddEntry(new MenuEntry(safety,"EnableTriggerCommands","Enable trigger commands",
            "Allows commands that change game and player behavior. Use at your own risk.",false,
            new UserEditNotifier { OnUserEdit=value=>MapCommands.SetEnabled(value is bool enabled && enabled) }));
        safety.AddEntry(new MenuEntry(safety,"SafetyPlatform","Safety Platform",
            "Spawns in a safety platform preventing you from falling on map loading",false,
            new UserEditNotifier { OnUserEdit=value=>LoadFeatures.SetEnabled(value is bool enabled && enabled) }));
        safety.AddEntry(new MenuEntry(safety,"KillFloorStep","Kill floor raise amount",
            "World units raised per click. A missing floor starts below the course.",1f,
            new SliderDescriptor {Min=.25f,Max=25,DecimalPlaces=2,OnUserEdit=value=>MapTools.SetRaiseAmount(Convert.ToSingle(value))}));
        AddButton(safety,"RaiseKillFloor","Raise Kill Floor",MapTools.RaiseKillFloor);
        started=true;
        wasInGym=InGym();
        MelonEvents.OnUpdate.Subscribe(Tick);
        try { DefaultMaps.AddMissing(MapDirectory); }
        catch(Exception ex) { MelonLogger.Warning("Default map installation: " + ex.Message); }
        Refresh();
        Log("GymMaps UI ready. F9; folder: " + MapDirectory);
    }
    private static void AddButton(EmptyCategory category,string id,string title,Action action) =>
        category.AddEntry(new MenuEntry(category,id,title,"",id,new ActionDescriptor { ButtonText=id,Handler=action }));
    internal static bool GameplayAllowed => started && SceneManager.GetActiveScene().name == "Gym";
    private static bool InGym() => SceneManager.GetActiveScene().name == "Gym";
    internal static void SuspendForScene()
    {
        if(!started) return;
        ClearOwned();
        wasInGym=false;
        SetStatus("GymMaps disabled outside the Gym (matches and Park).");
    }
    private static void Tick()
    {
        bool gym=InGym();
        if(wasInGym && !gym) SuspendForScene();
        if(!wasInGym && gym) SetStatus("Ready — select a map and press Load.");
        wasInGym=gym;

        if(gym && !busy) { MapTools.CheckKillFloor();TriggerTracking.Tick(); }
    }
    public static void Refresh()
    {
        try
        {
            Directory.CreateDirectory(MapDirectory);
            var discovered=new List<string>(); int skipped=0;
            foreach(var path in Directory.EnumerateFiles(MapDirectory,"*",SearchOption.TopDirectoryOnly)
                .Where(p=>Path.GetExtension(p).Equals(".txt",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
            {
                try { MapFile.Read(path); discovered.Add(Path.GetFileName(path)); }
                catch(Exception ex) { skipped++; Log("Skipped " + Path.GetFileName(path) + ": " + ex.Message); }
            }
            files=discovered;
            string selected=selection.ModelBoxedValue as string;
            if(!files.Contains(selected)) selected=files.FirstOrDefault() ?? "";
            selection.Set(selected);
            dropdown.SetDropdownItems(files.Count>0 ? files.Select(f=>new DropdownItem(Path.GetFileNameWithoutExtension(f),f)).ToList()
                : new List<DropdownItem> { new DropdownItem("No valid maps found","") });
            SetStatus($"Found {files.Count} maps" + (skipped>0 ? $"; skipped {skipped} invalid files" : "") + $". Active: {ActiveName}");
        }
        catch(Exception ex) { SetStatus("Refresh failed: " + ex.Message); Log(ex.ToString()); }
    }
    public static void LoadSelected() => Request(selection.ModelBoxedValue as string);
    internal static string ResolveMap(string mapName)
    {
        if(string.IsNullOrWhiteSpace(mapName) || mapName=="." || mapName==".." || mapName.IndexOfAny(Path.GetInvalidFileNameChars())>=0
            || !Path.GetFileName(mapName).Equals(mapName,StringComparison.Ordinal)) throw new FormatException("Invalid map name: "+mapName);
        string requested=mapName.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)?mapName:mapName+".txt";
        foreach(string file in Directory.EnumerateFiles(MapDirectory,"*",SearchOption.TopDirectoryOnly))
            if(Path.GetFileName(file).Equals(requested,StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(file);
        throw new FileNotFoundException("Map not found: "+requested);
    }
    public static void Request(string filename) => Request(filename,false);
    public static void Request(string filename,bool append,bool teleport=false)
    {
        if(busy) return;
        if(!GameplayAllowed) { SetStatus("GymMaps disabled outside the Gym (matches and Park)."); return; }
        try { filename=ResolveMap(filename); }
        catch(Exception ex) { SetStatus(ex.Message);return; }
        if(append && loadedFiles.Contains(filename,StringComparer.OrdinalIgnoreCase))
        { SetStatus("Map already present: "+Path.GetFileNameWithoutExtension(filename));return; }
        busy=true;
        MelonCoroutines.Start(Switch(filename,++generation,append,teleport));
    }
    private static IEnumerator Switch(string filename,int ticket,bool append=false,bool teleport=false)
    {
        try
        {
        GameObject staged=null;
        List<Material> stagedMaterials=new();
        List<TrackedPrimitive> stagedTriggers=new();
        MapEntry[] entries=null;
        Exception failure=null;
        try
        {
            entries=MapFile.Read(Path.Combine(MapDirectory,filename));
            foreach(var e in entries) MapCommands.Validate(e.Tags);
            if(teleport && !entries.Any(e=>e.Tags.OnLoad.Any(c=>c.Equals("interface.respawn.start",StringComparison.OrdinalIgnoreCase))))
                throw new FormatException("Map teleport requires CubeStart.OnMapLoad.interface.respawn.start in the destination map.");
        }
        catch(Exception ex) { failure=ex; }
        if(failure!=null) { Fail(failure); yield break; }
        SetStatus("Loading " + Path.GetFileNameWithoutExtension(filename));
        try
        {
            staged = new GameObject(RootName + ".Staging"); staged.SetActive(false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("RUMBLE surface shader unavailable.");
            var materials = new Dictionary<string, Material>();
            foreach (var entry in entries)
            {
                // Authoring-only marker; never create its renderer or collision in the loaded map.
                if(entry.Tags.Placeholder) continue;
                if(entry.Tags.Marker)
                {
                    // The authoring cube becomes only an invisible transform + tracked volume.
                    // It has no renderer, mesh or physical collider in the loaded map.
                    var marker=new GameObject(entry.Name);
                    marker.transform.SetParent(staged.transform,false);
                    marker.transform.localPosition=Vector(entry.Position);
                    marker.transform.localRotation=Quaternion.Euler(Vector(entry.Rotation));
                    marker.transform.localScale=Vector(entry.Scale);
                    stagedTriggers.Add(new TrackedPrimitive { Object=marker,Tags=entry.Tags,LocalBounds=new Bounds(Vector3.zero,Vector3.one) });
                    continue;
                }
                var primitive = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), entry.Shape);
                var go = GameObject.CreatePrimitive(primitive);
                go.transform.SetParent(staged.transform, false); go.name = entry.Name;
                go.transform.localPosition = Vector(entry.Position);
                go.transform.localRotation = Quaternion.Euler(Vector(entry.Rotation));
                go.transform.localScale = Vector(entry.Scale);
                foreach (var col in go.GetComponents<Collider>()) { col.enabled=false; Object.Destroy(col); }
                if (entry.Has("visual") || entry.Tags.TriggerOnly) go.layer=0;
                else
                {
                    go.layer=entry.Has("environment") ? 10 : 9;
                    var mesh = go.AddComponent<MeshCollider>(); mesh.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                    if (entry.Has("wall")) go.AddComponent<BoxCollider>();
                    if (entry.Has("death")) go.AddComponent<KillPlayerOnCollision>();
                    if (entry.Has("environment")) go.AddComponent<KillStructureOnImpact>();
                }
                if(entry.Tags.OnEnter.Count>0)
                    stagedTriggers.Add(new TrackedPrimitive { Object=go,Tags=entry.Tags,LocalBounds=go.GetComponent<MeshFilter>().sharedMesh.bounds });
                var renderer = go.GetComponent<Renderer>();
                if (entry.Has("invisible")) renderer.enabled = false;
                else
                {
                    string key = string.Join("|",entry.Color);
                    if (!materials.TryGetValue(key,out var material))
                    {
                        material = new Material(shader) { color = new Color(entry.Color[0],entry.Color[1],entry.Color[2]) };
                        materials.Add(key,material); stagedMaterials.Add(material);
                    }
                    renderer.sharedMaterial = material;
                }
            }
        }
        catch (Exception ex) { failure=ex; }
        if (failure != null)
        {
            if (staged) Object.Destroy(staged); ReleaseMaterials(stagedMaterials); Fail(failure); yield break;
        }
        // Build successfully before removing the previous custom map.
        bool retain=append && activeMap;
        if(!retain) DestroyActive();
        int removed=retain?0:ClearMapCreator();
        yield return null;
        if(ticket!=generation || !started || !InGym() || !staged)
        {
            if(staged) Object.Destroy(staged);
            ReleaseMaterials(stagedMaterials);
            if(ticket==generation) busy=false;
            yield break;
        }
        var incoming=stagedTriggers.Where(e=>e.Tags.OnEnter.Count>0).ToList();
        if(retain)
        {
            while(staged.transform.childCount>0) staged.transform.GetChild(0).SetParent(activeMap.transform,true);
            Object.Destroy(staged);
            mapMaterials.AddRange(stagedMaterials);
            TriggerTracking.Append(incoming);
        }
        else
        {
            activeMap=staged; mapMaterials=stagedMaterials;
            staged.name=RootName; staged.SetActive(true);
            TriggerTracking.Set(incoming);
        }
        loadedFiles.Add(filename);
        ActiveName=string.Join(" + ",loadedFiles.Select(Path.GetFileNameWithoutExtension));
        SetStatus($"{(retain?"Appended":"Loaded")} {Path.GetFileNameWithoutExtension(filename)} - {ActiveCount} objects, {TriggerTracking.Count} entry triggers");
        Log($"{(retain?"Appended":"Loaded")} {filename}: {entries.Length} objects; cleared {removed} MapCreator objects.");
        bool safetyRequested=LoadFeatures.Enabled;
        yield return new WaitForFixedUpdate();
        if(ticket!=generation || !started || !InGym() || !activeMap) yield break;
        Physics.SyncTransforms();
        foreach(var entry in stagedTriggers)
        {
            if(ticket!=generation || !entry.Object) yield break;
            foreach(string command in entry.Tags.OnLoad)
            {
                if(ticket!=generation || !entry.Object) yield break;
                MapCommands.Run(entry.Object,command);
            }
        }
        // Wait only for the local body to become available; never poll for airborne state.
        double deadline=Time.realtimeSinceStartupAsDouble+5;
        while((teleport || (safetyRequested && LoadFeatures.Enabled)) && !LoadFeatures.PlayerBody() && Time.realtimeSinceStartupAsDouble<deadline)
        {
            if(ticket!=generation || !started || !InGym() || !activeMap) yield break;
            yield return null;
        }
        if(teleport && ticket==generation && started && InGym() && activeMap)
        {
            bool moved=false;
            try { RespawnPoint.TeleportLocal();moved=true; }
            catch(Exception ex) { ReportTrigger("Map loaded; teleport failed: "+ex.Message); }
            if(moved)
            {
                yield return new WaitForSecondsRealtime(.5f);
                if(ticket!=generation || !started || !InGym() || !activeMap) yield break;
                ReportTrigger("Loaded and teleported to "+Path.GetFileNameWithoutExtension(filename)+".");
            }
        }
        if(ticket==generation && started && InGym() && activeMap && safetyRequested && LoadFeatures.Enabled)
            LoadFeatures.CheckSafetyOnce();
        if(ticket==generation) busy=false;
        }
        finally { if(ticket==generation) busy=false; }
    }

    private static int ClearMapCreator()
    {
        var root=GameObject.Find("StoreHere"); int removed=0;
        if(root) for(int i=root.transform.childCount-1;i>=0;i--)
        { var child=root.transform.GetChild(i).gameObject; child.SetActive(false); Object.Destroy(child); removed++; }
        return removed;
    }
    private static void DestroyActive()
    {
        TriggerTracking.Clear();
        if(activeMap) { activeMap.SetActive(false); Object.Destroy(activeMap); }
        activeMap=null; ReleaseMaterials(mapMaterials); loadedFiles.Clear();ActiveName="None";
    }
    private static void ClearOwned() { generation++; busy=false; DestroyActive(); }
    public static void Unload()
    {
        ClearOwned();
        if(InGym()) ClearMapCreator();
        SetStatus("Custom map unloaded. Active: None");
    }
    private static Vector3 Vector(float[] a) => new(a[0],a[1],a[2]);
    private static void ReleaseMaterials(List<Material> materials)
    { foreach(var mat in materials) if(mat) Object.Destroy(mat); materials.Clear(); }
    private static void SetStatus(string value)
    { Status=value; statusEntry?.Set(value); menu?.RequestUpdateUI(); }
    internal static bool TryCourseBounds(out Bounds bounds)
    {
        bounds=default;bool found=false;
        if(!activeMap) return false;
        foreach(var mesh in activeMap.GetComponentsInChildren<MeshFilter>())
        {
            var renderer=mesh.GetComponent<Renderer>();if(!renderer) continue;
            if(!found) { bounds=renderer.bounds;found=true; } else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }
    internal static bool OwnsMapObject(GameObject source) =>
        activeMap && source && source.transform.IsChildOf(activeMap.transform);
    internal static void ReportTrigger(string value) { SetStatus(value);Log(value); }
    private static void Fail(Exception ex)
    { busy=false; SetStatus("Load failed; previous map kept. " + ex.Message); Log("LOAD FAILED: " + ex); }
    private static void Log(string text) => MelonLogger.Msg("[GymMaps] " + text);
    public static string Inspect()
    {
        string result=$"Gym={InGym()}; active={ActiveName}; count={ActiveCount}; busy={busy}; maps={files.Count}; status={Status}";
        Log(result); return result;
    }
    public static void Shutdown()
    { MelonEvents.OnUpdate.Unsubscribe(Tick); started=false; ClearOwned();  MapCommands.Shutdown();Log("Stopped."); }
    public static void RunTests() => MelonCoroutines.Start(TestAll());
    private static IEnumerator TestAll()
    {
        foreach(string file in files.ToArray())
        {
            selection.TryApply(file); LoadSelected();
            yield return new WaitForSecondsRealtime(.5f);
            var entries=MapFile.Read(Path.Combine(MapDirectory,file));
            int roots=Object.FindObjectsOfType<Transform>().Count(t=>t.name==RootName);
            int expectedColliders=entries.Count(e=>!e.Has("visual") && !e.Tags.TriggerOnly && !e.Tags.Placeholder);
            var colliders=activeMap ? activeMap.GetComponentsInChildren<MeshCollider>(true) : null;
            bool pass=ActiveName==Path.GetFileNameWithoutExtension(file) && ActiveCount==entries.Count(e=>!e.Tags.Placeholder) && roots==1
                && colliders!=null && colliders.Length==expectedColliders && colliders.All(c=>c.sharedMesh);
            Log($"TEST {(pass?"PASS":"FAIL")} {file}: objects={ActiveCount}/{entries.Length}, roots={roots}, colliders={colliders?.Length}/{expectedColliders}");
            if(!pass) yield break;
        }
        Unload(); yield return null;
        Log("TEST " + (ActiveCount==0 && !Object.FindObjectsOfType<Transform>().Any(t=>t.name==RootName) ? "PASS" : "FAIL") + " unload");
        if(files.Count>0)
        {
            LoadSelected(); Unload(); yield return null; yield return null;
            Log("TEST " + (ActiveCount==0 && !Object.FindObjectsOfType<Transform>().Any(t=>t.name.StartsWith(RootName)) ? "PASS" : "FAIL") + " cancel pending load");
        }
    }
}
