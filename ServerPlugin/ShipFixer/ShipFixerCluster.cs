using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PluginSdk.Clustering;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ServerPlugin.Commands;

namespace ServerPlugin.ShipFixer;

// The ship fixer on a Magnetar cluster. A node may cut and paste only grids it owns and
// simulates: a grid frozen for a handover would come back on the target node next to its
// pasted copy, and a copy of a grid another node owns is not this node's to replace. A grid
// asked for by name or id may be on any node, so the search and the fix go to every node
// (PluginSdk broadcast), and only the node holding the grid acts. On a plain server none of
// this runs: every grid is local.
internal static class ShipFixerCluster
{
    private const string FindTopic = "fixship-find";
    private const string FixTopic = "fixship-fix";
    private const string CooldownTopic = "fixship-cooldown";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly List<IDisposable> Handlers = new List<IDisposable>();
    private static int nextRegistrationTick;

    /// <summary>Cluster process with the broadcast handlers registered.</summary>
    public static bool Routes => Handlers.Count > 0;

    /// <summary>Game thread: registers the handlers once the cluster's plugin services exist.</summary>
    public static void Update(long tick)
    {
        if (Routes || !PluginCluster.IsClusterProcess || tick < nextRegistrationTick)
            return;

        nextRegistrationTick = (int)tick + 60;
        if (PluginCluster.Current == null || Client() is not PluginClusterClient cluster)
            return;

        try
        {
            Handlers.Add(cluster.RegisterHandler(FindTopic, message => Handle(message, false)));
            Handlers.Add(cluster.RegisterHandler(FixTopic, message => Handle(message, true)));
            Handlers.Add(cluster.RegisterHandler(CooldownTopic, OnCooldown));
            Plugin.Instance.Log.Info("Ship fixer: cluster handlers registered");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Ship fixer: cluster handlers not registered");
            Dispose();
        }
    }

    public static void Dispose()
    {
        foreach (IDisposable handler in Handlers)
            handler.Dispose();
        Handlers.Clear();
    }

    private static PluginClusterClient Client() => EssentialsCluster.Client();

    /// <summary>Game thread: OK when this node may cut and paste every grid, else why not.</summary>
    public static ShipFixerResult CheckResidence(IEnumerable<MyCubeGrid> grids)
    {
        if (!PluginCluster.IsClusterProcess || Client() is not PluginClusterClient cluster)
            return ShipFixerResult.OK;

        foreach (MyCubeGrid grid in grids)
        {
            // Null: this cluster build has no views; behave as before
            PluginEntityPlacement placement = cluster.LocateEntity(grid.EntityId);
            if (placement != null && placement.Residence != PluginEntityResidence.Local)
                return ShipFixerResult.GridMoving;
        }

        return ShipFixerResult.OK;
    }

    /// <summary>Game thread: a copy of a grid owned by another node (or by none), not to be found here.</summary>
    public static bool IsCopy(MyCubeGrid grid)
    {
        if (!PluginCluster.IsClusterProcess || Client() is not PluginClusterClient cluster)
            return false;

        PluginEntityResidence? residence = cluster.LocateEntity(grid.EntityId)?.Residence;
        return residence == PluginEntityResidence.Unowned
            || residence == PluginEntityResidence.Absent;
    }

    /// <summary>The answer of the node that has the grid, or the most telling refusal.</summary>
    public sealed class Answer
    {
        public ShipFixerResult Result;
        public long GridId;
        public string GridName;

        public byte[] Encode() => Encoding.UTF8.GetBytes($"{Result}\n{GridId}\n{GridName}");

        public static Answer Decode(byte[] payload)
        {
            string[] parts = Encoding.UTF8.GetString(payload ?? Array.Empty<byte>()).Split('\n');
            return
                parts.Length == 3
                && Enum.TryParse(parts[0], out ShipFixerResult result)
                && long.TryParse(parts[1], out long gridId)
                ? new Answer
                {
                    Result = result,
                    GridId = gridId,
                    GridName = parts[2],
                }
                : null;
        }
    }

    // Lower rank wins when nodes answer differently: the node that has the grid speaks for it
    private static readonly ShipFixerResult[] Rank =
    {
        ShipFixerResult.ShipFixed,
        ShipFixerResult.OK,
        ShipFixerResult.GridMoving,
        ShipFixerResult.GridOccupied,
        ShipFixerResult.OwnedByDifferentPlayer,
        ShipFixerResult.GridNotFound,
        ShipFixerResult.UnknownProblem,
    };

    /// <summary>
    /// Asks every node for the grid (fix = false: may it be fixed; fix = true: fix it). Calls
    /// back on the game thread with the best answer, or null when no node could be asked.
    /// </summary>
    public static void Request(
        bool fix,
        long playerId,
        string gridName,
        long gridId,
        Action<Answer> done
    )
    {
        string payload = $"{playerId}\n{gridId}\n{gridName}";
        Task<PluginBroadcastResult> broadcast = Client()
            .BroadcastAsync(
                fix ? FixTopic : FindTopic,
                Encoding.UTF8.GetBytes(payload),
                Guid.NewGuid(),
                Timeout,
                includeWorldAuthority: false
            );
        broadcast.ContinueWith(task =>
        {
            Answer best = Best(task.IsCompleted && !task.IsFaulted ? task.Result : null);
            MyAPIGateway.Utilities.InvokeOnGameThread(() => done(best));
        });
    }

    private static Answer Best(PluginBroadcastResult result)
    {
        List<Answer> answers = (result?.Nodes ?? new Dictionary<string, PluginResult>())
            .OrderBy(node => node.Key, StringComparer.Ordinal)
            .Where(node => node.Value.Code == PluginResultCode.Success)
            .Select(node => Answer.Decode(node.Value.Payload))
            .Where(answer => answer != null && Array.IndexOf(Rank, answer.Result) >= 0)
            .ToList();
        return answers.OrderBy(answer => Array.IndexOf(Rank, answer.Result)).FirstOrDefault();
    }

    public static void StartCooldownEverywhere(string callerKey)
    {
        Client()
            ?.BroadcastAsync(
                CooldownTopic,
                Encoding.UTF8.GetBytes(callerKey),
                Guid.NewGuid(),
                Timeout,
                includeWorldAuthority: false
            );
    }

    private static Task<byte[]> OnCooldown(PluginMessage message)
    {
        ShipFixerCommands.StartCommandCooldown(
            Encoding.UTF8.GetString(message.Payload ?? Array.Empty<byte>())
        );
        return Task.FromResult<byte[]>(null);
    }

    // Game thread, on every node: look for the grid among the grids this node owns
    private static Task<byte[]> Handle(PluginMessage message, bool fix)
    {
        string[] parts = Encoding
            .UTF8.GetString(message.Payload ?? Array.Empty<byte>())
            .Split(new[] { '\n' }, 3);
        Answer answer = new Answer { Result = ShipFixerResult.GridNotFound };
        if (
            parts.Length == 3
            && long.TryParse(parts[0], out long playerId)
            && long.TryParse(parts[1], out long gridId)
        )
        {
            try
            {
                answer = fix
                    ? ShipFixerCore.FixHere(playerId, parts[2], gridId)
                    : ShipFixerCore.FindHere(playerId, parts[2], gridId);
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.Error(ex, "Ship fixer: cluster request failed");
                answer = new Answer { Result = ShipFixerResult.UnknownProblem };
            }
        }

        return Task.FromResult(answer.Encode());
    }
}
