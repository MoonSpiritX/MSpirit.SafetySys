// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Models
{
    public class FieldMappingInfo
    {
        /// <summary>
        /// 程序内部字段名（如 ResponsibleUnit）
        /// </summary>
        public string DataField { get; set; } = "";

        /// <summary>
        /// 标准名称（月度统计表表头文本）
        /// </summary>
        public string StandardName { get; set; } = "";

        /// <summary>
        /// 同义词列表（包含标准名）
        /// </summary>
        public List<string> Aliases { get; set; } = new();
    }
}