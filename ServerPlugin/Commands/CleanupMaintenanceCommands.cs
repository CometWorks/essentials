using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using PluginSdk.Commands;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Game.World.Generator;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Groups;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private delegate bool CleanupPredicate(MyCubeGrid grid);

    private sealed class CleanupCondition
    {
        public CleanupCondition(
            string command,
            string invertCommand,
            string helpText,
            bool requiresArgument,
            bool acceptsArgument,
            Func<string, (bool Ok, string Error, CleanupPredicate Predicate)> build)
        {
            Command = command;
            InvertCommand = invertCommand;
            HelpText = helpText;
            RequiresArgument = requiresArgument;
            AcceptsArgument = acceptsArgument;
            Build = build;
        }

        public string Command { get; }
        public string InvertCommand { get; }
        public string HelpText { get; }
        public bool RequiresArgument { get; }
        public bool AcceptsArgument { get; }
        public Func<string, (bool Ok, string Error, CleanupPredicate Predicate)> Build { get; }
    }

    private static readonly Dictionary<string, DateTime> MaintenanceConfirmations = new Dictionary<string, DateTime>();
    private static readonly FieldInfo GpssField = typeof(MyGpsCollection).GetField("m_playerGpss", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo SeedParamsField = typeof(MyProceduralWorldGenerator).GetField("m_existingObjectsSeeds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo CamerasField = typeof(MySession).GetField("Cameras", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo EntityCameraSettingsField = CamerasField?.FieldType.GetField("m_entityCameraSettings", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo FactionRelationsField = typeof(MyFactionCollection).GetField("m_relationsBetweenFactions", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo PlayerFactionRelationsField = typeof(MyFactionCollection).GetField("m_relationsBetweenPlayersAndFactions", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly IReadOnlyList<CleanupCondition> CleanupConditions = new List<CleanupCondition>
    {
        Arg("name", null, "Finds grids with a matching name. Accepts regex format.", pattern =>
        {
            try
            {
                Regex regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(25));
                return Ok(grid => !string.IsNullOrEmpty(grid.DisplayName) && MatchesGridName(regex, grid.DisplayName));
            }
            catch (ArgumentException ex)
            {
                return Error("Invalid regex: " + ex.Message);
            }
        }),
        Arg("blockslessthan", null, "Finds grids with less than the given number of blocks.", value => ParseInt(value, count => grid => grid.BlocksCount < count)),
        Arg("blocksgreaterthan", null, "Finds grids with more than the given number of blocks.", value => ParseInt(value, count => grid => grid.BlocksCount > count)),
        Arg("pcugreaterthan", null, "Finds grids with more than the given number of PCU.", value => ParseInt(value, pcu => grid => grid.BlocksPCU > pcu)),
        Arg("pculessthan", null, "Finds grids with less than the given number of PCU.", value => ParseInt(value, pcu => grid => grid.BlocksPCU < pcu)),
        Arg("hasgridtype", null, "Finds grids with the specified grid type (large | small | ship | static).", value => Ok(grid => HasGridType(grid, value))),
        Arg("hasownertype", null, "Finds grids with the specified owner type (npc | player | nobody).", value => Ok(grid => HasOwnerType(grid, value))),
        NoArg("haspower", "nopower", "Finds grids with, or without power.", grid => HasPower(grid)),
        NoArg("insideplanet", null, "Finds grids that are trapped inside planets.", InsidePlanet),
        Arg("playerdistancelessthan", "playerdistancegreaterthan", "Finds grids that are nearer/farther than the given distance from players.", value => ParseDouble(value, distance => grid => PlayerDistanceLessThan(grid, distance))),
        Arg("poweredgriddistancegreaterthan", null, "Finds grids that are farther than the given distance from other powered grids.", value => ParseDouble(value, distance => grid => PoweredGridDistanceGreaterThan(grid, distance))),
        Arg("centerdistancelessthan", "centerdistancegreaterthan", "Finds grids that are nearer/farther than the given distance from world center.", value => ParseDouble(value, distance => grid => CenterDistanceLessThan(grid, distance))),
        Arg("ownedby", null, "Finds grids owned by the given player. Accepts player name, identity id, Steam id, nobody, npc, or pirates.", value => Ok(grid => OwnedBy(grid, value))),
        Arg("hastype", "notype", "Finds grids containing blocks of the given type.", value => Ok(grid => HasBlockType(grid, value))),
        Arg("hastype-fast", "notype-fast", "Finds grids containing blocks of any comma-separated object-builder type.", value => Ok(grid => HasBlockTypeFast(grid, value))),
        Arg("hassubtype", "nosubtype", "Finds grids containing blocks of the given subtype.", value => Ok(grid => HasBlockSubtype(grid, value))),
        Arg("hassubtype-fast", "nosubtype-fast", "Finds grids containing blocks of any comma-separated subtype.", value => Ok(grid => HasBlockSubtypeFast(grid, value))),
        NoArg("haspilot", null, "Finds grids with pilots. Without this condition cleanup commands skip piloted grids by default.", HasPilot)
    };

    [Command("cleanup scan", "Find grids matching cleanup conditions.")]
    [Permission(MyPromoteLevel.Admin)]
    public string CleanupScan(params string[] args)
    {
        if (!TryScanCleanupGrids(args, out List<MyCubeGrid> grids, out string error))
            return error;

        return $"Found {grids.Count} grids matching the given conditions.";
    }

    [Command("cleanup list", "List grids matching cleanup conditions.")]
    [Permission(MyPromoteLevel.Admin)]
    public string CleanupList(params string[] args)
    {
        if (!TryScanCleanupGrids(args, out List<MyCubeGrid> grids, out string error))
            return error;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Found {grids.Count} grids matching the given conditions.");
        foreach (MyCubeGrid grid in grids.OrderBy(grid => grid.DisplayName))
            sb.AppendLine($"{grid.DisplayName} ({grid.BlocksCount:#,##0} block(s), {grid.BlocksPCU:#,##0} PCU, entity {grid.EntityId})");

        return sb.ToString();
    }

    [Command("cleanup delete", "Delete grids matching cleanup conditions.")]
    [Permission(MyPromoteLevel.Admin)]
    public void CleanupDelete(params string[] args)
    {
        if (!TryScanCleanupGrids(args, out List<MyCubeGrid> grids, out string error))
        {
            Context.Respond(error);
            return;
        }

        if (grids.Count == 0)
        {
            Context.Respond("Found 0 grids matching the given conditions.");
            return;
        }

        string operation = "cleanup delete:" + string.Join(" ", args);
        if (!ConfirmMaintenance(operation, $"This will delete {grids.Count:#,##0} grid(s). Run the same command again within 30 seconds to confirm."))
            return;

        int count = 0;
        foreach (MyCubeGrid grid in grids)
        {
            if (grid.MarkedForClose)
                continue;

            Plugin.Instance?.Log.Info("Cleanup deleting grid {0}: {1}", grid.EntityId, grid.DisplayName);
            EjectPilots(grid);
            grid.Close();
            count++;
        }

        Context.Respond($"Deleted {count:#,##0} grids matching the given conditions.");
        Plugin.Instance?.Log.Info("Cleanup deleted {0} grids matching conditions: {1}", count, string.Join(" ", args));
    }

    [Command("cleanup delete floatingobjects", "Delete floating objects.")]
    [Permission(MyPromoteLevel.Admin)]
    public void CleanupDeleteFloatingObjects()
    {
        List<MyFloatingObject> objects = MyEntities.GetEntities().OfType<MyFloatingObject>().ToList();
        if (objects.Count == 0)
        {
            Context.Respond("Deleted 0 floating objects.");
            return;
        }

        if (!ConfirmMaintenance("cleanup delete floatingobjects", $"This will delete {objects.Count:#,##0} floating object(s). Run the same command again within 30 seconds to confirm."))
            return;

        int count = 0;
        foreach (MyFloatingObject floater in objects)
        {
            if (floater.MarkedForClose)
                continue;

            Plugin.Instance?.Log.Info("Cleanup deleting floating object {0}: {1}", floater.EntityId, floater.DisplayName);
            floater.Close();
            count++;
        }

        Context.Respond($"Deleted {count:#,##0} floating objects.");
        Plugin.Instance?.Log.Info("Cleanup deleted {0} floating objects", count);
    }

    [Command("cleanup help", "List cleanup conditions.")]
    [Permission(MyPromoteLevel.Admin)]
    public string CleanupHelp()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Cleanup conditions:");
        foreach (CleanupCondition condition in CleanupConditions)
        {
            string aliases = string.IsNullOrEmpty(condition.InvertCommand) ? condition.Command : condition.Command + " / " + condition.InvertCommand;
            sb.AppendLine($"{aliases}: {condition.HelpText}");
        }
        sb.AppendLine("No haspilot condition means piloted grids are skipped by default.");
        return sb.ToString();
    }

    [Command("identity clean", "Remove identities that have not logged on in the given number of days.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void IdentityClean(int days, bool includeNpcs = false)
    {
        List<MyIdentity> identities = FindOldIdentities(days, includeNpcs);
        if (identities.Count == 0)
        {
            Context.Respond($"No identities found older than {days} day(s).");
            return;
        }

        string operation = $"identity clean:{days}:{includeNpcs}";
        if (!ConfirmMaintenance(operation, $"This will remove {identities.Count:#,##0} old identit(y/ies) and preserve their grids. Run the same command again within 30 seconds to confirm."))
            return;

        int fixedGrids = FixGridOwnership(identities.Select(identity => identity.IdentityId).ToList(), deleteGrids: false);
        RemoveFromFactionAndDeleteIdentities(identities);
        int factions = CleanFactionsInternal(1);
        Context.Respond($"Removed {identities.Count:#,##0} old identit(y/ies), reassigned ownership on {fixedGrids:#,##0} grid(s), removed {factions:#,##0} empty faction(s).");
    }

    [Command("identity purge", "Remove old identities and grids solely owned by them.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void IdentityPurge(int days, bool includeNpcs = false)
    {
        List<MyIdentity> identities = FindOldIdentities(days, includeNpcs);
        if (identities.Count == 0)
        {
            Context.Respond($"No identities found older than {days} day(s).");
            return;
        }

        int affectedGrids = CountOwnedGrids(identities.Select(identity => identity.IdentityId));
        string operation = $"identity purge:{days}:{includeNpcs}";
        if (!ConfirmMaintenance(operation, $"This will remove {identities.Count:#,##0} old identit(y/ies) and close up to {affectedGrids:#,##0} solely-owned grid(s). Run the same command again within 30 seconds to confirm."))
            return;

        int deletedGrids = FixGridOwnership(identities.Select(identity => identity.IdentityId).ToList(), deleteGrids: true);
        RemoveFromFactionAndDeleteIdentities(identities);
        int factions = CleanFactionsInternal(1);
        Context.Respond($"Removed {identities.Count:#,##0} old identit(y/ies), closed {deletedGrids:#,##0} grid(s), removed {factions:#,##0} empty faction(s).");
    }

    [Command("identity clear", "Remove one identity and grids solely owned by it.")]
    [Permission(MyPromoteLevel.Admin)]
    public void IdentityClear(string player)
    {
        MyIdentity identity = Utilities.GetIdentityByNameOrIds(player) as MyIdentity;
        if (identity == null)
        {
            Context.Respond($"No identity found for {player}.");
            return;
        }

        int affectedGrids = CountOwnedGrids(new[] { identity.IdentityId });
        string operation = $"identity clear:{identity.IdentityId}";
        if (!ConfirmMaintenance(operation, $"This will remove identity '{identity.DisplayName}' and close up to {affectedGrids:#,##0} solely-owned grid(s). Run the same command again within 30 seconds to confirm."))
            return;

        int deletedGrids = FixGridOwnership(new List<long> { identity.IdentityId }, deleteGrids: true);
        RemoveFromFactionAndDeleteIdentities(new List<MyIdentity> { identity });
        int factions = CleanFactionsInternal(1);
        Context.Respond($"Removed identity '{identity.DisplayName}', closed {deletedGrids:#,##0} grid(s), removed {factions:#,##0} empty faction(s).");
    }

    [Command("rep wipe", "Reset reputation relations on the server.")]
    [Permission(MyPromoteLevel.Admin)]
    public void RepWipe(bool removePlayerToFaction = true, bool removeFactionToFaction = true)
    {
        int count = CountReputations(removePlayerToFaction, removeFactionToFaction);
        if (count == 0)
        {
            Context.Respond("Wiped 0 reputations.");
            return;
        }

        string operation = $"rep wipe:{removePlayerToFaction}:{removeFactionToFaction}";
        if (!ConfirmMaintenance(operation, $"This will wipe {count:#,##0} reputation relation(s). Run the same command again within 30 seconds to confirm."))
            return;

        int wiped = WipeReputations(removePlayerToFaction, removeFactionToFaction);
        Context.Respond($"Wiped {wiped:#,##0} reputation relation(s).");
        Plugin.Instance?.Log.Info("Wiped {0} reputation relations", wiped);
    }

    [Command("faction clean", "Remove factions with fewer than the given number of valid members.")]
    [Permission(MyPromoteLevel.Admin)]
    public void FactionClean(int memberCount = 1)
    {
        List<MyFaction> factions = FindCleanableFactions(memberCount);
        if (factions.Count == 0)
        {
            Context.Respond($"No factions found with fewer than {memberCount} valid member(s).");
            return;
        }

        string operation = $"faction clean:{memberCount}";
        if (!ConfirmMaintenance(operation, $"This will remove {factions.Count:#,##0} faction(s) with fewer than {memberCount} valid member(s). Run the same command again within 30 seconds to confirm."))
            return;

        int count = 0;
        foreach (MyFaction faction in factions)
        {
            RemoveFaction(faction);
            count++;
        }

        Context.Respond($"Removed {count:#,##0} faction(s) with fewer than {memberCount} valid member(s).");
    }

    [Command("faction remove", "Remove a faction by tag.")]
    [Permission(MyPromoteLevel.Admin)]
    public void FactionRemove(string tag)
    {
        MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
        if (faction == null)
        {
            Context.Respond($"{tag} is not a faction on this server.");
            return;
        }

        string operation = $"faction remove:{faction.FactionId}";
        if (!ConfirmMaintenance(operation, $"This will remove faction {faction.Tag} ({faction.Name}). Run the same command again within 30 seconds to confirm."))
            return;

        RemoveFaction(faction);
        Context.Respond(MySession.Static.Factions.FactionTagExists(tag) ? $"{tag} removal failed." : $"{tag} removed.");
    }

    [Command("faction info", "List factions and members.")]
    [Permission(MyPromoteLevel.Admin)]
    public string FactionInfo(string tag = null)
    {
        IEnumerable<MyFaction> factions = MySession.Static.Factions.Select(pair => pair.Value);
        if (!string.IsNullOrWhiteSpace(tag))
            factions = factions.Where(faction => string.Equals(faction.Tag, tag, StringComparison.InvariantCultureIgnoreCase));

        StringBuilder sb = new StringBuilder();
        foreach (MyFaction faction in factions.OrderBy(faction => faction.Tag))
        {
            sb.AppendLine($"{faction.Tag} - {faction.Name} - {faction.Members.Count} member(s){(faction.IsEveryoneNpc() ? " - NPC" : "")}");
            foreach (KeyValuePair<long, MyFactionMember> member in faction.Members)
            {
                MyIdentity identity = MySession.Static.Players.TryGetIdentity(member.Key);
                string name = string.IsNullOrWhiteSpace(identity?.DisplayName) ? member.Key.ToString() : identity.DisplayName;
                string role = member.Value.IsFounder ? "Founder" : member.Value.IsLeader ? "Leader" : "Member";
                sb.AppendLine($"  {name} ({member.Key}) - {role}");
            }
        }

        return sb.Length == 0 ? "No matching factions found." : sb.ToString();
    }

    [Command("sandbox clean", "Clean stale identities, factions, GPS/camera/procedural data, and block ownership.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void SandboxClean()
    {
        if (!ConfirmMaintenance("sandbox clean", "This will remove stale sandbox data and cannot be previewed exactly. Run the same command again within 30 seconds to confirm."))
            return;

        int count = 0;
        HashSet<long> validIdentities = BuildValidIdentitySet();

        foreach (MyIdentity identity in MySession.Static.Players.GetAllIdentities().OfType<MyIdentity>().ToList())
        {
            if (MySession.Static.Players.IdentityIsNpc(identity.IdentityId) || string.IsNullOrEmpty(identity.DisplayName))
            {
                validIdentities.Add(identity.IdentityId);
                continue;
            }

            if (validIdentities.Contains(identity.IdentityId))
                continue;

            RemoveFromFaction(identity);
            MySession.Static.Players.RemoveIdentity(identity.IdentityId);
            count++;
        }

        count += FixBlockOwnership();
        count += CleanFactionsInternal(1);
        count += CleanupReputations(validIdentities);
        count += CleanupGps(validIdentities);
        count += ClearProceduralSeeds();
        count += ClearCameraSettings();

        Context.Respond($"Removed {count:#,##0} unnecessary sandbox element(s).");
        Plugin.Instance?.Log.Info("Sandbox clean removed {0} unnecessary elements", count);
    }

    private static CleanupCondition Arg(
        string command,
        string invertCommand,
        string helpText,
        Func<string, (bool Ok, string Error, CleanupPredicate Predicate)> build)
        => new CleanupCondition(command, invertCommand, helpText, requiresArgument: true, acceptsArgument: true, build);

    private static CleanupCondition NoArg(string command, string invertCommand, string helpText, CleanupPredicate predicate)
        => new CleanupCondition(command, invertCommand, helpText, requiresArgument: false, acceptsArgument: false, _ => Ok(predicate));

    private static (bool Ok, string Error, CleanupPredicate Predicate) Ok(CleanupPredicate predicate)
        => (true, null, predicate);

    private static (bool Ok, string Error, CleanupPredicate Predicate) Error(string error)
        => (false, error, null);

    private static bool MatchesGridName(Regex regex, string name)
    {
        try
        {
            return regex.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static (bool Ok, string Error, CleanupPredicate Predicate) ParseInt(string value, Func<int, CleanupPredicate> predicate)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Ok(predicate(parsed))
            : Error($"Could not parse integer argument '{value}'.");
    }

    private static (bool Ok, string Error, CleanupPredicate Predicate) ParseDouble(string value, Func<double, CleanupPredicate> predicate)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? Ok(predicate(parsed))
            : Error($"Could not parse number argument '{value}'.");
    }

    private bool TryScanCleanupGrids(IReadOnlyList<string> args, out List<MyCubeGrid> grids, out string error)
    {
        grids = new List<MyCubeGrid>();
        if (!TryBuildCleanupPredicates(args, out List<CleanupPredicate> predicates, out error))
            return false;

        foreach (MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group group in MyCubeGridGroups.Static.Logical.Groups)
        {
            if (MatchesCleanupGroup(group, predicates, out List<MyCubeGrid> groupGrids))
                grids.AddRange(groupGrids);
        }

        grids = grids
            .GroupBy(grid => grid.EntityId)
            .Select(group => group.First())
            .ToList();
        return true;
    }

    internal static bool MatchesCleanupRule(MyCubeGrid grid, string conditions, out string error)
    {
        string[] args = Regex.Matches(conditions, "\"[^\"]*\"|\\S+")
            .Cast<Match>()
            .Select(match => match.Value.Trim('"'))
            .ToArray();
        if (!TryBuildCleanupPredicates(args, out List<CleanupPredicate> predicates, out error))
            return false;

        MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group group = MyCubeGridGroups.Static.Logical.GetGroup(grid);
        return IsCleanupCandidate(grid) && group != null && MatchesCleanupGroup(group, predicates, out _);
    }

    private static bool MatchesCleanupGroup(
        MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group group,
        IReadOnlyList<CleanupPredicate> predicates,
        out List<MyCubeGrid> grids)
    {
        grids = group.Nodes.Select(node => node.NodeData).Where(IsCleanupCandidate).ToList();
        return grids.Count > 0 && grids.All(grid => predicates.All(predicate => predicate(grid)));
    }

    private static bool TryBuildCleanupPredicates(IReadOnlyList<string> args, out List<CleanupPredicate> predicates, out string error)
    {
        predicates = new List<CleanupPredicate>();
        error = null;
        bool explicitPilotCondition = false;

        for (int i = 0; i < args.Count; i++)
        {
            string token = args[i];
            CleanupCondition condition = FindCleanupCondition(token, out bool invert);
            if (condition == null)
            {
                error = $"Unknown cleanup condition '{token}'. Use !ess cleanup help.";
                return false;
            }

            if (string.Equals(condition.Command, "haspilot", StringComparison.InvariantCultureIgnoreCase))
                explicitPilotCondition = true;

            string value = null;
            if (condition.RequiresArgument)
            {
                if (i + 1 >= args.Count || IsCleanupConditionToken(args[i + 1]))
                {
                    error = $"Cleanup condition '{token}' requires an argument.";
                    return false;
                }

                value = args[++i];
            }
            else if (i + 1 < args.Count && !IsCleanupConditionToken(args[i + 1]))
            {
                error = $"Cleanup condition '{token}' does not accept an argument.";
                return false;
            }

            (bool ok, string buildError, CleanupPredicate predicate) = condition.Build(value);
            if (!ok)
            {
                error = buildError;
                return false;
            }

            predicates.Add(invert ? grid => !predicate(grid) : predicate);
        }

        if (!explicitPilotCondition)
            predicates.Add(grid => !HasPilot(grid));

        return true;
    }

    private static CleanupCondition FindCleanupCondition(string token, out bool invert)
    {
        foreach (CleanupCondition condition in CleanupConditions)
        {
            if (string.Equals(token, condition.Command, StringComparison.InvariantCultureIgnoreCase))
            {
                invert = false;
                return condition;
            }

            if (!string.IsNullOrEmpty(condition.InvertCommand) &&
                string.Equals(token, condition.InvertCommand, StringComparison.InvariantCultureIgnoreCase))
            {
                invert = true;
                return condition;
            }
        }

        invert = false;
        return null;
    }

    private static bool IsCleanupConditionToken(string token)
        => FindCleanupCondition(token, out _) != null;

    private static bool IsCleanupCandidate(MyCubeGrid grid)
        => grid != null && grid.Projector == null && !grid.MarkedForClose && !grid.MarkedAsTrash && grid.InScene;

    private bool ConfirmMaintenance(string operation, string prompt)
    {
        string key = MaintenanceCallerKey() + ":" + operation;
        DateTime now = DateTime.UtcNow;
        if (MaintenanceConfirmations.TryGetValue(key, out DateTime expiresAt) && expiresAt >= now)
        {
            MaintenanceConfirmations.Remove(key);
            return true;
        }

        MaintenanceConfirmations[key] = now.AddSeconds(30);
        Context.Respond(prompt);
        return false;
    }

    private string MaintenanceCallerKey()
    {
        if (Context.Caller.SteamId != 0)
            return Context.Caller.SteamId.ToString();

        if (Context.Caller.IdentityId != 0)
            return Context.Caller.IdentityId.ToString();

        return "console";
    }

    private static void EjectPilots(MyCubeGrid grid)
    {
        foreach (MyCockpit cockpit in grid.GetFatBlocks().OfType<MyCockpit>())
            cockpit.RemovePilot();
    }

    private static bool HasGridType(MyCubeGrid grid, string gridType)
    {
        if (string.Equals(gridType, "static", StringComparison.InvariantCultureIgnoreCase))
            return grid.IsStatic;

        if (string.Equals(gridType, "ship", StringComparison.InvariantCultureIgnoreCase))
            return !grid.IsStatic;

        if (string.Equals(gridType, "large", StringComparison.InvariantCultureIgnoreCase))
            return grid.GridSizeEnum == VRage.Game.MyCubeSize.Large;

        if (string.Equals(gridType, "small", StringComparison.InvariantCultureIgnoreCase))
            return grid.GridSizeEnum == VRage.Game.MyCubeSize.Small;

        return false;
    }

    private static bool HasOwnerType(MyCubeGrid grid, string ownerType)
    {
        long owner = GetGridOwner(grid);
        if (string.Equals(ownerType, "nobody", StringComparison.InvariantCultureIgnoreCase))
            return owner == 0;

        if (string.Equals(ownerType, "npc", StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(ownerType, "npcs", StringComparison.InvariantCultureIgnoreCase))
            return owner != 0 && MySession.Static.Players.IdentityIsNpc(owner);

        if (string.Equals(ownerType, "player", StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(ownerType, "players", StringComparison.InvariantCultureIgnoreCase))
            return owner != 0 && !MySession.Static.Players.IdentityIsNpc(owner);

        return false;
    }

    private static long GetGridOwner(MyCubeGrid grid)
    {
        if (grid.BigOwners.Count > 0 && grid.BigOwners[0] != 0)
            return grid.BigOwners[0];

        return grid.BigOwners.Count > 1 ? grid.BigOwners[1] : 0;
    }

    private static bool HasPower(MyCubeGrid grid)
    {
        foreach (MyCubeBlock block in grid.GetFatBlocks())
        {
            MyResourceSourceComponent source = block.Components?.Get<MyResourceSourceComponent>();
            if (source == null || !source.ResourceTypes.Contains(MyResourceDistributorComponent.ElectricityId))
                continue;

            if (source.HasCapacityRemainingByType(MyResourceDistributorComponent.ElectricityId) &&
                source.ProductionEnabledByType(MyResourceDistributorComponent.ElectricityId))
                return true;
        }

        return false;
    }

    private static bool InsidePlanet(MyCubeGrid grid)
    {
        BoundingSphereD sphere = grid.PositionComp.WorldVolume;
        List<MyVoxelBase> voxels = new List<MyVoxelBase>();
        MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, voxels);

        foreach (MyPlanet planet in voxels.OfType<MyPlanet>())
        {
            double distanceSquared = Vector3D.DistanceSquared(sphere.Center, planet.PositionComp.WorldVolume.Center);
            if (distanceSquared <= planet.MaximumRadius * planet.MaximumRadius / 2)
                return true;
        }

        return false;
    }

    private static bool PlayerDistanceLessThan(MyCubeGrid grid, double distance)
    {
        double distanceSquared = distance * distance;
        foreach (MyPlayer player in MySession.Static.Players.GetOnlinePlayers())
        {
            if (Vector3D.DistanceSquared(player.GetPosition(), grid.PositionComp.GetPosition()) < distanceSquared)
                return true;
        }

        return false;
    }

    private static bool PoweredGridDistanceGreaterThan(MyCubeGrid grid, double distance)
    {
        double distanceSquared = distance * distance;
        Vector3D gridPosition = grid.PositionComp.GetPosition();

        foreach (MyCubeGrid other in MyEntities.GetEntities().OfType<MyCubeGrid>())
        {
            if (other.EntityId == grid.EntityId || other.Projector != null || !HasPower(other))
                continue;

            if (Vector3D.DistanceSquared(other.PositionComp.GetPosition(), gridPosition) < distanceSquared)
                return false;
        }

        return true;
    }

    private static bool CenterDistanceLessThan(MyCubeGrid grid, double distance)
    {
        double distanceSquared = distance * distance;
        return Vector3D.DistanceSquared(Vector3D.Zero, grid.PositionComp.GetPosition()) < distanceSquared;
    }

    private static bool OwnedBy(MyCubeGrid grid, string value)
    {
        if (string.Equals(value, "nobody", StringComparison.InvariantCultureIgnoreCase))
            return grid.BigOwners.Count == 0;

        if (string.Equals(value, "npc", StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(value, "npcs", StringComparison.InvariantCultureIgnoreCase))
            return grid.BigOwners.Count > 0 && MySession.Static.Factions.IsNpcFaction(grid.BigOwners.FirstOrDefault());

        long identityId;
        if (string.Equals(value, "pirates", StringComparison.InvariantCultureIgnoreCase))
        {
            identityId = MyPirateAntennas.GetPiratesId();
            IMyFaction pirateFaction = MySession.Static.Factions.GetPlayerFaction(identityId);
            return pirateFaction != null &&
                   grid.BigOwners.Count > 0 &&
                   pirateFaction.Members.ContainsKey(grid.BigOwners.FirstOrDefault());
        }

        IMyIdentity identity = Utilities.GetIdentityByNameOrIds(value);
        if (identity != null)
            return grid.BigOwners.Contains(identity.IdentityId);

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out identityId) &&
               grid.BigOwners.Contains(identityId);
    }

    private static bool HasBlockType(MyCubeGrid grid, string typeName)
    {
        foreach (MyCubeBlock block in grid.GetFatBlocks())
        {
            string blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
            if (string.Equals(blockType, typeName, StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasBlockSubtype(MyCubeGrid grid, string subtypeName)
    {
        foreach (MyCubeBlock block in grid.GetFatBlocks())
        {
            if (string.Equals(block.BlockDefinition.Id.SubtypeName, subtypeName, StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasBlockTypeFast(MyCubeGrid grid, string typeName)
    {
        List<MyObjectBuilderType> types = new List<MyObjectBuilderType>();
        foreach (string raw in typeName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string value = raw.Trim();
            if (MyObjectBuilderType.TryParse(value, out MyObjectBuilderType typeId) ||
                MyObjectBuilderType.TryParse("MyObjectBuilder_" + value, out typeId))
                types.Add(typeId);
        }

        return types.Count > 0 && grid.GetFatBlocks().Any(block => types.Contains(block.BlockDefinition.Id.TypeId));
    }

    private static bool HasBlockSubtypeFast(MyCubeGrid grid, string subtypeName)
    {
        List<MyStringHash> subtypes = subtypeName
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => MyStringHash.TryGet(value.Trim()))
            .ToList();

        return subtypes.Count > 0 && grid.GetFatBlocks().Any(block => subtypes.Contains(block.BlockDefinition.Id.SubtypeId));
    }

    private static bool HasPilot(MyCubeGrid grid)
        => grid.GetFatBlocks().OfType<MyShipController>().Any(block => block.Pilot != null);

    private static List<MyIdentity> FindOldIdentities(int days, bool includeNpcs)
    {
        DateTime cutoff = DateTime.Now - TimeSpan.FromDays(days);
        return MySession.Static.Players.GetAllIdentities()
            .OfType<MyIdentity>()
            .Where(identity => identity.LastLoginTime < cutoff)
            .Where(identity => includeNpcs || !MySession.Static.Players.IdentityIsNpc(identity.IdentityId))
            .ToList();
    }

    private static void RemoveFromFactionAndDeleteIdentities(IEnumerable<MyIdentity> identities)
    {
        foreach (MyIdentity identity in identities)
        {
            RemoveFromFaction(identity);
            MySession.Static.Players.RemoveIdentity(identity.IdentityId);
        }
    }

    private static bool RemoveFromFaction(MyIdentity identity)
    {
        if (MySession.Static.Factions.GetPlayerFaction(identity.IdentityId) == null)
            return false;

        MyVisualScriptLogicProvider.KickPlayerFromFaction(identity.IdentityId);
        return true;
    }

    private static int CountOwnedGrids(IEnumerable<long> identityIds)
    {
        HashSet<long> ids = new HashSet<long>(identityIds);
        return MyEntities.GetEntities()
            .OfType<MyCubeGrid>()
            .Count(grid => grid.Projector == null && grid.BigOwners.Any(ids.Contains));
    }

    private static int FixGridOwnership(List<long> identityIds, bool deleteGrids)
    {
        if (identityIds.Count == 0)
            return 0;

        List<MyCubeGrid> grids = MyEntities.GetEntities().OfType<MyCubeGrid>().Where(grid => grid.Projector == null).ToList();
        int count = 0;

        foreach (long identityId in identityIds.Where(id => id != 0))
        {
            foreach (MyCubeGrid grid in grids.Where(grid => grid.BigOwners.Contains(identityId)))
            {
                if (grid.BigOwners.Count > 1)
                {
                    long newOwnerId = grid.BigOwners.First(id => id != identityId);
                    MyMultiplayer.RaiseEvent(grid, target => new Action<long, long>(target.TransferBlocksBuiltByID), identityId, newOwnerId, new EndpointId());

                    foreach (MySlimBlock block in grid.GetBlocks().Where(block => block.FatBlock?.OwnerId == identityId))
                        block.FatBlock.ChangeOwner(newOwnerId, MyOwnershipShareModeEnum.Faction);

                    grid.RecalculateOwners();
                    count++;
                    continue;
                }

                if (deleteGrids)
                {
                    EjectPilots(grid);
                    grid.Close();
                }
                else
                {
                    RemoveGridOwnershipAndPcu(grid, identityId);
                    grid.RecalculateOwners();
                }

                count++;
            }
        }

        return count;
    }

    private static void RemoveGridOwnershipAndPcu(MyCubeGrid grid, long identityId)
    {
        bool removedPcu = false;
        foreach (MySlimBlock block in grid.GetBlocks())
        {
            if (block == null || block.CubeGrid == null || block.IsDestroyed)
                continue;

            if (block.FatBlock?.OwnerId == identityId)
                grid.ChangeOwnerRequest(grid, block.FatBlock, 0, MyOwnershipShareModeEnum.Faction);

            if (block.BuiltBy != identityId)
                continue;

            block.RemoveAuthorship();
            block.TransferAuthorshipClient(0L);
            removedPcu = true;
        }

        if (!removedPcu)
            return;

        MyIdentity identity = MySession.Static.Players.TryGetIdentity(identityId);
        identity?.BlockLimits.SetAllDirty();
        identity?.BlockLimits.CallLimitsChanged();
    }

    private static int FixBlockOwnership()
    {
        int count = 0;
        foreach (MyCubeGrid grid in MyEntities.GetEntities().OfType<MyCubeGrid>())
        {
            long owner = grid.BigOwners.FirstOrDefault();
            MyOwnershipShareModeEnum share = owner == 0 ? MyOwnershipShareModeEnum.All : MyOwnershipShareModeEnum.Faction;
            foreach (MyCubeBlock block in grid.GetFatBlocks())
            {
                if (block.OwnerId == 0 || MySession.Static.Players.HasIdentity(block.OwnerId))
                    continue;

                block.ChangeOwner(owner, share);
                count++;
            }
        }

        return count;
    }

    private static List<MyFaction> FindCleanableFactions(int memberCount)
    {
        List<MyFaction> factions = new List<MyFaction>();
        foreach (MyFaction faction in MySession.Static.Factions.Select(pair => pair.Value))
        {
            if ((faction.IsEveryoneNpc() || !faction.AcceptHumans) && faction.Members.Count != 0)
                continue;

            int validMembers = 0;
            foreach (KeyValuePair<long, MyFactionMember> member in faction.Members)
            {
                if (!MySession.Static.Players.HasIdentity(member.Key) && !MySession.Static.Players.IdentityIsNpc(member.Key))
                    continue;

                validMembers++;
                if (validMembers >= memberCount)
                    break;
            }

            if (validMembers < memberCount)
                factions.Add(faction);
        }

        return factions;
    }

    private static int CleanFactionsInternal(int memberCount)
    {
        List<MyFaction> factions = FindCleanableFactions(memberCount);
        foreach (MyFaction faction in factions)
            RemoveFaction(faction);

        return factions.Count;
    }

    private static void RemoveFaction(MyFaction faction)
    {
        Plugin.Instance?.Log.Info("Removing faction {0} ({1})", faction.Tag, faction.FactionId);
        MyFactionCollection.RemoveFaction(faction.FactionId);
        if (MyAPIGateway.Session.Factions.FactionTagExists(faction.Tag))
            MyAPIGateway.Session.Factions.RemoveFaction(faction.FactionId);
    }

    private static HashSet<long> BuildValidIdentitySet()
    {
        HashSet<long> validIdentities = new HashSet<long>();

        foreach (MyCubeGrid grid in MyEntities.GetEntities().OfType<MyCubeGrid>())
            validIdentities.UnionWith(grid.SmallOwners);

        foreach (MyPlayer online in MySession.Static.Players.GetOnlinePlayers())
            validIdentities.Add(online.Identity.IdentityId);

        validIdentities.Remove(0);
        return validIdentities;
    }

    private static int CleanupReputations(HashSet<long> validIdentities)
    {
        HashSet<long> valid = new HashSet<long>(validIdentities);
        foreach (MyIdentity identity in MySession.Static.Players.GetAllIdentities().OfType<MyIdentity>())
        {
            if (MySession.Static.Players.IdentityIsNpc(identity.IdentityId))
                valid.Add(identity.IdentityId);
        }

        foreach (KeyValuePair<long, MyFaction> faction in MySession.Static.Factions.Where(pair => pair.Value.Members.Count > 0))
            valid.Add(faction.Key);

        valid.Remove(0);

        int count = 0;
        count += RemoveInvalidRelations(GetDictionaryField(MySession.Static.Factions, FactionRelationsField), valid);
        count += RemoveInvalidRelations(GetDictionaryField(MySession.Static.Factions, PlayerFactionRelationsField), valid);
        return count;
    }

    private static int CountReputations(bool removePlayerToFaction, bool removeFactionToFaction)
    {
        int count = 0;
        if (removeFactionToFaction)
            count += GetDictionaryField(MySession.Static.Factions, FactionRelationsField)?.Count ?? 0;

        if (removePlayerToFaction)
            count += GetDictionaryField(MySession.Static.Factions, PlayerFactionRelationsField)?.Count ?? 0;

        return count;
    }

    private static int WipeReputations(bool removePlayerToFaction, bool removeFactionToFaction)
    {
        int count = 0;
        if (removeFactionToFaction)
            count += ClearDictionary(GetDictionaryField(MySession.Static.Factions, FactionRelationsField));

        if (removePlayerToFaction)
            count += ClearDictionary(GetDictionaryField(MySession.Static.Factions, PlayerFactionRelationsField));

        return count;
    }

    private static int ClearDictionary(IDictionary dictionary)
    {
        if (dictionary == null)
            return 0;

        int count = dictionary.Count;
        dictionary.Clear();
        return count;
    }

    private static int RemoveInvalidRelations(IDictionary relations, HashSet<long> validIdentities)
    {
        if (relations == null)
            return 0;

        List<object> remove = new List<object>();
        foreach (DictionaryEntry entry in relations)
        {
            if (entry.Key is not MyFactionCollection.MyRelatablePair pair)
                continue;

            if (!validIdentities.Contains(pair.RelateeId1) || !validIdentities.Contains(pair.RelateeId2))
                remove.Add(entry.Key);
        }

        foreach (object key in remove)
            relations.Remove(key);

        return remove.Count;
    }

    private static int CleanupGps(HashSet<long> validIdentities)
    {
        IDictionary gpss = GetDictionaryField(MySession.Static.Gpss, GpssField);
        if (gpss == null)
            return 0;

        List<object> remove = new List<object>();
        foreach (object key in gpss.Keys)
        {
            if (key is long identityId && !validIdentities.Contains(identityId))
                remove.Add(key);
        }

        foreach (object key in remove)
            gpss.Remove(key);

        return remove.Count;
    }

    private static int ClearProceduralSeeds()
    {
        MyProceduralWorldGenerator generator = MySession.Static.GetComponent<MyProceduralWorldGenerator>();
        return ClearCollectionField(generator, SeedParamsField);
    }

    private static int ClearCameraSettings()
    {
        if (CamerasField == null || EntityCameraSettingsField == null)
            return 0;

        object cameras = CamerasField.GetValue(MySession.Static);
        IDictionary settings = GetDictionaryField(cameras, EntityCameraSettingsField);
        if (settings == null)
            return 0;

        int count = settings.Count;
        settings.Clear();
        return count;
    }

    private static IDictionary GetDictionaryField(object instance, FieldInfo field)
        => instance == null || field == null ? null : field.GetValue(instance) as IDictionary;

    private static int ClearCollectionField(object instance, FieldInfo field)
    {
        object collection = instance == null || field == null ? null : field.GetValue(instance);
        if (collection == null)
            return 0;

        PropertyInfo countProperty = collection.GetType().GetProperty("Count");
        MethodInfo clearMethod = collection.GetType().GetMethod("Clear", Type.EmptyTypes);
        if (countProperty == null || clearMethod == null)
            return 0;

        int count = (int)countProperty.GetValue(collection);
        clearMethod.Invoke(collection, null);
        return count;
    }
}
