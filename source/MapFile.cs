using System;
using System.Globalization;
using System.IO;

namespace GymMaps;

public sealed class MapEntry
{
    public string Name = "";
    public string Shape = "";
    public float[] Color = Array.Empty<float>();
    public float[] Position = Array.Empty<float>();
    public float[] Rotation = Array.Empty<float>();
    public float[] Scale = Array.Empty<float>();
    public TriggerTags Tags = new();
    public bool Has(string category)
    {
        // Do not mistake command arguments (e.g. 'log death') for physics tags.
        string shortTag=category.ToLowerInvariant() switch
        { "invisible"=>"i", "wall"=>"w", "environment"=>"e", "visual"=>"v", "death"=>"d", _=>category };
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Name,@"\(([^()]*)\)"))
            if(m.Groups[1].Value.Equals(category,StringComparison.OrdinalIgnoreCase) || m.Groups[1].Value.Equals(shortTag,StringComparison.OrdinalIgnoreCase)) return true;
        // Preserve legacy unparenthesized names, excluding all new command tags.
        string baseName=System.Text.RegularExpressions.Regex.Replace(Name,@"\([^()]*\)","");
        var evt=System.Text.RegularExpressions.Regex.Match(baseName,@"\.On(?:TriggerEnter|MapLoad)\.",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        int eventIndex=evt.Success?evt.Index:-1;
        if(eventIndex>=0) baseName=baseName.Substring(0,eventIndex);
        return baseName.IndexOf(category,StringComparison.OrdinalIgnoreCase)>=0;
    }
}

public static class MapFile
{
    public static readonly string[] Files = { "slipnslide.txt", "rings3.txt", "towers.txt", "rings.txt" };
    public static readonly string[] Labels = { "Slip 'n Slide", "Rings 3", "Towers", "Rings" };
    public static MapEntry[] Read(string path) => Parse(File.ReadAllLines(path));
    public static MapEntry[] Parse(string[] lines)
    {
        int end = lines.Length;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
        if (end == 0 || !int.TryParse(lines[0].Trim().Trim('\uFEFF'), out int count) || count < 1 || count > 10000)
            throw new FormatException("Invalid object count.");
        if (end != 1 + count * 5) throw new FormatException("Object count does not match the five-line records.");
        var entries = new MapEntry[count];
        for (int i = 0; i < count; i++)
        {
            int line = 1 + i * 5;
            string name = lines[line].Trim();
            string shape = "";
            foreach (string candidate in new[] { "Capsule", "Cube", "Cylinder", "Plane", "Quad", "Sphere" })
                if (name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) { shape = candidate; break; }
            if (shape.Length == 0) throw new FormatException($"Unsupported primitive at line {line + 1}: {name}");
            var e = new MapEntry { Name = name, Shape = shape, Color = Triple(lines[line+1]),
                Position = Triple(lines[line+2]), Rotation = Triple(lines[line+3]), Scale = Triple(lines[line+4]) };
            foreach (float size in e.Scale) if (size == 0) throw new FormatException($"Zero scale: {name}");
            e.Tags = TriggerTags.Parse(name);
            if(e.Tags.Marker && shape!="Cube") throw new FormatException("Event markers must be Cube primitives: "+name);
            entries[i] = e;
        }
        return entries;
    }
    private static float[] Triple(string line)
    {
        var parts = line.Split('|');
        if (parts.Length != 3) throw new FormatException("Expected three pipe-separated numbers.");
        var values = new float[3];
        for (int i = 0; i < 3; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i]))
                throw new FormatException("Invalid or non-finite number.");
        return values;
    }
}
