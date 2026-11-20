using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.SessionComponents;
using VRage.Game.ObjectBuilders.Components;

namespace ServerPlugin.Commands;

/// <summary>
/// Converts grids between ship and station on the server, after the command
/// has checked the caller's rights itself. Needs game 1.211.018 or later.
/// </summary>
internal static class GridConversion
{
    /// <summary>
    /// Returns false when the game refuses the conversion, for example for a
    /// station in voxels.
    /// </summary>
    /// <remarks>
    /// The request event runs on the server at once. Since 1.211.018 it skips
    /// the rights check for a local call, and on failure it raises
    /// OnConvertToShipFailed to the server itself, which runs the callback
    /// before RequestConversionToShip returns.
    /// </remarks>
    public static bool ToShip(MyCubeGrid grid)
    {
        bool refused = false;
        grid.RequestConversionToShip(() => refused = true);
        return !refused;
    }

    /// <remarks>
    /// The game's station request would check the safe zone for the server's
    /// own Steam id when called locally. This checks it for the caller, so an
    /// admin with Ignore Safe Zones on can convert in a zone, as in the game.
    /// </remarks>
    public static void ToStation(MyCubeGrid grid, ulong steamId)
    {
        if (
            !grid.IsStatic
            && MySessionComponentSafeZones.IsActionAllowed(
                grid,
                MySafeZoneAction.ConvertToStation,
                0L,
                steamId
            )
        )
            MyMultiplayer.RaiseEvent(grid, x => x.ConvertToStatic);
    }
}
