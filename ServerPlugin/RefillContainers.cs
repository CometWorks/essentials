using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Shared.Config;
using Shared.Logging;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;

namespace ServerPlugin;

// Tops up NPC-owned cargo containers of a configured name to a minimum stock on a timer.
// Player-owned containers are skipped, so players can't name their own after a rule and farm it.
internal sealed class RefillContainers
{
    private const string TypePrefix = "MyObjectBuilder_";

    private readonly PluginConfig config;
    private readonly IPluginLogger log;
    private readonly Dictionary<int, DateTime> nextRefill = new();
    private readonly HashSet<string> reportedInvalidItems = new();
    private readonly List<RefillContainer> due = new();
    private DateTime lastCheck;

    public RefillContainers(PluginConfig config, IPluginLogger log)
    {
        this.config = config;
        this.log = log;
    }

    public void Update()
    {
        var now = DateTime.UtcNow;
        if ((now - lastCheck).TotalSeconds < 1)
            return;
        lastCheck = now;

        var rules = config.RefillContainers;
        if (
            !config.Enabled
            || rules == null
            || rules.Count == 0
            || !Sync.IsServer
            || MySession.Static?.Ready != true
        )
            return;

        due.Clear();
        for (var i = 0; i < rules.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rules[i].ContainerName))
                continue;
            if (nextRefill.TryGetValue(i, out var next) && now < next)
                continue;
            nextRefill[i] = now.AddSeconds(Math.Max(1, rules[i].IntervalSeconds));
            due.Add(rules[i]);
        }

        if (due.Count == 0)
            return;

        foreach (var entity in MyEntities.GetEntities())
        {
            if (entity is not MyCubeGrid grid || grid.MarkedForClose)
                continue;
            foreach (var container in grid.GetFatBlocks<MyCargoContainer>())
            {
                if (
                    container.MarkedForClose
                    || !MySession.Static.Players.IdentityIsNpc(container.OwnerId)
                )
                    continue;
                var name = container.CustomName?.ToString().Trim();
                foreach (var rule in due)
                {
                    if (
                        string.Equals(
                            name,
                            rule.ContainerName.Trim(),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        Refill(container, rule);
                }
            }
        }
    }

    private void Refill(MyCargoContainer container, RefillContainer rule)
    {
        if (rule.Items == null || container.InventoryCount == 0)
            return;
        var inventory = container.GetInventory();
        foreach (var item in rule.Items)
        {
            if (item.MinimumAmount <= 0 || !TryGetItem(item.Item, out var id))
                continue;
            var missing = (MyFixedPoint)item.MinimumAmount - inventory.GetItemAmount(id);
            if (missing > 0)
                inventory.AddItems(missing, MyObjectBuilderSerializer.CreateNewObject(id));
        }
    }

    private bool TryGetItem(string text, out MyDefinitionId id)
    {
        text = text?.Trim() ?? "";
        var full = text.StartsWith(TypePrefix) ? text : TypePrefix + text;
        if (
            MyDefinitionId.TryParse(full, out id)
            && MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out _)
        )
            return true;
        if (reportedInvalidItems.Add(text))
            log.Warning(
                "Refill containers: unknown item '{0}', expected Type/Subtype such as PhysicalGunObject/Welder4Item",
                text
            );
        return false;
    }
}
