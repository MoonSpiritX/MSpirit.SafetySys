// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 文件名自然排序比较器（数字感知，忽略大小写）："a2.jpg" &lt; "a10.jpg"、"照片3" &lt; "照片12"。
    /// 2026-08-16 新增：标注与剪裁列表的"第一张"（拖入自动选中）需按自然排序取，
    /// 且组内列表排序统一走本比较器，保证"排序后的第一项"就是用户看到的第一项。
    /// 与 <see cref="StringComparer.OrdinalIgnoreCase"/> 的差异：连续数字段按数值比较而非逐字符。
    /// </summary>
    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new();

        private NaturalStringComparer() { }

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                char cx = x[i], cy = y[j];
                bool dx = char.IsDigit(cx), dy = char.IsDigit(cy);

                if (dx && dy)
                {
                    // 数字段：跳过前导零后按数值比较（长度不同 → 长的数值大）
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    string nx = x[si..i].TrimStart('0');
                    string ny = y[sj..j].TrimStart('0');
                    // 全零段归一为 "0"，保证 "0" 与 "00" 相等
                    if (nx.Length == 0) nx = "0";
                    if (ny.Length == 0) ny = "0";
                    if (nx.Length != ny.Length) return nx.Length < ny.Length ? -1 : 1;
                    int numCmp = string.CompareOrdinal(nx, ny);
                    if (numCmp != 0) return numCmp;
                    continue; // 数值相等（如 "01" vs "1"）→ 继续比较后续字符
                }

                char ux = char.ToUpperInvariant(cx);
                char uy = char.ToUpperInvariant(cy);
                if (ux != uy) return ux < uy ? -1 : 1;
                i++;
                j++;
            }

            // 公共前缀相同：短的在前（"a" < "a2" < "a20"）
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
