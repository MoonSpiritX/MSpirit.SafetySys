// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 人员模块统一排序服务：收集 + 排序。
    ///
    /// 背景：统计界面与导入导出界面多处需要"按分组树顺序"的人员数据，
    /// 且收集遍历逻辑（单位→部门→月份分组）在两个 ViewModel 中重复。
    /// 本类统一收集与排序，保证所有入口（统计表/统计导出/导入导出）顺序一致：
    ///   单位树顺序 → 单位内部门树顺序 → 部门内（在场优先 → 退场时间倒序 → 卡片顺序）
    ///
    /// 注意：仅负责"收集 + 排序"，行构建（PersonnelStatisticsRow）由调用方完成，
    /// 因为统计界面与导出界面的证书信息来源不同（槽位字段 vs 附件）。
    /// </summary>
    public static class PersonnelOrderingService
    {
        /// <summary>
        /// 收集到的人员源记录（含树顺序索引）
        /// </summary>
        public sealed class SourceRecord
        {
            public required PersonnelRecord Record { get; init; }
            /// <summary>所属单位名称（根节点）</summary>
            public required string Unit { get; init; }
            /// <summary>所属部门名称（单位直属记录为空串）</summary>
            public required string Department { get; init; }
            /// <summary>单位在分组树中的序号（全项目时有效；单单位恒 0）</summary>
            public int UnitIndex { get; init; }
            /// <summary>部门在单位内的序号（单位直属记录为 -1）</summary>
            public int DeptIndex { get; init; }
        }

        /// <summary>
        /// 收集人员源记录（含树顺序索引）。
        /// </summary>
        /// <param name="dataService">项目数据服务</param>
        /// <param name="unitId">单位 ID；null = 项目所有单位</param>
        /// <param name="deptId">部门 ID；非空时仅收集该部门（含其子分组）；null = 收集整个单位/全项目</param>
        /// <param name="fallbackUnitName">单位名兜底（unitId 查不到名称时使用）</param>
        public static List<SourceRecord> CollectRecords(ProjectDataService dataService, string? unitId, string? deptId, string fallbackUnitName)
        {
            var all = new List<SourceRecord>();

            if (unitId == null)
            {
                // 项目所有单位：按单位树顺序遍历
                var units = dataService.GetGroups(ModuleNames.Personnel, null); // 已按 Order 排序
                for (int ui = 0; ui < units.Count; ui++)
                {
                    var unit = units[ui];
                    CollectUnitRecords(dataService, unit.Id, unit.Name, ui, deptId, all);
                }
                Log.Information("PersonnelOrdering: 项目所有单位共收集到 {Count} 条记录", all.Count);
                return all;
            }

            // 单个单位：查单位名（兜底用 fallbackUnitName）
            var unitName = dataService.GetGroups(ModuleNames.Personnel, null)
                .FirstOrDefault(g => g.Id == unitId)?.Name ?? fallbackUnitName;
            CollectUnitRecords(dataService, unitId, unitName, 0, deptId, all);
            Log.Information("PersonnelOrdering: 单位 {GroupId} 下共收集到 {Count} 条记录", unitId, all.Count);
            return all;
        }

        /// <summary>
        /// 收集单个单位下的人员记录。
        /// deptId 非空时只收集该部门（含其月份子分组），否则收集整个单位。
        /// </summary>
        private static void CollectUnitRecords(ProjectDataService dataService, string unitGroupId, string unitName,
            int unitIndex, string? deptId, List<SourceRecord> all)
        {
            var departments = dataService.GetGroups(ModuleNames.Personnel, unitGroupId);

            for (int di = 0; di < departments.Count; di++)
            {
                var dept = departments[di];
                if (deptId != null && dept.Id != deptId) continue; // 仅收集指定部门

                // 部门下月份子分组的记录
                var monthGroups = dataService.GetGroups(ModuleNames.Personnel, dept.Id);
                foreach (var mg in monthGroups)
                {
                    foreach (var r in dataService.GetPersonnelRecordsByGroup(mg.Id))
                        all.Add(new SourceRecord { Record = r, Unit = unitName, Department = dept.Name, UnitIndex = unitIndex, DeptIndex = di });
                }

                // 部门下直接挂载的记录
                foreach (var r in dataService.GetPersonnelRecordsByGroup(dept.Id))
                    all.Add(new SourceRecord { Record = r, Unit = unitName, Department = dept.Name, UnitIndex = unitIndex, DeptIndex = di });
            }

            // 单位下直接挂载的记录（无部门）——仅全单位模式包含；指定部门时不包含
            if (deptId == null)
            {
                foreach (var r in dataService.GetPersonnelRecordsByGroup(unitGroupId))
                    all.Add(new SourceRecord { Record = r, Unit = unitName, Department = "", UnitIndex = unitIndex, DeptIndex = -1 });
            }
        }

        /// <summary>
        /// 统一排序：单位树顺序 → 单位内部门树顺序 → 部门内（在场优先 → 退场时间倒序 → 卡片顺序）。
        /// 返回新列表，不修改入参。
        /// </summary>
        public static List<SourceRecord> SortByTree(IEnumerable<SourceRecord> records)
        {
            return records
                .OrderBy(r => r.UnitIndex)
                .ThenBy(r => r.DeptIndex)
                .ThenBy(r => string.IsNullOrWhiteSpace(r.Record.ExitTime) ? 0 : 1)
                .ThenByDescending(r =>
                {
                    var (success, _, dt) = DateParser.TryParse(r.Record.ExitTime);
                    return success && dt.HasValue ? dt.Value : DateTime.MinValue;
                })
                .ThenBy(r => r.Record.DisplayIndex)
                .ToList();
        }

        /// <summary>
        /// 按人员状态过滤（全部人员 / 在场人员 / 退场人员）。
        /// status 为空串或"全部人员"时不过滤。返回新列表，不修改入参。
        /// </summary>
        public static List<SourceRecord> FilterByStatus(IEnumerable<SourceRecord> records, string status)
        {
            return records.Where(r => MatchesStatus(r.Record.ExitTime, status)).ToList();
        }

        /// <summary>
        /// 按人员类别过滤（全部人员 / 三类人员 / 特种作业人员）。
        /// category 为空串或"全部人员"时不过滤。返回新列表，不修改入参。
        /// </summary>
        public static List<SourceRecord> FilterByCategory(IEnumerable<SourceRecord> records, string category)
        {
            return records.Where(r =>
                MatchesCategory(
                    r.Record.ThreeCategoryPersonnel ? "是" : "否",
                    r.Record.SpecialOperationsPersonnel ? "是" : "否",
                    category)).ToList();
        }

        /// <summary>
        /// 单条记录是否满足人员状态条件（纯字符串判定，统计/导出共用）。
        /// 全部人员恒 true；在场人员=无退场时间；退场人员=有退场时间。
        /// </summary>
        public static bool MatchesStatus(string? exitTime, string status)
        {
            if (string.IsNullOrWhiteSpace(status) || status == "全部人员")
                return true;
            if (status == "在场人员")
                return string.IsNullOrWhiteSpace(exitTime);
            if (status == "退场人员")
                return !string.IsNullOrWhiteSpace(exitTime);
            return true;
        }

        /// <summary>
        /// 单条记录是否满足人员类别条件（纯字符串判定，统计/导出共用）。
        /// 全部人员恒 true；三类/特种作业分别按各自显示文本判断。
        /// </summary>
        public static bool MatchesCategory(string threeCategoryDisplay, string specialOpsDisplay, string category)
        {
            if (string.IsNullOrWhiteSpace(category) || category == "全部人员")
                return true;
            if (category == "三类人员")
                return threeCategoryDisplay == "是";
            if (category == "特种作业人员")
                return specialOpsDisplay == "是";
            return true;
        }
    }
}
