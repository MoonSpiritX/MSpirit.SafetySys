// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 自动备份服务：把数据库与配置文件备份到数据根内的 ProjectsDate.Backup\ 目录。
    /// 策略（2026-08-10 定稿）：只备份数据库 + json（不备份图片附件，图片与库解耦，恢复库后引用自动接上）；
    /// 启动时在线备份一次（兜底），退出时关库后复制备份一次（主备份）；保留最近 30 天、最多 100 份自动轮换（双重兜底）。
    /// </summary>
    public static class BackupService
    {
        /// <summary>备份根目录名（位于数据根内，与 ProjectsDate 平级）</summary>
        public const string BackupRootName = "ProjectsDate.Backup";

        /// <summary>保留的备份份数（超出自动删除最旧，封顶）</summary>
        public const int MaxBackups = 100;

        /// <summary>备份保留期限（天）：超过此天数的备份删除（时间为主，份数封顶双保险）</summary>
        public const int RetentionDays = 30;

        /// <summary>备份目录名前缀（后跟 yyyyMMdd_HHmmss 时间戳）</summary>
        private const string BackupFolderPrefix = "Backup_";

        /// <summary>配置文件清单（数据根下与项目无关的全局配置，随备份带走）</summary>
        private static readonly string[] ConfigJsonFiles =
        {
            "MappingDictionary.json",
            "CommonHazardTypes.json",
            "FavoriteLinks.json",
            "ViewState.json"
        };

        /// <summary>备份根目录完整路径</summary>
        public static string GetBackupRoot(DataPathService dataPathService)
            => Path.Combine(dataPathService.RootPath, BackupRootName);

        // ==================== 启动备份（在线备份，程序运行中） ====================

        /// <summary>
        /// 启动时自动备份（兜底）：程序刚启动、数据库可能正被占用，用 SQLite 在线备份生成一致快照。
        /// 后台静默执行，失败仅记日志不打扰用户。
        /// </summary>
        public static void RunStartupBackup(DataPathService dataPathService)
        {
            try
            {
                RunBackupCore(dataPathService, useOnlineBackup: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Backup] 启动自动备份失败");
            }
        }

        // ==================== 退出备份（关库后复制） ====================

        /// <summary>
        /// 退出时自动备份（主备份）：调用方需先关闭所有数据库连接（OnExit 已 ClearPool），
        /// 此时直接复制文件即可，最可靠。
        /// </summary>
        public static void RunShutdownBackup(DataPathService dataPathService)
        {
            try
            {
                RunBackupCore(dataPathService, useOnlineBackup: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Backup] 退出自动备份失败");
            }
        }

        // ==================== 核心 ====================

        private static void RunBackupCore(DataPathService dataPathService, bool useOnlineBackup)
        {
            var backupRoot = GetBackupRoot(dataPathService);
            Directory.CreateDirectory(backupRoot);

            // 收集本次要备份的文件：源路径 → 备份内相对路径
            var files = CollectBackupFiles(dataPathService);
            if (files.Count == 0)
            {
                Log.Warning("[Backup] 无可备份的数据文件，跳过本次备份");
                return;
            }

            // 时间戳含毫秒：防止同一秒内多次备份（如快速重启）撞同一目录导致覆盖
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            string backupFolder = Path.Combine(backupRoot, $"{BackupFolderPrefix}{stamp}");
            Directory.CreateDirectory(backupFolder);

            foreach (var (sourcePath, relPath) in files)
            {
                string targetPath = Path.Combine(backupFolder, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                if (Path.GetExtension(sourcePath).Equals(".db", StringComparison.OrdinalIgnoreCase))
                {
                    if (useOnlineBackup)
                        BackupDatabaseOnline(sourcePath, targetPath);
                    else
                        File.Copy(sourcePath, targetPath, true);
                }
                else
                {
                    // json 等配置文件：直接复制（体积 KB 级，无并发写）
                    File.Copy(sourcePath, targetPath, true);
                }
            }

            // 轮换：删除超出 MaxBackups 的最旧备份
            PruneOldBackups(backupRoot);

            Log.Information("[Backup] 自动备份完成：{Folder}（{Count} 个文件）", backupFolder, files.Count);
        }

        /// <summary>收集备份文件清单：源绝对路径 → 备份内相对路径（保持 ProjectsDate 下的目录层次）</summary>
        private static List<(string Source, string Rel)> CollectBackupFiles(DataPathService dataPathService)
        {
            var list = new List<(string, string)>();
            string root = dataPathService.RootPath;
            string projectsDate = Path.Combine(root, "ProjectsDate");

            // 1. 主库 master.db（2026-08-10 起位于 ProjectsDate\）
            string masterDb = Path.Combine(projectsDate, "master.db");
            if (File.Exists(masterDb))
                list.Add((masterDb, "master.db"));

            // 2. 全局配置文件（数据根下 json）
            foreach (var name in ConfigJsonFiles)
            {
                string path = Path.Combine(root, name);
                if (File.Exists(path))
                    list.Add((path, name));
            }

            // 3. 各项目 data.db（保持 Project_x\data.db 相对结构）
            if (Directory.Exists(projectsDate))
            {
                foreach (var projectFolder in Directory.GetDirectories(projectsDate, "Project_*"))
                {
                    string dataDb = Path.Combine(projectFolder, "data.db");
                    if (File.Exists(dataDb))
                    {
                        string folderName = Path.GetFileName(projectFolder);
                        list.Add((dataDb, Path.Combine(folderName, "data.db")));
                    }
                }
            }

            return list;
        }

        /// <summary>SQLite 在线备份：源库 → 临时文件 → 目标文件（解决运行中数据库被占用的备份）</summary>
        private static void BackupDatabaseOnline(string dbPath, string targetPath)
        {
            string temp = Path.GetTempFileName();
            try
            {
                // 临时库连接禁用连接池（Pooling=False），否则 Close 后句柄仍被池持有，文件无法读取/删除
                using (var src = new SqliteConnection($"Data Source={dbPath}"))
                using (var dst = new SqliteConnection($"Data Source={temp};Pooling=False"))
                {
                    src.Open();
                    dst.Open();
                    src.BackupDatabase(dst);
                }
                File.Copy(temp, targetPath, true);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }

        /// <summary>
        /// 轮换删除（时间为主 + 份数封顶双保险）：
        /// 1. 删除超过 RetentionDays 天的备份（时间戳从目录名解析）；
        /// 2. 若剩余仍超过 MaxBackups 份，按时间倒序保留最新的 MaxBackups 份，删除更旧的。
        /// </summary>
        private static void PruneOldBackups(string backupRoot)
        {
            var backups = Directory.GetDirectories(backupRoot, $"{BackupFolderPrefix}*")
                .Select(d => new { Dir = d, Time = ParseBackupTime(Path.GetFileName(d)) })
                .Where(x => x.Time != null)
                .OrderByDescending(x => x.Time) // 最新在前
                .ToList();

            // 条件 1：超期删除（时间为主）
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var old in backups.Where(x => x.Time < cutoff))
            {
                TryDeleteBackup(old.Dir);
            }

            // 条件 2：份数封顶（删最旧）
            var remaining = Directory.GetDirectories(backupRoot, $"{BackupFolderPrefix}*")
                .OrderByDescending(d => d)
                .ToList();
            foreach (var old in remaining.Skip(MaxBackups))
            {
                TryDeleteBackup(old);
            }
        }

        /// <summary>从备份目录名解析时间戳（Backup_yyyyMMdd_HHmmssfff），解析失败返回 null（不删除）</summary>
        private static DateTime? ParseBackupTime(string folderName)
        {
            if (!folderName.StartsWith(BackupFolderPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            string stamp = folderName.Substring(BackupFolderPrefix.Length);
            return DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmssfff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var t) ? t : null;
        }

        /// <summary>删除备份目录（失败仅记日志，不中断）</summary>
        private static void TryDeleteBackup(string folder)
        {
            try
            {
                Directory.Delete(folder, true);
                Log.Information("[Backup] 轮换删除备份：{Folder}", folder);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Backup] 删除旧备份失败: {Folder}", folder);
            }
        }
    }
}
