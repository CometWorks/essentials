using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using PluginSdk.Commands;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Private;
using VRage.Plugins;
using VRage.Voxels;
using VRageMath;

namespace EssentialsProbe;

// Test hooks of the Essentials suite: the state of the world on the server, the
// plugin config swapped at runtime, the plugin's in-memory state cleared between
// tests, and test fixtures (grids, identities, factions, asteroids) made on demand.
// Reaches Essentials by reflection, so it needs no build dependency on it.
public class Probe : IPlugin
{
    public void Init(object gameInstance) =>
        ServerCommands.Register(Assembly.GetExecutingAssembly(), typeof(ProbeCommands));

    public void Update() { }

    public void Dispose() { }
}

[CommandRoot("eprobe", "EssentialsProbe", "Essentials test hooks")]
public sealed class ProbeCommands : CommandModule
{
    private const BindingFlags Any =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // Identities, factions and asteroids the probe made, removed by reset
    private static readonly List<long> MadeIdentities = new();
    private static readonly List<string> MadeFactions = new();

    private static Assembly Essentials =>
        AppDomain
            .CurrentDomain.GetAssemblies()
            .First(a => a.GetType("ServerPlugin.AutoCommands.AutoCommandExecutor") != null);

    private static Type EssType(string name) => Essentials.GetType(name, true);

    private static object Plugin =>
        EssType("ServerPlugin.Plugin").GetProperty("Instance").GetValue(null);

    private static object Get(object target, string name)
    {
        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, Any);
        return property != null
            ? property.GetValue(target)
            : type.GetField(name, Any).GetValue(target);
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string V(Vector3D v) => $"{F(v.X)},{F(v.Y)},{F(v.Z)}";

    private static string Ok(string name, string body) => $"Probe {name}: {body}";

    // --- plugin config and state ----------------------------------------------------

    [Command(
        "reload",
        "Loads EssentialsProbe.cfg into the running plugin, which saves it as Essentials.cfg."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string Reload()
    {
        object live = Get(Plugin, "PluginConfig");
        Type type = live.GetType();
        string path = Path.Combine(MyFileSystem.UserDataPath, "EssentialsProbe.cfg");
        object loaded;
        using (StreamReader reader = File.OpenText(path))
            loaded = new XmlSerializer(type).Deserialize(reader);

        foreach (
            PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )
        )
        {
            if (property.CanRead && property.CanWrite)
                property.SetValue(live, property.GetValue(loaded));
        }

        return "Probe: config reloaded";
    }

    [Command("cfg", "A live config value of the plugin.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Cfg(string name)
    {
        object live = Get(Plugin, "PluginConfig");
        object value = live.GetType().GetProperty(name).GetValue(live);
        return Ok("cfg", $"{name}={Convert.ToString(value, CultureInfo.InvariantCulture)}");
    }

    // Clears the plugin's cooldowns, confirmations, offers, mutes, homes, votes and auto commands
    private void Forget()
    {
        foreach (
            string name in new[]
            {
                "ServerPlugin.Commands.EssentialsModule",
                "ServerPlugin.Commands.ShipFixerCommands",
                "ServerPlugin.Commands.StoneCommand",
                "ServerPlugin.ChatMuteService",
            }
        )
        {
            foreach (
                FieldInfo field in EssType(name)
                    .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            )
            {
                if (field.GetValue(null) is IDictionary dictionary)
                    dictionary.Clear();
            }
        }

        // The homes are cached; the file goes too
        EssType("ServerPlugin.HomeStore").GetField("data", Any).SetValue(null, null);
        File.Delete(Path.Combine(MyFileSystem.UserDataPath, "Essentials.Homes.xml"));

        object executor = Get(Plugin, "AutoCommands");
        foreach (object sequence in (IList)Get(executor, "running"))
            sequence.GetType().GetMethod("ForceComplete").Invoke(sequence, null);
        ((IList)Get(executor, "running")).Clear();
        ((IDictionary)Get(executor, "schedules")).Clear();
        executor.GetType().GetMethod("ResetVoteState").Invoke(executor, null);

        object notices = Get(Plugin, "cleanupLookNotifications");
        ((IDictionary)Get(notices, "playerStates")).Clear();
        ((ISet<string>)Get(notices, "reportedInvalidRules")).Clear();
    }

    [Command("flushlog", "Writes the game log out, so the tests can read what the plugin logged.")]
    [Permission(MyPromoteLevel.Admin)]
    public string FlushLog()
    {
        VRage.Utils.MyLog.Default.Flush();
        return Ok("flushlog", "done");
    }

    [Command("auto", "Names of the running auto commands, in list order.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Auto()
    {
        IEnumerable<string> names = ((IEnumerable)Get(Get(Plugin, "AutoCommands"), "running"))
            .Cast<object>()
            .Select(sequence => (string)Get(sequence, "Name"));
        return Ok("auto", string.Join("|", names));
    }

    // --- world reset ----------------------------------------------------------------

    [Command(
        "clear",
        "Removes every grid, floating object, meteor, made asteroid, safe zone, made identity and made faction."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string Clear()
    {
        int closed = 0;
        foreach (MyEntity entity in MyEntities.GetEntities().ToList())
        {
            bool remove =
                entity is MyCubeGrid
                || entity is MyFloatingObject
                || entity is MyMeteor
                || entity is MySafeZone
                || entity is MyInventoryBagEntity
                || (
                    entity is MyVoxelMap map
                    && (map.StorageName ?? "").StartsWith("ProcAsteroid", StringComparison.Ordinal)
                );
            if (!remove || entity.MarkedForClose)
                continue;

            if (entity is MyCubeGrid grid)
            {
                foreach (
                    MyShipController controller in grid.GetFatBlocks().OfType<MyShipController>()
                )
                {
                    if (controller.Pilot != null)
                        controller.Use();
                }
            }

            entity.Close();
            closed++;
        }

        foreach (string tag in MadeFactions)
        {
            IMyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
            if (faction != null)
                MyFactionCollection.RemoveFaction(faction.FactionId);
        }
        MadeFactions.Clear();

        foreach (long identityId in MadeIdentities)
        {
            if (MySession.Static.Players.HasIdentity(identityId))
                MySession.Static.Players.RemoveIdentity(identityId);
        }
        MadeIdentities.Clear();

        foreach (MyFaction faction in MySession.Static.Factions.Select(pair => pair.Value))
        {
            foreach (long station in faction.Stations.Select(s => s.Id).ToList())
                ((IDictionary)Get(faction, "m_stations")).Remove(station);
        }

        return Ok("clear", closed.ToString(CultureInfo.InvariantCulture));
    }

    [Command(
        "reset",
        "Puts back the state every test starts from: the config loaded, the plugin's state "
            + "forgotten, the world cleared, no other characters, B without rights, no GPS "
            + "entries and both players at their homes."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string Reset(
        ulong steamA,
        double ax,
        double ay,
        double az,
        ulong steamB,
        double bx,
        double by,
        double bz
    )
    {
        // The players' clients get out of their seats themselves. After an eject on
        // the server, the plugin's look notices sometimes missed the player.
        ulong[] seated = new[] { steamA, steamB }
            .Where(id => OnlinePlayer(id)?.Controller?.ControlledEntity is MyShipController)
            .ToArray();
        if (seated.Length > 0)
            return Ok("reset", "seated=" + string.Join(",", seated));

        Reload();
        Forget();
        Clear();
        // Characters a player left behind, like Delta's from the new-player MOTD test
        var ours = new HashSet<MyCharacter>(
            new[] { steamA, steamB }.Select(id => OnlinePlayer(id)?.Character)
        );
        foreach (MyCharacter character in MyEntities.GetEntities().OfType<MyCharacter>().ToList())
        {
            if (!ours.Contains(character) && !character.MarkedForClose)
                character.Close();
        }
        MySession.Static.SetUserPromoteLevel(steamB, MyPromoteLevel.None);
        foreach (ulong steamId in new[] { steamA, steamB })
        {
            long identityId = MySession.Static.Players.TryGetIdentityId(steamId);
            foreach (IMyGps gps in MyAPIGateway.Session.GPS.GetGpsList(identityId))
                MyAPIGateway.Session.GPS.RemoveGps(identityId, gps);
        }
        int moved = Home(steamA, new Vector3D(ax, ay, az)) + Home(steamB, new Vector3D(bx, by, bz));
        return Ok("reset", $"moved={moved}");
    }

    private int Home(ulong steamId, Vector3D home)
    {
        MyCharacter character = OnlinePlayer(steamId)?.Character;
        if (
            character == null
            || Vector3D.Distance(character.PositionComp.GetPosition(), home) < 0.5
        )
            return 0;
        Teleport(steamId, home.X, home.Y, home.Z);
        return 1;
    }

    private static MyPlayer OnlinePlayer(ulong steamId) =>
        MySession.Static.Players.GetOnlinePlayers().FirstOrDefault(p => p.Id.SteamId == steamId);

    [Command("ping", "Answers with its token, after the replies to the caller's earlier commands.")]
    [Permission(MyPromoteLevel.None)]
    public string Ping(string token) => Ok("ping", token);

    // --- grids and blocks -----------------------------------------------------------

    [Command(
        "spawn",
        "Creates the grids of an XML file under the server's data folder, ids as written."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string Spawn(string file)
    {
        string path = Path.Combine(MyFileSystem.UserDataPath, file);
        if (
            !MyObjectBuilderSerializerKeen.DeserializeXML(
                path,
                out MyObjectBuilder_Definitions definitions
            )
            || definitions.Prefabs == null
        )
            return Ok("spawn", "bad file");

        var ids = new List<long>();
        foreach (
            MyObjectBuilder_CubeGrid builder in definitions.Prefabs.SelectMany(p => p.CubeGrids)
        )
        {
            if (MyEntities.TryGetEntityById(builder.EntityId, out MyEntity old))
                old.Close();

            MyEntity entity = MyEntities.CreateFromObjectBuilderAndAdd(builder, true);
            ids.Add(entity?.EntityId ?? 0);
        }

        return Ok("spawn", string.Join(" ", ids));
    }

    [Command("grid", "A grid on the server.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Grid(long gridId)
    {
        if (!(MyEntities.GetEntityById(gridId) is MyCubeGrid grid) || grid.MarkedForClose)
            return Ok("grid", "none");

        Vector3D velocity = grid.Physics?.LinearVelocity ?? Vector3D.Zero;
        return Ok(
            "grid",
            $"name={grid.DisplayName.Replace(' ', '_')} blocks={grid.BlocksCount} pcu={grid.BlocksPCU} "
                + $"static={(grid.IsStatic ? 1 : 0)} editable={(grid.Editable ? 1 : 0)} "
                + $"destructible={(grid.DestructibleBlocks ? 1 : 0)} pos={V(grid.PositionComp.GetPosition())} "
                + $"vel={V(velocity)} owners={string.Join(",", grid.BigOwners)}"
        );
    }

    [Command("named", "Entity ids of the grids with this name.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Named(string name) =>
        Ok(
            "named",
            string.Join(
                " ",
                MyEntities
                    .GetEntities()
                    .OfType<MyCubeGrid>()
                    .Where(g => !g.MarkedForClose && g.DisplayName == name)
                    .Select(g => g.EntityId)
                    .OrderBy(i => i)
            )
        );

    [Command("block", "A block on the server: its grid, owner, builder and on/off state.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Block(long blockId)
    {
        if (
            !(MyEntities.GetEntityById(blockId) is MyCubeBlock block)
            || block.MarkedForClose
            || block.SlimBlock.IsDestroyed
        )
            return Ok("block", "none");

        string enabled = block is MyFunctionalBlock functional
            ? (functional.Enabled ? "1" : "0")
            : "-";
        return Ok(
            "block",
            $"grid={block.CubeGrid.EntityId} owner={block.OwnerId} builtby={block.SlimBlock.BuiltBy} "
                + $"share={block.IDModule?.ShareMode} enabled={enabled}"
        );
    }

    [Command("cells", "The min cells of a grid's blocks, as x,y,z.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Cells(long gridId)
    {
        if (!(MyEntities.GetEntityById(gridId) is MyCubeGrid grid))
            return Ok("cells", "none");

        return Ok(
            "cells",
            string.Join(
                " ",
                grid.GetBlocks().Select(b => $"{b.Min.X},{b.Min.Y},{b.Min.Z}").OrderBy(s => s)
            )
        );
    }

    [Command("velocity", "Sets a grid's linear velocity in m/s.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Velocity(long gridId, double x, double y, double z)
    {
        if (!(MyEntities.GetEntityById(gridId) is MyCubeGrid grid) || grid.Physics == null)
            return Ok("velocity", "none");

        grid.Physics.LinearVelocity = new Vector3(x, y, z);
        return Ok("velocity", "set");
    }

    [Command("action", "Applies a terminal action to a block on the server.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Action(long blockId, string action)
    {
        if (!(MyEntities.GetEntityById(blockId) is Sandbox.ModAPI.IMyTerminalBlock block))
            return Ok("action", "none");

        var terminalAction = block.GetActionWithName(action);
        if (terminalAction == null)
            return Ok("action", "unknown");

        terminalAction.Apply(block);
        return Ok("action", "applied");
    }

    [Command("attach", "Builds the top part of a rotor that has none, and names its grid.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Attach(long blockId, string topName)
    {
        if (!(MyEntities.GetEntityById(blockId) is MyMechanicalConnectionBlockBase stator))
            return Ok("attach", "none");

        if (stator.TopGrid == null)
            typeof(MyMechanicalConnectionBlockBase)
                .GetMethod("RecreateTop", Any)
                .Invoke(
                    stator,
                    new object[]
                    {
                        stator.OwnerId,
                        MyMechanicalConnectionBlockBase.MyTopBlockSize.Normal,
                        true,
                    }
                );

        if (stator.TopGrid == null)
            return Ok("attach", "failed");

        stator.TopGrid.DisplayName = topName;
        return Ok("attach", stator.TopGrid.EntityId.ToString(CultureInfo.InvariantCulture));
    }

    [Command("projected", "Block count of what a projector projects.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Projected(long blockId)
    {
        if (!(MyEntities.GetEntityById(blockId) is MyProjectorBase projector))
            return Ok("projected", "none");

        int blocks = projector.Clipboard?.PreviewGrids?.Sum(g => g.CubeBlocks.Count) ?? 0;
        return Ok("projected", $"enabled={(projector.Enabled ? 1 : 0)} blocks={blocks}");
    }

    [Command(
        "gridprojector",
        "The first projector of a grid: on or off, and how many blueprint grids it holds."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string GridProjector(long gridId)
    {
        MyProjectorBase projector = (MyEntities.GetEntityById(gridId) as MyCubeGrid)
            ?.GetFatBlocks()
            .OfType<MyProjectorBase>()
            .FirstOrDefault();
        if (projector == null)
            return Ok("gridprojector", "none");

        var builder = (MyObjectBuilder_ProjectorBase)projector.GetObjectBuilderCubeBlock(true);
        return Ok(
            "gridprojector",
            $"enabled={(projector.Enabled ? 1 : 0)} blueprints={builder.ProjectedGrids?.Count ?? 0}"
        );
    }

    // --- inventories and floating objects -------------------------------------------

    [Command("inv", "Items in an entity's inventories, as type/subtype=amount.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Inventory(long entityId)
    {
        if (!(MyEntities.GetEntityById(entityId) is MyEntity entity) || !entity.HasInventory)
            return Ok("inv", "none");

        var amounts = new SortedDictionary<string, MyFixedPoint>(StringComparer.Ordinal);
        for (int i = 0; i < entity.InventoryCount; i++)
        {
            foreach (
                MyPhysicalInventoryItem item in ((MyInventory)entity.GetInventoryBase(i)).GetItems()
            )
            {
                string key =
                    item.Content.TypeId.ToString().Replace("MyObjectBuilder_", "")
                    + "/"
                    + item.Content.SubtypeName;
                amounts[key] =
                    (amounts.TryGetValue(key, out MyFixedPoint sum) ? sum : 0) + item.Amount;
            }
        }

        return Ok(
            "inv",
            amounts.Count == 0
                ? "empty"
                : string.Join(" ", amounts.Select(p => $"{p.Key}={F((double)p.Value)}"))
        );
    }

    [Command("additem", "Adds items to an entity's first inventory.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AddItem(long entityId, string type, string subtype, double amount)
    {
        if (!(MyEntities.GetEntityById(entityId) is MyEntity entity) || !entity.HasInventory)
            return Ok("additem", "none");

        var content = (MyObjectBuilder_PhysicalObject)
            MyObjectBuilderSerializerKeen.CreateNewObject(
                MyObjectBuilderType.Parse("MyObjectBuilder_" + type),
                subtype
            );
        ((MyInventory)entity.GetInventoryBase(0)).AddItems((MyFixedPoint)amount, content);
        return Ok("additem", "done");
    }

    [Command("floating", "Spawns a floating object of ore.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Floating(string subtype, double amount, double x, double y, double z)
    {
        var ore = MyObjectBuilderSerializerKeen.CreateNewObject<MyObjectBuilder_Ore>(subtype);
        MyFloatingObjects.Spawn(
            new MyPhysicalInventoryItem((MyFixedPoint)amount, ore),
            new Vector3D(x, y, z),
            Vector3D.Forward,
            Vector3D.Up
        );
        return Ok("floating", "spawned");
    }

    [Command("floats", "How many floating objects there are.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Floats() =>
        Ok(
            "floats",
            MyEntities
                .GetEntities()
                .OfType<MyFloatingObject>()
                .Count(f => !f.MarkedForClose)
                .ToString(CultureInfo.InvariantCulture)
        );

    // --- players, identities and factions -------------------------------------------

    [Command("player", "A player on the server: identity, promote level, character and seat.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Player(ulong steamId)
    {
        long identityId = MySession.Static.Players.TryGetIdentityId(steamId);
        MyIdentity identity = MySession.Static.Players.TryGetIdentity(identityId);
        MyPlayer player = MySession
            .Static.Players.GetOnlinePlayers()
            .FirstOrDefault(p => p.Id.SteamId == steamId);
        MyCharacter character = player?.Character;
        long seat = player?.Controller?.ControlledEntity is MyShipController controller
            ? controller.EntityId
            : 0;
        string position = character != null ? V(character.PositionComp.GetPosition()) : "-";
        return Ok(
            "player",
            $"identity={identityId} name={identity?.DisplayName?.Replace(' ', '_') ?? "-"} "
                + $"online={(player != null ? 1 : 0)} level={MySession.Static.GetUserPromoteLevel(steamId)} "
                + $"character={character?.EntityId ?? 0} dead={(character?.IsDead == true ? 1 : 0)} seat={seat} pos={position}"
        );
    }

    [Command(
        "tp",
        "Teleports a player the way the admin teleport does, for players without the right."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string Teleport(ulong steamId, double x, double y, double z)
    {
        typeof(MyMultiplayer)
            .GetMethod("OnTeleport", Any)
            .Invoke(null, new object[] { steamId, new Vector3D(x, y, z) });
        return Ok("tp", "done");
    }

    [Command(
        "dropplayer",
        "Removes a player who left: the identity, its account and its link to the Steam id."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string DropPlayer(ulong steamId)
    {
        if (OnlinePlayer(steamId) != null)
            return Ok("dropplayer", "online");
        long identityId = MySession.Static.Players.TryGetIdentityId(steamId);
        if (identityId == 0)
            return Ok("dropplayer", "none");
        MySession.Static.Players.RemoveIdentity(identityId, new MyPlayer.PlayerId(steamId, 0));
        return Ok("dropplayer", "done");
    }

    [Command("promote", "Sets a player's promote level.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Promote(ulong steamId, MyPromoteLevel level)
    {
        MySession.Static.SetUserPromoteLevel(steamId, level);
        return Ok("promote", MySession.Static.GetUserPromoteLevel(steamId).ToString());
    }

    [Command("identity", "Makes an identity that last logged in this many days ago.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Identity(string name, double daysAgo, bool npc = false)
    {
        MyIdentity identity = npc
            ? MySession.Static.Players.CreateNewNpcIdentity(name)
            : MySession.Static.Players.CreateNewIdentity(name);
        identity.LastLoginTime = DateTime.Now - TimeSpan.FromDays(daysAgo);
        MadeIdentities.Add(identity.IdentityId);
        return Ok("identity", identity.IdentityId.ToString(CultureInfo.InvariantCulture));
    }

    [Command("pirates", "The pirates identity and its faction.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Pirates()
    {
        long id = Sandbox.Game.World.MyPirateAntennas.GetPiratesId();
        return Ok(
            "pirates",
            $"id={id} faction={MySession.Static.Factions.GetPlayerFaction(id)?.Tag ?? "-"}"
        );
    }

    [Command("lastlogin", "Sets when an identity last logged in, in days ago.")]
    [Permission(MyPromoteLevel.Admin)]
    public string LastLogin(long identityId, double daysAgo)
    {
        MyIdentity identity = MySession.Static.Players.TryGetIdentity(identityId);
        if (identity == null)
            return Ok("lastlogin", "none");

        identity.LastLoginTime = DateTime.Now - TimeSpan.FromDays(daysAgo);
        return Ok("lastlogin", "set");
    }

    [Command("hasidentity", "Whether an identity exists, and its name.")]
    [Permission(MyPromoteLevel.Admin)]
    public string HasIdentity(long identityId)
    {
        MyIdentity identity = MySession.Static.Players.TryGetIdentity(identityId);
        return Ok(
            "hasidentity",
            identity == null ? "none" : identity.DisplayName.Replace(' ', '_')
        );
    }

    [Command("faction", "Makes a faction with a founder. The game may add it a frame later.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Faction(string tag, long founderId)
    {
        MyAPIGateway.Session.Factions.CreateFaction(founderId, tag, tag + " Testers", "", "");
        MadeFactions.Add(tag);
        return Ok("faction", "requested");
    }

    [Command("factioninfo", "A faction's id and members.")]
    [Permission(MyPromoteLevel.Admin)]
    public string FactionInfo(string tag)
    {
        IMyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
        return faction == null
            ? Ok("factioninfo", "none")
            : Ok(
                "factioninfo",
                $"id={faction.FactionId} members={string.Join(",", faction.Members.Keys.OrderBy(k => k))} "
                    + $"stations={faction.Stations.Count()}"
            );
    }

    [Command("rep", "Sets the reputation of a player towards a faction.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Rep(long identityId, string tag, int reputation)
    {
        IMyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
        if (faction == null)
            return Ok("rep", "none");

        MySession.Static.Factions.SetReputationBetweenPlayerAndFaction(
            identityId,
            faction.FactionId,
            reputation,
            ReputationChangeReason.Admin
        );
        return Ok("rep", "set");
    }

    [Command("reps", "How many player-faction and faction-faction relations are stored.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Reps()
    {
        int players = (
            (IDictionary)Get(MySession.Static.Factions, "m_relationsBetweenPlayersAndFactions")
        ).Count;
        int factions = (
            (IDictionary)Get(MySession.Static.Factions, "m_relationsBetweenFactions")
        ).Count;
        return Ok("reps", $"players={players} factions={factions}");
    }

    [Command("gps", "A player's GPS entries as name=shownOnHud.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Gps(long identityId)
    {
        List<IMyGps> list = MyAPIGateway.Session.GPS.GetGpsList(identityId);
        return Ok(
            "gps",
            string.Join(
                " ",
                list.OrderBy(g => g.Name)
                    .Select(g => $"{g.Name.Replace(' ', '_')}={(g.ShowOnHud ? 1 : 0)}")
            )
        );
    }

    [Command("gpspos", "The positions of a player's GPS entries with this name.")]
    [Permission(MyPromoteLevel.Admin)]
    public string GpsPos(long identityId, string name) =>
        Ok(
            "gpspos",
            string.Join(
                " ",
                MyAPIGateway
                    .Session.GPS.GetGpsList(identityId)
                    .Where(g => g.Name == name)
                    .Select(g => V(g.Coords))
            )
        );

    [Command("addgps", "Adds a GPS entry to a player.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AddGps(long identityId, string name, double x, double y, double z)
    {
        IMyGps gps = MyAPIGateway.Session.GPS.Create(name, "", new Vector3D(x, y, z), true);
        MyAPIGateway.Session.GPS.AddGps(identityId, gps);
        return Ok("addgps", "done");
    }

    [Command("balance", "An identity's or a faction's bank balance, -1 without an account.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Balance(long ownerId) =>
        Ok("balance", MyBankingSystem.GetBalance(ownerId).ToString(CultureInfo.InvariantCulture));

    [Command(
        "setbalance",
        "Sets an identity's or a faction's bank balance by making its account anew."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string SetBalance(long ownerId, long amount)
    {
        // A new account reaches the clients with its balance, not a change to it, so
        // the tests can set any balance without it being a debit.
        MyBankingSystem.Static.RemoveAccount(ownerId);
        MyBankingSystem.Static.CreateAccount(ownerId, amount);
        return Ok(
            "setbalance",
            MyBankingSystem.GetBalance(ownerId).ToString(CultureInfo.InvariantCulture)
        );
    }

    [Command(
        "changebalance",
        "Changes a bank balance the way the game does, in a broadcast delta."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string ChangeBalance(long ownerId, long amount) =>
        Ok("changebalance", MyBankingSystem.ChangeBalance(ownerId, amount).ToString());

    // --- voxels ---------------------------------------------------------------------

    [Command("asteroid", "Makes a procedural asteroid centred on a point.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Asteroid(int seed, float radius, double x, double y, double z)
    {
        IMyVoxelMap map = MyAPIGateway.Session.VoxelMaps.CreateProceduralVoxelMap(
            seed,
            radius,
            MatrixD.CreateWorld(new Vector3D(x, y, z)),
            seed
        );
        return Ok("asteroid", $"id={map.EntityId} name={map.StorageName}");
    }

    [Command("cut", "Cuts a sphere out of a voxel map.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Cut(long voxelId, double x, double y, double z, float radius)
    {
        if (!(MyEntities.GetEntityById(voxelId) is MyVoxelBase voxel))
            return Ok("cut", "none");

        IMyVoxelShapeSphere sphere = MyAPIGateway.Session.VoxelMaps.GetSphereVoxelHand();
        sphere.Center = new Vector3D(x, y, z);
        sphere.Radius = radius;
        MyAPIGateway.Session.VoxelMaps.CutOutShape(voxel, sphere);
        return Ok("cut", "done");
    }

    [Command("content", "Sum of voxel content in a box around a point, in full voxels.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Content(long voxelId, double x, double y, double z, float radius)
    {
        if (!(MyEntities.GetEntityById(voxelId) is MyVoxelBase voxel))
            return Ok("content", "none");

        Vector3D center = new Vector3D(x, y, z);
        MyVoxelCoordSystems.WorldPositionToVoxelCoord(
            voxel.PositionLeftBottomCorner,
            ref center,
            out Vector3I voxelCenter
        );
        Vector3I min = Vector3I.Max(voxelCenter - (int)Math.Ceiling(radius), Vector3I.Zero);
        Vector3I max = Vector3I.Min(
            voxelCenter + (int)Math.Ceiling(radius),
            voxel.Storage.Size - 1
        );
        // Outside the storage: the game's ReadRange throws on an empty range
        if (max.X < min.X || max.Y < min.Y || max.Z < min.Z)
            return Ok("content", F(0));
        var data = new MyStorageData();
        data.Resize(min, max);
        voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.Content, 0, min, max);
        long sum = 0;
        for (int i = 0; i < data.SizeLinear; i++)
            sum += data.Content(i);
        return Ok("content", F(sum / 255.0));
    }

    [Command("meteor", "Spawns a meteor flying in a direction.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Meteor(double x, double y, double z, float dx, float dy, float dz)
    {
        MyEntity meteor = MyMeteor.SpawnRandom(
            new Vector3D(x, y, z),
            Vector3.Normalize(new Vector3(dx, dy, dz))
        );
        return Ok("meteor", (meteor?.EntityId ?? 0).ToString(CultureInfo.InvariantCulture));
    }

    // --- safe zones and stations ----------------------------------------------------

    [Command("safezone", "Makes an enabled spherical safe zone.")]
    [Permission(MyPromoteLevel.Admin)]
    public string SafeZone(double x, double y, double z, float radius)
    {
        MyEntity zone = MySessionComponentSafeZones.CrateSafeZone(
            MatrixD.CreateWorld(new Vector3D(x, y, z)),
            MySafeZoneShape.Sphere,
            MySafeZoneAccess.Blacklist,
            null,
            null,
            radius,
            true
        );
        return Ok("safezone", zone.EntityId.ToString(CultureInfo.InvariantCulture));
    }

    [Command("zoneinfo", "A safe zone's listed players and factions.")]
    [Permission(MyPromoteLevel.Admin)]
    public string ZoneInfo(long zoneId)
    {
        if (!(MyEntities.GetEntityById(zoneId) is MySafeZone zone))
            return Ok("zoneinfo", "none");

        return Ok(
            "zoneinfo",
            $"players={string.Join(",", zone.Players.OrderBy(p => p))} "
                + $"factions={string.Join(",", zone.Factions.Select(f => f.Tag).OrderBy(t => t))}"
        );
    }

    [Command("station", "Registers an economy station with a grid at a point for a faction.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Station(string tag, long gridId, double x, double y, double z)
    {
        if (!(MySession.Static.Factions.TryGetFactionByTag(tag) is MyFaction faction))
            return Ok("station", "none");

        var station = new MyFactionStation(
            MyEntityIdentifier.AllocateId(
                MyEntityIdentifier.ID_OBJECT_TYPE.STATION,
                MyEntityIdentifier.ID_ALLOCATION_METHOD.RANDOM
            ),
            "ProbeStation",
            new Vector3D(x, y, z),
            MyStationTypeEnum.SpaceStation,
            faction,
            "ProbeStation",
            null
        );
        station.StationEntityId = gridId;
        faction.AddStation(station);
        return Ok("station", station.Id.ToString(CultureInfo.InvariantCulture));
    }
}
