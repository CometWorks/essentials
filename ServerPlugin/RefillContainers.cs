using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
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
    private const float FullGasLevel = 0.999f;

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
            var missing = (MyFixedPoint)item.MinimumAmount - CountUsable(inventory, id);
            if (missing <= 0)
                continue;
            var content = MyObjectBuilderSerializer.CreateNewObject(id);
            // New gas bottles would be empty
            if (content is MyObjectBuilder_GasContainerObject bottle)
                bottle.GasLevel = 1f;
            inventory.AddItems(missing, content);
        }
    }

    // Empty or partly used bottles don't count, so players can't keep the stock down with them
    private static MyFixedPoint CountUsable(MyInventory inventory, MyDefinitionId id)
    {
        MyFixedPoint amount = 0;
        foreach (var item in inventory.GetItems())
        {
            if (item.Content.GetObjectId() != id)
                continue;
            if (
                item.Content is MyObjectBuilder_GasContainerObject bottle
                && bottle.GasLevel < FullGasLevel
            )
                continue;
            amount += item.Amount;
        }
        return amount;
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
