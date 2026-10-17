// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 手机图片传输的位置列表配置服务：数据根目录 PhotoTransferLocations.json。
    /// 结构：Default（默认=用户-图片）+ Custom（自定义列表）+ Current（当前选中，空=用 Default）。
    /// 容错与 FavoriteLinksService 同模式：文件损坏/不存在时回默认值，不向调用方抛出。
    /// </summary>
    public class PhotoTransferLocationsService
    {
        public class LocationsData
        {
            public string Default { get; set; } = "";
            public List<string> Custom { get; set; } = new();
            public string Current { get; set; } = "";
        }

        private readonly string _filePath;
        private readonly object _lock = new();
        private LocationsData _data = new();

        public PhotoTransferLocationsService(string rootPath)
        {
            _filePath = Path.Combine(rootPath, "PhotoTransferLocations.json");
            _data.Default = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            Load();
            // 当前选中无效（空/目录不存在/已删除）时回退默认
            if (string.IsNullOrEmpty(_data.Current) || !Directory.Exists(_data.Current))
                _data.Current = _data.Default;
        }

        /// <summary>默认位置（用户-图片）</summary>
        public string DefaultLocation => _data.Default;

        /// <summary>当前选中位置（空=默认）</summary>
        public string CurrentLocation => string.IsNullOrEmpty(_data.Current) ? _data.Default : _data.Current;

        /// <summary>全部可选位置：默认 + 自定义（去重，保序）</summary>
        public List<string> GetAllLocations()
        {
            lock (_lock)
            {
                var list = new List<string> { _data.Default };
                list.AddRange(_data.Custom.Where(p =>
                    !string.IsNullOrWhiteSpace(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase)));
                return list;
            }
        }

        /// <summary>设置当前选中位置（须为有效目录）</summary>
        public void SetCurrent(string path)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                _data.Current = path;
            }
            Save();
        }

        /// <summary>添加自定义位置（已存在/与默认重复则忽略），返回是否新增</summary>
        public bool AddCustom(string path)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
                if (_data.Custom.Contains(path, StringComparer.OrdinalIgnoreCase)) return false;
                if (string.Equals(path, _data.Default, StringComparison.OrdinalIgnoreCase)) return false;
                _data.Custom.Add(path);
            }
            Save();
            return true;
        }

        /// <summary>移除自定义位置（默认位置不可移除），返回是否移除成功</summary>
        public bool RemoveCustom(string path)
        {
            bool removed;
            lock (_lock)
            {
                removed = _data.Custom.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
                // 当前选中被移除 → 回退默认
                if (removed && string.Equals(_data.Current, path, StringComparison.OrdinalIgnoreCase))
                    _data.Current = _data.Default;
            }
            if (removed) Save();
            return removed;
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
                        var data = JsonSerializer.Deserialize<LocationsData>(json);
                        if (data != null && !string.IsNullOrEmpty(data.Default))
                        {
                            _data.Default = data.Default;
                            _data.Custom = data.Custom ?? new List<string>();
                            _data.Current = data.Current ?? "";
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "读取位置列表配置失败，使用默认配置: {Path}", _filePath);
                    }
                }
            }
        }

        private void Save()
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
                    Log.Warning(ex, "保存位置列表配置失败: {Path}", _filePath);
                }
            }
        }
    }
}
