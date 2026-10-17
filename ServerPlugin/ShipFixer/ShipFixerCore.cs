using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using ServerPlugin;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Groups;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using IMyShipController = Sandbox.ModAPI.IMyShipController;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace ServerPlugin.ShipFixer;

public sealed class ShipFixerCore
{
    public ShipFixerResult FixShip(IMyCharacter character, long playerId)
    {
        List<MyCubeGrid> groups = FindLookAtGridGroup(character, playerId, out _);
        return FixGroups(groups, playerId);
    }

    public ShipFixerResult FixShip(long playerId, string gridName)
    {
        List<MyCubeGrid> groups = FindGridGroupsForPlayer(gridName, playerId, out _);
        return FixGroups(groups, playerId);
    }

    public ShipFixerResult FixShip(long playerId, long gridId, string gridName = "nogrid")
    {
        List<MyCubeGrid> groups = FindGridGroupsForPlayer(gridName, playerId, out _, gridId);
        return FixGroups(groups, playerId);
    }

    public static ShipFixerResult CheckGroups(
        List<MyCubeGrid> groups,
        out List<MyCubeGrid> group,
        long playerId,
        bool ejectPlayers = true
    )
    {
        group = groups;

        if (groups == null || groups.Count == 0)
            return ShipFixerResult.GridNotFound;

        if (playerId != 0)
        {
            MyCubeGrid referenceGrid = null;
            foreach (MyCubeGrid grid in groups)
            {
                if (grid.Physics == null)
                    continue;

                if (!OwnershipCorrect(grid, playerId))
                    continue;

                referenceGrid = grid;
                break;
            }

            if (referenceGrid == null)
                return ShipFixerResult.OwnedByDifferentPlayer;

            foreach (MyCubeGrid grid in groups)
            {
                if (
                    grid.Physics == null
                    || grid == referenceGrid
                    || grid.IsSameConstructAs(referenceGrid)
                )
                    continue;

                if (!OwnershipCorrect(grid, playerId))
                    return ShipFixerResult.OwnedByDifferentPlayer;
            }
        }

        long ownerId = groups[0].BigOwners?.FirstOrDefault() ?? 0;
        MyFaction gridOwnerFaction =
            MySession.Static.Factions.TryGetPlayerFaction(ownerId) as MyFaction;
        bool gridOccupied = false;

        foreach (MyCubeGrid grid in groups)
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            IMyGridTerminalSystem terminalSystem =
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            terminalSystem.GetBlocksOfType<Sandbox.ModAPI.IMyTerminalBlock>(blocks);

            foreach (IMyTerminalBlock block in blocks)
            {
                if (block is not IMyShipController controller || !controller.IsUnderControl)
                    continue;

                if (playerId == 0)
                {
                    if (
                        ejectPlayers
                        && controller.Pilot != null
                        && controller is MyShipController shipController
                    )
                        shipController.Use();
                    else
                        gridOccupied = true;

                    continue;
                }

                long? controllingIdentityId = controller.ControllerInfo?.ControllingIdentityId;
                MyFaction controllingPlayerFaction = controllingIdentityId.HasValue
                    ? MySession.Static.Factions.TryGetPlayerFaction(controllingIdentityId.Value)
                        as MyFaction
                    : null;

                if (gridOwnerFaction != null && controllingPlayerFaction != null)
                {
                    Tuple<MyRelationsBetweenFactions, int> relation =
                        MySession.Static.Factions.GetRelationBetweenFactions(
                            gridOwnerFaction.FactionId,
                            controllingPlayerFaction.FactionId
                        );

                    if (
                        gridOwnerFaction.FactionId != controllingPlayerFaction.FactionId
                        && relation.Item1 == MyRelationsBetweenFactions.Enemies
                    )
                        return ShipFixerResult.GridOccupied;
                }

                if (
                    ejectPlayers
                    && controller.Pilot != null
                    && controller is MyShipController controlledBlock
                )
                    controlledBlock.Use();
                else
                    gridOccupied = true;
            }
        }

        if (!gridOccupied)
            return ShipFixerResult.OK;

        return Plugin.Instance.Config.ShipFixerEjectPlayers
            ? ShipFixerResult.OK
            : ShipFixerResult.GridOccupied;
    }

    public static List<MyCubeGrid> FindLookAtGridGroup(
        IMyCharacter controlledEntity,
        long playerId,
        out ShipFixerResult result
    )
    {
        const float range = 5000;
        List<MyCubeGrid> gridsGroup = new List<MyCubeGrid>();
        Vector3D charLocation = controlledEntity.PositionComp.GetPosition();
        BoundingSphereD sphere = new BoundingSphereD(charLocation, range);
        List<IMyEntity> entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

        if (entities == null || entities.Count == 0)
        {
            result = ShipFixerResult.GridNotFound;
            return gridsGroup;
        }

        Matrix worldMatrix = controlledEntity.GetHeadMatrix(true, true, false);
        Vector3D startPosition = worldMatrix.Translation + worldMatrix.Forward * 0.5f;
        Vector3D endPosition = worldMatrix.Translation + worldMatrix.Forward * (range + 0.5f);
        RayD ray = new RayD(startPosition, worldMatrix.Forward);
        Dictionary<MyCubeGrid, double> matches = new Dictionary<MyCubeGrid, double>();
        bool foundWrongOwner = false;

        foreach (IMyEntity entity in entities)
        {
            if (entity is not MyCubeGrid cubeGrid || cubeGrid.Physics == null)
                continue;

            if (!ray.Intersects(cubeGrid.PositionComp.WorldAABB).HasValue)
                continue;

            Vector3I? hit = cubeGrid.RayCastBlocks(startPosition, endPosition);
            if (!hit.HasValue)
                continue;

            if (playerId != 0 && !OwnershipCorrect(cubeGrid, playerId))
            {
                foundWrongOwner = true;
                continue;
            }

            double distance = (startPosition - cubeGrid.GridIntegerToWorld(hit.Value)).Length();
            if (!matches.TryGetValue(cubeGrid, out double oldDistance) || distance < oldDistance)
                matches[cubeGrid] = distance;
        }

        if (matches.Count > 0)
        {
            MyCubeGrid grid = matches.OrderBy(match => match.Value).First().Key;
            AddPhysicalGroup(grid, gridsGroup);
            result = ShipFixerResult.OK;
            return gridsGroup;
        }

        result = foundWrongOwner
            ? ShipFixerResult.OwnedByDifferentPlayer
            : ShipFixerResult.GridNotFound;
        return gridsGroup;
    }

    public static List<MyCubeGrid> FindGridGroupsForPlayer(
        string gridName,
        long playerId,
        out ShipFixerResult result,
        long id = 0
    )
    {
        List<MyCubeGrid> gridsGroup = new List<MyCubeGrid>();
        bool wrongOwner = false;

        foreach (
            MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group in MyCubeGridGroups
                .Static
                .Physical
                .Groups
        )
        {
            foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Node node in group.Nodes)
            {
                MyCubeGrid grid = node.NodeData;
                if (grid == null || grid.Physics == null)
                    continue;

                if (id != 0)
                {
                    if (grid.EntityId != id)
                        continue;

                    gridsGroup.Add(grid);
                    break;
                }

                if (!string.Equals(grid.DisplayName, gridName, StringComparison.Ordinal))
                    continue;

                if (playerId != 0 && !OwnershipCorrect(grid, playerId))
                {
                    wrongOwner = true;
                    continue;
                }

                gridsGroup.Add(grid);
                break;
            }
        }

        if (gridsGroup.Count > 0)
        {
            MyCubeGrid first = gridsGroup.First();
            gridsGroup.Clear();
            AddPhysicalGroup(first, gridsGroup);
            result = ShipFixerResult.OK;
            return gridsGroup;
        }

        result = wrongOwner ? ShipFixerResult.OwnedByDifferentPlayer : ShipFixerResult.GridNotFound;
        return gridsGroup;
    }

    public static bool OwnershipCorrect(MyCubeGrid grid, long playerId)
    {
        long gridOwner = grid.BigOwners?.FirstOrDefault() ?? 0;
        if (gridOwner == playerId || gridOwner == 0L)
            return true;

        if (!Plugin.Instance.Config.ShipFixerFactionEnabled)
            return false;

        IMyFaction playerFaction = MySession.Static.Factions.TryGetPlayerFaction(playerId);
        IMyFaction ownerFaction = MySession.Static.Factions.TryGetPlayerFaction(gridOwner);
        return playerFaction != null
            && ownerFaction != null
            && playerFaction.FactionId == ownerFaction.FactionId;
    }

    private ShipFixerResult FixGroups(List<MyCubeGrid> groups, long playerId)
    {
        if (groups.Count == 0)
            return ShipFixerResult.GridNotFound;

        ShipFixerResult result = CheckGroups(
            groups,
            out List<MyCubeGrid> group,
            playerId,
            Plugin.Instance.Config.ShipFixerEjectPlayers
        );
        if (result != ShipFixerResult.OK)
            return result;

        MyIdentity executingPlayer =
            playerId == 0 ? null : MySession.Static.Players.TryGetIdentity(playerId);
        return FixGroup(group, executingPlayer);
    }

    private ShipFixerResult FixGroup(List<MyCubeGrid> grids, MyIdentity executingPlayer)
    {
        string playerName = executingPlayer?.DisplayName ?? "Server";
        List<MyObjectBuilder_EntityBase> objectBuilders = new List<MyObjectBuilder_EntityBase>();
        List<MyCubeGrid> gridsToClose = new List<MyCubeGrid>();

        foreach (MyCubeGrid grid in grids)
        {
            gridsToClose.Add(grid);
            grid.Physics?.ClearSpeed();

            MyObjectBuilder_EntityBase objectBuilder = grid.GetObjectBuilder(true);
            if (objectBuilder is MyObjectBuilder_CubeGrid gridBuilder)
            {
                foreach (MyObjectBuilder_CubeBlock cubeBlock in gridBuilder.CubeBlocks)
                {
                    if (cubeBlock is MyObjectBuilder_ProjectorBase projector)
                    {
                        projector.Enabled = false;
                        if (Plugin.Instance.Config.ShipFixerRemoveBlueprintsFromProjectors)
                            projector.ProjectedGrids = null;
                    }

                    if (cubeBlock is MyObjectBuilder_OxygenTank oxygenTank)
                        oxygenTank.AutoRefill = false;
                }
            }

            objectBuilders.Add(objectBuilder);
        }

        foreach (MyCubeGrid grid in gridsToClose)
        {
            Plugin.Instance.Log.Warning(
                $"Player {playerName} used ShipFixer on Grid {grid.DisplayName} for cut & paste."
            );
            grid.Close();
        }

        SpawnObjectBuilders(objectBuilders, Plugin.Instance.Config.ShipFixerInParallel);
        return ShipFixerResult.ShipFixed;
    }

    private static void AddPhysicalGroup(MyCubeGrid grid, List<MyCubeGrid> output)
    {
        List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
        MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Physical, grids);

        foreach (IMyCubeGrid item in grids)
        {
            if (item is MyCubeGrid cubeGrid)
                output.Add(cubeGrid);
        }

        output.Sort((left, right) => right.BlocksCount.CompareTo(left.BlocksCount));
        output.Sort((left, right) => left.GridSizeEnum.CompareTo(right.GridSizeEnum));
    }

    private static void SpawnObjectBuilders(
        List<MyObjectBuilder_EntityBase> objectBuilders,
        bool parallel
    )
    {
        MyAPIGateway.Entities.RemapObjectBuilderCollection(objectBuilders);

        if (parallel)
        {
            foreach (MyObjectBuilder_EntityBase objectBuilder in objectBuilders)
                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(
                    objectBuilder,
                    true,
                    OnEntitySpawned
                );

            return;
        }

        foreach (MyObjectBuilder_EntityBase objectBuilder in objectBuilders)
        {
            IMyEntity entity = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(objectBuilder);
            OnEntitySpawned(entity);
        }
    }

    private static void OnEntitySpawned(IMyEntity entity)
    {
        if (entity is MyCubeGrid grid)
            grid.DetectDisconnectsAfterFrame();
    }
}
