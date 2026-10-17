// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Group = MS_SafetyManagementSystem.Models.Group;

namespace MS_SafetyManagementSystem.Services
{
    public class ProjectDataService : IDisposable
    {
        private readonly ProjectDbContext _dbContext;
        public string ProjectId { get; }

        public ProjectDataService(string projectId, string projectFolderPath)
        {
            ProjectId = projectId;
            _dbContext = new ProjectDbContext(projectFolderPath, projectId);
        }

        public string GetDbPath() => _dbContext.GetDbPath();

        // Groups
        public List<Group> GetGroups(string module, string? parentId = null) => _dbContext.GetGroups(module, parentId);
        public bool AddGroup(Group group) => _dbContext.AddGroup(group);
        public bool UpdateGroup(Group group)
        {
            return _dbContext.UpdateGroup(group);
        }
        public bool DeleteGroup(string id) => _dbContext.DeleteGroup(id);

        // Records
        public List<Record> GetRecordsByGroup(string groupId) => _dbContext.GetRecordsByGroup(groupId);
        public List<Record> GetRecordsByGroupPaged(string groupId, int offset, int limit) => _dbContext.GetRecordsByGroupPaged(groupId, offset, limit);
        public int GetRecordsCountByGroup(string groupId) => _dbContext.GetRecordsCountByGroup(groupId);
        public int GetMaxDisplayIndex(string groupId) => _dbContext.GetMaxDisplayIndex(groupId);
        public Record? GetRecordById(string id) => _dbContext.GetRecordById(id);
        public bool AddRecord(Record record) => _dbContext.AddRecord(record);
        public bool UpdateRecord(Record record) => _dbContext.UpdateRecord(record);

        public bool DeleteRecord(string id)
        {
            // 先获取该记录的所有附件
            var attachments = GetAttachmentsByRecord(id);
            // 删除物理文件
            var attachmentsRoot = GetAttachmentsRootPath();
            foreach (var att in attachments)
            {
                var fullPath = Path.Combine(attachmentsRoot, att.RelativePath);
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); } catch { /* 附件文件删除失败可忽略 */ }
                }
            }
            // 再删除数据库记录（级联删除附件记录）
            return _dbContext.DeleteRecord(id);
        }

        // PersonnelRecords
        public List<PersonnelRecord> GetPersonnelRecordsByGroup(string groupId) => _dbContext.GetPersonnelRecordsByGroup(groupId);
        public List<PersonnelRecord> GetAllPersonnelRecords() => _dbContext.GetAllPersonnelRecords(ProjectId);
        public List<PersonnelRecord> GetPersonnelRecordsByGroupPaged(string groupId, int offset, int limit) => _dbContext.GetPersonnelRecordsByGroupPaged(groupId, offset, limit);
        public int GetPersonnelRecordsCountByGroup(string groupId) => _dbContext.GetPersonnelRecordsCountByGroup(groupId);
        public int GetMaxPersonnelDisplayIndex(string groupId) => _dbContext.GetMaxPersonnelDisplayIndex(groupId);
        public PersonnelRecord? GetPersonnelRecord(string id) => _dbContext.GetPersonnelRecord(id);
        public bool AddPersonnelRecord(PersonnelRecord record) => _dbContext.AddPersonnelRecord(record);
        public bool UpdatePersonnelRecord(PersonnelRecord record) => _dbContext.UpdatePersonnelRecord(record);
        public bool DeletePersonnelRecord(string id)
        {
            // 先查找记录以获取 GroupId
            var record = GetPersonnelRecord(id);
            if (record == null) return false;

            // 删除附件文件（使用 GetProjectsDateRoot 拼接，因为 RelativePath 以 Project_{id} 开头）
            var attachments = GetAttachmentsByRecord(id);
            string projectsDateRoot;
            try { projectsDateRoot = GetProjectsDateRoot(); }
            catch { projectsDateRoot = ""; }

            foreach (var att in attachments)
            {
                if (!string.IsNullOrEmpty(projectsDateRoot) && !string.IsNullOrEmpty(att.RelativePath))
                {
                    var fullPath = Path.Combine(projectsDateRoot, att.RelativePath);
                    if (File.Exists(fullPath))
                    {
                        try { File.Delete(fullPath); } catch { /* 附件文件删除失败可忽略 */ }
                    }
                    if (!string.IsNullOrEmpty(att.ThumbnailPath))
                    {
                        var thumbPath = Path.Combine(projectsDateRoot, att.ThumbnailPath);
                        if (File.Exists(thumbPath))
                        {
                            try { File.Delete(thumbPath); } catch { /* 缩略图删除失败可忽略 */ }
                        }
                    }
                }
            }

            // 删除卡片所在文件夹（按单位路径）
            if (!string.IsNullOrEmpty(projectsDateRoot))
            {
                string unitGroupId = GetPersonnelUnitGroupId(record.GroupId);
                string cardFolder = $"Card_{id}";
                string fullCardFolder = Path.Combine(projectsDateRoot, $"Project_{ProjectId}", $"Personnel_{unitGroupId}", cardFolder);
                if (Directory.Exists(fullCardFolder))
                {
                    try { Directory.Delete(fullCardFolder, true); }
                    catch (System.Exception ex) { Log.Warning(ex, "删除人员卡片文件夹失败: {Folder}", fullCardFolder); }
                }
            }

            // 删除 Attachments 数据库记录
            foreach (var att in attachments)
                _dbContext.DeleteAttachment(att.Id);

            return _dbContext.DeletePersonnelRecord(id);
        }

        /// <summary>
        /// 删除指定记录的所有附件（数据库记录 + 物理文件）
        /// </summary>
        public void DeleteAttachmentsByRecord(string recordId)
        {
            var attachments = GetAttachmentsByRecord(recordId);
            if (attachments.Count == 0) return;

            string projectsDateRoot;
            try
            {
                projectsDateRoot = GetProjectsDateRoot();
            }
            catch (InvalidOperationException ex)
            {
                Log.Error(ex, "无法获取 ProjectsDate 根目录，跳过物理文件删除，仅删除数据库记录。");
                // 仅删除数据库记录
                foreach (var att in attachments)
                    DeleteAttachment(att.Id);
                return;
            }

            foreach (var att in attachments)
            {
                // 检查相对路径有效性
                if (string.IsNullOrEmpty(att.RelativePath))
                {
                    Log.Warning("附件 {AttachmentId} 的 RelativePath 为空，跳过物理文件删除", att.Id);
                    DeleteAttachment(att.Id);
                    continue;
                }

                var fullPath = Path.Combine(projectsDateRoot, att.RelativePath);
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); }
                    catch (Exception ex) { Log.Warning(ex, "删除附件文件失败: {Path}", fullPath); }
                }

                if (!string.IsNullOrEmpty(att.ThumbnailPath))
                {
                    var thumbFullPath = Path.Combine(projectsDateRoot, att.ThumbnailPath);
                    if (File.Exists(thumbFullPath))
                    {
                        try { File.Delete(thumbFullPath); }
                        catch (Exception ex) { Log.Warning(ex, "删除缩略图文件失败: {Path}", thumbFullPath); }
                    }
                }

                // 删除数据库记录
                DeleteAttachment(att.Id);
            }
        }

        // Attachments
        public List<Attachment> GetAttachmentsByRecord(string recordId) => _dbContext.GetAttachmentsByRecord(recordId);
        public bool AddAttachment(Attachment attachment) => _dbContext.AddAttachment(attachment);
        public bool DeleteAttachment(string id) => _dbContext.DeleteAttachment(id);

        public List<Group> GetAllGroups(string module)
        {
            return _dbContext.GetAllGroups(module);
        }

        public void Dispose()
        {
            var dbPath = _dbContext.GetDbPath();
            if (File.Exists(dbPath))
            {
                try
                {
                    SqliteConnection.ClearPool(new SqliteConnection($"Data Source={dbPath}"));
                }
                catch { /* 连接池清理失败不影响后续释放 */ }
            }
            _dbContext?.Dispose();
        }

        private string GetAttachmentsRootPath()
        {
            var dbPath = _dbContext.GetDbPath();
            if (string.IsNullOrEmpty(dbPath))
                throw new InvalidOperationException("数据库路径为空，无法获取项目文件夹。");
            var projectFolder = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(projectFolder))
                throw new InvalidOperationException("无法获取项目文件夹路径。");
            return projectFolder;
        }
        public List<Attachment> GetAttachmentsByRecordAndType(string recordId, string attachmentType)
        {
            var all = GetAttachmentsByRecord(recordId);
            return all.Where(a => a.AttachmentType == attachmentType).OrderBy(a => a.DisplayOrder).ToList();
        }

        public bool IsTagDuplicate(string parentId, string name, string tag, string? excludeGroupId = null)
        {
            return _dbContext.IsTagDuplicate(parentId, name, tag, excludeGroupId);
        }

        /// <summary>
        /// 获取或创建日期分组（年月下，指定日期和标签）
        /// 若年月分组不存在则自动创建
        /// </summary>
        public string GetOrCreateDateGroup(string yearMonth, int day, string? tag)
        {
            // 解析年月
            var parts = yearMonth.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int month))
                throw new InvalidOperationException($"无效的 yearMonth 格式: {yearMonth}");

            string yearMonthGroupName = $"{yearMonth.Replace("-", "年")}月";
            var yearMonthGroups = _dbContext.GetGroups(ModuleNames.Hazard, null);
            var yearMonthGroup = yearMonthGroups.FirstOrDefault(g => g.Name == yearMonthGroupName);

            // 年月分组不存在时自动创建
            if (yearMonthGroup == null)
            {
                yearMonthGroup = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = ProjectId,
                    Module = ModuleNames.Hazard,
                    Name = yearMonthGroupName,
                    Order = yearMonthGroups.Count,
                    CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                };
                if (!_dbContext.AddGroup(yearMonthGroup))
                    throw new Exception("创建年月分组失败");
            }

            string dateName = $"{month:D2}月{day:D2}日";  // 修复：使用真实的月份，而不是 day
            var children = _dbContext.GetGroups(ModuleNames.Hazard, yearMonthGroup.Id);
            var existing = children.FirstOrDefault(g => g.Name == dateName && g.Tag == tag);
            if (existing != null)
                return existing.Id;

            // 创建新分组
            var newGroup = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = ProjectId,
                Module = ModuleNames.Hazard,
                ParentId = yearMonthGroup.Id,
                Name = dateName,
                Tag = tag,
                Order = children.Count,
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
            };
            if (!_dbContext.AddGroup(newGroup))
                throw new Exception("创建日期分组失败");
            return newGroup.Id;
        }

        /// <summary>
        /// 获取 ProjectsDate 根目录路径（用于拼接附件完整路径）
        /// </summary>
        /// <exception cref="InvalidOperationException">路径无效时抛出</exception>
        public string GetProjectsDateRoot()
        {
            var dbPath = _dbContext.GetDbPath();
            if (string.IsNullOrEmpty(dbPath))
                throw new InvalidOperationException("数据库路径为空，无法获取 ProjectsDate 根目录。");
            var projectFolder = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(projectFolder))
                throw new InvalidOperationException("无法获取项目文件夹路径。");
            var root = Path.GetDirectoryName(projectFolder);
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("无法获取 ProjectsDate 根目录。");
            return root;
        }

        /// <summary>
        /// 获取人员记录所属单位的顶层分组 ID（用于按单位统一管理卡片文件夹）
        /// </summary>
        public string GetPersonnelUnitGroupId(string recordGroupId)
        {
            return _dbContext.GetRootGroupId(recordGroupId);
        }

        /// <summary>
        /// 根据分组 ID 获取该分组所属的年月字符串（格式 yyyy-MM）
        /// </summary>
        public string GetYearMonthFromGroupId(string groupId)
        {
            var allGroups = _dbContext.GetAllGroups("");
            var group = allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return "";

            string? parentId = group.ParentId;
            string name;
            if (!string.IsNullOrEmpty(parentId))
            {
                var parent = allGroups.FirstOrDefault(g => g.Id == parentId);
                if (parent == null) return "";
                name = parent.Name;
            }
            else
            {
                name = group.Name;
            }

            var match = Regex.Match(name, @"(\d{4})年(\d{1,2})月");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int year) &&
                    int.TryParse(match.Groups[2].Value, out int month))
                {
                    return $"{year:D4}-{month:D2}";
                }
            }
            return "";
        }

        /// <summary>
        /// 获取记录所属模块（Hazard / Personnel）
        /// </summary>
        private string GetModuleForRecord(string recordId)
        {
            var record = _dbContext.GetRecordById(recordId);
            if (record == null) return "";
            var group = _dbContext.GetGroupById(record.GroupId);
            return group?.Module ?? "";
        }

        /// <summary>
        /// 将源记录的所有附件复制到新记录，并更新数据库（用于复制卡片）。
        /// 逐张容错：单张失败只记录并继续，不中断整体复制。
        /// </summary>
        /// <param name="sourceRecordId">源记录 ID</param>
        /// <param name="targetRecordId">目标（新）记录 ID</param>
        /// <param name="targetGroupId">目标分组 ID（用于确定目标年月，跨年月复制时路径才会正确）</param>
        /// <param name="yearMonth">源记录年月（yyyy-MM），仅作回退</param>
        /// <returns>未能复制的附件说明列表（空表示全部成功）</returns>
        public List<string> CopyAttachmentsForNewRecord(string sourceRecordId, string targetRecordId, string targetGroupId, string yearMonth)
        {
            var failures = new List<string>();

            var attachments = _dbContext.GetAttachmentsByRecord(sourceRecordId);
            if (attachments.Count == 0) return failures;

            string projectsDateRoot;
            try
            {
                projectsDateRoot = GetProjectsDateRoot();
            }
            catch (InvalidOperationException ex)
            {
                Log.Error(ex, "无法获取 ProjectsDate 根目录，复制附件失败");
                failures.Add("无法获取附件存储目录，图片未复制");
                return failures;
            }

            string module = GetModuleForRecord(sourceRecordId);
            if (string.IsNullOrEmpty(module)) module = "Hazard";

            foreach (var att in attachments)
            {
                try
                {
                    // 构造源文件完整路径
                    string sourceFullPath = Path.Combine(projectsDateRoot, att.RelativePath);
                    if (!File.Exists(sourceFullPath))
                    {
                        Log.Warning("复制附件：源文件缺失，跳过 {Path}", att.RelativePath);
                        failures.Add($"图片文件缺失：{att.FileName}");
                        continue;
                    }

                    // 目标年月：优先用目标分组推导，失败则回退源年月
                    string targetYearMonth = GetYearMonthFromGroupId(targetGroupId);
                    if (string.IsNullOrEmpty(targetYearMonth))
                        targetYearMonth = yearMonth;

                    // 目标路径：按 目标年月 重建（不能只替换 Card_ID，否则跨年月复制时路径仍指向源月份）
                    // 原路径形如 Project_{id}\{module}_{yyyy-MM}\Card_{sourceRecordId}\{fileName}
                    // 新路径形如 Project_{id}\{module}_{targetYearMonth}\Card_{targetRecordId}\{fileName}
                    string targetRelativePath = RebuildAttachmentPath(
                        att.RelativePath, sourceRecordId, targetRecordId, module, targetYearMonth);
                    string targetFullPath = Path.Combine(projectsDateRoot, targetRelativePath);
                    var targetDir = Path.GetDirectoryName(targetFullPath);
                    if (targetDir != null && !Directory.Exists(targetDir))
                        Directory.CreateDirectory(targetDir);

                    File.Copy(sourceFullPath, targetFullPath, true);

                    // 复制缩略图
                    string? targetThumbRelativePath = null;
                    if (!string.IsNullOrEmpty(att.ThumbnailPath))
                    {
                        targetThumbRelativePath = RebuildAttachmentPath(
                            att.ThumbnailPath, sourceRecordId, targetRecordId, module, targetYearMonth);
                        string sourceThumbPath = Path.Combine(projectsDateRoot, att.ThumbnailPath);
                        string targetThumbFullPath = Path.Combine(projectsDateRoot, targetThumbRelativePath);
                        // 缩略图目录（thumb\）可能不存在，必须创建，否则 File.Copy 抛 DirectoryNotFoundException
                        var targetThumbDir = Path.GetDirectoryName(targetThumbFullPath);
                        if (targetThumbDir != null && !Directory.Exists(targetThumbDir))
                            Directory.CreateDirectory(targetThumbDir);
                        if (File.Exists(sourceThumbPath))
                            File.Copy(sourceThumbPath, targetThumbFullPath, true);
                    }

                    // 创建新附件记录
                    var newAttachment = new Attachment
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RecordId = targetRecordId,
                        FileName = att.FileName,
                        RelativePath = targetRelativePath,
                        ThumbnailPath = targetThumbRelativePath,
                        AttachmentType = att.AttachmentType,
                        DisplayOrder = att.DisplayOrder,
                        FileSize = att.FileSize,
                        MimeType = att.MimeType,
                        UploadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    _dbContext.AddAttachment(newAttachment);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "复制附件失败: {SourceRecordId} -> {TargetRecordId}, 文件 {File}", sourceRecordId, targetRecordId, att.FileName);
                    failures.Add($"图片复制失败：{att.FileName}（{ex.Message}）");
                }
            }
            return failures;
        }

        /// <summary>
        /// 重建附件目标路径：将源路径中的 Card_{sourceRecordId} 与 年月段 替换为目标记录与目标年月。
        /// 原路径形如 Project_{id}\{module}_{yyyy-MM}\Card_{sourceRecordId}\{fileName}（含缩略图子目录 thumb）。
        /// </summary>
        private static string RebuildAttachmentPath(
            string relativePath, string sourceRecordId, string targetRecordId,
            string module, string targetYearMonth)
        {
            // 1) 替换 Card_{sourceRecordId} -> Card_{targetRecordId}
            string path = relativePath.Replace($"Card_{sourceRecordId}", $"Card_{targetRecordId}");

            // 2) 替换年月段 {module}_{yyyy-MM} -> {module}_{targetYearMonth}
            //    源路径中的年月段，例如 "Hazard_2026-08"
            var yearMonthMatch = System.Text.RegularExpressions.Regex.Match(
                path, $@"{System.Text.RegularExpressions.Regex.Escape(module)}_\d{{4}}-\d{{2}}");
            if (yearMonthMatch.Success)
            {
                path = path.Replace(yearMonthMatch.Value, $"{module}_{targetYearMonth}");
            }

            return path;
        }

        public bool UpdateAttachment(Attachment attachment)
        {
            return _dbContext.UpdateAttachment(attachment);
        }
        public List<Attachment> GetAttachmentsByRecordIds(List<string> recordIds)
        {
            return _dbContext.GetAttachmentsByRecordIds(recordIds);
        }
    }
}
