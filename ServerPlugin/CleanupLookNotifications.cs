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
    private const double NotificationDistance = 500;
    private readonly PluginConfig config;
    private readonly Dictionary<long, PlayerState> playerStates = new();
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

        DateTime nextReminder = now.AddMinutes(Math.Max(1, Math.Min(1440, config.CleanupLookReminderMinutes)));
        online.Clear();
        foreach (MyPlayer player in MySession.Static.Players.GetOnlinePlayers())
        {
            if (!player.IsRealPlayer || string.IsNullOrEmpty(player.DisplayName))
                continue;
            long identityId = player.Identity?.IdentityId ?? 0;
            if (identityId == 0)
                continue;
            online.Add(identityId);

            if (!playerStates.TryGetValue(identityId, out PlayerState state))
                playerStates[identityId] = state = new PlayerState();

            try
            {
                if (state.ReminderGridId != 0)
                {
                    // Between reminders this costs only a time check; no grid or look work.
                    if (now < state.NextReminder)
                        continue;
                }

                if (TryNotifyLookedAtGrid(player, identityId, state, nextReminder))
                    continue;

                if (state.ReminderGridId != 0)
                {
                    MyCubeGrid reminderGrid = MyEntities.GetEntityByIdOrDefault(state.ReminderGridId) as MyCubeGrid;
                    if (reminderGrid != null && reminderGrid.BigOwners.Contains(identityId) &&
                        player.Controller?.ControlledEntity?.Entity != null &&
                        Vector3D.DistanceSquared(player.GetPosition(), reminderGrid.PositionComp.GetPosition()) <= NotificationDistance * NotificationDistance &&
                        state.ReminderRuleIndex < config.CleanupLookNotices.Count &&
                        Matches(reminderGrid, config.CleanupLookNotices[state.ReminderRuleIndex]))
                    {
                        Show(reminderGrid, config.CleanupLookNotices[state.ReminderRuleIndex], identityId);
                        state.NextReminder = nextReminder;
                        continue;
                    }

                    state.ReminderGridId = 0;
                    state.LastViewedGridId = 0;
                }
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

    private bool TryNotifyLookedAtGrid(MyPlayer player, long identityId, PlayerState state, DateTime nextReminder)
    {
        MyCubeGrid grid = player.Controller?.ControlledEntity is MyCharacter character ? LookedAtGrid(character) : null;
        if (grid == null || !grid.BigOwners.Contains(identityId))
        {
            state.LastViewedGridId = 0;
            return false;
        }

        if (grid.EntityId == state.LastViewedGridId || grid.EntityId == state.ReminderGridId)
            return false;

        state.LastViewedGridId = grid.EntityId;
        for (int i = 0; i < config.CleanupLookNotices.Count; i++)
        {
            CleanupLookNotice rule = config.CleanupLookNotices[i];
            if (!Matches(grid, rule))
                continue;

            Show(grid, rule, identityId);
            state.ReminderGridId = grid.EntityId;
            state.ReminderRuleIndex = i;
            state.NextReminder = nextReminder;
            return true;
        }

        return false;
    }

    private bool Matches(MyCubeGrid grid, CleanupLookNotice rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Conditions) || string.IsNullOrWhiteSpace(rule.Message))
            return false;

        if (EssentialsModule.MatchesCleanupRule(grid, rule.Conditions, out string error))
            return true;

        if (error != null && reportedInvalidRules.Add(rule.Conditions))
            Plugin.Instance?.Log.Warning("Invalid cleanup look notification rule '{0}': {1}", rule.Conditions, error);
        return false;
    }

    private static void Show(MyCubeGrid grid, CleanupLookNotice rule, long identityId)
        => MyVisualScriptLogicProvider.ShowNotification(
            rule.Message.Replace("{GridName}", grid.DisplayName ?? ""), 7000, MyFontEnum.White, identityId);

    private static MyCubeGrid LookedAtGrid(MyCharacter character)
    {
        MatrixD head = character.GetHeadMatrix(true);
        Vector3D from = head.Translation + head.Forward * 0.5;
        LineD line = new LineD(from, from + head.Forward * NotificationDistance);
        var hit = MyEntities.GetIntersectionWithLine(ref line, character, null, ignoreCharacters: true);
        return hit?.Entity switch
        {
            MyCubeBlock block => block.CubeGrid,
            MyCubeGrid grid => grid,
            _ => null
        };
    }

    private sealed class PlayerState
    {
        public long LastViewedGridId;
        public long ReminderGridId;
        public int ReminderRuleIndex;
        public DateTime NextReminder;
    }
}
