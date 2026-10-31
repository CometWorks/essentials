using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PluginSdk.Clustering;
using Sandbox.Game;
using ServerPlugin.Commands;
using VRage.Game;
using VRageMath;

namespace ServerPlugin;

// A private message to a player on another node of a Magnetar cluster (that node shows it). On a
// plain server none of this runs.
internal static class ClusterCommands
{
    private const string MessageTopic = "private-message";
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
}
