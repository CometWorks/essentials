using System;
using HarmonyLib;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Network;

namespace ServerPlugin;

[HarmonyPatch(typeof(MyMultiplayerBase), "OnChatMessageReceived_Server")]
[HarmonyPatch(new[] { typeof(ChatMsg) })]
public static class ChatMutePatch
{
    [HarmonyPriority(Priority.Last)]
    public static bool Prefix(ChatMsg msg)
    {
        try
        {
            ChatChannel channel = (ChatChannel)msg.Channel;
            if (
                channel != ChatChannel.Global
                && channel != ChatChannel.Faction
                && channel != ChatChannel.Private
            )
                return true;

            ulong steamId = MyEventContext.Current.Sender.Value;
            if (steamId == 0 || !ChatMuteService.IsMuted(steamId, out ChatMuteRecord record))
                return true;

            long identityId =
                record.IdentityId != 0
                    ? record.IdentityId
                    : MySession.Static?.Players?.TryGetIdentityId(steamId) ?? 0;

            if (identityId != 0)
                MyVisualScriptLogicProvider.SendChatMessage(
                    "You are muted in chat.",
                    Plugin.Name,
                    identityId,
                    MyFontEnum.Red
                );

            return false;
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.Warning(ex, "Chat mute patch failed.");
            return true;
        }
    }
}
