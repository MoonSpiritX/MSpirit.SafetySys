// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 导入行数据：存放从源文件中解析出的一行数据（通用，与具体模块无关）
    /// </summary>
    public class ImportRowData
    {
        /// <summary>原始行号（从 0 开始）</summary>
        public int RowIndex { get; set; }

        /// <summary>字段名 → 文本值（如 ResponsibleUnit → "工程部"）</summary>
        public Dictionary<string, string> TextValues { get; set; } = new();

        /// <summary>字段名 → 图片文件路径列表（如 HazardPhoto → ["C:/a.jpg", "C:/b.jpg"]）</summary>
        public Dictionary<string, List<string>> PhotoPaths { get; set; } = new();

        /// <summary>该行是否有效</summary>
        public bool IsValid { get; set; } = true;

        /// <summary>验证信息（无效原因或警告）</summary>
        public string? ValidationMessage { get; set; }
    }

    /// <summary>
    /// 导入解析结果：存放一次导入操作的完整解析、校验和预览数据（通用，与具体模块无关）
    /// </summary>
    public class ImportParseResult
    {
        /// <summary>源文件路径</summary>
        public string SourceFilePath { get; set; } = "";

        /// <summary>源文件类型描述</summary>
        public string SourceFileType { get; set; } = "";

        /// <summary>解析后的行数据</summary>
        public List<ImportRowData> Rows { get; set; } = new();

        /// <summary>警告信息列表</summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>无效行的索引列表</summary>
        public List<int> InvalidRowIndices { get; set; } = new();

        /// <summary>各字段匹配到的列数统计</summary>
        public Dictionary<string, int> FieldColumnCount { get; set; } = new();

        /// <summary>匹配到的字段总数</summary>
        public int MatchedFieldCount => FieldColumnCount.Count;

        /// <summary>有效行数</summary>
        public int ValidRowCount => Rows.Count - InvalidRowIndices.Count;

        /// <summary>总行数</summary>
        public int TotalRowCount => Rows.Count;
    }
}
