using System;
using System.Collections.Generic;
using System.Linq;
using PluginSdk;
using Sandbox.Game;
using Shared.Config;
using VRage.Game;

namespace ServerPlugin;

internal static class InfoCommandHandler
{
    public static List<string> ConfiguredNames(PluginConfig config)
        => (config?.InfoCommands ?? new List<InfoCommand>())
            .Where(HasResponse)
            .Select(command => Name(command.Command))
            .Where(name => name != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => "!" + name)
            .ToList();

    public static bool TryHandle(string text, long identityId)
    {
        if (identityId == 0 || string.IsNullOrEmpty(text) || text[0] != '!')
            return false;

        int end = 1;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;
        string name = text.Substring(1, end - 1);

        List<InfoCommand> commands = Plugin.Instance?.PluginConfig?.InfoCommands;
        if (commands == null)
            return false;

        foreach (InfoCommand command in commands)
        {
            if (!HasResponse(command) || !string.Equals(Name(command.Command), name, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                Respond(command, identityId, name);
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Log.Warning(ex, "Info command '{0}' failed.", name);
                try { SendChat("Info command failed. Please tell a server admin.", identityId); }
                catch { /* The chat transport may be the failing operation. */ }
            }
            return true;
        }

        return false;
    }

    private static string Name(string configured)
    {
        string name = configured?.Trim().TrimStart('!');
        if (string.IsNullOrEmpty(name) ||
            string.Equals(name, "ess", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "stone", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "help", StringComparison.OrdinalIgnoreCase) ||
            name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            return null;

        return name;
    }

    private static bool HasResponse(InfoCommand command)
        => !string.IsNullOrWhiteSpace(command.ChatResponse) ||
           !string.IsNullOrWhiteSpace(command.NotificationResponse) ||
           !string.IsNullOrWhiteSpace(command.DialogResponse) ||
           !string.IsNullOrWhiteSpace(command.URL);

    private static void Respond(InfoCommand command, long identityId, string name)
    {
        if (!string.IsNullOrWhiteSpace(command.ChatResponse))
            SendChat(command.ChatResponse, identityId);

        if (!string.IsNullOrWhiteSpace(command.NotificationResponse))
            MyVisualScriptLogicProvider.ShowNotification(
                command.NotificationResponse,
                command.NotificationDurationMs > 0 ? command.NotificationDurationMs : 5000,
                MyFontEnum.White,
                identityId);

        if (!string.IsNullOrWhiteSpace(command.DialogResponse) &&
            !MissionScreens.ShowToPlayer(identityId, "Information", null, "!" + name, command.DialogResponse, "Close"))
            SendChat(command.DialogResponse, identityId);

        if (string.IsNullOrWhiteSpace(command.URL))
            return;

        if (!Uri.TryCreate(command.URL.Trim(), UriKind.Absolute, out Uri url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            SendChat("This info command has an invalid URL.", identityId);
            return;
        }

        SendChat(url.AbsoluteUri, identityId);
        MyVisualScriptLogicProvider.OpenSteamOverlay(url.AbsoluteUri, identityId);
    }

    private static void SendChat(string message, long identityId)
        => MyVisualScriptLogicProvider.SendChatMessage(message, Plugin.Name, identityId, MyFontEnum.White);
}
