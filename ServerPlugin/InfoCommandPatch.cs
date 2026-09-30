using System;
using HarmonyLib;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using VRage.Network;

namespace ServerPlugin;

[HarmonyPatch(typeof(MyMultiplayerBase), "OnChatMessageReceived_Server")]
[HarmonyPatch(new[] { typeof(ChatMsg) })]
public static class InfoCommandPatch
{
    // The host's registered commands run first; configured info names fill only unused roots.
    [HarmonyPriority(Priority.Low)]
    public static bool Prefix(ChatMsg msg)
    {
        ChatChannel channel = (ChatChannel)msg.Channel;
        if (channel != ChatChannel.Global && channel != ChatChannel.Faction && channel != ChatChannel.Private)
            return true;

        if (string.IsNullOrEmpty(msg.Text) || msg.Text[0] != '!')
            return true;

        try
        {
            ulong steamId = MyEventContext.Current.Sender.Value;
            long identityId = MySession.Static?.Players?.TryGetIdentityId(steamId) ?? 0;
            return !InfoCommandHandler.TryHandle(msg.Text, identityId);
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.Warning(ex, "Info command interception failed.");
            return true;
        }
    }
}
