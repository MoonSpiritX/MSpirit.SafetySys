// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 人员模块数据写入器：将通用解析结果写入 PersonnelRecord 表和附件表
    /// 目标分组结构为 单位 → 部门，targetYearMonth 和 targetDateGroupId 中：
    ///   - targetDateGroupId 视为目标部门（或单位）的 GroupId
    ///   - autoAssignByDate 时按文件中的部门字段自动分配
    /// </summary>
    public class PersonnelDataWriter : IDataWriter
    {
        public (int success, int failed, List<string> errors) Import(
            ImportParseResult result,
            ProjectDataService dataService,
            string targetYearMonth,
            string targetDateGroupId,
            bool autoAssignByDate)
        {
            int success = 0;
            int failed = 0;
            var errors = new List<string>();
            string attachmentsRoot = "";

            try
            {
                attachmentsRoot = dataService.GetProjectsDateRoot();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取附件根目录失败");
            }

            Log.Debug("[导入调试] PersonnelDataWriter 开始导入: 总行 {Total}, 有效行 {Valid}, autoAssignByDate={AutoAssign}, targetGroupId={GroupId}",
                result.Rows.Count, result.Rows.Count(r => r.IsValid), autoAssignByDate, targetDateGroupId);

            // 预查 targetDateGroupId 是单位（根节点）还是部门（有父节点）
            bool targetIsUnit = false;
            if (!string.IsNullOrEmpty(targetDateGroupId) && !autoAssignByDate)
            {
                var allGroups = dataService.GetAllGroups("Personnel");
                var targetGroup = allGroups.FirstOrDefault(g => g.Id == targetDateGroupId);
                targetIsUnit = targetGroup != null && string.IsNullOrEmpty(targetGroup.ParentId);
            }


            // 同一分组内 DisplayIndex 唯一递增（取目标分组当前最大值 + 1）
            var nextIndexByGroup = new Dictionary<string, int>();
            foreach (var row in result.Rows)
            {
                if (!row.IsValid)
                {
                    failed++;
                    continue;
                }

                try
                {
                    // 1. 确定目标部门 GroupId
                    string targetGroupId;

                    if (autoAssignByDate)
                    {
                        // 自动分配：单位优先 → 部门次之（2026-08-10 重构）
                        // 所有数据禁止写入单位根级：先解析单位，再解析该单位下的部门
                        string unitId = ResolveUnitGroup(dataService, row);
                        if (string.IsNullOrEmpty(unitId))
                        {
                            errors.Add($"第 {row.RowIndex + 1} 行：无法确定或创建单位");
                            failed++;
                            continue;
                        }

                        string? deptName = row.TextValues.TryGetValue("Department", out var dn) ? dn : null;
                        if (!string.IsNullOrWhiteSpace(deptName))
                        {
                            // 有部门名 → 在该单位下匹配/创建部门（不跨单位查找，杜绝重名串组）
                            targetGroupId = GetOrCreateDepartmentUnderUnit(dataService, unitId, deptName.Trim());
                            if (string.IsNullOrEmpty(targetGroupId))
                            {
                                errors.Add($"第 {row.RowIndex + 1} 行：单位 '{unitNameOf(row)}' 下创建部门 '{deptName}' 失败");
                                failed++;
                                continue;
                            }
                            Log.Information("第 {RowIndex} 行分配到单位 '{Unit}' → 部门 '{Dept}'", row.RowIndex, unitNameOf(row), deptName);
                        }
                        else
                        {
                            // 有单位无部门 → 该单位下"未命名部门"（复用）
                            targetGroupId = GetOrCreateUnnamedDepartment(dataService, unitId);
                            if (string.IsNullOrEmpty(targetGroupId))
                            {
                                errors.Add($"第 {row.RowIndex + 1} 行：单位 '{unitNameOf(row)}' 下创建未命名部门失败");
                                failed++;
                                continue;
                            }
                            Log.Information("第 {RowIndex} 行无部门字段，归入单位 '{Unit}' 的未命名部门", row.RowIndex, unitNameOf(row));
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(targetDateGroupId))
                        {
                            // 兜底3：未指定目标部门时归入"未命名部门"
                            targetGroupId = GetOrCreateDefaultDepartment(dataService, row);
                            if (string.IsNullOrEmpty(targetGroupId))
                            {
                                errors.Add("未指定目标部门且无可用单位");
                                failed++;
                                continue;
                            }
                        }
                        else
                        {
                            // 指定了目标分组（可能是单位ID或部门ID）
                            if (targetIsUnit)
                            {
                                // 目标是单位 → 按行 Department 字段在该单位下创建/匹配部门
                                string? deptName = row.TextValues.TryGetValue("Department", out var dn) ? dn : null;
                                if (!string.IsNullOrEmpty(deptName))
                                {
                                    targetGroupId = GetOrCreateDepartmentUnderUnit(dataService, targetDateGroupId, deptName);
                                    if (string.IsNullOrEmpty(targetGroupId))
                                    {
                                        errors.Add($"第 {row.RowIndex + 1} 行：在目标单位下创建部门 '{deptName}' 失败");
                                        failed++;
                                        continue;
                                    }
                                    Log.Information("第 {RowIndex} 行部门 '{Dept}' 已分配到目标单位", row.RowIndex, deptName);
                                }
                                else
                                {
                                    // 无部门字段：在目标单位下创建"未命名部门"
                                    targetGroupId = GetOrCreateDefaultDepartment(dataService, row, targetDateGroupId);
                                    if (string.IsNullOrEmpty(targetGroupId))
                                    {
                                        errors.Add($"第 {row.RowIndex + 1} 行：无法在目标单位下创建默认部门");
                                        failed++;
                                        continue;
                                    }
                                    Log.Information("第 {RowIndex} 行无部门字段，在目标单位下归入默认部门", row.RowIndex);
                                }
                            }
                            else
                            {
                                // 目标已是部门 → 直接使用，不解析行内字段
                                targetGroupId = targetDateGroupId;
                            }
                        }
                    }

                    // 2. 构建 PersonnelRecord 对象
                    if (!nextIndexByGroup.TryGetValue(targetGroupId, out int nextIndex))
                    {
                        nextIndex = dataService.GetMaxPersonnelDisplayIndex(targetGroupId) + 1;
                    }
                    nextIndexByGroup[targetGroupId] = nextIndex + 1;
                    // 2. 构建 Record 对象（2026-08-14 修复问题5：有效期区间解析、字段名对齐）
                    var idPeriod = ResolvePeriod(row, "IDCardStart", "IDCardEnd", "IDCardValidPeriod");
                    var certPeriod = ResolvePeriod(row, "CertStart", "CertEnd", "CertValidPeriod");
                    var record = new PersonnelRecord
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        GroupId = targetGroupId,
                        Title = GetText(row, "Title"),
                        Gender = GetText(row, "Gender"),
                        Age = GetText(row, "Age"),
                        EmployeeID = GetText(row, "EmployeeID"),
                        Position = GetText(row, "Position"),
                        PhoneNumber = GetText(row, "PhoneNumber"),
                        EducationLevel = GetText(row, "EducationLevel"),
                        NativePlace = GetText(row, "NativePlace"),
                        BirthDate = GetText(row, "BirthDate"),
                        EntryTime = GetText(row, "EntryTime"),
                        EduTime = GetText(row, "EduTime"),
                        ExitTime = GetText(row, "ExitTime"),
                        IDCardDocNumber = GetText(row, "IDCardDocNumber"),
                        IDCardStart = idPeriod.start,
                        IDCardEnd = idPeriod.end,
                        // 证书信息：单数字段（统计/附件复制用）与 Cert1 槽位（卡片 UI 显示用）同步写入
                        CertName = GetText(row, "CertName"),
                        CertDocNumber = GetText(row, "CertDocNumber") is { Length: > 0 } cd ? cd : GetText(row, "CertNumber"),
                        CertStart = certPeriod.start,
                        CertEnd = certPeriod.end,
                        Cert1Name = GetText(row, "CertName"),
                        Cert1DocNumber = GetText(row, "CertDocNumber") is { Length: > 0 } cd2 ? cd2 : GetText(row, "CertNumber"),
                        Cert1Start = certPeriod.start,
                        Cert1End = certPeriod.end,
                        ThreeCategoryPersonnel = TryParseBool(
                            GetText(row, "ThreeCategoryPersonnel") is { Length: > 0 } tcp ? tcp : GetText(row, "ThreeCategoryPersonnelDisplay"), false),
                        SpecialOperationsPersonnel = TryParseBool(
                            GetText(row, "SpecialOperationsPersonnel") is { Length: > 0 } sop ? sop : GetText(row, "SpecialOperationsPersonnelDisplay"), false),
                        Remark = GetText(row, "Remark"),
                        CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        DisplayIndex = nextIndex,
                        YearMonth = DateTime.Now.ToString("yyyy-MM")
                    };

                    // 3. 写入记录
                    if (!dataService.AddPersonnelRecord(record))
                    {
                        errors.Add($"第 {row.RowIndex + 1} 行：写入人员记录失败");
                        failed++;
                        continue;
                    }

                    // 4. 处理图片附件（IDCardFront, IDCardBack, Cert1 ~ Cert5）
                    int certSlot = 1;
                    foreach (var kv in row.PhotoPaths)
                    {
                        string photoField = kv.Key;
                        var photoPaths = kv.Value;

                        for (int i = 0; i < photoPaths.Count; i++)
                        {
                            string photoPath = photoPaths[i];
                            if (!File.Exists(photoPath)) continue;

                            try
                            {
                                string attachmentType;
                                int displayOrder;

                                if (photoField == "IDCardFront" || photoField == "IDCardBack")
                                {
                                    attachmentType = PersonnelConstants.AttachmentTypeIDCard;
                                    displayOrder = photoField == "IDCardFront" ? 1 : 2;
                                }
                                else if (photoField.StartsWith("Cert"))
                                {
                                    attachmentType = PersonnelConstants.AttachmentTypeCertificate;
                                    displayOrder = certSlot++;
                                }
                                else
                                {
                                    continue;
                                }

                                if (!string.IsNullOrEmpty(attachmentsRoot))
                                {
                                    using var fs = File.OpenRead(photoPath);
                                    var paths = Task.Run(() => ImageHelper.SaveImageToRecordAsync(
                                        fs,
                                        record.Id,
                                        Path.GetFileName(photoPath),
                                        attachmentsRoot,
                                        dataService.ProjectId,
                                        "Personnel",
                                        record.YearMonth,
                                        attachmentType,
                                        displayOrder
                                    )).GetAwaiter().GetResult();

                                    var attachment = new Attachment
                                    {
                                        Id = Guid.NewGuid().ToString("N"),
                                        RecordId = record.Id,
                                        FileName = Path.GetFileName(photoPath),
                                        RelativePath = paths.originalPath,
                                        ThumbnailPath = paths.thumbPath,
                                        FileSize = new FileInfo(photoPath).Length,
                                        MimeType = "image/jpeg",
                                        AttachmentType = attachmentType,
                                        DisplayOrder = displayOrder,
                                        // 2026-08-14 修复：证书信息读映射后的字段（CertNumber）与已解析的有效期（certPeriod）
                                        CertNumber = attachmentType == PersonnelConstants.AttachmentTypeCertificate ? (GetText(row, "CertDocNumber") is { Length: > 0 } cdn2 ? cdn2 : GetText(row, "CertNumber")) : null,
                                        CertName = attachmentType == PersonnelConstants.AttachmentTypeCertificate ? GetText(row, "CertName") : null,
                                        CertStart = attachmentType == PersonnelConstants.AttachmentTypeCertificate ? certPeriod.start : null,
                                        CertEnd = attachmentType == PersonnelConstants.AttachmentTypeCertificate ? certPeriod.end : null
                                    };
                                    dataService.AddAttachment(attachment);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "导入人员图片附件失败: {Path}", photoPath);
                                errors.Add($"第 {row.RowIndex + 1} 行：图片 {photoPath} 导入失败: {ex.Message}");
                            }
                        }
                    }

                    success++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "导入第 {RowIndex} 行时发生异常", row.RowIndex);
                    errors.Add($"第 {row.RowIndex + 1} 行：{ex.Message}");
                    failed++;
                }
            }

            Log.Debug("[导入调试] PersonnelDataWriter 导入完成: success={Success}, failed={Failed}, errors={ErrCount}",
                success, failed, errors.Count);
            return (success, failed, errors);
        }

        /// <summary>
        /// 解析行数据的单位分组（2026-08-10 重构）：
        /// 有 Unit 字段 → 按名称匹配/创建单位；无 Unit 字段 → 复用全局"未命名单位"。
        /// 返回单位 GroupId，失败返回空串。
        /// </summary>
        private static string ResolveUnitGroup(ProjectDataService dataService, ImportRowData row)
        {
            string? unitName = row.TextValues.TryGetValue("Unit", out var un) ? un : null;
            if (!string.IsNullOrWhiteSpace(unitName))
                return GetOrCreateUnitByName(dataService, unitName.Trim());
            return GetOrCreateUnnamedUnit(dataService);
        }

        /// <summary>按名称查找单位（顶级分组，ParentId 为空），不存在则创建</summary>
        private static string GetOrCreateUnitByName(ProjectDataService dataService, string unitName)
        {
            var units = dataService.GetGroups("Personnel", null);
            var existing = units.FirstOrDefault(u =>
                string.Equals(u.Name, unitName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;

            var newUnit = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = dataService.ProjectId,
                Module = "Personnel",
                ParentId = null,
                Name = unitName,
                Order = 0,
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
            };

            return dataService.AddGroup(newUnit) ? newUnit.Id : "";
        }

        /// <summary>获取全局唯一的"未命名单位"（复用已有，不存在则创建；禁止写入单位根级的兜底容器）</summary>
        private static string GetOrCreateUnnamedUnit(ProjectDataService dataService)
        {
            const string unnamedUnitName = "未命名单位";
            var units = dataService.GetGroups("Personnel", null);
            var existing = units.FirstOrDefault(u => u.Name == unnamedUnitName);
            if (existing != null) return existing.Id;
            return GetOrCreateUnitByName(dataService, unnamedUnitName);
        }

        /// <summary>获取指定单位下唯一的"未命名部门"（复用已有，不存在则创建）</summary>
        private static string GetOrCreateUnnamedDepartment(ProjectDataService dataService, string unitId)
        {
            const string unnamedDeptName = "未命名部门";
            return GetOrCreateDepartmentUnderUnit(dataService, unitId, unnamedDeptName);
        }

        /// <summary>取行数据中的单位名（日志/错误提示用）</summary>
        private static string unitNameOf(ImportRowData row)
            => row.TextValues.TryGetValue("Unit", out var un) ? un : "（无单位）";

        /// <summary>
        /// 获取或创建默认"未命名部门"。
        /// 指定 targetUnitId 时在该单位下创建，否则从行数据的 Unit 字段推断。
        /// </summary>
        private static string GetOrCreateDefaultDepartment(ProjectDataService dataService, ImportRowData row, string? targetUnitId = null)
        {
            const string defaultDeptName = "未命名部门";

            // 确定归属的单位
            string? unitId = targetUnitId;

            if (unitId == null)
            {
                // 未指定目标单位时，从行数据的 Unit 字段推断
                string? unitName = row.TextValues.TryGetValue("Unit", out var un) ? un : null;
                var units = dataService.GetGroups("Personnel", null);
                if (!string.IsNullOrEmpty(unitName))
                {
                    var unit = units.FirstOrDefault(u =>
                        string.Equals(u.Name, unitName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (unit != null)
                        unitId = unit.Id;
                }

                if (unitId == null && units.Count > 0)
                    unitId = units[0].Id;
            }

            if (unitId == null)
                return ""; // 项目中没有任何单位，无法创建

            // 检查是否已有"未命名部门"
            var existing = dataService.GetGroups("Personnel", unitId)
                .FirstOrDefault(d => d.Name == defaultDeptName);
            if (existing != null)
                return existing.Id;

            // 创建"未命名部门"
            var newDept = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = dataService.ProjectId,
                Module = "Personnel",
                ParentId = unitId,
                Name = defaultDeptName,
                Order = 0,
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
            };

            return dataService.AddGroup(newDept) ? newDept.Id : "";
        }

        /// <summary>
        /// 在指定单位下查找或创建指定名称的部门分组
        /// </summary>
        private static string GetOrCreateDepartmentUnderUnit(ProjectDataService dataService, string unitId, string deptName)
        {
            var existing = dataService.GetGroups("Personnel", unitId)
                .FirstOrDefault(d => string.Equals(d.Name, deptName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing.Id;

            var newDept = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = dataService.ProjectId,
                Module = "Personnel",
                ParentId = unitId,
                Name = deptName.Trim(),
                Order = 0,
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
            };

            return dataService.AddGroup(newDept) ? newDept.Id : "";
        }

        /// <summary>取行内某字段文本（缺失返回空串）</summary>
        private static string GetText(ImportRowData row, string field)
            => row.TextValues.TryGetValue(field, out var v) ? v : "";

        /// <summary>
        /// 解析有效期区间：优先行内已有的起止字段（未来 Mapping 扩展后），否则从区间文本字段解析；
        /// 解析失败时原样保存至起始字段（保守不丢数据，记日志）。
        /// </summary>
        private static (string start, string end) ResolvePeriod(
            ImportRowData row, string startField, string endField, string periodField)
        {
            string s = GetText(row, startField);
            string e = GetText(row, endField);
            if (!string.IsNullOrEmpty(s) || !string.IsNullOrEmpty(e))
                return (s, e);

            if (PersonnelHelper.TryParseValidPeriod(GetText(row, periodField), out var st, out var en))
                return (st, en);

            string raw = GetText(row, periodField);
            if (!string.IsNullOrEmpty(raw))
                Log.Warning("人员导入：{Field} 有效期 \"{Raw}\" 无法解析，已原样保存至起始日期", periodField, raw);
            return (raw, "");
        }

        private static bool TryParseBool(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            value = value.Trim();
            if (string.Equals(value, "是", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                value == "1")
                return true;
            if (string.Equals(value, "否", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                value == "0")
                return false;
            return defaultValue;
        }
    }
}
