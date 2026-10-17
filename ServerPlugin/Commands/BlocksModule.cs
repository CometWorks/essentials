using System;
using System.Collections.Generic;
using System.Linq;
using PluginSdk.Commands;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("blocks on type", "Turn on all blocks of the given type.")]
    public void OnType(string type)
    {
        int count = 0;
        foreach (MyFunctionalBlock block in FunctionalBlocks())
        {
            string blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
            if (string.Equals(type, blockType, StringComparison.InvariantCultureIgnoreCase))
            {
                block.Enabled = true;
                count++;
            }
        }

        Context.Respond($"Enabled {count} blocks of type {type}.");
    }

    [Command("blocks on subtype", "Turn on all blocks of the given subtype.")]
    public void OnSubtype(string subtype)
    {
        int count = 0;
        foreach (MyFunctionalBlock block in FunctionalBlocks())
        {
            string blockType = block.BlockDefinition.Id.SubtypeName;
            if (string.Equals(subtype, blockType, StringComparison.InvariantCultureIgnoreCase))
            {
                block.Enabled = true;
                count++;
            }
        }

        Context.Respond($"Enabled {count} blocks of subtype {subtype}.");
    }

    [Command("blocks off type", "Turn off all blocks of the given type.")]
    public void OffType(string type)
    {
        int count = 0;
        foreach (MyFunctionalBlock block in FunctionalBlocks())
        {
            string blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
            if (
                string.Equals(type, blockType, StringComparison.InvariantCultureIgnoreCase)
                && block.Enabled
            )
            {
                block.Enabled = false;
                count++;
            }
        }

        Context.Respond($"Disabled {count} blocks of type {type}.");
    }

    [Command("blocks off subtype", "Turn off all blocks of the given subtype.")]
    public void OffSubtype(string subtype)
    {
        int count = 0;
        foreach (MyFunctionalBlock block in FunctionalBlocks())
        {
            string blockType = block.BlockDefinition.Id.SubtypeName;
            if (string.Equals(subtype, blockType, StringComparison.InvariantCultureIgnoreCase))
            {
                block.Enabled = false;
                count++;
            }
        }

        Context.Respond($"Disabled {count} blocks of subtype {subtype}.");
    }

    [Command("blocks remove subtype", "Remove all blocks of the given subtype.")]
    public void RemoveSubtype(string subtype)
    {
        List<MySlimBlock> toRemove = Blocks()
            .Where(block =>
                string.Equals(
                    subtype,
                    block.BlockDefinition.Id.SubtypeName,
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            .ToList();

        foreach (MySlimBlock block in toRemove)
            block.CubeGrid?.RemoveBlock(block);

        Context.Respond($"Removed {toRemove.Count} blocks of subtype {subtype}.");
    }

    [Command("blocks remove type", "Remove all blocks of the given type.")]
    public void RemoveType(string type)
    {
        List<MySlimBlock> toRemove = Blocks()
            .Where(block =>
                string.Equals(
                    type,
                    block.BlockDefinition.Id.TypeId.ToString().Substring(16),
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
            .ToList();

        foreach (MySlimBlock block in toRemove)
            block.CubeGrid?.RemoveBlock(block);

        Context.Respond($"Removed {toRemove.Count} blocks of type {type}.");
    }

    [Command("blocks on general", "Turn on all blocks of the specified category.")]
    public void OnGeneral(BlockCategory category)
    {
        int count = 0;
        foreach (
            MyFunctionalBlock block in FunctionalBlocks()
                .Where(block => IsBlockTypeOf(block, category))
        )
        {
            block.Enabled = true;
            count++;
        }

        Context.Respond($"Enabled {count} {category} blocks.");
    }

    [Command("blocks off general", "Turn off all blocks of the specified category.")]
    public void OffGeneral(BlockCategory category)
    {
        int count = 0;
        foreach (
            MyFunctionalBlock block in FunctionalBlocks()
                .Where(block => IsBlockTypeOf(block, category))
        )
        {
            block.Enabled = false;
            count++;
        }

        Context.Respond($"Disabled {count} {category} blocks.");
    }

    private static IEnumerable<MyFunctionalBlock> FunctionalBlocks() =>
        MyEntities
            .GetEntities()
            .OfType<MyCubeGrid>()
            .Where(grid => grid.Projector == null)
            .SelectMany(grid => grid.GetFatBlocks().OfType<MyFunctionalBlock>());

    private static IEnumerable<MySlimBlock> Blocks() =>
        MyEntities
            .GetEntities()
            .OfType<MyCubeGrid>()
            .Where(grid => grid.Projector == null)
            .SelectMany(grid => grid.GetBlocks());

    private static bool IsBlockTypeOf(MyFunctionalBlock block, BlockCategory category)
    {
        switch (category)
        {
            case BlockCategory.Power:
                return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Reactor)
                    || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_BatteryBlock)
                    || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel)
                    || block.BlockDefinition.Id.TypeId
                        == typeof(MyObjectBuilder_FueledPowerProducer);

            case BlockCategory.Production:
                return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Assembler)
                    || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Refinery)
                    || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_OxygenGenerator);

            case BlockCategory.Weapons:
                return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret)
                    || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_LargeGatlingTurret)
                    || block.BlockDefinition.Id.TypeId
                        == typeof(MyObjectBuilder_LargeMissileTurret);

            default:
                throw new ArgumentOutOfRangeException(nameof(category), category, null);
        }
    }

    public enum BlockCategory
    {
        Power,
        Production,
        Weapons,
    }
}
