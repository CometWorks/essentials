using System.Collections.Generic;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Game;
using static Sandbox.Game.World.MyBlockLimits;

namespace ServerPlugin.PcuTransfer;

public sealed class VanillaLimitsChecker
{
    public LimitCheckResponse CheckLimits(List<MySlimBlock> blocks, MyIdentity newAuthor)
    {
        LimitCheckResponse response = new LimitCheckResponse();

        if (MySession.Static.BlockLimitsEnabled == MyBlockLimitsEnabledEnum.NONE)
            return response;

        Dictionary<string, short> limits = new Dictionary<string, short>(
            MySession.Static.BlockTypeLimits
        );
        MyBlockLimits blockLimits = newAuthor.BlockLimits;

        response.CurrentPcu = blockLimits.PCUBuilt;
        response.PcuLimit = blockLimits.PCU + blockLimits.PCUBuilt;
        response.CurrentBlocks = blockLimits.BlocksBuilt;
        response.BlockLimit = blockLimits.MaxBlocks;

        foreach (string blockType in blockLimits.BlockTypeBuilt.Keys)
        {
            MyTypeLimitData limit = blockLimits.BlockTypeBuilt[blockType];
            if (!limits.ContainsKey(blockType))
                continue;

            limits[blockType] = (short)(limits[blockType] - limit.BlocksBuilt);
        }

        int pcuOfGroup = 0;
        int blockCountOfGroup = 0;

        foreach (MySlimBlock block in blocks)
        {
            pcuOfGroup += block.BlockDefinition.PCU;
            blockCountOfGroup++;

            string blockType = block.BlockDefinition.BlockPairName;
            if (!limits.ContainsKey(blockType))
                continue;

            short remainingBlocks = (short)(limits[blockType] - 1);
            if (remainingBlocks < 0)
                response.AddOverLimitBlock(block);

            limits[blockType] = remainingBlocks;
        }

        if (response.BlockLimit > 0)
            response.BlockLimitAfterTransfer = response.CurrentBlocks + blockCountOfGroup;

        if (response.PcuLimit > 0)
            response.PcuAfterTransfer = response.CurrentPcu + pcuOfGroup;

        return response;
    }
}
