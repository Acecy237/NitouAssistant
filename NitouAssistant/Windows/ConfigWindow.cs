using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace NitouAssistant.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;

    public ConfigWindow(Plugin plugin) 
        : base("设置###AthCfg")
    {
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("这里什么都没有");
    }
}
