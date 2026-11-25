using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.EntityComponents.Blocks;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders.Components.Beacon;

namespace ServerPlugin.ShipFixer;

internal sealed class ShipFixerSafeZones
{
    // Vanilla saves the running upkeep clock, but uses a separate balance while activation is paused.
    private static readonly AccessTools.FieldRef<MySafeZoneComponent, TimeSpan> PausedCredit =
        AccessTools.FieldRefAccess<MySafeZoneComponent, TimeSpan>("m_timeLeft");

    private readonly Dictionary<
        MyObjectBuilder_SafeZoneBlock,
        (MyObjectBuilder_SafeZone Zone, MyObjectBuilder_SafeZoneComponent Timer, TimeSpan Credit)
    > blocks = new();

    internal void Capture(MyCubeGrid grid, MyObjectBuilder_SafeZoneBlock block)
    {
        // Clipboard copies omit SafeZoneId. The component still carries the activation and upkeep clocks.
        var manager = grid.GetCubeBlock(block.Min)?.FatBlock?.Components.Get<MySafeZoneComponent>();
        if (manager == null)
            return;

        MyObjectBuilder_SafeZone zone = null;
        if (
            manager.SafeZoneEntityId != 0
            && MyEntities.TryGetEntityById(manager.SafeZoneEntityId, out MySafeZone entity)
            && entity.SafeZoneBlockId == block.EntityId
        )
            zone = (MyObjectBuilder_SafeZone)entity.GetObjectBuilder();

        var timer = block
            .ComponentContainer?.Components.Select(c => c.Component)
            .OfType<MyObjectBuilder_SafeZoneComponent>()
            .FirstOrDefault();
        blocks.Add(block, (zone, timer, PausedCredit(manager)));
    }

    internal void Remap(List<MyObjectBuilder_CubeGrid> grids)
    {
        var oldIds = blocks.Count > 0 ? grids.ToDictionary(grid => grid.EntityId) : null;
        MyEntities.RemapObjectBuilderCollection(grids);

        foreach (var entry in blocks)
        {
            var block = entry.Key;
            if (entry.Value.Timer?.SafeZoneOb is MyObjectBuilder_SafeZone settings)
                RemapGridFilter(settings, oldIds);

            var zone = entry.Value.Zone;
            if (zone == null)
                continue;

            zone.EntityId = MyEntityIdentifier.AllocateId();
            block.SafeZoneId = zone.EntityId;
            zone.SafeZoneBlockId = block.EntityId;
            RemapGridFilter(zone, oldIds);
        }
    }

    private static void RemapGridFilter(
        MyObjectBuilder_SafeZone zone,
        Dictionary<long, MyObjectBuilder_CubeGrid> grids
    )
    {
        if (zone.Entities == null)
            return;
        for (int i = 0; i < zone.Entities.Length; i++)
            if (grids.TryGetValue(zone.Entities[i], out var pasted))
                zone.Entities[i] = pasted.EntityId;
    }

    internal void Restore(MyObjectBuilder_CubeGrid builder, MyCubeGrid grid)
    {
        if (blocks.Count == 0)
            return;
        foreach (var block in builder.CubeBlocks.OfType<MyObjectBuilder_SafeZoneBlock>())
        {
            if (!blocks.TryGetValue(block, out var state))
                continue;
            var manager = grid.GetCubeBlock(block.Min)
                ?.FatBlock?.Components.Get<MySafeZoneComponent>();
            if (manager == null)
                continue;
            // A parallel paste can spend frames building the grid before this callback.
            // Resume both relative timers when the replacement enters the world.
            if (state.Timer != null)
                manager.Deserialize(state.Timer);
            PausedCredit(manager) = state.Credit;
            if (state.Zone != null)
                MyEntities.CreateFromObjectBuilderAndAdd(state.Zone, fadeIn: false);
        }
    }
}
