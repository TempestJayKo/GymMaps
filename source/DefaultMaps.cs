using System;
using System.IO;
using System.Linq;
using MelonLoader;

namespace GymMaps;

// Ship the map bytes inside the DLL; no separate download or network access is needed.
internal static class DefaultMaps
{
    private const string Prefix = "GymMaps.DefaultMaps.";

    internal static void AddMissing(string directory)
    {
        Directory.CreateDirectory(directory);
        string installedMarker = Path.Combine(directory, ".default-maps-installed");
        if (File.Exists(installedMarker)) return;
        var assembly = typeof(DefaultMaps).Assembly;
        int added = 0;
        bool complete = true;
        foreach (string resource in assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)
                && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            string filename = resource.Substring(Prefix.Length);
            string temporary = null;
            try
            {
                if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || !Path.GetFileName(filename).Equals(filename, StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid bundled map filename.");
                string target = Path.Combine(directory, filename);
                if (File.Exists(target)) continue;
                temporary = Path.Combine(directory, ".GymMaps-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var input = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidDataException("Bundled map resource unavailable."))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
                // Validate before publishing, and never replace a user's edited map.
                MapFile.Read(temporary);
                try { File.Move(temporary, target); added++; }
                catch (IOException) when (File.Exists(target)) { /* Another writer supplied this map first. */ }
            }
            catch (Exception ex)
            {
                complete = false;
                MelonLogger.Warning("[GymMaps] Could not add default map " + filename + ": " + ex.Message);
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) { MelonLogger.Warning("[GymMaps] Temporary map cleanup: " + ex.Message); }
                }
            }
        }
        if (added > 0) MelonLogger.Msg("[GymMaps] Added " + added + " bundled default maps to " + directory);
        if (complete) File.WriteAllText(installedMarker, "Default maps installed once.");
    }
}
