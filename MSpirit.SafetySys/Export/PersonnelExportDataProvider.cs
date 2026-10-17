// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.Generic;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 人员模块的导出数据提供器。
    /// 包装 List&lt;PersonnelStatisticsRow&gt;，实现字段映射和一键导出元数据。
    /// 人员模块无图片附件，GetPhotoPaths 始终返回空列表。
    /// </summary>
    public class PersonnelExportDataProvider : IExportDataProvider
    {
        private readonly List<PersonnelStatisticsRow> _dataRows;

        public PersonnelExportDataProvider(List<PersonnelStatisticsRow> dataRows, string contextName)
        {
            _dataRows = dataRows ?? throw new ArgumentNullException(nameof(dataRows));
            _contextName = contextName;
        }

        private readonly string _contextName;

        public int RowCount => _dataRows.Count;

        public string GetTextValue(int rowIndex, string dataField)
        {
            var row = _dataRows[rowIndex];
            switch (dataField)
            {
                case "RowNumber": return row.RowNumber.ToString();
                case "Title": return row.Title ?? "";
                case "Unit": return row.Unit ?? "";
                case "Department": return row.Department ?? "";
                case "Gender": return row.Gender ?? "";
                case "Position": return row.Position ?? "";
                case "ThreeCategoryPersonnelDisplay": return row.ThreeCategoryPersonnelDisplay ?? "";
                case "SpecialOperationsPersonnelDisplay": return row.SpecialOperationsPersonnelDisplay ?? "";
                case "BirthDate": return row.BirthDate ?? "";
                case "Age": return row.Age ?? "";
                case "EmployeeID": return row.EmployeeID ?? "";
                case "NativePlace": return row.NativePlace ?? "";
                case "EducationLevel": return row.EducationLevel ?? "";
                case "PhoneNumber": return row.PhoneNumber ?? "";
                case "EntryTime": return row.EntryTime ?? "";
                case "EduTime": return row.EduTime ?? "";
                case "ExitTime": return row.ExitTime ?? "";
                case "IDCardDocNumber": return row.IDCardDocNumber ?? "";
                case "IDCardValidPeriod": return row.IDCardValidPeriod ?? "";
                case "CertName": return row.CertName ?? "";
                case "CertNumber": return row.CertNumber ?? "";
                case "CertValidPeriod": return row.CertValidPeriod ?? "";
                case "Remark": return row.Remark ?? "";
                default: return "";
            }
        }

        public List<string> GetPhotoPaths(int rowIndex, string dataField, string attachmentsRootPath)
        {
            return new List<string>();
        }

        // ===== 一键导出元数据 =====

        public string OneClickTitleSuffix => _contextName;

        public string OneClickFileSuffix => _contextName;

        public string[] OneClickHeaders => new[]
        {
            "序号", "姓名", "单位", "部门", "性别", "岗位", "三类人员", "特种作业",
            "出生年月", "年龄", "工号", "籍贯", "文化程度", "手机号",
            "入场时间", "教育时间", "退场时间", "身份证号", "身份证有效期",
            "证书名称", "证书编号", "证书有效期", "备注"
        };

        public string[] OneClickFieldNames => new[]
        {
            "RowNumber", "Title", "Unit", "Department", "Gender", "Position", "ThreeCategoryPersonnelDisplay", "SpecialOperationsPersonnelDisplay",
            "BirthDate", "Age", "EmployeeID", "NativePlace", "EducationLevel", "PhoneNumber",
            "EntryTime", "EduTime", "ExitTime", "IDCardDocNumber", "IDCardValidPeriod",
            "CertName", "CertNumber", "CertValidPeriod", "Remark"
        };

        public int[] OneClickColumnWidths => new[]
        {
            6, 8, 10, 8, 6, 10, 8, 8, 12, 6, 10, 10, 8, 14, 12, 10, 10, 22, 24, 12, 16, 24, 16
        };
    }
}
