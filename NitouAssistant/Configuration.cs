using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace NitouAssistant;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // 保存地图选择状态
    public int SelectedVersionIndex { get; set; } = 0;

    // 保存勾选框状态
    public Dictionary<string, bool> SavedMapSelections = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
