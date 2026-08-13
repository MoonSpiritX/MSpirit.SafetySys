// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Models
{
    /// <summary>
    /// 非标准记录扫描结果中的一条记录信息
    /// </summary>
    public class OrphanRecordInfo
    {
        public int Index { get; set; }
        public string ProjectName { get; set; } = "";
        public string ModuleName { get; set; } = "";      // "隐患" / "人员"
        public string Level1Group { get; set; } = "";     // 一级分组（年月/单位）
        public string Level2Group { get; set; } = "";     // 二级分组（日期/部门），孤儿为空
        public string RecordSummary { get; set; } = "";   // 记录摘要
        public string RecordId { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string GroupId { get; set; } = "";         // 当前所属 GroupId
    }
}
