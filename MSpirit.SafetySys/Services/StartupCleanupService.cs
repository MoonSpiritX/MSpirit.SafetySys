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
    /// 启动清理服务：程序加载完界面后执行，处理两类残留：
    /// 1. setting.json 中记录的待清理目录（删除项目文件夹失败等遗留，重试删除）
    /// 2. ProjectsDate 下的空文件夹（空项目目录 + 空卡片文件夹 Card_*）
    /// 删除成功从记录移除；再次失败保留记录，下次启动继续重试。
    /// </summary>
    public static class StartupCleanupService
    {
        /// <summary>
        /// 执行启动清理（应在界面加载完成后调用，内部放后台线程执行不阻塞 UI）。
        /// </summary>
        public static void Run(string rootPath)
        {
            try
            {
                var setting = new SettingFileService(rootPath);

                // 1. 处理记录的待清理目录（PendingDeleteDirs getter 内部已加锁，直接取副本）
                foreach (var dir in setting.PendingDeleteDirs.ToList())
                {
                    TryDeleteDir(dir, setting, "记录的待清理目录");
                }

                // 2. 扫描 ProjectsDate 下的空文件夹
                ScanEmptyDirs(rootPath, setting);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "启动清理执行失败（不影响程序使用）");
            }
        }

        /// <summary>扫描 ProjectsDate：空项目目录 + 项目内空卡片文件夹</summary>
        private static void ScanEmptyDirs(string rootPath, SettingFileService setting)
        {
            string projectsRoot = Path.Combine(rootPath, "ProjectsDate");
            if (!Directory.Exists(projectsRoot)) return;

            // master 已注册的项目 ID 集合（有记录的项目目录即使为空也不删，防误删刚建项目）
            var config = new ConfigManager(rootPath);
            var registeredIds = new HashSet<string>(config.GetProjectList().Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

            foreach (var projDir in Directory.GetDirectories(projectsRoot))
            {
                string projName = Path.GetFileName(projDir);
                if (!projName.StartsWith("Project_", StringComparison.OrdinalIgnoreCase)) continue;

                string id = projName.Substring("Project_".Length);

                // 2a. master 无记录且目录为空 → 孤儿空项目目录
                if (!registeredIds.Contains(id))
                {
                    if (IsDirEmptyRecursive(projDir))
                    {
                        TryDeleteDir(projDir, setting, "无记录的孤儿空项目目录");
                    }
                    // 有数据的孤儿目录（如旧测试残留副本）不在自动清理范围，留待用户在管理窗口处理
                    continue;
                }

                // 2b. 已注册项目内：递归查找空卡片文件夹 Card_*（无任何文件）
                foreach (var cardDir in Directory.GetDirectories(projDir, "Card_*", SearchOption.AllDirectories))
                {
                    if (IsDirEmptyRecursive(cardDir))
                    {
                        TryDeleteDir(cardDir, setting, "空卡片文件夹");
                    }
                }
            }
        }

        /// <summary>目录递归是否完全为空（无任何文件，含子目录）</summary>
        private static bool IsDirEmptyRecursive(string dir)
        {
            try
            {
                return !Directory.EnumerateFileSystemEntries(dir).Any()
                    && !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试删除目录（删除前释放 data.db 连接池句柄）；成功则移除记录，失败保留记录下次重试。
        /// </summary>
        private static void TryDeleteDir(string dir, SettingFileService setting, string reason)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    // 目录已不存在（可能已手动删除），直接清记录
                    Log.Information("[启动清理] 目录已不存在，移除记录: {Dir}（{Reason}）", dir, reason);
                    setting.RemovePendingDeleteDir(dir);
                    return;
                }

                // 先释放 data.db 连接池句柄，避免 Windows 文件占用导致删除失败
                ReleaseDatabaseHandle(dir);

                Directory.Delete(dir, true);
                Log.Information("[启动清理] 已删除 {Reason}: {Dir}", reason, dir);
                setting.RemovePendingDeleteDir(dir);
            }
            catch (Exception ex)
            {
                // 仍失败：确保留在待清理记录中，下次启动重试
                setting.AddPendingDeleteDir(dir);
                Log.Warning(ex, "[启动清理] 删除失败（保留记录下次重试）: {Dir}", dir);
            }
        }

        /// <summary>清空目录内 data.db 的连接池，释放文件句柄</summary>
        private static void ReleaseDatabaseHandle(string projectDir)
        {
            string dbPath = Path.Combine(projectDir, "data.db");
            if (!File.Exists(dbPath)) return;
            try
            {
                using var probe = new SqliteConnection($"Data Source={dbPath}");
                SqliteConnection.ClearPool(probe);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[启动清理] 释放 data.db 句柄失败: {Db}", dbPath);
            }
        }
    }
}
