using System.Collections.Generic;
using PluginSdk.Config;

namespace Shared.Config;

[Tab("general", caption: "General")]
[Tab("motd", caption: "MOTD")]
[Tab("cleanup", caption: "Cleanup")]
[Tab("pcu", caption: "PCU Tools")]
[Tab("shipfixer", caption: "Ship Fixer")]
[Tab("autocmd", caption: "Auto Commands")]
[Tab("homes", caption: "Homes")]
[Tab("info", caption: "Info Commands")]
[Section("core", "general", "Core")]
[Section("grid", "general", "Grid Utilities")]
[Section("motd-main", "motd", "Messages")]
[Section("motd-url", "motd", "URL")]
[Section("cleanup-bags", "cleanup", "Backpacks")]
[Section("cleanup-notices", "cleanup", "Grid Look Notifications")]
[Section("pcu-core", "pcu", "Limits")]
[Section("shipfixer-core", "shipfixer", "Core")]
[Section("autocmd-list", "autocmd", "Auto Commands")]
[Section("autocmd-lifecycle", "autocmd", "Restart / Shutdown Sequences")]
[Section("homes-core", "homes", "Homes")]
[Section("info-list", "info", "Info Commands")]
public class PluginConfig : PluginSdk.Config.PluginConfig, IPluginConfig
{
    [BoolOption("Enable Essentials plugin runtime.", Parent = "core")]
    public bool Enabled { get; set => SetField(ref field, value); } = true;

    [BoolOption("Cut mods and block limits from matchmaking server info.", Parent = "core")]
    public bool CutGameTags { get; set => SetField(ref field, value); }

    [StringOption(description: "Message displayed to players when they connect.", Multiline = true, Parent = "motd-main")]
    public string Motd { get; set => SetField(ref field, value); } = "";

    [StringOption(description: "Message displayed only to first-time players.", Multiline = true, Parent = "motd-main")]
    public string NewUserMotd { get; set => SetField(ref field, value); } = "";

    [StringOption(description: "URL opened in the Steam overlay on player connect.", Parent = "motd-url")]
    public string MotdUrl { get; set => SetField(ref field, value); } = "";

    [BoolOption("Open MOTD URL only for first-time players.", Parent = "motd-url")]
    public bool NewUserMotdUrl { get; set => SetField(ref field, value); }

    [BoolOption("Stop all entities when the server starts.", Parent = "core")]
    public bool StopShipsOnStart { get; set => SetField(ref field, value); }

    [BoolOption("Show positions in owned-grid list output.", Parent = "grid")]
    public bool UtilityShowPosition { get; set => SetField(ref field, value); }

    [BoolOption("Show GPS markers for owned-grid list output.", Parent = "grid")]
    public bool MarkerShowPosition { get; set => SetField(ref field, value); }

    [IntOption(0, int.MaxValue, "Player !stone cooldown in seconds.", Parent = "grid")]
    public int StoneCooldownInSeconds { get; set => SetField(ref field, value); } = 10 * 60;

    [BoolOption("Allow players to save and teleport to home locations.", Parent = "homes-core")]
    public bool HomesEnabled { get; set => SetField(ref field, value); } = true;

    [IntOption(0, 100, "Maximum saved homes per player.", Parent = "homes-core")]
    public int MaxHomesPerPlayer { get; set => SetField(ref field, value); } = 3;

    [StructOption("Configured info command names listed by !ess info list.", Parent = "info-list")]
    public List<InfoCommand> InfoCommands { get; set => SetField(ref field, value); } = new();

    [IntOption(-1, int.MaxValue, "Maximum empty backpacks per player. Set -1 for no limit.", Parent = "cleanup-bags")]
    public int BackpackLimit { get; set => SetField(ref field, value); } = 3;

    [StructOption("Notify a grid's BigOwners when they look at a grid matching cleanup conditions. Empty by default.", Parent = "cleanup-notices")]
    public List<CleanupLookNotice> CleanupLookNotices { get; set => SetField(ref field, value); } = new();

    [IntOption(1, 1440, "Minutes between reminders for a grid after its BigOwner receives a cleanup notice.", Parent = "cleanup-notices")]
    public int CleanupLookReminderMinutes { get; set => SetField(ref field, value); } = 1;

    [BoolOption("Use BlockLimits Plugin when validating PCU transfer limits. Ignored when BlockLimits is detected and enabled.", Parent = "pcu-core")]
    public bool UseBlockLimitsPlugin { get; set => SetField(ref field, value); }

    [IntOption(0, int.MaxValue, "Player fixship cooldown in seconds.", Parent = "shipfixer-core")]
    public int ShipFixerCooldownInSeconds { get; set => SetField(ref field, value); } = 5 * 60;

    [IntOption(0, int.MaxValue, "Fixship confirmation window in seconds.", Parent = "shipfixer-core")]
    public int ShipFixerConfirmationInSeconds { get; set => SetField(ref field, value); } = 30;

    [BoolOption("Remove blueprints from projectors during fixship.", Parent = "shipfixer-core")]
    public bool ShipFixerRemoveBlueprintsFromProjectors { get; set => SetField(ref field, value); }

    [BoolOption("Allow player fixship command.", Parent = "shipfixer-core")]
    public bool ShipFixerPlayerCommandEnabled { get; set => SetField(ref field, value); } = true;

    [BoolOption("Allow faction ownership to satisfy player fixship ownership checks.", Parent = "shipfixer-core")]
    public bool ShipFixerFactionEnabled { get; set => SetField(ref field, value); }

    [BoolOption("Allow fixship to eject seated players.", Parent = "shipfixer-core")]
    public bool ShipFixerEjectPlayers { get; set => SetField(ref field, value); }

    [BoolOption("Process fixship grids in parallel.", Parent = "shipfixer-core")]
    public bool ShipFixerInParallel { get; set => SetField(ref field, value); } = true;

    [StructOption("Timed, scheduled and triggered server command sequences. Empty by default.", Parent = "autocmd-list")]
    public List<AutoCommand> AutoCommands { get; set => SetField(ref field, value); } = new();

    [StringOption(description: "Name of the auto command run as the countdown when an admin issues !ess restart. " +
                              "Empty restarts immediately.", Parent = "autocmd-lifecycle")]
    public string OnRestartSequence { get; set => SetField(ref field, value); } = "";

    [StringOption(description: "Name of the auto command run as the countdown when an admin issues !ess stop. " +
                              "Empty stops immediately.", Parent = "autocmd-lifecycle")]
    public string OnShutdownSequence { get; set => SetField(ref field, value); } = "";

    [IntOption(5, 3600, "Duration in seconds of a player vote started with !ess vote.", Parent = "autocmd-lifecycle")]
    public int VoteDurationSeconds { get; set => SetField(ref field, value); } = 60;
}

public struct InfoCommand
{
    [StructMember("Command name shown by !ess info list."), StructCaption]
    public string Command { get; set; }
}

public struct CleanupLookNotice
{
    [StructMember("!ess cleanup condition arguments without the command prefix. Quote values with spaces; include haspilot to match occupied grids."), StructCaption]
    public string Conditions { get; set; }

    [StructMember("HUD notification shown to BigOwners looking at a matching grid. Use {GridName} for the grid's name.")]
    public string Message { get; set; }
}
