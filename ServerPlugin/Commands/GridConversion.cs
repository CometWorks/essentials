using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.SessionComponents;
using VRage.Game.ObjectBuilders.Components;

namespace ServerPlugin.Commands;

/// <summary>
/// Converts grids between ship and station on the server, after the command
/// has checked the caller's rights itself.
/// </summary>
/// <remarks>
/// Since game 1.211.017, MyCubeGrid.OnConvertedToShipRequest and
/// OnConvertedToStationRequest also call HasConversionRights with the sender
/// of the current network event. A server-side call has no player sender (the
/// ship request raises its event on the server itself), so the game refused
/// every conversion Essentials asked for. These run the steps after that check.
/// </remarks>
internal static class GridConversion
{
    // Private, with an internal enum parameter: the checks the ship request
    // runs after the rights check (station, physics, blocks, not in voxels)
    private static readonly MethodInfo TryConvertToDynamic = AccessTools.Method(
        typeof(MyCubeGrid),
        "TryConvertToDynamic"
    );

    private static readonly object ConvertToShipReason = Enum.Parse(
        AccessTools.Inner(typeof(MyCubeGrid), "MyTestDynamicReason"),
        "ConvertToShip"
    );

    public static void ToShip(MyCubeGrid grid) =>
        TryConvertToDynamic.Invoke(grid, new[] { ConvertToShipReason });

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
