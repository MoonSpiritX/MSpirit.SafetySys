// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 管理常用隐患类型配置（与 FavoriteLinks.json、ViewState.json 同目录）
    /// </summary>
    public class CommonHazardTypeService
    {
        private readonly string _configPath;
        private CommonHazardTypeConfig? _config;  // 改为可空类型

        // 全量类型列表（Code -> Name），从 HazardTypes.json 资源加载
        private Dictionary<string, string> _allTypes = new();

        // 默认常用类型 Code 列表（物体打击、触电、高处坠落、机械致害、起重致害、坍塌、火灾、中毒、文明施工）
        private static readonly List<string> DefaultCommonCodes = new List<string>
        {
            "01", "04", "05", "06", "09", "10", "12", "27", "28", "29", "30"
        };

        public CommonHazardTypeService(string rootPath)
        {
            // 配置文件放在数据根目录下（与 FavoriteLinks.json 同级）
            _configPath = Path.Combine(rootPath, "CommonHazardTypes.json");
            LoadAllTypes();
            LoadConfig();
        }

        private void LoadAllTypes()
        {
            _allTypes = new Dictionary<string, string>();
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("HazardTypes.json"));
                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        var items = JsonSerializer.Deserialize<List<HazardTypeItem>>(json);
                        if (items != null)
                        {
                            foreach (var item in items)
                                _allTypes[item.Code] = item.Name;
                        }
                    }
                    else
                    {
                        Log.Warning("未能获取 HazardTypes.json 资源流");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载全量隐患类型失败");
            }

            if (_allTypes.Count == 0)
            {
                _allTypes = GetFallbackAllTypes();
            }
        }

        private Dictionary<string, string> GetFallbackAllTypes()
        {
            return new Dictionary<string, string>
            {
                {"01", "物体打击"}, {"02", "厂（场）内车辆致害"}, {"03", "道路（轨道）车辆致害"},
                {"04", "机械致害"}, {"05", "起重致害"}, {"06", "触 电"},
                {"07", "淹 溺"}, {"08", "灼 烫"}, {"09", "火 灾"},
                {"10", "高处坠落"}, {"11", "跌 落"}, {"12", "坍 塌"},
                {"13", "水 害"}, {"14", "容器爆炸"}, {"15", "管道爆炸"},
                {"16", "可燃气体爆炸"}, {"17", "可燃液体蒸气爆炸"}, {"18", "粉尘爆炸"},
                {"19", "民用爆炸物品爆炸"}, {"20", "烟花爆竹爆炸"}, {"21", "其他可燃固体爆炸"},
                {"22", "高温熔融物爆炸"}, {"23", "中 毒"}, {"24", "窒 息"},
                {"25", "滑 坡"}, {"26", "泄 漏"}, {"27", "其 他"},
                {"28", "文明施工"}, {"29", "资料问题"}, {"30", "管理问题"}
            };
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize<CommonHazardTypeConfig>(json);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "加载常用类型配置失败，将使用默认配置");
                }
            }

            if (_config == null || _config.CommonCodes == null || _config.CommonCodes.Count == 0)
            {
                _config = new CommonHazardTypeConfig
                {
                    CommonCodes = new List<string>(DefaultCommonCodes)
                };
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            // 确保 _config 不为空再保存
            if (_config == null) return;

            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存常用类型配置失败");
            }
        }

        /// <summary>
        /// 获取当前常用类型的 Code 列表（按顺序）
        /// </summary>
        public List<string> GetCommonCodes()
        {
            return new List<string>(_config?.CommonCodes ?? new List<string>());
        }

        /// <summary>
        /// 获取当前常用类型的名称列表（按顺序）
        /// </summary>
        public List<string> GetCommonNames()
        {
            var names = new List<string>();
            var commonCodes = _config?.CommonCodes ?? new List<string>();
            foreach (var code in commonCodes)
            {
                if (_allTypes.TryGetValue(code, out var name))
                    names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// 设置常用类型（按顺序）
        /// </summary>
        public void SetCommonCodes(List<string> codes)
        {
            if (_config == null) _config = new CommonHazardTypeConfig();
            _config.CommonCodes = codes ?? new List<string>();
            SaveConfig();
        }

        /// <summary>
        /// 获取全量类型列表（用于管理窗口和弹窗）
        /// </summary>
        public List<HazardTypeItem> GetAllTypes()
        {
            return _allTypes.Select(kv => new HazardTypeItem { Code = kv.Key, Name = kv.Value }).ToList();
        }

        /// <summary>
        /// 根据 Code 获取名称
        /// </summary>
        public string GetNameByCode(string code)
        {
            return _allTypes.TryGetValue(code, out var name) ? name : "";
        }

    }

    public class CommonHazardTypeConfig
    {
        public List<string> CommonCodes { get; set; } = new();
    }

    // 复用 HazardTypeItem 定义
    public class HazardTypeItem
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
