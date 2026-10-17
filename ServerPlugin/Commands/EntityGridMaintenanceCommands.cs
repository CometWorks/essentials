using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using PluginSdk.Commands;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Network;
using VRage.ObjectBuilders.Private;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private static readonly Dictionary<ulong, DateTime> EntityRefreshCooldowns =
        new Dictionary<ulong, DateTime>();
    private static readonly FieldInfo ReplicationClientStatesField =
        typeof(MyReplicationServer).GetField(
            "m_clientStates",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
    private static readonly MethodInfo ReplicationRemoveForClientMethod =
        typeof(MyReplicationServer)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name == "RemoveForClient" && method.GetParameters().Length == 3
            );
    private static readonly MethodInfo ReplicationForceReplicableMethod =
        typeof(MyReplicationServer)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return method.Name == "ForceReplicable"
                    && parameters.Length == 2
                    && parameters[1].ParameterType == typeof(Endpoint);
            });

    [Command("entities refresh", "Resync all entities for the caller.")]
    [Permission(MyPromoteLevel.None)]
    public string EntitiesRefresh()
    {
        if (Context.Caller.IsConsole || Context.Caller.SteamId == 0)
            return "Only players can refresh entity replication.";

        DateTime now = DateTime.UtcNow;
        if (EntityRefreshCooldowns.TryGetValue(Context.Caller.SteamId, out DateTime lastRefresh))
        {
            TimeSpan elapsed = now - lastRefresh;
            if (elapsed < TimeSpan.FromMinutes(1))
                return $"Cooldown active. You can use this command again in {(60 - elapsed.TotalSeconds):N0} seconds.";
        }

        EntityRefreshCooldowns[Context.Caller.SteamId] = now;

        MyReplicationServer replicationServer =
            MyMultiplayer.ReplicationLayer as MyReplicationServer;
        if (
            replicationServer == null
            || ReplicationClientStatesField == null
            || ReplicationRemoveForClientMethod == null
            || ReplicationForceReplicableMethod == null
        )
            return "Replication refresh is not available.";

        Endpoint endpoint = new Endpoint(Context.Caller.SteamId, 0);
        object clientStates = ReplicationClientStatesField.GetValue(replicationServer);
        MethodInfo tryGetValue = clientStates?.GetType().GetMethod("TryGetValue");
        if (tryGetValue == null)
            return "Replication client state is not available.";

        object[] args = { endpoint, null };
        if (!(bool)tryGetValue.Invoke(clientStates, args) || args[1] == null)
            return "Could not find your replication client state.";

        object client = args[1];
        FieldInfo replicablesField = client
            .GetType()
            .GetField("Replicables", BindingFlags.Public | BindingFlags.Instance);
        if (replicablesField?.GetValue(client) is not IEnumerable replicables)
            return "Could not enumerate client replicables.";

        List<IMyReplicable> list = new List<IMyReplicable>();
        foreach (object pair in replicables)
        {
            object key = pair.GetType().GetProperty("Key")?.GetValue(pair);
            if (key is IMyReplicable replicable)
                list.Add(replicable);
        }

        foreach (IMyReplicable replicable in list)
        {
            ReplicationRemoveForClientMethod.Invoke(
                replicationServer,
                new object[] { replicable, client, true }
            );
            ReplicationForceReplicableMethod.Invoke(
                replicationServer,
                new object[] { replicable, endpoint }
            );
        }

        return $"Forced replication of {list.Count:#,##0} entities.";
    }

    [Command("entities kill", "Kill a player or character entity.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public string EntitiesKill(string playerName)
    {
        if (
            Utilities.GetPlayerByNameOrId(playerName) is MyPlayer player
            && player.Identity?.IdentityId != 0
        )
        {
            MyVisualScriptLogicProvider.SetPlayersHealth(player.Identity.IdentityId, 0);
            MyVisualScriptLogicProvider.SendChatMessage(
                $"{player.DisplayName} was killed by an admin",
                "Server",
                0L,
                MyFontEnum.White
            );
            return $"Killed {player.DisplayName}.";
        }

        if (!Utilities.TryGetEntityByNameOrId(playerName, out IMyEntity entity))
            return $"Entity '{playerName}' not found.";

        if (entity is not IMyCharacter || entity is not IMyDestroyableObject destroyable)
            return $"Entity '{EntityDisplayName(entity)}' is not a character.";

        destroyable.DoDamage(1000f, MyDamageType.Radioactivity, true);
        MyVisualScriptLogicProvider.SendChatMessage(
            $"{EntityDisplayName(entity)} was killed by an admin",
            "Server",
            0L,
            MyFontEnum.White
        );
        return $"Killed {EntityDisplayName(entity)}.";
    }

    [Command("grids export", "Export a grid to an XML file.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void GridsExport(string gridName, string exportName)
    {
        if (!TryResolveGrid(gridName, out MyCubeGrid grid))
            return;

        string path = GridExportPath(exportName);
        if (File.Exists(path))
        {
            Context.Respond("Export file already exists.");
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        bool saved = MyObjectBuilderSerializerKeen.SerializeXML(
            path,
            false,
            grid.GetObjectBuilder()
        );
        Context.Respond(saved ? $"Grid saved to {path}" : "Grid export failed.");
    }

    [Command("grids import", "Import a grid XML file near a target entity or player.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public string GridsImport(string exportName, string targetName = null)
    {
        IMyEntity target;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            target = GetCallerControlledEntity();
            if (target == null)
                return "Target entity must be specified.";
        }
        else if (!Utilities.TryGetEntityByNameOrId(targetName, out target))
        {
            return "Target entity not found.";
        }

        string path = GridExportPath(exportName);
        if (!File.Exists(path))
            return "File does not exist.";

        if (!MyObjectBuilderSerializerKeen.DeserializeXML(path, out MyObjectBuilder_CubeGrid grid))
            return "Grid import failed.";

        MyEntities.RemapObjectBuilder(grid);
        Vector3D? position = FindFreePlaceNear(
            target.GetPosition(),
            grid.CalculateBoundingSphere().Radius
        );
        if (position == null)
            return "No free place.";

        MyPositionAndOrientation orientation =
            grid.PositionAndOrientation ?? new MyPositionAndOrientation();
        orientation.Position = position.Value;
        grid.PositionAndOrientation = orientation;
        MyEntities.CreateFromObjectBuilderParallel(grid, true);
        return $"Importing grid from {path}";
    }

    private static string GridExportPath(string exportName)
    {
        string fileName = SafeFileStem(exportName);
        if (!fileName.EndsWith(".xml", StringComparison.InvariantCultureIgnoreCase))
            fileName += ".xml";

        return System.IO.Path.Combine(
            MyFileSystem.UserDataPath,
            "Essentials",
            "ExportedGrids",
            fileName
        );
    }

    private static string SafeFileStem(string value)
    {
        string fileName = System.IO.Path.GetFileName(
            string.IsNullOrWhiteSpace(value) ? "grid" : value.Trim()
        );
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        string sanitized = new string(
            fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()
        );
        return string.IsNullOrWhiteSpace(sanitized)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            : sanitized;
    }

    private static Vector3D? FindFreePlaceNear(Vector3D position, float radius)
    {
        MatrixD matrix = MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Up);
        return MyEntities.FindFreePlace(ref matrix, Vector3.Up, radius);
    }
}
