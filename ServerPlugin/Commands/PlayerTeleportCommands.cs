using System;
using PluginSdk.Commands;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("tp", "Teleport one entity to another.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Teleport(string entityToMove = null, string destination = null) =>
        TeleportEntity(entityToMove, destination);

    [Command("tpto", "Teleport yourself or another entity to an entity.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TeleportTo(string destination, string entityToMove = null) =>
        TeleportEntity(entityToMove, destination);

    [Command("tphere", "Teleport an entity to you.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TeleportHere(string entityToMove, string destination = null) =>
        TeleportEntity(entityToMove, destination);

    private void TeleportEntity(string entityToMove, string destination)
    {
        if (
            !TryResolveTeleportEntity(
                destination,
                "Destination entity not found.",
                out IMyEntity destinationEntity
            )
        )
            return;

        if (
            !TryResolveTeleportEntity(
                entityToMove,
                "Target entity not found.",
                out IMyEntity targetEntity
            )
        )
            return;

        double radius = Math.Max(1.0, targetEntity.WorldAABB.Extents.Max());
        Vector3D? targetPosition = FindFreePlaceNear(
            destinationEntity.GetPosition(),
            (float)radius
        );
        if (targetPosition == null)
        {
            Context.Respond("No free place to teleport.");
            return;
        }

        targetEntity.PositionComp.SetPosition(targetPosition.Value);
        targetEntity.Physics?.ClearSpeed();
        Context.Respond(
            $"Teleported '{EntityDisplayName(targetEntity)}' to '{EntityDisplayName(destinationEntity)}'."
        );
    }

    private bool TryResolveTeleportEntity(string nameOrId, string error, out IMyEntity entity)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            entity = GetCallerControlledEntity();
            if (entity != null)
                return true;

            Context.Respond("Console must specify both source and destination entities.");
            return false;
        }

        if (Utilities.TryGetEntityByNameOrId(nameOrId, out entity))
            return true;

        Context.Respond(error);
        return false;
    }

    private IMyEntity GetCallerControlledEntity()
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return null;

        MyPlayer player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
        return player?.Controller?.ControlledEntity?.Entity;
    }
}
