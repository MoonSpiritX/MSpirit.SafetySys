// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 数据根目录 setting.json 的通用配置服务。
    /// 用途：记录需要延迟清理的目录（如删除项目文件夹失败时留下的残留）等程序级状态，
    /// 后续其他功能也可在此扩展新字段。
    /// </summary>
    public class SettingFileService
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private SettingFileData _data = new();

        public SettingFileService(string rootPath)
        {
            _filePath = Path.Combine(rootPath, "setting.json");
            Load();
        }

        /// <summary>当前完整配置数据（修改后需调用 Save 落盘）</summary>
        public SettingFileData Data
        {
            get { lock (_lock) return _data; }
        }

        /// <summary>待清理目录列表（删除项目文件夹失败等遗留，启动时重试删除）</summary>
        public List<string> PendingDeleteDirs
        {
            get { lock (_lock) return _data.PendingDeleteDirs; }
        }

        private void Load()
        {
            lock (_lock)
            {
                if (File.Exists(_filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_filePath);
                        _data = JsonSerializer.Deserialize<SettingFileData>(json) ?? new SettingFileData();
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "读取 setting.json 失败，使用默认配置: {Path}", _filePath);
                        _data = new SettingFileData();
                    }
                }
                else
                {
                    _data = new SettingFileData();
                }
            }
        }

        /// <summary>保存配置到 setting.json（幂等，重复调用无副作用）</summary>
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "保存 setting.json 失败: {Path}", _filePath);
                }
            }
        }

        /// <summary>登记一个待清理目录（去重后保存）</summary>
        public void AddPendingDeleteDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            lock (_lock)
            {
                if (_data.PendingDeleteDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) return;
                _data.PendingDeleteDirs.Add(dir);
            }
            Save();
        }

        /// <summary>从待清理列表移除一个目录（清理成功后调用），并保存</summary>
        public void RemovePendingDeleteDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            lock (_lock)
            {
                _data.PendingDeleteDirs.RemoveAll(d => d.Equals(dir, StringComparison.OrdinalIgnoreCase));
            }
            Save();
        }
    }

    /// <summary>setting.json 的数据模型（后续新字段在此扩展）</summary>
    public class SettingFileData
    {
        /// <summary>待清理目录列表（绝对路径）</summary>
        public List<string> PendingDeleteDirs { get; set; } = new();
    }
}
