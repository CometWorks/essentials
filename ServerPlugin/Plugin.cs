using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using PluginSdk;
using PluginSdk.Commands;
using ServerPlugin.AutoCommands;
using ServerPlugin.Commands;
using Shared.Config;
using Shared.Logging;
using Shared.Patches;
using Shared.Plugin;
using VRage.FileSystem;
using VRage.Game;
using VRage.Plugins;

// Define assembly version when compiled by Magnetar
#if !LOCAL_BUILD
using System.Reflection;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif

namespace ServerPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin, ICommonPlugin
{
    public const string Name = "Essentials";
    public static Plugin Instance { get; private set; }

    public long Tick { get; private set; }
    private static bool failed;

    public IPluginLogger Log => Logger;
    private static readonly IPluginLogger Logger = new PluginLogger(Name);

    public IPluginConfig Config => config?.Data;

    // Discovered by the Quasar agent, which scans public instance properties for the first
    // whose declared type is assignable to PluginSdk.Config.PluginConfig. The Config property
    // above is typed as the IPluginConfig interface (required by ICommonPlugin) and is NOT
    // assignable to PluginSdk's PluginConfig, so it is skipped. This concrete-typed property
    // makes the config visible; the "PluginConfig" name also gives it priority.
    public PluginConfig PluginConfig => config?.Data;

    private PersistentConfig<PluginConfig> config;
    private static readonly string ConfigFileName = $"{Name}.cfg";

    // Timed/triggered server command sequences. Null until Init has run.
    public AutoCommandExecutor AutoCommands { get; private set; }
    private CleanupLookNotifications cleanupLookNotifications;
    private RefillContainers refillContainers;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
#if DEBUG
        // Allow the debugger some time to connect once the plugin assembly is loaded
        Thread.Sleep(100);
#endif

        Instance = this;

        Log.Info("Loading");

        var configPath = Path.Combine(MyFileSystem.UserDataPath, ConfigFileName);
        config = PersistentConfig<PluginConfig>.Load(Log, configPath);

        var gameVersion = MyFinalBuildConstants.APP_VERSION_STRING.ToString();
        Common.SetPlugin(this, gameVersion, MyFileSystem.UserDataPath);

        ServerCommands.Register(
            Assembly.GetExecutingAssembly(),
            typeof(EssentialsModule),
            typeof(StoneCommand));

        AutoCommands = new AutoCommandExecutor(config.Data, Log);
        cleanupLookNotifications = new CleanupLookNotifications(config.Data);
        refillContainers = new RefillContainers(config.Data, Log);
        ServerControl.Terminating += OnTerminating;

        if (!PatchHelpers.HarmonyPatchAll(Log, new Harmony(Name)))
        {
            failed = true;
            return;
        }

        Log.Debug("Successfully loaded");
    }

    public void Dispose()
    {
        try
        {
            ServerControl.Terminating -= OnTerminating;
            // IMPORTANT: Do NOT call harmony.UnpatchAll() here! It may break other plugins.
        }
        catch (Exception ex)
        {
            Log.Critical(ex, "Dispose failed");
        }

        Instance = null;
    }

    private void OnTerminating(ServerTerminationKind kind)
    {
        // The countdown/warning sequences run from the !ess restart / !ess stop commands.
        // This only observes admin-driven termination (e.g. the host's own commands) for the log.
        Log.Info("Server {0} requested by admin", kind);
    }

    public void Update()
    {
        if (failed)
            return;
        
#if DEBUG
        CustomUpdate();
        Tick++;
#else        
        try
        {
            CustomUpdate();
            Tick++;
        }
        catch (Exception e)
        {
            Log.Critical(e, "Update failed");
            failed = true;
        }
#endif       
    }

    private void CustomUpdate()
    {
        PatchHelpers.PatchUpdates();
        AutoCommands?.Update();
        cleanupLookNotifications?.Update();
        refillContainers?.Update();
    }
}
