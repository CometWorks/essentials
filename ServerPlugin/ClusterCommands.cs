using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
#if NETCOREAPP
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
#endif


namespace ServerPlugin;

// Player commands that reach past the caller's node on a Magnetar cluster: a private message to
// a player on another node (that node shows it), and the list of the caller's grids, which may
// be on any node (each node lists the grids it owns) or in an offline partition (the gateway has
// those). On a plain server none of this runs.
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
        public bool Offline;
    }

    /// <summary>The player's grids in offline partitions, as the gateway keeps them from the freeze.</summary>
    private sealed class OfflineGrids
    {
        public List<GridLine> Grids = new List<GridLine>();

        // Offline partitions whose grids aren't known yet
        public int Unknown;
    }

    /// <summary>
    /// Asks every node for the grids it owns of the player and the gateway for those in offline
    /// partitions, which no node holds. Calls back on the game thread with them and a note on what
    /// may be missing (null when nothing), or with a null list when no node answered.
    /// </summary>
    public static void ListGrids(long identityId, Action<List<GridLine>, string> done)
    {
        Task<OfflineGrids> offline = ReadOfflineGrids(identityId);
        Task<PluginBroadcastResult> broadcast = EssentialsCluster
            .Client()
            .BroadcastAsync(
                GridsTopic,
                Encoding.UTF8.GetBytes(identityId.ToString()),
                Guid.NewGuid(),
                Timeout,
                includeWorldAuthority: false
            );
        Task.WhenAll(broadcast.ContinueWith(_ => { }), offline.ContinueWith(_ => { }))
            .ContinueWith(_ =>
            {
                List<GridLine> grids = null;
                if (
                    broadcast.Status == TaskStatus.RanToCompletion
                    && broadcast.Result?.Nodes != null
                )
                    foreach (PluginResult answer in broadcast.Result.Nodes.Values)
                        if (answer.Code == PluginResultCode.Success)
                            (grids ??= new List<GridLine>()).AddRange(Decode(answer.Payload));

                string note = null;
                if (offline.Status != TaskStatus.RanToCompletion)
                {
                    Plugin.Instance.Log.Error(offline.Exception, "Offline grids not read");
                    note = "Grids in areas nobody is near could not be listed. Try again.";
                }
                else if (grids != null)
                {
                    // A node's report wins: a partition that just woke can still have its offline row
                    HashSet<long> online = new HashSet<long>(grids.Select(grid => grid.Id));
                    grids.AddRange(offline.Result.Grids.Where(grid => !online.Contains(grid.Id)));
                    if (offline.Result.Unknown > 0)
                        note =
                            "Some grids in areas nobody is near are not listed yet. Try again later.";
                }

                MyAPIGateway.Utilities.InvokeOnGameThread(() => done(grids, note));
            });
    }

#if NETCOREAPP
    private static readonly HttpClient Gateway = NewGatewayClient();

    private static HttpClient NewGatewayClient()
    {
        HttpClient client = new HttpClient { Timeout = Timeout };
        string token =
            Environment.GetEnvironmentVariable("CLUSTER_JOIN_TOKEN_CURRENT")
            ?? Environment.GetEnvironmentVariable("CLUSTER_JOIN_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token
            );
        return client;
    }

    // Docs/Gateway/GatewayAPI.md of CometWorks/cluster. The plugin SDK has no view of this.
    private static async Task<OfflineGrids> ReadOfflineGrids(long identityId)
    {
        string root = Environment.GetEnvironmentVariable("CLUSTER_GATEWAY_REGISTRY")?.TrimEnd('/');
        if (string.IsNullOrEmpty(root))
            return new OfflineGrids();

        using HttpResponseMessage response = await Gateway
            .GetAsync(
                $"{root}/registry/partitions/offline/grids?owner={identityId.ToString(CultureInfo.InvariantCulture)}"
            )
            .ConfigureAwait(false);
        // A gateway that predates the route
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new OfflineGrids();
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false)
        );
        OfflineGrids result = new OfflineGrids
        {
            Unknown = document.RootElement.GetProperty("unknown").GetInt32(),
        };
        foreach (JsonElement row in document.RootElement.GetProperty("grids").EnumerateArray())
        {
            JsonElement grid = row.GetProperty("grid");
            result.Grids.Add(
                new GridLine
                {
                    Id = grid.GetProperty("id").GetInt64(),
                    Name = grid.GetProperty("n").GetString(),
                    Size = grid.GetProperty("l").GetBoolean() ? "Large" : "Small",
                    Blocks = grid.GetProperty("b").GetInt32(),
                    Position = new Vector3D(
                        grid.GetProperty("x").GetDouble(),
                        grid.GetProperty("y").GetDouble(),
                        grid.GetProperty("z").GetDouble()
                    ),
                    Offline = true,
                }
            );
        }

        return result;
    }
#else
    // A cluster only runs on .NET 10
    private static Task<OfflineGrids> ReadOfflineGrids(long identityId) =>
        Task.FromResult(new OfflineGrids());
#endif

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
