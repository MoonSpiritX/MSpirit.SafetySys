// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MS_SafetyManagementSystem.Services
{
    public class ViewStateManager
    {
        private readonly string _stateFilePath;
        private readonly object _lock = new object();
        private Dictionary<string, ViewStateData> _states = new();

        public ViewStateManager(string rootPath)
        {
            _stateFilePath = Path.Combine(rootPath, "ViewState.json");
            Load();
        }

        private void Load()
        {
            lock (_lock)
            {
                if (File.Exists(_stateFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_stateFilePath);
                        _states = JsonSerializer.Deserialize<Dictionary<string, ViewStateData>>(json)
                                  ?? new Dictionary<string, ViewStateData>();
                    }
                    catch
                    {
                        // 视图状态文件损坏/无法解析：回退默认状态（数据不影响，仅界面状态）
                        Serilog.Log.Debug("[ViewState] 读取视图状态失败，使用默认状态");
                        _states = new Dictionary<string, ViewStateData>();
                    }
                }
                else
                {
                    _states = new Dictionary<string, ViewStateData>();
                }
            }
        }

        private void Save()
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_states, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_stateFilePath, json);
            }
        }

        /// <summary>
        /// 保存视图状态（包括滚动偏移）
        /// </summary>
        public void SaveState(string projectId, string module, List<string> expandedTopLevelNames, string selectedNodeId, double scrollOffset)
        {
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(module))
                return;

            string key = $"{projectId}_{module}";
            var data = new ViewStateData
            {
                ExpandedTopLevelNames = expandedTopLevelNames ?? new List<string>(),
                SelectedNodeId = selectedNodeId ?? "",
                ScrollOffset = scrollOffset
            };

            _states[key] = data;
            Save();
        }

        /// <summary>
        /// 加载视图状态（包括滚动偏移）
        /// </summary>
        public (List<string>? ExpandedTopLevelNames, string? SelectedNodeId, double ScrollOffset) LoadState(string projectId, string module)
        {
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(module))
                return (null, null, 0);

            string key = $"{projectId}_{module}";
            if (_states.TryGetValue(key, out var state))
                return (state.ExpandedTopLevelNames, state.SelectedNodeId, state.ScrollOffset);
            return (null, null, 0);
        }

        private class ViewStateData
        {
            public List<string> ExpandedTopLevelNames { get; set; } = new();
            public string SelectedNodeId { get; set; } = "";
            public double ScrollOffset { get; set; } = 0;
        }
    }
}