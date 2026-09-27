using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using ServerPlugin.Commands;
using Shared.Config;
using VRage.Game;
using VRageMath;

namespace ServerPlugin;

internal sealed class CleanupLookNotifications
{
    private readonly PluginConfig config;
    private readonly Dictionary<long, (long GridId, DateTime NextNotice)> playerStates = new();
    private readonly HashSet<string> reportedInvalidRules = new();
    private readonly HashSet<long> online = new();
    private readonly List<long> disconnected = new();
    private DateTime lastCheck;

    public CleanupLookNotifications(PluginConfig config) => this.config = config;

    public void Update()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - lastCheck).TotalSeconds < 3)
            return;
        lastCheck = now;

        if (!config.Enabled || config.CleanupLookNotices == null || config.CleanupLookNotices.Count == 0 || MySession.Static?.Ready != true)
        {
            playerStates.Clear();
            return;
        }

        online.Clear();
        foreach (MyPlayer player in MySession.Static.Players.GetOnlinePlayers())
        {
            if (!player.IsRealPlayer || string.IsNullOrEmpty(player.DisplayName))
                continue;
            long identityId = player.Identity?.IdentityId ?? 0;
            if (identityId == 0)
                continue;
            online.Add(identityId);

            playerStates.TryGetValue(identityId, out var state);
            // A recent recipient costs only a dictionary lookup, not a raycast or rule scan.
            if (now < state.NextNotice)
                continue;

            try
            {
                MyCubeGrid grid = player.Controller?.ControlledEntity is MyCharacter character ? LookedAtGrid(character) : null;
                if (grid == null || !grid.BigOwners.Contains(identityId))
                {
                    playerStates[identityId] = (0, state.NextNotice);
                    continue;
                }

                if (state.GridId == grid.EntityId)
                    continue;

                state.GridId = grid.EntityId;
                foreach (CleanupLookNotice rule in config.CleanupLookNotices)
                {
                    if (string.IsNullOrWhiteSpace(rule.Conditions) || string.IsNullOrWhiteSpace(rule.Message))
                        continue;

                    if (!EssentialsModule.MatchesCleanupRule(grid, rule.Conditions, out string error))
                    {
                        if (error != null && reportedInvalidRules.Add(rule.Conditions))
                            Plugin.Instance?.Log.Warning("Invalid cleanup look notification rule '{0}': {1}", rule.Conditions, error);
                        continue;
                    }

                    MyVisualScriptLogicProvider.ShowNotification(
                        rule.Message.Replace("{GridName}", grid.DisplayName ?? ""),
                        7000, MyFontEnum.White, identityId);
                    state.NextNotice = now.AddMinutes(1);
                    state.GridId = 0;
                    break;
                }

                playerStates[identityId] = state;
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Log.Warning(ex, "Cleanup look notification failed for player {0}", identityId);
            }
        }

        disconnected.Clear();
        foreach (long identityId in playerStates.Keys)
            if (!online.Contains(identityId))
                disconnected.Add(identityId);
        foreach (long identityId in disconnected)
            playerStates.Remove(identityId);
    }

    private static MyCubeGrid LookedAtGrid(MyCharacter character)
    {
        MatrixD head = character.GetHeadMatrix(true);
        Vector3D from = head.Translation + head.Forward * 0.5;
        LineD line = new LineD(from, from + head.Forward * 500);
        var hit = MyEntities.GetIntersectionWithLine(ref line, character, null, ignoreCharacters: true);
        return hit?.Entity switch
        {
            MyCubeBlock block => block.CubeGrid,
            MyCubeGrid grid => grid,
            _ => null
        };
    }
}
