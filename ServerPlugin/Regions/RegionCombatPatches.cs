using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Components;
using VRageMath;

namespace ServerPlugin.Regions;

internal static class RegionCombatPatches
{
    [HarmonyPatch(
        typeof(MySessionComponentSafeZones),
        nameof(MySessionComponentSafeZones.IsActionAllowed),
        typeof(MyEntity),
        typeof(MySafeZoneAction),
        typeof(long),
        typeof(ulong)
    )]
    private static class EntityAction
    {
        private static void Postfix(MyEntity entity, MySafeZoneAction action, ref bool __result)
        {
            if (
                __result
                && (action & MySafeZoneAction.Shooting) != 0
                && entity?.PositionComp != null
            )
                __result = !RegionRuntime.BlocksFiring(entity.PositionComp.WorldAABB);
        }
    }

    [HarmonyPatch(
        typeof(MySessionComponentSafeZones),
        nameof(MySessionComponentSafeZones.IsActionAllowed),
        typeof(BoundingBoxD),
        typeof(MySafeZoneAction),
        typeof(long),
        typeof(ulong)
    )]
    private static class BoundsAction
    {
        private static void Postfix(BoundingBoxD aabb, MySafeZoneAction action, ref bool __result)
        {
            if (__result && (action & MySafeZoneAction.Shooting) != 0)
                __result = !RegionRuntime.BlocksFiring(aabb);
        }
    }

    // Also check at discharge: a held trigger or a delayed launcher can move after CanShoot.
    [HarmonyPatch(
        typeof(MyGunBase),
        nameof(MyGunBase.Shoot),
        typeof(Vector3D),
        typeof(Vector3),
        typeof(Vector3),
        typeof(MyEntity),
        typeof(uint?),
        typeof(bool)
    )]
    private static class Discharge
    {
        private static bool Prefix(MyGunBase __instance, Vector3D initialPosition, MyEntity owner)
        {
            // For several fixed guns User.Weapon is the entire grid. The user itself is the block.
            var user = __instance.User;
            MyEntity source =
                user as MyCubeBlock
                ?? (MyEntity)(user?.Owner as MyCharacter)
                ?? owner as MyCharacter
                ?? user?.Weapon;
            return !RegionRuntime.BlocksFiring(
                source?.PositionComp?.WorldAABB
                    ?? new BoundingBoxD(initialPosition, initialPosition)
            );
        }
    }

    // Vanilla shifts rounds forward at spawn. Include the muzzle-to-spawn segment so a
    // launcher immediately outside a border cannot place ammunition on the protected side.
    [HarmonyPatch(typeof(MyProjectile), nameof(MyProjectile.Start))]
    private static class BulletSpawn
    {
        private static void Postfix(ref MyProjectile __instance, Vector3D origin)
        {
            if (RegionRuntime.BlocksEntry(origin, __instance.Origin))
                __instance.Remove();
        }
    }

    [HarmonyPatch(typeof(MyMissile), nameof(MyMissile.GetSpawnPoint))]
    private static class MissileSpawn
    {
        private static void Postfix(Vector3D position, ref Vector3D __result)
        {
            if (RegionRuntime.BlocksEntry(position, __result))
                __result = position;
        }
    }

    [HarmonyPatch(typeof(MyProjectile), "IsThereAnyPossibleHit")]
    private static class BulletBroadphase
    {
        private static void Postfix(ref LineD line, ref bool __result)
        {
            if (!__result)
                __result = RegionRuntime.BlocksEntry(line.From, line.To);
        }
    }

    [HarmonyPatch(typeof(MyProjectile), "GetHitEntityAndPosition")]
    private static class BulletCollision
    {
        private static void Postfix(
            ref MyProjectile __instance,
            LineD line,
            MyHitInfo hitInfoRet,
            ref bool __result
        )
        {
            // Vanilla casts ahead several frames. Stop at its nearer physical hit, including
            // for a narrow zone the line would otherwise pass all the way through in one cast.
            if (!RegionRuntime.BlocksEntry(line.From, __result ? hitInfoRet.Position : line.To))
                return;
            __instance.Remove();
            __result = false;
        }
    }

    [HarmonyPatch(typeof(MyMissile), nameof(MyMissile.UpdateAfterSimulation))]
    private static class MissileMovement
    {
        private static bool Prefix(
            MyMissile __instance,
            Vector3D ___m_previousPosition,
            MyEntity ___m_collidedEntity
        )
        {
            if (
                ___m_collidedEntity != null
                || !RegionRuntime.BlocksEntry(
                    ___m_previousPosition,
                    __instance.PositionComp.GetPosition()
                )
            )
                return true;
            RemoveMissile(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(MyMissile), "OnContactStart")]
    private static class MissileContact
    {
        private static bool Prefix(
            MyMissile __instance,
            ref MyPhysics.MyContactPointEvent value,
            Vector3D ___m_previousPosition,
            MyEntity ___m_collidedEntity
        )
        {
            if (
                ___m_collidedEntity != null
                || !RegionRuntime.BlocksEntry(___m_previousPosition, value.Position)
            )
                return true;
            // Havok owns this callback. Never remove a physics entity until back on the game thread.
            ScheduleRemoval(__instance, __instance.EntityId);
            return false;
        }
    }

    // Railgun slugs trace the remaining travel in the impact tick from the contact
    // point. Keep any nearer exterior blocks in that trace and clip it at the shield.
    [HarmonyPatch]
    private static class MissilePenetration
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MyMissile), "HitGrid");
            yield return AccessTools.DeclaredMethod(typeof(MyMissile), "HitMultipleGrids");
            yield return AccessTools.DeclaredMethod(typeof(MyMissile), "HitRicochet");
        }

        private static void Prefix(Vector3D? ___m_collisionPoint, ref Vector3D nextPosition)
        {
            if (___m_collisionPoint.HasValue)
                RegionRuntime.ClipPenetration(___m_collisionPoint.Value, ref nextPosition);
        }
    }

    private static void ScheduleRemoval(MyMissile missile, long id) =>
        MySandboxGame.Static.Invoke(
            () =>
            {
                if (missile.EntityId == id)
                    RemoveMissile(missile);
            },
            "Essentials region missile interception"
        );

    private static void RemoveMissile(MyMissile missile)
    {
        if (missile.MarkedForClose || !missile.InScene)
            return;
        // On the supported server build this also sends the launcher removal event to clients.
        missile.MarkForDestroy();
    }
}
