// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;

namespace MS_SafetyManagementSystem.Helpers
{
    /// <summary>
    /// 人员管理模块专用日期解析器，支持多种输入格式，统一输出 yyyy-MM-dd。
    /// 用于退场时间、入场时间、教育时间等日期字段的自动格式化。
    /// </summary>
    public static class DateParser
    {
        /// <summary>
        /// 尝试解析用户输入的日期字符串，成功时返回标准格式 "yyyy-MM-dd"。
        /// </summary>
        /// <param name="input">用户输入，如 "2026-07-01"、"2026.7.1"、"2026年7月6日"、"06.02" 等</param>
        /// <returns>(success, formattedDate)。success=false 时 formattedDate 为 null。</returns>
        public static (bool success, string? formattedDate, DateTime? parsedDate) TryParse(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, null, null);

            input = input.Trim();

            // ——— 第1组：含完整年份的完整日期格式 ———
            DateTime dt;

            // "2026-07-01" / "2026-7-01"
            if (DateTime.TryParseExact(input, new[] { "yyyy-MM-dd", "yyyy-M-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, dt.ToString("yyyy-MM-dd"), dt);

            // "2026.07.01" / "2026.7.1"
            if (DateTime.TryParseExact(input, new[] { "yyyy.MM.dd", "yyyy.M.d" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, dt.ToString("yyyy-MM-dd"), dt);

            // "2026年7月6日" / "2026年07月06日"
            if (DateTime.TryParseExact(input, new[] { "yyyy年M月d日", "yyyy年MM月dd日" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, dt.ToString("yyyy-MM-dd"), dt);

            // ——— 第2组：缺少年份的简写格式（自动补当年） ———
            int currentYear = DateTime.Now.Year;

            // "07-01" / "7-1"
            if (DateTime.TryParseExact(input, new[] { "MM-dd", "M-d" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                dt = new DateTime(currentYear, dt.Month, dt.Day);
                return (true, dt.ToString("yyyy-MM-dd"), dt);
            }

            // "07.01" / "7.1" / "6.2"
            if (DateTime.TryParseExact(input, new[] { "MM.dd", "M.d" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                dt = new DateTime(currentYear, dt.Month, dt.Day);
                return (true, dt.ToString("yyyy-MM-dd"), dt);
            }

            // "6月2日" / "06月02日"
            if (DateTime.TryParseExact(input, new[] { "M月d日", "MM月dd日" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                dt = new DateTime(currentYear, dt.Month, dt.Day);
                return (true, dt.ToString("yyyy-MM-dd"), dt);
            }

            return (false, null, null);
        }

        /// <summary>
        /// 将输入格式化为标准 yyyy-MM-dd，无效输入返回原值。
        /// </summary>
        public static string Format(string? input)
        {
            var (success, formatted, _) = TryParse(input);
            return success && formatted != null ? formatted : (input ?? "");
        }

        /// <summary>
        /// 有效期专用解析：短格式年份补全为 "202x" 形式（当前年份前3位 + "x"），
        /// 方便用户确认后手动修改。完整格式正常解析为 yyyy-MM-dd。
        /// </summary>
        /// <param name="input">用户输入，如 "6-2"、"2026-07-01"、"03.05" 等</param>
        /// <returns>(success, formatted)。短格式如 2026年输入"6-2" → "202x-06-02"</returns>
        public static (bool success, string? formatted) TryParseValidityDate(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, null);

            input = input.Trim();

            int currentYear = DateTime.Now.Year;
            string yearPrefix = currentYear.ToString()[..3]; // "202" / "203" 等

            DateTime dt;

            // ——— 完整年份格式：正常解析 ———

            if (DateTime.TryParseExact(input, new[] { "yyyy-MM-dd", "yyyy-M-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, dt.ToString("yyyy-MM-dd"));

            if (DateTime.TryParseExact(input, new[] { "yyyy.MM.dd", "yyyy.M.d" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, dt.ToString("yyyy-MM-dd"));

            if (DateTime.TryParseExact(input, new[] { "yyyy年M月d日", "yyyy年MM月dd日" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, dt.ToString("yyyy-MM-dd"));

            // ——— 短格式：年份补全为 "202x" ———

            // "07-01" / "7-1"
            if (DateTime.TryParseExact(input, new[] { "MM-dd", "M-d" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, $"{yearPrefix}x-{dt:MM-dd}");

            // "07.01" / "7.1" / "6.2"
            if (DateTime.TryParseExact(input, new[] { "MM.dd", "M.d" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, $"{yearPrefix}x-{dt:MM-dd}");

            // "6月2日" / "06月02日"
            if (DateTime.TryParseExact(input, new[] { "M月d日", "MM月dd日" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (true, $"{yearPrefix}x-{dt:MM-dd}");

            return (false, null);
        }
    }
}
