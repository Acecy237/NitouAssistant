using Dalamud.Game.ClientState.Conditions;

using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using ImGuiNET;
using Lumina.Excel.Sheets;
using NitouAssistant.data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Delegates;
using static NitouAssistant.data.MapMetaData;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NitouAssistant.Windows;

public unsafe class MainWindow : Window, IDisposable
{
    private Plugin Plugin;
    private Configuration Configuration;

    private string state = "";
    private int versionsSelectedIndex = 0;
    private Dictionary<string, bool> mapSelection = new();

    private enum TaskState
    {
        Idle,
        Prepare,
        Teleporting,
        WaitingMapLoad,
        Flying,
    }
    private TaskState currentState = TaskState.Idle;

    private int nextMapIndex = 0;
    private List<string> mapQueue = new();

    private DateTime teleportAttemptTime = DateTime.MinValue;
    private DateTime lastTeleportRetryTime = DateTime.MinValue;

    private int currentPointIndex = 0;
    private List<(float X, float Y, float Z)> currentPointsList = new();

    private Vector3? lastFlyTarget = null;
    private Vector3 lastPlayerPos = Vector3.Zero;
    private DateTime? stuckStartTime = null;


    public MainWindow(Plugin plugin)
        : base("Nitou Assistant##NitouAssistant", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(200, 100);
        SizeCondition = ImGuiCond.FirstUseEver;

        Plugin = plugin;
        Configuration = plugin.Configuration;

        versionsSelectedIndex = Configuration.SelectedVersionIndex;

        Plugin.Framework.Update += Update;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;

    }

    public void Dispose() 
    {
        Plugin.Framework.Update -= Update;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;

    }

    private void Update(IFramework framework)
    {
        switch (currentState)
        {
            case TaskState.Idle:
                state = "未运行";
                break;
            case TaskState.Prepare:
                PrepareNextMap();
                break;
            case TaskState.WaitingMapLoad:
                {
                    state = "计算最优路径";
                    CheckMapReadyToFly();
                    break;
                }
            case TaskState.Teleporting:
                {
                    if (!Plugin.Condition[ConditionFlag.Casting] && !Plugin.Condition[ConditionFlag.BetweenAreas])
                    {
                        if ((DateTime.Now - teleportAttemptTime).TotalSeconds <= 1)
                            break;
                        if ((DateTime.Now - lastTeleportRetryTime).TotalSeconds >= 1)
                        {
                            Plugin.Chat.Print("传送被打断，重试中...");
                            currentState = TaskState.Prepare;
                            lastTeleportRetryTime = DateTime.Now;
                        }
                    }
                    break;
                }
            case TaskState.Flying:
                {
                    FlyToNextPoint();
                    break;
                }
        }
    }

    public override void Draw()
    {
        ImGui.PushTextWrapPos();
        ImGui.TextUnformatted("欢迎使用 Nitou Assistant 插件！\n" +
            "这个插件用于解放泥头车头找怪，让车头在找怪期间去打OW\n" +
            "依赖插件: Vnavmesh、Teleport");
        ImGui.PopTextWrapPos();

        ImGui.Separator();

        ImGui.Text($"状态：{state}");

        using (var combo = ImRaii.Combo("##版本Combo", MapMetaData.VersionOptions[versionsSelectedIndex]))
        {
            if (combo)
            {
                for (var i = 0; i < MapMetaData.VersionOptions.Length; i++)
                {
                    bool isSelected = (i == versionsSelectedIndex);
                    if (ImGui.Selectable(MapMetaData.VersionOptions[i], isSelected))
                    {
                        versionsSelectedIndex = i;
                        Plugin.Configuration.SelectedVersionIndex = i;
                        Plugin.Configuration.Save();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();

                    if (i == 0)
                        ImGui.Separator();
                }
            }
        }

        if (versionsSelectedIndex > 0)
        {
            string selectedVersion = MapMetaData.VersionOptions[versionsSelectedIndex];

            if (MapMetaData.VersionToMaps.TryGetValue(selectedVersion, out var maps))
            {
                foreach (var map in maps)
                {
                    if (!mapSelection.ContainsKey(map))
                    {
                        bool selected = Configuration.SavedMapSelections.TryGetValue(map, out var saved) && saved;
                        mapSelection[map] = selected;
                    }

                    bool isChecked = mapSelection[map];
                    if (ImGui.Checkbox(map, ref isChecked))
                    {
                        mapSelection[map] = isChecked;
                        Configuration.SavedMapSelections[map] = isChecked;
                        Configuration.Save();
                    }
                }
            }
        }

        if (ImGui.Button("开始"))
        {
            mapQueue = getMapList();
            nextMapIndex = 0;
            currentState = TaskState.Prepare;
        }
        ImGui.SameLine();
        if (ImGui.Button("停止"))
        {
            VnavmeshStop();
            currentState = TaskState.Idle;
            nextMapIndex = 0;
            mapQueue.Clear();
            currentPointsList.Clear();
            lastFlyTarget = null;
            stuckStartTime = null;
        }
    }

    /* -------------------- Tools Func -------------------- */
    public void Mount()
    {
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
    }

    public static void Flyto(float x, float y, float z)
    {
        Plugin.CommandManager.ProcessCommand($"/vnav flyto {x} {y} {z}");
    }

    public static void VnavmeshStop()
    {
        Plugin.CommandManager.ProcessCommand("/vnav stop");
    }

    public static void Teleport(string Aetheryte)
    {
        Plugin.CommandManager.ProcessCommand($"/tp {Aetheryte}");
    }
    public static void TeleportToMap(string mapName)
    {
        MapMetaData.TpPoints.AetheryteList.TryGetValue(mapName, out var aetheryte);
        Teleport(aetheryte);
    }

    public List<string> getMapList()
    {
        var currentVersion = MapMetaData.VersionOptions[versionsSelectedIndex];

        MapMetaData.VersionToMaps.TryGetValue(currentVersion, out var versionMaps);

        var selectedMaps = versionMaps
            .Where(map => mapSelection.TryGetValue(map, out var selected) && selected)
            .ToList();

        return selectedMaps;
    }

    private void PrepareNextMap()
    {
        if (nextMapIndex >= mapQueue.Count)
        {
            state = "所有地图已完成";
            currentState = TaskState.Idle;
            return;
        }

        string nextMap = mapQueue[nextMapIndex];
        MapMetaData.MapTerritoryType.MapID.TryGetValue(nextMap, out var nextMapId);
        var currentMapId = Plugin.ClientState.TerritoryType;

        if (currentMapId != nextMapId)
        {
            TeleportToMap(nextMap);
            state = $"正在传送到 {nextMap}";
            teleportAttemptTime = DateTime.Now;
            currentState = TaskState.Teleporting;
            return;
        }
        else
        {
            currentState = TaskState.WaitingMapLoad;
            return;
        }
    }

    private void OnTerritoryChanged(ushort newTerritory)
    {
        currentState = TaskState.WaitingMapLoad;
    }

    private void CheckMapReadyToFly()
    {
        if (Plugin.ClientState.LocalPlayer != null && Plugin.ClientState.TerritoryType != 0)
        {
            var currentMap = mapQueue[nextMapIndex];
            currentPointsList = MapMetaData.RankASpawnPoints.Points[currentMap];
            var currentPos = Plugin.ClientState.LocalPlayer.Position;
            currentPointsList = PathHelper.GetGreedy2OptPath(currentPointsList, currentPos);
            currentPointIndex = 0;
            currentState = TaskState.Flying;
        }
    }

    private void FlyToNextPoint()
    {
        if (currentPointIndex >= currentPointsList.Count)
        {
            state = "当前地图飞行完成，切换下一个地图";
            nextMapIndex++;
            VnavmeshStop();
            currentState = TaskState.Prepare;
            lastFlyTarget = null;
            stuckStartTime = null;
            return;
        }

        var targetTuple = currentPointsList[currentPointIndex];
        var target = new Vector3(targetTuple.X, targetTuple.Y, targetTuple.Z);
        var player = Plugin.ClientState.LocalPlayer;
        if (player == null) return;

        var playerPos = player.Position;
        var distance = Vector3.Distance(playerPos, target);

        if (distance < 75f)
        {
            currentPointIndex++;
            lastFlyTarget = null;
            stuckStartTime = null;
            return;
        }

        float stillThreshold = 0.01f;
        bool isExactlyStill = Vector3.Distance(playerPos, lastPlayerPos) < stillThreshold;

        if (isExactlyStill)
        {
            if (stuckStartTime == null)
            {
                stuckStartTime = DateTime.Now;
            }
            else
            {
                var stuckDuration = (DateTime.Now - stuckStartTime.Value).TotalSeconds;
                if (stuckDuration > 1.0)
                {
                    Flyto(target.X, target.Y, target.Z);
                    lastFlyTarget = target;
                    stuckStartTime = null;
                }
            }
        }
        else
        {
            stuckStartTime = null;
        }

        lastPlayerPos = playerPos;

        if (lastFlyTarget == null)
        {
            Flyto(target.X, target.Y, target.Z);
            lastFlyTarget = target;
            state = $"飞往第 {currentPointIndex + 1}/{currentPointsList.Count} 个点...";
        }
    }

    /* -------------------- Helper class for getPath -------------------- */
    public static class PathHelper
    {
        public static List<(float X, float Y, float Z)> GetGreedy2OptPath(
            List<(float X, float Y, float Z)> points,
            Vector3 currentPos)
        {
            var path = GetGreedyPath(points, currentPos);

            path = Apply2Opt(path);

            return path;
        }

        private static List<(float X, float Y, float Z)> GetGreedyPath(
            List<(float X, float Y, float Z)> points,
            Vector3 currentPos)
        {
            var remaining = new List<(float X, float Y, float Z)>(points);
            var path = new List<(float X, float Y, float Z)>();

            var current = (currentPos.X, currentPos.Y, currentPos.Z);
            while (remaining.Count > 0)
            {
                var next = remaining.OrderBy(p => Distance(current, p)).First();
                path.Add(next);
                remaining.Remove(next);
                current = next;
            }

            return path;
        }

        private static List<(float X, float Y, float Z)> Apply2Opt(List<(float X, float Y, float Z)> path)
        {
            bool improved = true;
            while (improved)
            {
                improved = false;
                for (int i = 1; i < path.Count - 2; i++)
                {
                    for (int j = i + 1; j < path.Count - 1; j++)
                    {
                        double d1 = Distance(path[i - 1], path[i]) + Distance(path[j], path[j + 1]);
                        double d2 = Distance(path[i - 1], path[j]) + Distance(path[i], path[j + 1]);

                        if (d2 < d1)
                        {
                            path.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
                }
            }
            return path;
        }

        public static double Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
