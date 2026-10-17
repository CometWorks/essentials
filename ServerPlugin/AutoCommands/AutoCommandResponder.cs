using PluginSdk.Commands;
using Sandbox.Game;
using Shared.Logging;
using VRage.Game;

namespace ServerPlugin.AutoCommands;

/// <summary>
/// Reply sink used when auto-command steps dispatch <c>!ess</c> command lines as
/// the server. Broadcast replies go to every player's chat; private replies have
/// no recipient (the server is not a player), so they are written to the log.
/// </summary>
internal sealed class AutoCommandResponder : ICommandResponder
{
    private readonly IPluginLogger log;

    public AutoCommandResponder(IPluginLogger log) => this.log = log;

    public void Send(in CommandReply reply, in CommandCaller caller)
    {
        if (!reply.HasContent)
            return;

        string author = string.IsNullOrEmpty(reply.Author) ? "Server" : reply.Author;
        string font = string.IsNullOrEmpty(reply.Font) ? MyFontEnum.White : reply.Font;

        if (!reply.Broadcast)
        {
            log.Info("AutoCommand: {0}", reply.Text);
            return;
        }

        if (reply.Color.HasValue)
            MyVisualScriptLogicProvider.SendChatMessageColored(
                reply.Text,
                reply.Color.Value,
                author,
                0L,
                font
            );
        else
            MyVisualScriptLogicProvider.SendChatMessage(reply.Text, author, 0L, font);
    }
}
