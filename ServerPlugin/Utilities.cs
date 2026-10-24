using System;
using System.Collections.Generic;
using System.Linq;
using PluginSdk.Clustering;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace ServerPlugin;

public static class Utilities
{
    public static bool TryGetEntityByNameOrId(string nameOrId, out IMyEntity entity)
    {
        if (long.TryParse(nameOrId, out long id))
            return MyAPIGateway.Entities.TryGetEntityById(id, out entity);

        foreach (IMyEntity ent in MyEntities.GetEntities())
        {
            if (
                string.Equals(
                    ent.DisplayName,
                    nameOrId,
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            {
                entity = ent;
                return true;
            }
        }

        entity = null;
        return false;
    }

    public static IMyIdentity GetIdentityByNameOrIds(string playerNameOrIds)
    {
        foreach (IMyIdentity identity in MySession.Static.Players.GetAllIdentities())
        {
            if (
                string.Equals(
                    identity.DisplayName,
                    playerNameOrIds,
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
                return identity;

            if (
                long.TryParse(playerNameOrIds, out long identityId)
                && identity.IdentityId == identityId
            )
                return identity;

            if (
                ulong.TryParse(playerNameOrIds, out ulong steamId)
                && GetSteamId(identity.IdentityId) == steamId
            )
                return identity;
        }

        return null;
    }

    public static IMyPlayer GetPlayerByNameOrId(string nameOrPlayerId)
    {
        if (!long.TryParse(nameOrPlayerId, out long id))
        {
            foreach (IMyIdentity identity in MySession.Static.Players.GetAllIdentities())
            {
                if (
                    string.Equals(
                        identity.DisplayName,
                        nameOrPlayerId,
                        StringComparison.InvariantCultureIgnoreCase
                    )
                )
                    id = identity.IdentityId;
            }
        }

        if (
            MySession.Static.Players.TryGetPlayerId(id, out MyPlayer.PlayerId playerId)
            && MySession.Static.Players.TryGetPlayerById(playerId, out MyPlayer player)
        )
            return player;

        return null;
    }

    /// <summary>
    /// A player online anywhere in a cluster, by name, identity id or Steam id; null on a plain
    /// server. A player on another node has an identity on this one but no player.
    /// </summary>
    public static PluginPlayerInfo FindClusterPlayer(string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
            return null;

        return EssentialsCluster
            .OnlinePlayers()
            ?.FirstOrDefault(player =>
                string.Equals(player.Name, nameOrId, StringComparison.InvariantCultureIgnoreCase)
                || player.IdentityId.ToString() == nameOrId
                || player.SteamId.ToString() == nameOrId
            );
    }

    public static MyPlayer GetPlayerByIdentityId(long identityId)
    {
        if (
            MySession.Static.Players.TryGetPlayerId(identityId, out MyPlayer.PlayerId playerId)
            && MySession.Static.Players.TryGetPlayerById(playerId, out MyPlayer player)
        )
            return player;

        return null;
    }

    public static string GetPlayerNameById(long identityId)
    {
        IMyIdentity identity = MySession.Static.Players.TryGetIdentity(identityId);
        return identity?.DisplayName ?? identityId.ToString();
    }

    public static ulong GetSteamId(long identityId) =>
        MySession.Static.Players.TryGetSteamId(identityId);

    public static int GetOnlinePlayerCount() =>
        MySession
            .Static.Players.GetOnlinePlayers()
            .Count(player => player.IsRealPlayer && !string.IsNullOrEmpty(player.DisplayName));

    public static List<MyPlayer> GetOnlinePlayers() =>
        MySession
            .Static.Players.GetOnlinePlayers()
            .Where(player => player.IsRealPlayer && !string.IsNullOrEmpty(player.DisplayName))
            .ToList();
}
