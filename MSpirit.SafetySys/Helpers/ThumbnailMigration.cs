// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Helpers
{
    /// <summary>
    /// 缩略图迁移工具 — 将旧版 Card_{id}/ 目录下的缩略图
    /// 迁移到 Card_{id}/thumb/ 子目录，并更新数据库中的 ThumbnailPath。
    /// </summary>
    public static class ThumbnailMigration
    {
        /// <summary>
        /// 执行缩略图迁移。幂等：如果 thumb/ 已存在且文件已迁移，则无操作。
        /// </summary>
        public static void Run(string dataRootPath)
        {
            try
            {
                // 2026-08-10 修正：master.db 位于 ProjectsDate\（此前检查 {root}\master.db 与 Data\ 实际位置不符，迁移从未执行）
                var masterDbPath = Path.Combine(dataRootPath, "ProjectsDate", "master.db");
                if (!File.Exists(masterDbPath))
                {
                    Log.Information("缩略图迁移跳过：未找到主数据库 {MasterDb}", masterDbPath);
                    return;
                }

                string projectsDateRoot = Path.Combine(dataRootPath, "ProjectsDate");

                // 获取所有项目 ID
                var projectIds = GetProjectIds(masterDbPath);
                if (projectIds.Count == 0)
                {
                    Log.Information("缩略图迁移跳过：没有项目");
                    return;
                }

                int totalMigrated = 0;
                int totalSkipped = 0;

                foreach (var projectId in projectIds)
                {
                    var projectDbPath = Path.Combine(dataRootPath, "ProjectsDate", $"Project_{projectId}", "data.db");
                    if (!File.Exists(projectDbPath)) continue;

                    var migrated = MigrateProject(projectDbPath, projectsDateRoot);
                    totalMigrated += migrated.moved;
                    totalSkipped += migrated.skipped;
                }

                if (totalMigrated > 0 || totalSkipped > 0)
                {
                    Log.Information("缩略图迁移完成：迁移 {Moved} 个文件，跳过 {Skipped} 个", totalMigrated, totalSkipped);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "缩略图迁移过程出现异常（不影响主程序启动）");
            }
        }

        private static List<string> GetProjectIds(string masterDbPath)
        {
            var ids = new List<string>();
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={masterDbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id FROM Projects";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "读取项目列表失败");
            }
            return ids;
        }

        private static (int moved, int skipped) MigrateProject(string projectDbPath, string projectsDateRoot)
        {
            int moved = 0;
            int skipped = 0;

            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={projectDbPath}");
                conn.Open();

                // 读取所有 ThumbnailPath
                var attachments = new List<(string id, string? thumbPath)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, ThumbnailPath FROM Attachments WHERE ThumbnailPath IS NOT NULL AND ThumbnailPath != ''";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        attachments.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
                    }
                }

                foreach (var (id, thumbPath) in attachments)
                {
                    if (string.IsNullOrEmpty(thumbPath)) continue;

                    // 旧路径：Project_{id}/.../Card_{id}/thumbFileName
                    // 新路径：Project_{id}/.../Card_{id}/thumb/thumbFileName
                    string oldFullPath = Path.Combine(projectsDateRoot, thumbPath.Replace('/', Path.DirectorySeparatorChar));

                    // 检查是否已包含 thumb/ — 跳过已迁移的
                    if (thumbPath.Contains("/thumb/") || thumbPath.Contains("\\thumb\\"))
                    {
                        skipped++;
                        continue;
                    }

                    if (!File.Exists(oldFullPath))
                    {
                        // 缩略图已不存在，仍更新数据库记录
                        UpdateThumbnailPath(conn, id, UpdateThumbPathToNew(thumbPath));
                        skipped++;
                        continue;
                    }

                    // 构建新路径
                    string directory = Path.GetDirectoryName(oldFullPath)!;
                    string fileName = Path.GetFileName(oldFullPath);
                    string thumbDir = Path.Combine(directory, "thumb");
                    string newFullPath = Path.Combine(thumbDir, fileName);

                    string newRelPath = UpdateThumbPathToNew(thumbPath);

                    try
                    {
                        if (!Directory.Exists(thumbDir))
                            Directory.CreateDirectory(thumbDir);

                        // 如果目标已存在，删除原文件
                        if (File.Exists(newFullPath))
                        {
                            File.Delete(oldFullPath);
                        }
                        else
                        {
                            File.Move(oldFullPath, newFullPath);
                        }

                        UpdateThumbnailPath(conn, id, newRelPath);
                        moved++;
                        Log.Debug("迁移缩略图：{Old} → {New}", oldFullPath, newFullPath);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "迁移缩略图失败：{Path}", oldFullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "迁移项目数据库失败：{Db}", projectDbPath);
            }

            return (moved, skipped);
        }

        private static string UpdateThumbPathToNew(string oldRelativePath)
        {
            // 将 "Project_X/.../Card_Y/file_thumb.jpg" 
            // 改为 "Project_X/.../Card_Y/thumb/file_thumb.jpg"
            int lastSlash = oldRelativePath.LastIndexOf('/');
            if (lastSlash < 0)
                lastSlash = oldRelativePath.LastIndexOf('\\');
            if (lastSlash < 0) return oldRelativePath;

            string basePath = oldRelativePath.Substring(0, lastSlash);
            string fileName = oldRelativePath.Substring(lastSlash + 1);
            return $"{basePath}/thumb/{fileName}";
        }

        private static void UpdateThumbnailPath(Microsoft.Data.Sqlite.SqliteConnection conn, string attachmentId, string newThumbPath)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Attachments SET ThumbnailPath = @ThumbnailPath WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@ThumbnailPath", newThumbPath);
            cmd.Parameters.AddWithValue("@Id", attachmentId);
            cmd.ExecuteNonQuery();
        }
    }
}
