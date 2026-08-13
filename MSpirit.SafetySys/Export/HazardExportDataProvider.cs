// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 隐患模块的导出数据提供器。
    /// 包装 List&lt;MonthlyRecordRow&gt;，实现字段映射、图片路径获取和一键导出元数据。
    /// </summary>
    public class HazardExportDataProvider : IExportDataProvider
    {
        private readonly List<MonthlyRecordRow> _dataRows;
        private readonly ProjectDataService _projectDataService;

        public HazardExportDataProvider(List<MonthlyRecordRow> dataRows, ProjectDataService projectDataService, string contextName = "隐患统计")
        {
            _dataRows = dataRows ?? throw new ArgumentNullException(nameof(dataRows));
            _projectDataService = projectDataService ?? throw new ArgumentNullException(nameof(projectDataService));
            _contextName = contextName;
        }

        private readonly string _contextName;

        public int RowCount => _dataRows.Count;

        public string GetTextValue(int rowIndex, string dataField)
        {
            var row = _dataRows[rowIndex];
            switch (dataField)
            {
                case "Status": return row.Status;
                case "RowNumber": return row.RowNumber.ToString();
                case "RecordDate": return row.RecordDate?.ToString("yyyy-MM-dd") ?? "";
                case "ResponsibleUnit": return row.ResponsibleUnit;
                case "ResponsiblePerson": return row.ResponsiblePerson;
                case "Location": return row.Location;
                case "HazardType": return row.HazardType;
                case "HazardLevel": return row.HazardLevel;
                case "Deadline": return row.Deadline?.ToString("MM-dd") ?? "";
                case "CompletedDate": return row.CompletedDate?.ToString("MM-dd") ?? "";
                case "Reviewer": return row.Reviewer;
                case "Description": return row.Description;
                case "Measure": return row.Measure;
                case "HazardPhotoCount": return row.HazardPhotoCount.ToString();
                case "RemedyPhotoCount": return row.RemedyPhotoCount.ToString();
                default: return "";
            }
        }

        public List<string> GetPhotoPaths(int rowIndex, string dataField, string attachmentsRootPath)
        {
            var result = new List<string>();
            if (_projectDataService == null || string.IsNullOrEmpty(attachmentsRootPath))
                return result;

            var row = _dataRows[rowIndex];
            string attachmentType = dataField == "HazardPhoto" ? "Hazard" : "Remedy";
            Log.Debug("GetPhotoPaths: RecordId={RecordId}, dataField={Field}, attachmentType={Type}", row.RecordId, dataField, attachmentType);

            var attachments = _projectDataService.GetAttachmentsByRecord(row.RecordId);
            Log.Debug("GetPhotoPaths: GetAttachmentsByRecord 返回 {Count} 条记录", attachments.Count);

            var filtered = attachments
                .Where(a => a.AttachmentType == attachmentType)
                .OrderBy(a => a.DisplayOrder)
                .ToList();
            Log.Debug("GetPhotoPaths: 按类型 {Type} 过滤后剩余 {Count} 条", attachmentType, filtered.Count);

            Log.Debug("GetPhotoPaths: attachmentsRootPath = {Path}", attachmentsRootPath);

            foreach (var att in filtered)
            {
                string fullPath = Path.Combine(attachmentsRootPath, att.RelativePath);
                Log.Debug("GetPhotoPaths: RelativePath={RelPath}, FullPath={FullPath}, Exists={Exists}",
                    att.RelativePath, fullPath, File.Exists(fullPath));
                if (File.Exists(fullPath))
                    result.Add(fullPath);
                else
                    Log.Warning("图片文件不存在: {Path}", fullPath);
            }
            Log.Debug("GetPhotoPaths: 最终返回 {Count} 张图片", result.Count);
            return result;
        }

        // ===== 一键导出元数据 =====

        public string OneClickTitleSuffix => _contextName;

        public string OneClickFileSuffix => _contextName;

        public string[] OneClickHeaders => new[]
        {
            "进度", "序号", "日期", "责任单位", "责任人",
            "隐患位置", "隐患类型", "级别", "整改期限", "完成日期",
            "复查人", "隐患描述", "整改措施", "隐患照片", "整改照片"
        };

        public string[] OneClickFieldNames => new[]
        {
            "Status", "RowNumber", "RecordDate", "ResponsibleUnit", "ResponsiblePerson",
            "Location", "HazardType", "HazardLevel", "Deadline", "CompletedDate",
            "Reviewer", "Description", "Measure", "HazardPhotoCount", "RemedyPhotoCount"
        };

        public int[] OneClickColumnWidths => new[]
        {
            8, 6, 12, 14, 8, 14, 14, 6, 10, 10, 8, 30, 30, 10, 10
        };
    }
}
