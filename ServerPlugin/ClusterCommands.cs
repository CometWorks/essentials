using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using PluginSdk.Clustering;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ServerPlugin.Commands;
using ServerPlugin.ShipFixer;
using VRage.Game;
using VRageMath;

namespace ServerPlugin;

// Player commands that reach past the caller's node on a Magnetar cluster: a private message to
// a player on another node (that node shows it), and the list of the caller's grids, which may
// be on any node (each node lists the grids it owns). On a plain server none of this runs.
internal static class ClusterCommands
{
    private const string MessageTopic = "private-message";
    private const string GridsTopic = "grids-list";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly List<IDisposable> Handlers = new List<IDisposable>();
    private static long nextRegistrationTick;

    /// <summary>Game thread: registers the handler once the cluster's plugin services exist.</summary>
    public static void Update(long tick)
    {
        if (Handlers.Count > 0 || !PluginCluster.IsClusterProcess || tick < nextRegistrationTick)
            return;

        nextRegistrationTick = tick + 60;
        if (
            PluginCluster.Current == null
            || EssentialsCluster.Client() is not PluginClusterClient cluster
        )
            return;

        try
        {
            Handlers.Add(cluster.RegisterHandler(MessageTopic, OnMessage));
            Handlers.Add(cluster.RegisterHandler(GridsTopic, OnGridsList));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Cluster command handlers not registered");
            Dispose();
        }
    }

    public static void Dispose()
    {
        foreach (IDisposable handler in Handlers)
            handler.Dispose();
        Handlers.Clear();
    }

    /// <summary>Sends the message to the node of a recipient online on another node.</summary>
    public static void SendMessage(long recipientIdentityId, string senderName, string message) =>
        EssentialsCluster
            .Client()
            ?.BroadcastAsync(
                MessageTopic,
                Encoding.UTF8.GetBytes($"{recipientIdentityId}\n{senderName}\n{message}"),
                Guid.NewGuid(),
                Timeout,
                includeWorldAuthority: false
            );

    // Game thread, on every node: only the node the recipient is connected to shows it
    private static Task<byte[]> OnMessage(PluginMessage message)
    {
        string[] parts = Encoding
            .UTF8.GetString(message.Payload ?? Array.Empty<byte>())
            .Split(new[] { '\n' }, 3);
        if (
            parts.Length == 3
            && long.TryParse(parts[0], out long recipient)
            && Utilities.GetPlayerByIdentityId(recipient) != null
        )
            Show(parts[2], "PM from " + parts[1], recipient);

        return Task.FromResult<byte[]>(null);
    }

    public static void Show(string message, string author, long identityId) =>
        MyVisualScriptLogicProvider.SendChatMessageColored(
            message,
            Color.MediumPurple,
            author,
            identityId,
            MyFontEnum.White
        );

    /// <summary>One grid of a player, as the node that owns it lists it.</summary>
    public sealed class GridLine
    {
        public long Id;
        public string Name;
        public string Size;
        public int Blocks;
        public Vector3D Position;
    }

    /// <summary>
    /// Asks every node for the grids it owns of the player. Calls back on the game thread with them,
    /// or with null when no node answered.
    /// </summary>
    public static void ListGrids(long identityId, Action<List<GridLine>> done)
    {
        Task<PluginBroadcastResult> broadcast = EssentialsCluster
            .Client()
            .BroadcastAsync(
                GridsTopic,
                Encoding.UTF8.GetBytes(identityId.ToString()),
                Guid.NewGuid(),
                Timeout,
                includeWorldAuthority: false
            );
        broadcast.ContinueWith(task =>
        {
            List<GridLine> grids = null;
            if (task.IsCompleted && !task.IsFaulted && task.Result?.Nodes != null)
                foreach (PluginResult answer in task.Result.Nodes.Values)
                    if (answer.Code == PluginResultCode.Success)
                        (grids ??= new List<GridLine>()).AddRange(Decode(answer.Payload));

            MyAPIGateway.Utilities.InvokeOnGameThread(() => done(grids));
        });
    }

    // Game thread, on every node: the player's grids this node owns (copies of other nodes' grids left out)
    private static Task<byte[]> OnGridsList(PluginMessage message)
    {
        StringBuilder sb = new StringBuilder();
        if (
            long.TryParse(
                Encoding.UTF8.GetString(message.Payload ?? Array.Empty<byte>()),
                out long identityId
            )
        )
            foreach (MyCubeGrid grid in EssentialsModule.OwnedGrids(identityId))
            {
                if (ShipFixerCluster.IsCopy(grid))
                    continue;
                Vector3D p = grid.PositionComp.GetPosition();
                sb.Append(grid.EntityId)
                    .Append('\t')
                    .Append(grid.DisplayName?.Replace('\t', ' ').Replace('\n', ' '))
                    .Append('\t')
                    .Append(grid.GridSizeEnum)
                    .Append('\t')
                    .Append(grid.BlocksCount)
                    .Append('\t')
                    .Append(p.X.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(p.Y.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(p.Z.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\n');
            }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static IEnumerable<GridLine> Decode(byte[] payload)
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        foreach (string line in Encoding.UTF8.GetString(payload ?? Array.Empty<byte>()).Split('\n'))
        {
            string[] f = line.Split('\t');
            if (
                f.Length == 7
                && long.TryParse(f[0], out long id)
                && int.TryParse(f[3], out int blocks)
                && double.TryParse(f[4], NumberStyles.Float, invariant, out double x)
                && double.TryParse(f[5], NumberStyles.Float, invariant, out double y)
                && double.TryParse(f[6], NumberStyles.Float, invariant, out double z)
            )
                yield return new GridLine
                {
                    Id = id,
                    Name = f[1],
                    Size = f[2],
                    Blocks = blocks,
                    Position = new Vector3D(x, y, z),
                };
        }
    }
}
