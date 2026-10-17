// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MS_SafetyManagementSystem.Helpers;

namespace MS_SafetyManagementSystem.Converters
{
    /// <summary>
    /// 有效期文本到颜色转换器。从 "起始日期 至 结束日期" 格式中提取结束日期，
    /// 判定是否过期或即将过期。
    /// </summary>
    /// <remarks>
    /// 规则：
    ///   - 无法提取有效结束日期 → Black（正常/长期/乱按）
    ///   - 结束日期已过今天 → #E91E63 玫红（已过期）
    ///   - 结束日期在 30 天内 → #4CAF50 绿色（即将过期）
    ///   - 其余 → Black（正常）
    /// </remarks>
    public class ValidityDateToBrushConverter : IValueConverter
    {
        private static readonly Brush ExpiredBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E91E63"));
        private static readonly Brush ExpiringBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                var endDate = ExtractEndDate(text);
                if (endDate.HasValue)
                {
                    var today = DateTime.Today;
                    if (endDate.Value < today)
                        return ExpiredBrush;
                    if (endDate.Value <= today.AddDays(30))
                        return ExpiringBrush;
                }
            }
            return Brushes.Black;
        }

        /// <summary>
        /// 从 "起始 至 结束" 格式中提取结束日期。
        /// 支持 "至"、"~" 分隔，也支持单独日期格式。
        /// </summary>
        private static DateTime? ExtractEndDate(string text)
        {
            // 分离结束日期部分：取最后一个分隔符之后的内容
            string endPart = text;
            foreach (var sep in new[] { "至", "~" })
            {
                int idx = endPart.LastIndexOf(sep, StringComparison.Ordinal);
                if (idx >= 0 && idx + 1 < endPart.Length)
                {
                    endPart = endPart[(idx + 1)..].Trim();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(endPart))
                return null;

            var (success, _, dt) = DateParser.TryParse(endPart);
            return success && dt.HasValue ? dt.Value : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
