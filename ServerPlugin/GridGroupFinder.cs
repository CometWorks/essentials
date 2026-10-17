using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Groups;
using VRageMath;

namespace ServerPlugin;

public static class GridGroupFinder
{
    public static ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> FindGridGroup(
        string gridName
    )
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups =
            new ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group>();

        Parallel.ForEach(
            MyCubeGridGroups.Static.Physical.Groups,
            group =>
            {
                foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Node node in group.Nodes)
                {
                    MyCubeGrid grid = node.NodeData;
                    if (grid == null || grid.MarkedForClose || grid.MarkedAsTrash || !grid.InScene)
                        continue;

                    if (grid.DisplayName == gridName || grid.EntityId.ToString() == gridName)
                        groups.Add(group);
                }
            }
        );

        return groups;
    }

    public static ConcurrentBag<MyGroups<
        MyCubeGrid,
        MyGridPhysicalGroupData
    >.Group> FindLookAtGridGroup(MyCharacter controlledEntity)
    {
        const float range = 5000;

        Matrix worldMatrix = controlledEntity.GetHeadMatrix(true);
        Vector3D startPosition = worldMatrix.Translation + worldMatrix.Forward * 0.5f;
        Vector3D endPosition = worldMatrix.Translation + worldMatrix.Forward * range;
        RayD ray = new RayD(startPosition, worldMatrix.Forward);
        Dictionary<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group, double> matches =
            new Dictionary<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group, double>();

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
                if (grid == null || grid.MarkedForClose || grid.MarkedAsTrash || !grid.InScene)
                    continue;

                if (!ray.Intersects(grid.PositionComp.WorldAABB).HasValue)
                    continue;

                Vector3I? hit = grid.RayCastBlocks(startPosition, endPosition);
                if (!hit.HasValue)
                    continue;

                double distance = (startPosition - grid.GridIntegerToWorld(hit.Value)).Length();
                if (!matches.TryGetValue(group, out double oldDistance) || distance < oldDistance)
                    matches[group] = distance;
            }
        }

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> result =
            new ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group>();
        if (matches.Count > 0)
            result.Add(matches.OrderBy(item => item.Value).First().Key);

        return result;
    }

    public static ConcurrentBag<MyGroups<
        MyCubeGrid,
        MyGridMechanicalGroupData
    >.Group> FindMechanicalGridGroup(string gridName)
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> groups =
            new ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group>();

        Parallel.ForEach(
            MyCubeGridGroups.Static.Mechanical.Groups,
            group =>
            {
                foreach (MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Node node in group.Nodes)
                {
                    MyCubeGrid grid = node.NodeData;
                    if (grid == null || grid.MarkedForClose || grid.MarkedAsTrash || !grid.InScene)
                        continue;

                    if (grid.DisplayName == gridName || grid.EntityId.ToString() == gridName)
                        groups.Add(group);
                }
            }
        );

        return groups;
    }

    public static ConcurrentBag<MyGroups<
        MyCubeGrid,
        MyGridMechanicalGroupData
    >.Group> FindLookAtMechanicalGridGroup(MyCharacter controlledEntity)
    {
        const float range = 5000;

        Matrix worldMatrix = controlledEntity.GetHeadMatrix(true);
        Vector3D startPosition = worldMatrix.Translation + worldMatrix.Forward * 0.5f;
        Vector3D endPosition = worldMatrix.Translation + worldMatrix.Forward * range;
        RayD ray = new RayD(startPosition, worldMatrix.Forward);
        Dictionary<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group, double> matches =
            new Dictionary<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group, double>();

        foreach (
            MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group in MyCubeGridGroups
                .Static
                .Mechanical
                .Groups
        )
        {
            foreach (MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Node node in group.Nodes)
            {
                MyCubeGrid grid = node.NodeData;
                if (grid == null || grid.MarkedForClose || grid.MarkedAsTrash || !grid.InScene)
                    continue;

                if (!ray.Intersects(grid.PositionComp.WorldAABB).HasValue)
                    continue;

                Vector3I? hit = grid.RayCastBlocks(startPosition, endPosition);
                if (!hit.HasValue)
                    continue;

                double distance = (startPosition - grid.GridIntegerToWorld(hit.Value)).Length();
                if (!matches.TryGetValue(group, out double oldDistance) || distance < oldDistance)
                    matches[group] = distance;
            }
        }

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> result =
            new ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group>();
        if (matches.Count > 0)
            result.Add(matches.OrderBy(item => item.Value).First().Key);

        return result;
    }
}
