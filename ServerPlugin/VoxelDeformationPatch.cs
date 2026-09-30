using System;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;

namespace ServerPlugin;

// The game creates collision cutouts after it has already applied grid damage and impulses.
// Suppressing only their queue entry keeps the collision response in the game's hands.
internal static class VoxelDeformationPatch
{
    [ThreadStatic] private static int missileExplosionDepth;
    [ThreadStatic] private static int meteorDeformationDepth;

    [HarmonyPatch(typeof(MyMissile), "ExecuteExplosion")]
    private static class MissileExplosion
    {
        private static void Prefix() => missileExplosionDepth++;

        // Finalizers also run if the game method throws; a stale thread-local flag would
        // otherwise affect unrelated explosions on this simulation thread.
        private static Exception Finalizer(Exception __exception)
        {
            missileExplosionDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MyExplosions), nameof(MyExplosions.AddExplosion))]
    private static class ExplosionQueue
    {
        private static void Prefix(ref MyExplosionInfo explosionInfo)
        {
            var config = Plugin.Instance?.PluginConfig;
            if (missileExplosionDepth > 0 && config?.Enabled == true && config.ProtectVoxelsFromMissiles)
                explosionInfo.AffectVoxels = false;
        }
    }

    [HarmonyPatch(typeof(MyGridPhysics), nameof(MyGridPhysics.PerformMeteoriteDeformation))]
    private static class MeteorDeformation
    {
        private static void Prefix() => meteorDeformationDepth++;

        private static Exception Finalizer(Exception __exception)
        {
            meteorDeformationDepth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MyGridPhysics), "EnqueueExplosion")]
    private static class GridCutoutQueue
    {
        private static bool Prefix()
        {
            var config = Plugin.Instance?.PluginConfig;
            if (config?.Enabled != true)
                return true;

            return meteorDeformationDepth > 0
                ? !config.ProtectVoxelsFromMeteors
                : !config.ProtectVoxelsFromGridCollisions;
        }
    }
}
