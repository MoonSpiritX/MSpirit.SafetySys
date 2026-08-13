// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Converters
{
    /// <summary>
    /// 通用文本到画刷转换器，通过 ConverterParameter 传入多组映射。
    /// 格式："rule|color,rule|color,..."
    /// 规则类型：
    ///   "text"          → 字符串相等匹配
    ///   ">=number"      → 数值大于等于匹配（如 ">=61"）
    /// 颜色支持命名颜色（"Green"）和十六进制（"#4CAF50"）。无匹配时返回 Black。
    /// </summary>
    /// <example>
    /// ConverterParameter='是|Green'
    /// ConverterParameter='完成|Black,进行|#4CAF50,超期|#E91E63,无效|#9E9E9E'
    /// ConverterParameter='>=61|#E91E63'
    /// </example>
    public class TextToBrushConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string rules && value != null)
            {
                var pairs = rules.Split(',');
                foreach (var pair in pairs)
                {
                    var kv = pair.Split('|');
                    if (kv.Length != 2) continue;

                    var rule = kv[0].Trim();
                    var color = kv[1].Trim();

                    if (rule.StartsWith(">="))
                    {
                        // 数值比较
                        if (int.TryParse(rule[2..], out int threshold)
                            && int.TryParse(value.ToString(), out int num)
                            && num >= threshold)
                        {
                            return new BrushConverter().ConvertFromString(color) as Brush ?? Brushes.Black;
                        }
                    }
                    else
                    {
                        // 字符串相等
                        if (value.ToString() == rule)
                        {
                            return new BrushConverter().ConvertFromString(color) as Brush ?? Brushes.Black;
                        }
                    }
                }
            }
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
