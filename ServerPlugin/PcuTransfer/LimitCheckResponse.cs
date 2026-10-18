using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;

namespace ServerPlugin.PcuTransfer;

public sealed class LimitCheckResponse
{
    private readonly Dictionary<MyCubeBlockDefinition, int> overLimitBlocks =
        new Dictionary<MyCubeBlockDefinition, int>();

    public int PcuLimit { get; set; }
    public int CurrentPcu { get; set; }
    public int PcuAfterTransfer { get; set; }
    public int BlockLimit { get; set; }
    public int CurrentBlocks { get; set; }
    public int BlockLimitAfterTransfer { get; set; }

    public bool TypeLimitsFine => overLimitBlocks.Count == 0;
    public bool PcuFine => PcuLimit >= PcuAfterTransfer;
    public bool BlockLimitFine => BlockLimit >= BlockLimitAfterTransfer;
    public IReadOnlyDictionary<MyCubeBlockDefinition, int> OverLimitBlocks => overLimitBlocks;

    public void AddOverLimitBlock(MySlimBlock block)
    {
        MyCubeBlockDefinition definition = block.BlockDefinition;
        overLimitBlocks.TryGetValue(definition, out int count);
        overLimitBlocks[definition] = count + 1;
    }
}
