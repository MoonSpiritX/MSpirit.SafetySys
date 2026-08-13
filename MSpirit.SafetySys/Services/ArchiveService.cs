// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>导入冲突处理策略</summary>
    public enum ImportStrategy
    {
        /// <summary>跳过，不解压该项目</summary>
        Skip,
        /// <summary>覆盖：删除本地同名文件夹后整体替换（破坏性，需二次确认）</summary>
        Overwrite,
        /// <summary>新建副本：以新项目 ID 导入，名称加"(副本)"后缀</summary>
        Duplicate,
        /// <summary>合并：把包内数据并入本地项目（按主键去重 + 附件按路径去重）</summary>
        Merge
    }

    /// <summary>导入计划（导入向导确认后传给 ArchiveService.ImportArchive）</summary>
    public class ArchiveImportPlan
    {
        /// <summary>包内项目文件夹名（Project_xxx）</summary>
        public string Folder { get; set; } = "";
        /// <summary>包内项目名称</summary>
        public string Name { get; set; } = "";
        /// <summary>包内项目创建日期</summary>
        public string Created { get; set; } = "";
        /// <summary>用户选择的处理策略</summary>
        public ImportStrategy Strategy { get; set; } = ImportStrategy.Duplicate;
        /// <summary>合并/覆盖时的本地目标项目（ID 或名称匹配到的本地项目，可能为 null）</summary>
        public ProjectInfo? TargetLocalProject { get; set; }
    }

    /// <summary>包检查结果（导入向导第一步展示）</summary>
    public class ArchiveInspection
    {
        public ArchiveManifest? Manifest { get; set; }
        public List<ArchiveInspectItem> Items { get; set; } = new();
        /// <summary>是否有 manifest（false 表示兼容包，名称用文件夹名兜底）</summary>
        public bool HasManifest { get; set; }
    }

    /// <summary>包内单个项目的检查结果</summary>
    public class ArchiveInspectItem
    {
        public string Folder { get; set; } = "";
        public string Name { get; set; } = "";
        public string Created { get; set; } = "";
        /// <summary>本地匹配到的项目（ID 或名称相同），null 表示本地无此项目</summary>
        public ProjectInfo? LocalProject { get; set; }
    }

    /// <summary>单个项目的导入执行结果（供导入完成后逐项展示）</summary>
    public class ArchiveImportResult
    {
        /// <summary>包内项目文件夹名（Project_xxx）</summary>
        public string Folder { get; set; } = "";
        /// <summary>包内项目名称</summary>
        public string Name { get; set; } = "";
        /// <summary>是否执行成功</summary>
        public bool Success { get; set; }
        /// <summary>实际操作描述（如"已新建项目：xxx(副本)"、"已合并到本地项目：xxx"、"已跳过"）</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 存档核心服务：项目数据整体打包（.msarch，标准 zip 容器）与导入。
    /// 职责：WAL checkpoint、manifest 读写、zip slip 防护、冲突策略执行、数据库合并、注册 master.db。
    /// </summary>
    public static class ArchiveService
    {
        public const string ArchiveExtension = ".msarch";
        private const string ManifestEntryName = "manifest.json";
        private const int ManifestFormatVersion = 1;

        // ==================== 导出 ====================

        /// <summary>
        /// 把指定项目打包为 .msarch 文件。
        /// </summary>
        /// <param name="rootPath">数据根目录（DataPathService.RootPath）</param>
        /// <param name="projects">要导出的项目列表</param>
        /// <param name="targetFile">目标文件完整路径（.msarch）</param>
        public static void ExportProjects(string rootPath, List<ProjectInfo> projects, string targetFile)
        {
            if (projects == null || projects.Count == 0) throw new ArgumentException("未选择要导出的项目");
            if (string.IsNullOrWhiteSpace(targetFile)) throw new ArgumentException("未指定导出文件");

            var manifest = new ArchiveManifest
            {
                FormatVersion = ManifestFormatVersion,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            string projectsRoot = Path.Combine(rootPath, "ProjectsDate");
            using var zip = ZipFile.Open(targetFile, ZipArchiveMode.Create);

            // 1. 先收集项目信息到 manifest（顺序必须在写入 manifest.json 之前！）
            foreach (var proj in projects)
            {
                manifest.Projects.Add(new ArchiveProjectInfo
                {
                    Folder = $"Project_{proj.Id}",
                    Name = proj.Name,
                    Created = proj.Created
                });
            }

            // 2. manifest.json 最先写入（便于导入时快速读取）
            var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open(), System.Text.Encoding.UTF8))
            {
                writer.Write(JsonSerializer.Serialize(manifest,
                    new JsonSerializerOptions { WriteIndented = true }));
            }

            // 3. 逐项目打包（data.db 先做 WAL checkpoint 保证一致性；实际文件读取走在线备份）
            foreach (var proj in projects)
            {
                string projectFolder = Path.Combine(projectsRoot, $"Project_{proj.Id}");
                if (!Directory.Exists(projectFolder))
                {
                    Log.Warning("[Archive] 导出时项目文件夹不存在，跳过: {Folder}", projectFolder);
                    continue;
                }

                CheckpointDatabase(Path.Combine(projectFolder, "data.db"));
                AddFolderToZip(zip, projectFolder, $"Project_{proj.Id}");
            }
        }

        /// <summary>把文件夹下所有文件写入 zip（跳过 Cache 目录与临时文件；data.db 走 SQLite 在线备份）</summary>
        private static void AddFolderToZip(ZipArchive zip, string folder, string zipRoot)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
                // 跳过缓存目录与临时文件（文件名以 .tmp / ~$ 开头；WAL 伴随文件 .db-wal/.db-shm 也跳过，
                // 其数据已由 data.db 的 SQLite 在线备份快照包含）
                if (rel.StartsWith("Cache/", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(rel);
                if (name.StartsWith("~$") || name.StartsWith(".tmp")) continue;
                if (name.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)) continue;

                var entry = zip.CreateEntry($"{zipRoot}/{rel}", CompressionLevel.Optimal);
                if (name.Equals("data.db", StringComparison.OrdinalIgnoreCase))
                {
                    // data.db 可能被当前进程占用（正在使用的项目），File.OpenRead 会失败；
                    // 改用 SQLite 在线备份（同进程多连接共享文件）生成一致快照再写入 zip。
                    using var dst = entry.Open();
                    BackupDatabaseToEntry(Path.Combine(folder, file), dst);
                }
                else
                {
                    using var src = File.OpenRead(file);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
            }
        }

        /// <summary>通过 SQLite 在线备份把 data.db 一致快照写入目标流（解决运行中项目文件被占用的导出失败）</summary>
        private static void BackupDatabaseToEntry(string dbPath, Stream target)
        {
            string temp = Path.GetTempFileName();
            try
            {
                // 注意：临时库连接必须禁用连接池（Pooling=False），否则 Close 后句柄仍被池持有，文件无法读取
                using (var src = new SqliteConnection($"Data Source={dbPath}"))
                using (var dst = new SqliteConnection($"Data Source={temp};Pooling=False"))
                {
                    src.Open();
                    dst.Open();
                    src.BackupDatabase(dst);
                }
                using var fs = File.OpenRead(temp);
                fs.CopyTo(target);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }

        /// <summary>对 data.db 执行 WAL checkpoint(TRUNCATE)，确保主库文件落盘一致</summary>
        private static void CheckpointDatabase(string dbPath)
        {
            if (!File.Exists(dbPath)) return;
            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                SqliteConnection.ClearPool(conn);
                conn.Open();
                using var cmd = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", conn);
                cmd.ExecuteNonQuery();
                conn.Close();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Archive] WAL checkpoint 失败（继续导出）: {Db}", dbPath);
            }
        }

        // ==================== 检查（导入第一步） ====================

        /// <summary>读取存档包清单并匹配本地项目，供导入向导展示</summary>
        public static ArchiveInspection InspectArchive(string archiveFile, string rootPath)
        {
            var result = new ArchiveInspection();
            var localProjects = new ConfigManager(rootPath).GetProjectList();

            using var zip = ZipFile.OpenRead(archiveFile);
            var manifestEntry = zip.GetEntry(ManifestEntryName);
            if (manifestEntry != null)
            {
                using var reader = new StreamReader(manifestEntry.Open(), System.Text.Encoding.UTF8);
                var manifest = JsonSerializer.Deserialize<ArchiveManifest>(reader.ReadToEnd());
                result.Manifest = manifest;
                result.HasManifest = true;
                if (manifest?.Projects != null)
                {
                    foreach (var p in manifest.Projects)
                    {
                        result.Items.Add(new ArchiveInspectItem
                        {
                            Folder = p.Folder,
                            Name = p.Name,
                            Created = p.Created,
                            LocalProject = FindLocalMatch(localProjects, p.Folder, p.Name)
                        });
                    }
                    return result;
                }
            }

            // 兜底：无 manifest 的兼容包 → 枚举根目录 Project_* 文件夹
            result.HasManifest = false;
            foreach (var entry in zip.Entries)
            {
                string rootFolder = entry.FullName.Split('/')[0];
                if (rootFolder.StartsWith("Project_", StringComparison.OrdinalIgnoreCase)
                    && result.Items.All(i => i.Folder != rootFolder))
                {
                    result.Items.Add(new ArchiveInspectItem
                    {
                        Folder = rootFolder,
                        Name = rootFolder, // 名称兜底 = 文件夹名
                        Created = "",
                        LocalProject = FindLocalMatch(localProjects, rootFolder, "")
                    });
                }
            }
            return result;
        }

        /// <summary>ID（文件夹名）或名称任一匹配本地项目即视为同一项目</summary>
        private static ProjectInfo? FindLocalMatch(List<ProjectInfo> localProjects, string folder, string name)
        {
            string? id = folder.StartsWith("Project_", StringComparison.OrdinalIgnoreCase)
                ? folder.Substring("Project_".Length)
                : null;

            foreach (var p in localProjects)
            {
                if (id != null && p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return p;
                if (!string.IsNullOrEmpty(name) && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        // ==================== 导入 ====================

        /// <summary>
        /// 按导入计划执行导入：解压（zip slip 防护）→ 逐项目执行策略 → 清理临时目录。
        /// 返回逐项目处理结果（每个项目独立 try/catch，单项失败不影响其他项）。
        /// </summary>
        public static List<ArchiveImportResult> ImportArchive(string rootPath, string archiveFile, List<ArchiveImportPlan> plans)
        {
            var results = new List<ArchiveImportResult>();
            if (plans == null || plans.Count == 0) throw new ArgumentException("导入计划为空");

            string tempDir = Path.Combine(rootPath, "Temp", $"Import_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var config = new ConfigManager(rootPath);
            string projectsRoot = Path.Combine(rootPath, "ProjectsDate");
            Directory.CreateDirectory(projectsRoot);

            try
            {
                // 1. 解压到临时目录（含路径穿越校验）
                ExtractArchive(archiveFile, tempDir);

                // 2. 逐项目执行（单项失败记录结果，不中断后续项）
                foreach (var plan in plans)
                {
                    var result = new ArchiveImportResult
                    {
                        Folder = plan.Folder,
                        Name = plan.Name
                    };

                    try
                    {
                        string sourceFolder = Path.Combine(tempDir, plan.Folder);
                        if (!Directory.Exists(sourceFolder))
                        {
                            Log.Warning("[Archive] 包内项目文件夹缺失: {Folder}", plan.Folder);
                            result.Success = false;
                            result.Message = "包内项目文件夹缺失";
                            results.Add(result);
                            continue;
                        }

                        switch (plan.Strategy)
                        {
                            case ImportStrategy.Skip:
                                result.Success = true;
                                result.Message = "已跳过";
                                break;

                            case ImportStrategy.Overwrite:
                                result.Message = DoOverwrite(config, projectsRoot, plan, sourceFolder);
                                result.Success = true;
                                break;

                            case ImportStrategy.Duplicate:
                                result.Message = DoDuplicate(config, projectsRoot, plan, sourceFolder);
                                result.Success = true;
                                break;

                            case ImportStrategy.Merge:
                                result.Message = DoMerge(config, projectsRoot, plan, sourceFolder);
                                result.Success = true;
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Archive] 导入项目失败: {Folder}", plan.Folder);
                        result.Success = false;
                        result.Message = $"导入失败：{ex.Message}";
                    }

                    results.Add(result);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { /* 临时目录清理失败可忽略 */ }
            }

            return results;
        }

        /// <summary>解压存档到目标目录，逐条目校验路径（zip slip 防护）</summary>
        private static void ExtractArchive(string archiveFile, string targetDir)
        {
            using var zip = ZipFile.OpenRead(archiveFile);
            foreach (var entry in zip.Entries)
            {
                string entryName = entry.FullName.Replace('\\', '/');
                if (entryName == ManifestEntryName) continue; // manifest 为元数据，导入时不落盘
                if (!IsSafeEntryName(entryName))
                    throw new InvalidDataException($"存档包包含非法路径，已中止导入: {entryName}");

                if (entryName.EndsWith("/"))
                {
                    Directory.CreateDirectory(Path.Combine(targetDir, entryName));
                    continue;
                }

                string destPath = Path.Combine(targetDir, entryName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                using var src = entry.Open();
                using var dst = File.Create(destPath);
                src.CopyTo(dst);
            }
        }

        /// <summary>路径穿越防护：条目必须位于 Project_ 根目录下，且无 .. / 盘符 / 绝对路径</summary>
        private static bool IsSafeEntryName(string entryName)
        {
            if (entryName.StartsWith("/") || entryName.Contains(":") || entryName.Contains(".."))
                return false;
            var parts = entryName.Split('/');
            return parts.Length >= 1 && parts[0].StartsWith("Project_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 覆盖：删除本地目标项目文件夹（同 ID 或同名，由 TargetLocalProject 定位）后整体替换；
        /// 保持 ID=文件夹名=库内 ProjectId 一致。返回操作描述。
        /// 说明：导入向导仅在按 ID/名称匹配到本地项目时才提供「覆盖」选项，故选覆盖时 TargetLocalProject 必非空；
        /// 异 ID 同名覆盖时包内 data.db 的 Groups.ProjectId 需重写为本地 ID，否则分组查不出来。
        /// </summary>
        private static string DoOverwrite(ConfigManager config, string projectsRoot, ArchiveImportPlan plan, string sourceFolder)
        {
            // 本地目标项目：向导已按 ID 或名称匹配（选覆盖时必有 TargetLocalProject）
            var target = plan.TargetLocalProject;

            // 防御：无本地匹配（理论不可达——向导选覆盖必有匹配）→ 按包内 ID 注册新项目，保持原 ID
            if (target == null)
            {
                string? newId = plan.Folder.StartsWith("Project_") ? plan.Folder.Substring("Project_".Length) : null;
                if (newId == null)
                    return DoDuplicate(config, projectsRoot, plan, sourceFolder); // 包名异常兜底

                string newFolder = Path.Combine(projectsRoot, plan.Folder);
                if (Directory.Exists(newFolder))
                {
                    ReleaseDatabase(Path.Combine(newFolder, "data.db"));
                    Directory.Delete(newFolder, true);
                }
                Directory.Move(sourceFolder, newFolder);

                // 按包内 ID 注册（名称重名失败则回退副本）
                if (!config.AddProjectRecord(newId, plan.Name, plan.Created))
                    return DoDuplicate(config, projectsRoot, plan, newFolder);

                return $"已覆盖本地项目：{plan.Name}";
            }

            string targetFolder = Path.Combine(projectsRoot, $"Project_{target.Id}");
            if (Directory.Exists(targetFolder))
            {
                ReleaseDatabase(Path.Combine(targetFolder, "data.db")); // 清连接池释放句柄，否则删除失败
                Directory.Delete(targetFolder, true);
            }
            Directory.Move(sourceFolder, targetFolder);

            // 关键：包内 ID ≠ 本地 ID（同名覆盖）时，data.db 内 Groups.ProjectId 必须重写为本地 ID
            string packageId = plan.Folder.StartsWith("Project_") ? plan.Folder.Substring("Project_".Length) : "";
            if (!string.IsNullOrEmpty(packageId))
            {
                UpdateGroupsProjectId(Path.Combine(targetFolder, "data.db"), packageId, target.Id);
            }

            // 名称同步（同名无需改；异名更新为包内名称，重名限制失败则忽略）
            if (!target.Name.Equals(plan.Name, StringComparison.OrdinalIgnoreCase))
            {
                config.RenameProject(target.Id, plan.Name);
            }

            return $"已覆盖本地项目：{plan.Name}";
        }

        /// <summary>新建副本：以唯一名称新建项目（新 ID + 空文件夹），用包内内容替换，并同步库内 Groups.ProjectId。返回操作描述</summary>
        private static string DoDuplicate(ConfigManager config, string projectsRoot, ArchiveImportPlan plan, string sourceFolder)
        {
            string newId = CreateProjectWithUniqueName(config, plan.Name);
            string targetFolder = Path.Combine(projectsRoot, $"Project_{newId}");
            if (Directory.Exists(targetFolder))
            {
                ReleaseDatabase(Path.Combine(targetFolder, "data.db")); // 清连接池释放句柄，否则删除失败
                Directory.Delete(targetFolder, true); // 删掉 AddProject 创建的空库/Cache
            }
            Directory.Move(sourceFolder, targetFolder);

            // 关键：项目 ID 变了，data.db 内 Groups.ProjectId 必须同步为新 ID，否则分组数据查不出来
            string oldId = plan.Folder.StartsWith("Project_") ? plan.Folder.Substring("Project_".Length) : "";
            UpdateGroupsProjectId(Path.Combine(targetFolder, "data.db"), oldId, newId);

            string newName = config.GetProjectById(newId)?.Name ?? plan.Name;
            return $"已新建项目：{newName}";
        }

        /// <summary>清空 data.db 连接池并执行 checkpoint，释放文件句柄（删除目录前必须调用）</summary>
        private static void ReleaseDatabase(string dbPath)
        {
            if (!File.Exists(dbPath)) return;
            try
            {
                // 先清掉池中残留连接（AddProject 等留下的句柄）
                using (var probe = new SqliteConnection($"Data Source={dbPath}"))
                {
                    SqliteConnection.ClearPool(probe);
                }
                // 再用非池化连接执行 checkpoint：用完直接关闭，不驻留句柄
                using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                {
                    conn.Open();
                    using var cmd = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", conn);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Archive] 释放数据库句柄失败: {Db}", dbPath);
            }
        }

        /// <summary>更新库内 Groups.ProjectId（副本导入后 ID 变化时调用，保证 文件夹名=ID=库内 ProjectId 一致）</summary>
        private static void UpdateGroupsProjectId(string dbPath, string oldId, string newId)
        {
            if (!File.Exists(dbPath) || string.IsNullOrEmpty(oldId) || oldId == newId) return;
            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();
                using var cmd = new SqliteCommand("UPDATE Groups SET ProjectId = @NewId WHERE ProjectId = @OldId;", conn);
                cmd.Parameters.AddWithValue("@NewId", newId);
                cmd.Parameters.AddWithValue("@OldId", oldId);
                cmd.ExecuteNonQuery();
                conn.Close();
                SqliteConnection.ClearPool(conn);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Archive] 更新 Groups.ProjectId 失败: {Db}", dbPath);
            }
        }

        /// <summary>合并：数据库按主键 INSERT OR IGNORE + 附件按相对路径补齐。返回操作描述</summary>
        private static string DoMerge(ConfigManager config, string projectsRoot, ArchiveImportPlan plan, string sourceFolder)
        {
            var target = plan.TargetLocalProject;
            if (target == null)
            {
                // 无本地目标（理论上不会进入此分支，防御性走副本）
                return DoDuplicate(config, projectsRoot, plan, sourceFolder);
            }

            string targetFolder = Path.Combine(projectsRoot, $"Project_{target.Id}");
            if (!Directory.Exists(targetFolder))
            {
                Log.Warning("[Archive] 合并目标文件夹不存在: {Folder}", targetFolder);
                throw new InvalidOperationException($"本地目标项目文件夹不存在：{target.Name}");
            }

            MergeProjectDatabases(
                Path.Combine(targetFolder, "data.db"),
                Path.Combine(sourceFolder, "data.db"),
                target.Id);

            CopyMissingAttachments(sourceFolder, targetFolder);

            return $"已合并到本地项目：{target.Name}";
        }

        /// <summary>
        /// 数据库合并：ATTACH 源库 → 先把源库 Groups.ProjectId 重写为本地目标项目 ID → 四表按显式列名 INSERT OR IGNORE（满足外键顺序）。
        /// 显式列名采用"两库共有列"动态求交：跨版本表结构不一致时只合并共同列，避免 SELECT * 按位置插入错位。
        /// </summary>
        private static void MergeProjectDatabases(string targetDb, string sourceDb, string targetProjectId)
        {
            if (!File.Exists(sourceDb)) return;
            if (!File.Exists(targetDb))
            {
                File.Copy(sourceDb, targetDb);
                return;
            }

            using var conn = new SqliteConnection($"Data Source={targetDb}");
            conn.Open();
            try
            {
                Exec(conn, $"ATTACH DATABASE '{EscapeSql(sourceDb)}' AS src;");

                // 关键：源库 Groups.ProjectId 原样搬入会导致按 ProjectId 过滤时数据不可见，
                // 必须在插入前重写为本地目标项目 ID（保证 ID=文件夹名=库内 ProjectId 三者一致）。
                if (!string.IsNullOrEmpty(targetProjectId))
                {
                    Exec(conn, $"UPDATE src.Groups SET ProjectId = '{EscapeSql(targetProjectId)}' WHERE ProjectId IS NOT NULL AND ProjectId <> '{EscapeSql(targetProjectId)}';");
                }

                foreach (var table in new[] { "Groups", "Records", "PersonnelRecords", "Attachments" })
                {
                    var commonCols = GetCommonColumnNames(conn, table);
                    if (commonCols.Count == 0)
                    {
                        Log.Warning("[Archive] 合并时表 {Table} 无共同列，跳过", table);
                        continue;
                    }
                    string colList = string.Join(", ", commonCols.Select(c => $"[{c}]"));
                    Exec(conn, $"INSERT OR IGNORE INTO main.{table} ({colList}) SELECT {colList} FROM src.{table};");
                }
                Exec(conn, "DETACH DATABASE src;");
            }
            catch (Exception ex)
            {
                try { Exec(conn, "DETACH DATABASE src;"); } catch { }
                Log.Error(ex, "[Archive] 合并数据库失败: {Db}", targetDb);
                throw;
            }
            finally
            {
                conn.Close();
                SqliteConnection.ClearPool(conn);
            }
        }

        /// <summary>取两库（main + src）同名字段的列名交集（忽略大小写，按 main 顺序），跨版本表结构差异时只合并共同列</summary>
        private static List<string> GetCommonColumnNames(SqliteConnection conn, string table)
        {
            var mainCols = GetTableColumnNames(conn, "main", table);
            var srcCols = GetTableColumnNames(conn, "src", table);
            var srcSet = new HashSet<string>(srcCols, StringComparer.OrdinalIgnoreCase);
            return mainCols.Where(c => srcSet.Contains(c)).ToList();
        }

        /// <summary>读取指定 schema 下表的所有列名（PRAGMA table_info 的 name 列）</summary>
        private static List<string> GetTableColumnNames(SqliteConnection conn, string schema, string table)
        {
            var cols = new List<string>();
            using var cmd = new SqliteCommand($"PRAGMA {schema}.table_info(\"{table}\");", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                cols.Add(reader.GetString(1)); // 第 2 列为 name
            }
            return cols;
        }

        /// <summary>复制缺失附件（相对路径已存在则跳过；排除 data.db 与 Cache）</summary>
        private static void CopyMissingAttachments(string sourceFolder, string targetFolder)
        {
            foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceFolder, file).Replace('\\', '/');
                if (rel.Equals("data.db", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("Cache/", StringComparison.OrdinalIgnoreCase)) continue;

                string targetFile = Path.Combine(targetFolder, rel);
                if (File.Exists(targetFile)) continue; // 已存在跳过（目标库优先）

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(file, targetFile);
            }
        }

        /// <summary>以唯一名称创建项目（重名自动加"(副本)"后缀），返回新项目 ID</summary>
        private static string CreateProjectWithUniqueName(ConfigManager config, string name)
        {
            if (config.AddProject(name))
                return GetProjectIdByName(config, name);

            string candidate = $"{name}(副本)";
            if (config.AddProject(candidate))
                return GetProjectIdByName(config, candidate);

            candidate = $"{name}(副本{DateTime.Now:HHmmss})";
            config.AddProject(candidate);
            return GetProjectIdByName(config, candidate);
        }

        private static string GetProjectIdByName(ConfigManager config, string name)
            => config.GetProjectList().First(p => p.Name == name).Id;

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>SQL 字符串转义（单引号翻倍）</summary>
        private static string EscapeSql(string value) => value.Replace("'", "''");
    }
}
