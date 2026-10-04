using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GymMaps;

// These tags travel in the existing primitive name; the five-line map format is unchanged.
public sealed class TriggerTags
{
    public readonly List<string> OnEnter = new();
    public readonly List<string> OnLoad = new();
    public bool TriggerOnly, Once, Placeholder, Marker;
    public double Cooldown = 0.5;
    public float? Height;
    public static TriggerTags Parse(string name)
    {
        var result = new TriggerTags();
        string untagged=Regex.Replace(name,@"\([^()]*\)","");
        var events=Regex.Matches(untagged,@"\.On(?:TriggerEnter|MapLoad)(?=\.|$)",RegexOptions.IgnoreCase);
        if(events.Count>1) throw new FormatException("Use one dot event per marker: "+name);
        if(events.Count==1)
        {
            var evt=events[0];
            string template=untagged.Substring(evt.Index+evt.Length);
            if(!template.StartsWith(".")) throw new FormatException("Event requires a template: "+name);
            template=Regex.Replace(template.Substring(1),@"\.\d{3,}$","");
            if(!Regex.IsMatch(template,@"^[A-Za-z][A-Za-z0-9_.-]*$") || template.EndsWith(".") || template.Contains(".."))
                throw new FormatException("Invalid event template: "+name);
            result.Marker=true;result.TriggerOnly=true;
            (evt.Value.Equals(".OnMapLoad",StringComparison.OrdinalIgnoreCase)?result.OnLoad:result.OnEnter).Add(template);
        }
        foreach(Match start in Regex.Matches(name,@"\((?:OnEnter|Cooldown|Height|Trigger|Once|Placeholder)\b",RegexOptions.IgnoreCase))
        {
            int close=name.IndexOf(')',start.Index),nested=name.IndexOf('(',start.Index+1);
            if(close<0 || (nested>=0 && nested<close)) throw new FormatException("Unclosed trigger tag: "+name);
        }
        foreach (Match match in Regex.Matches(name, @"\(([^()]*)\)"))
        {
            string tag = match.Groups[1].Value.Trim();
            int separator = tag.IndexOf('=');
            string key = (separator < 0 ? tag : tag.Substring(0, separator)).Trim().ToLowerInvariant();
            string value = separator < 0 ? "" : tag.Substring(separator + 1).Trim();
            switch (key)
            {
                case "onenter":
                    if (value.Length == 0) throw new FormatException("Empty OnEnter command: " + name);
                    result.OnEnter.Add(value); break;
                case "cooldown":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result.Cooldown)
                        || !double.IsFinite(result.Cooldown) || result.Cooldown < 0)
                        throw new FormatException("Invalid Cooldown: " + name);
                    break;
                case "height":
                    if(result.Height.HasValue || !float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float height) || !float.IsFinite(height))
                        throw new FormatException("Height requires one finite world Y value: "+name);
                    result.Height=height;
                    break;
                case "trigger": case "once": case "placeholder":
                    if (separator >= 0) throw new FormatException("Flag tag takes no value: " + tag);
                    if (key == "trigger") result.TriggerOnly = true;
                    if (key == "once") result.Once = true;
                    if (key == "placeholder") result.Placeholder = true;
                    break;
            }
        }
        if (result.Placeholder && (result.OnEnter.Count > 0 || result.OnLoad.Count > 0))
            throw new FormatException("A Placeholder cannot also run OnEnter commands: " + name);
        return result;
    }
}

// Separate from Unity so enter/cooldown/once behavior is testable without the game.
public sealed class EntryGate
{
    private bool initialized, inside, fired;
    private double last = double.NegativeInfinity;
    public void ResetOccupancy() { initialized=false;inside=false; }
    public bool Step(bool nowInside, bool crossed, double time, bool once, double cooldown)
    {
        if (!initialized) { initialized = true; inside = nowInside; return false; }
        bool entered = !inside && (nowInside || crossed);
        inside = nowInside;
        if (!entered || (once && fired) || time - last < cooldown) return false;
        fired = true; last = time; return true;
    }
}
