// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Serilog;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class SolarTermHelper
    {
        private static readonly Dictionary<DateTime, string> _solarTerms;
        private static readonly bool _initialized;

        static SolarTermHelper()
        {
            try
            {
                _solarTerms = LoadSolarTerms();
                _initialized = true;
                Log.Information("节气数据加载成功，共 {Count} 条记录", _solarTerms.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "节气数据加载失败，将使用日期范围判定季节");
                _solarTerms = new Dictionary<DateTime, string>();
                _initialized = false;
            }
        }

        private static Dictionary<DateTime, string> LoadSolarTerms()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("SolarTerms.json"));
            if (resourceName == null)
                throw new FileNotFoundException("未找到 SolarTerms.json 嵌入资源");

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException($"无法加载资源 {resourceName}");

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var entries = JsonSerializer.Deserialize<List<SolarTermEntry>>(json);
            if (entries == null)
                throw new InvalidOperationException("反序列化 SolarTerms.json 失败，返回 null");

            return entries
                .Where(e => e.Date != null && e.Term != null)
                .ToDictionary(e => DateTime.Parse(e.Date!), e => e.Term!);
        }

        /// <summary>
        /// 根据日期获取季节背景资源键
        /// </summary>
        public static string GetSeasonBackgroundKey(DateTime date)
        {
            if (!_initialized)
                return GetSeasonByDate(date);

            int year = date.Year;

            DateTime? GetTermDate(int y, string termName)
            {
                foreach (var kv in _solarTerms)
                {
                    if (kv.Key.Year == y && kv.Value == termName)
                        return kv.Key;
                }
                return null;
            }

            DateTime? spring = GetTermDate(year, "立春");
            DateTime? summer = GetTermDate(year, "立夏");
            DateTime? autumn = GetTermDate(year, "立秋");
            DateTime? winter = GetTermDate(year, "立冬");

            if (!spring.HasValue) spring = GetTermDate(year - 1, "立春");
            if (!summer.HasValue) summer = GetTermDate(year, "立夏") ?? GetTermDate(year + 1, "立夏");
            if (!autumn.HasValue) autumn = GetTermDate(year, "立秋") ?? GetTermDate(year + 1, "立秋");
            if (!winter.HasValue) winter = GetTermDate(year, "立冬") ?? GetTermDate(year + 1, "立冬");

            if (!spring.HasValue || !summer.HasValue || !autumn.HasValue || !winter.HasValue)
                return GetSeasonByDate(date);

            if (date >= spring.Value && date < summer.Value)
                return "SpringBg";
            if (date >= summer.Value && date < autumn.Value)
                return "SummerBg";
            if (date >= autumn.Value && date < winter.Value)
                return "AutumnBg";
            if (date >= winter.Value)
                return "WinterBg";
            if (date < spring.Value)
                return "WinterBg";

            return GetSeasonByDate(date);
        }

        private static string GetSeasonByDate(DateTime date)
        {
            int month = date.Month, day = date.Day;
            if (month == 3 && day >= 21 || month > 3 && month < 6 || month == 6 && day < 21)
                return "SpringBg";
            if (month == 6 && day >= 21 || month > 6 && month < 9 || month == 9 && day < 21)
                return "SummerBg";
            if (month == 9 && day >= 21 || month > 9 && month < 12 || month == 12 && day < 21)
                return "AutumnBg";
            return "WinterBg";
        }

        private class SolarTermEntry
        {
            public string? Date { get; set; }
            public string? Term { get; set; }
        }

        public static string GetCalendarSeasonBackgroundKey(DateTime date)
        {
            var seasonKey = GetSeasonBackgroundKey(date);
            return seasonKey switch
            {
                "SpringBg" => "CalendarSpringBg",
                "SummerBg" => "CalendarSummerBg",
                "AutumnBg" => "CalendarAutumnBg",
                "WinterBg" => "CalendarWinterBg",
                _ => "CalendarSpringBg"
            };
        }
    }
}