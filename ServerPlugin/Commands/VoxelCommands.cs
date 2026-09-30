using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("voxels protect status", "Show voxel deformation protection settings.")]
    [Permission(MyPromoteLevel.Admin)]
    public string VoxelsProtectStatus()
    {
        var config = Plugin.Instance.PluginConfig;
        return $"Voxel protection: missiles {ProtectionState(config.ProtectVoxelsFromMissiles)}, " +
               $"meteors {ProtectionState(config.ProtectVoxelsFromMeteors)}, " +
               $"grid collisions {ProtectionState(config.ProtectVoxelsFromGridCollisions)}.";
    }

    [Command("voxels protect missiles", "Enable or disable protection from missile voxel deformation.")]
    [Permission(MyPromoteLevel.Admin)]
    public string VoxelsProtectMissiles(bool enabled)
    {
        Plugin.Instance.PluginConfig.ProtectVoxelsFromMissiles = enabled;
        return $"Missile voxel protection {ProtectionState(enabled)}.";
    }

    [Command("voxels protect meteors", "Enable or disable protection from meteor voxel deformation.")]
    [Permission(MyPromoteLevel.Admin)]
    public string VoxelsProtectMeteors(bool enabled)
    {
        Plugin.Instance.PluginConfig.ProtectVoxelsFromMeteors = enabled;
        return $"Meteor voxel protection {ProtectionState(enabled)}.";
    }

    [Command("voxels protect collisions", "Enable or disable protection from grid collision voxel deformation.")]
    [Permission(MyPromoteLevel.Admin)]
    public string VoxelsProtectCollisions(bool enabled)
    {
        Plugin.Instance.PluginConfig.ProtectVoxelsFromGridCollisions = enabled;
        return $"Grid collision voxel protection {ProtectionState(enabled)}.";
    }

    private static string ProtectionState(bool enabled) => enabled ? "on" : "off";

    [Command("voxels reset all", "Reset all voxel maps and planets.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsResetAll()
    {
        List<MyVoxelBase> voxels = MyEntities.GetEntities()
            .OfType<MyVoxelBase>()
            .Where(IsResettableVoxel)
            .GroupBy(voxel => voxel.EntityId)
            .Select(group => group.First())
            .ToList();

        if (!ConfirmVoxelReset("voxels reset all", voxels, "voxel map(s) and planet(s)"))
            return;

        ResetVoxelStorages(voxels, "all voxels");
    }

    [Command("voxels cleanup asteroids", "Reset asteroids without grids or players nearby.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsCleanupAsteroids()
    {
        List<MyVoxelMap> maps = MyEntities.GetEntities()
            .OfType<MyVoxelMap>()
            .Where(IsResettableVoxel)
            .Where(map => !HasGridOrCharacterNear(map.PositionComp.WorldVolume))
            .ToList();

        if (!ConfirmVoxelReset("voxels cleanup asteroids", maps.Cast<MyVoxelBase>().ToList(), "asteroid voxel map(s)"))
            return;

        ResetVoxelStorages(maps.Cast<MyVoxelBase>().ToList(), "unused asteroids");
    }

    [Command("voxels cleanup distant", "Reset asteroids without grids or players inside the radius.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsCleanupDistant(double distance = 1000)
    {
        if (distance <= 0)
        {
            Context.Respond("Distance must be greater than 0.");
            return;
        }

        List<MyVoxelMap> maps = MyEntities.GetEntities()
            .OfType<MyVoxelMap>()
            .Where(IsResettableVoxel)
            .Where(map => !HasGridOrCharacterNear(new BoundingSphereD(map.PositionComp.GetPosition(), distance)))
            .ToList();

        string operation = "voxels cleanup distant:" + distance.ToString("R", CultureInfo.InvariantCulture);
        if (!ConfirmVoxelReset(operation, maps.Cast<MyVoxelBase>().ToList(), "distant asteroid voxel map(s)"))
            return;

        ResetVoxelStorages(maps.Cast<MyVoxelBase>().ToList(), "distant asteroids");
    }

    [Command("voxels reset planets", "Reset all planets.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsResetPlanets()
    {
        List<MyPlanet> planets = MyEntities.GetEntities()
            .OfType<MyPlanet>()
            .Where(IsResettableVoxel)
            .ToList();

        if (!ConfirmVoxelReset("voxels reset planets", planets.Cast<MyVoxelBase>().ToList(), "planet(s)"))
            return;

        ResetVoxelStorages(planets.Cast<MyVoxelBase>().ToList(), "planets");
    }

    [Command("voxels reset planet", "Reset one planet by storage name.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsResetPlanet(string planetName)
    {
        if (string.IsNullOrWhiteSpace(planetName))
        {
            Context.Respond("Usage: !ess voxels reset planet <planetName>");
            return;
        }

        List<MyPlanet> planets = MyEntities.GetEntities()
            .OfType<MyPlanet>()
            .Where(IsResettableVoxel)
            .Where(planet => VoxelName(planet).IndexOf(planetName, StringComparison.InvariantCultureIgnoreCase) >= 0)
            .OrderBy(VoxelName)
            .ToList();

        switch (planets.Count)
        {
            case 0:
                Context.Respond($"Could not find planet matching '{planetName}'.");
                return;
            case 1:
                string operation = "voxels reset planet:" + planets[0].EntityId.ToString(CultureInfo.InvariantCulture);
                if (!ConfirmVoxelReset(operation, planets.Cast<MyVoxelBase>().ToList(), "planet"))
                    return;

                ResetVoxelStorages(planets.Cast<MyVoxelBase>().ToList(), "planet " + VoxelName(planets[0]));
                return;
            default:
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {planets.Count:#,##0} planets matching '{planetName}':");
                foreach (MyPlanet planet in planets)
                    sb.AppendLine(VoxelName(planet));

                Context.Respond(sb.ToString());
                return;
        }
    }

    [Command("voxels reset area", "Reset voxel damage in a radius around the caller.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsResetArea(float radius)
    {
        if (radius <= 0)
        {
            Context.Respond("Radius must be greater than 0.");
            return;
        }

        MyCharacter character = GetCallerCharacter("Console cannot use this command. Use !ess voxels reset gps <x> <y> <z> <radius>.");
        if (character == null)
            return;

        Vector3D center = character.PositionComp.GetPosition();
        ResetVoxelAreaAt(center, radius, "voxels reset area");
    }

    [Command("voxels reset gps", "Reset voxel damage in a radius around a GPS point.")]
    [Permission(MyPromoteLevel.Admin)]
    public void VoxelsResetGps(double x, double y, double z, float radius)
    {
        if (radius <= 0)
        {
            Context.Respond("Radius must be greater than 0.");
            return;
        }

        ResetVoxelAreaAt(new Vector3D(x, y, z), radius, "voxels reset gps");
    }

    private void ResetVoxelAreaAt(Vector3D center, float radius, string command)
    {
        BoundingSphereD sphere = new BoundingSphereD(center, radius);
        List<MyVoxelBase> voxels = MyEntities.GetEntitiesInSphere(ref sphere)
            .OfType<MyVoxelBase>()
            .Where(voxel => voxel != null && !voxel.MarkedForClose)
            .Select(voxel => voxel.RootVoxel ?? voxel)
            .Where(voxel => voxel != null && !voxel.MarkedForClose)
            .GroupBy(voxel => voxel.EntityId)
            .Select(group => group.First())
            .ToList();

        if (voxels.Count == 0)
        {
            Context.Respond("No voxel maps intersect that area.");
            return;
        }

        string operation = string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1:R}:{2:R}:{3:R}:{4:R}",
            command,
            center.X,
            center.Y,
            center.Z,
            radius);
        if (!ConfirmMaintenance(operation, $"This will reset voxel damage on {voxels.Count:#,##0} voxel map(s). Run the same command again within 30 seconds to confirm."))
            return;

        int count = 0;
        foreach (MyVoxelBase voxel in voxels)
        {
            try
            {
                MyShapeSphere shape = new MyShapeSphere
                {
                    Center = center,
                    Radius = radius
                };
                MyVoxelGenerator.RevertShape(voxel, shape);
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Log.Warning(ex, "Failed to reset voxel area on {0} ({1})", VoxelName(voxel), voxel.EntityId);
            }
        }

        Context.Respond($"Reset voxel damage on {count:#,##0} voxel map(s).");
        Plugin.Instance?.Log.Info(
            "Reset voxel area at {0}, {1}, {2} radius {3} on {4} voxel maps",
            center.X,
            center.Y,
            center.Z,
            radius,
            count);
    }

    private bool ConfirmVoxelReset(string operation, IReadOnlyList<MyVoxelBase> voxels, string noun)
    {
        if (voxels.Count == 0)
        {
            Context.Respond($"Found 0 {noun}.");
            return false;
        }

        return ConfirmMaintenance(operation, $"This will reset {voxels.Count:#,##0} {noun}. Run the same command again within 30 seconds to confirm.");
    }

    private void ResetVoxelStorages(IReadOnlyList<MyVoxelBase> voxels, string operation)
    {
        int count = 0;
        foreach (MyVoxelBase voxel in voxels)
        {
            try
            {
                if (!IsResettableVoxel(voxel))
                    continue;

                voxel.Storage.Reset(MyStorageDataTypeFlags.All);
                count++;
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Log.Warning(ex, "Failed to reset voxel {0} ({1})", VoxelName(voxel), voxel.EntityId);
            }
        }

        Context.Respond($"Reset {count:#,##0} voxel map(s).");
        Plugin.Instance?.Log.Info("Reset {0} voxel map(s) for {1}", count, operation);
    }

    private static bool IsResettableVoxel(MyVoxelBase voxel)
        => voxel != null &&
           !voxel.MarkedForClose &&
           voxel.RootVoxel == voxel &&
           !string.IsNullOrWhiteSpace(voxel.StorageName) &&
           voxel.Storage?.DataProvider != null;

    private static bool HasGridOrCharacterNear(BoundingSphereD sphere)
    {
        List<MyEntity> entities = new List<MyEntity>();
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities);
        return entities.Any(entity => entity is MyCubeGrid || entity is MyCharacter);
    }

    private static string VoxelName(MyVoxelBase voxel)
    {
        if (!string.IsNullOrWhiteSpace(voxel?.StorageName))
            return voxel.StorageName;

        if (!string.IsNullOrWhiteSpace(voxel?.Name))
            return voxel.Name;

        return voxel?.EntityId.ToString(CultureInfo.InvariantCulture) ?? "<null>";
    }
}
