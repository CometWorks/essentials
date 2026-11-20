using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ServerPlugin.Regions;

// The per-world file is replaced before publishing its immutable snapshot to simulation threads.
internal sealed class RegionStore
{
    private string path;
    private readonly object gate = new object();
    private volatile CombatRegion[] regions;
    public CombatRegion[] Snapshot => regions;

    public RegionStore(string worldPath)
    {
        path = FilePath(worldPath);
        regions = Read(path) ?? Array.Empty<CombatRegion>();
    }

    // A missing file can inherit a template. A saved, empty list intentionally overrides it.
    private static CombatRegion[] Read(string path)
    {
        try
        {
            XElement root = XElement.Load(path);
            if (root.Name != "Regions" || (int?)root.Attribute("version") != 1)
                throw new InvalidDataException("Unsupported regions file.");
            var loaded = root.Elements().Select(CombatRegion.FromXml).ToArray();
            if (
                loaded.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != loaded.Length
            )
                throw new InvalidDataException("Duplicate region names.");
            return loaded;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public string Change(string name, CombatRegion create = null, RegionRules? edit = null)
    {
        lock (gate)
        {
            int index = Array.FindIndex(regions, r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            if (create != null && index >= 0)
                return $"Region '{name}' already exists.";
            if (create == null && index < 0)
                return $"Unknown region '{name}'.";
            var next = regions.ToList();
            if (create != null)
                next.Add(create);
            else if (edit.HasValue)
                next[index] = next[index].WithRules(edit.Value);
            else
                next.RemoveAt(index);

            CombatRegion[] snapshot = next.ToArray();
            Write(path, snapshot);
            regions = snapshot;
            return create != null ? $"Created region '{name}'."
                : edit.HasValue ? $"Updated region '{name}'."
                : $"Deleted region '{name}'.";
        }
    }

    private static string FilePath(string worldPath) =>
        Path.Combine(Path.GetFullPath(worldPath), "Storage", "Essentials", "Regions.xml");

    public void FollowSave(string worldPath)
    {
        string destination = FilePath(worldPath);
        lock (gate)
        {
            if (destination == path)
                return;
            // The DS loads a premade world from its template, then saves under its live
            // name. Save As has the same behavior. Future edits belong to the new save.
            var existing = Read(destination);
            if (existing != null)
                regions = existing;
            else if (regions.Length != 0)
                Write(destination, regions);
            path = destination;
        }
    }

    private static void Write(string destination, CombatRegion[] snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temporary = destination + ".tmp";
        using (
            var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            )
        )
        {
            new XElement(
                "Regions",
                new XAttribute("version", 1),
                snapshot.Select(r => r.ToXml())
            ).Save(stream);
            stream.Flush(true);
        }
        if (File.Exists(destination))
            File.Replace(temporary, destination, null);
        else
            File.Move(temporary, destination);
    }
}
