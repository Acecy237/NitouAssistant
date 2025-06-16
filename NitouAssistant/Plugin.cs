using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NitouAssistant.data;
using NitouAssistant.Windows;
using System.IO;

namespace NitouAssistant;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; set; } = null!;

    private const string MainUiCmooand = "/nta";
    private const string CfgUiCommand = "/ntacfg";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("NitouAssistant");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin() // 构造函数
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(MainUiCmooand, new CommandInfo(UiOnCommand)
        {
            HelpMessage = "打开主界面"
        });
        CommandManager.AddHandler(CfgUiCommand, new CommandInfo(CfgOnCommand)
        {
            HelpMessage = "打开设置"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        // 初始化地图选择状态
        if (Configuration.SavedMapSelections.Count == 0)
        {
            foreach (var mapList in MapMetaData.VersionToMaps.Values)
            {
                foreach (var map in mapList)
                {
                    Configuration.SavedMapSelections[map] = true;
                }
            }
            Configuration.Save();
        }
    }

    public void Dispose() // 析构函数
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(MainUiCmooand);
        CommandManager.RemoveHandler(CfgUiCommand);
    }

    private void UiOnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void CfgOnCommand(string command, string args)
    {
        ToggleConfigUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
