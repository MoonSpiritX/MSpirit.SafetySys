// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 数据根定位器：解决"拔盘 / 盘符变化 / 环境漂移导致数据根找不到"的问题（2026-08-15）。
    /// 启动决策链（逐层降级，只有前一层失败才触发后一层）：
    ///   1️⃣ 上次记录：%LOCALAPPDATA%\MSpirit.SafetySys\DataRoot.txt（独立于数据根，不随数据丢失）
    ///       → 有效（目录存在 + 含提示文件/master.db/ProjectsDate）→ 直接用；
    ///   2️⃣ 浅扫：所有固定盘根 + LOCALAPPDATA 兜底位置找数据根 → 找到即用（盘符漂移自动恢复）；
    ///   3️⃣ 无记录（首启）→ 用默认路径并写记录，静默初始化；
    ///   4️⃣ 记录存在但失效 + 浅扫无果 → 返回 null，由 UI 弹窗引导（手动选择 / 全盘深扫 / 使用新位置）。
    /// 深扫（SearchDeepAsync）只由用户主动触发（异步、可取消、多结果选择），避免启动阻塞与误匹配。
    /// </summary>
    public static class DataRootLocator
    {
        /// <summary>记录文件所在目录名（位于 %LOCALAPPDATA% 下，独立于数据根）</summary>
        public const string RecordFolderName = "MSpirit.SafetySys";

        /// <summary>记录文件名</summary>
        public const string RecordFileName = "DataRoot.txt";

        /// <summary>记录文件默认完整路径</summary>
        public static string RecordFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            RecordFolderName, RecordFileName);

        // ==================== 记录读写 ====================

        public static string? ReadRecordedRoot(string? recordFilePath = null)
        {
            try
            {
                string path = recordFilePath ?? RecordFilePath;
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[DataRoot] 读取数据根记录失败（按无记录处理）");
                return null;
            }
        }

        public static void WriteRecordedRoot(string rootPath, string? recordFilePath = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rootPath)) return;
                string path = recordFilePath ?? RecordFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, rootPath.Trim());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[DataRoot] 写入数据根记录失败: {Root}", rootPath);
            }
        }

        // ==================== 有效性校验 ====================

        /// <summary>
        /// 校验目录是否为有效数据根：目录存在，且含"数据根身份证"——
        /// 提示文件（此目录为斜月安全管理系统存档目录，请勿手动操作）、master.db 或 ProjectsDate 子目录任一。
        /// </summary>
        public static bool IsValidDataRoot(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, DataPathService.DataRootNoticeFileName))
                || File.Exists(Path.Combine(dir, "ProjectsDate", "master.db"))
                || Directory.Exists(Path.Combine(dir, "ProjectsDate"));
        }

        // ==================== 浅扫（固定盘根 + LOCALAPPDATA 兜底位置） ====================

        /// <summary>
        /// 浅扫所有"默认可能放置数据根"的位置：每个就绪固定盘根目录 + LOCALAPPDATA 兜底位置。
        /// 覆盖当前产品形态下的全部真实场景（数据根只可能在这两处，除非用户手动移动）。
        /// </summary>
        public static List<string> FindCandidateRoots()
        {
            var result = new List<string>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                string candidate = Path.Combine(drive.Name, DataPathService.DataRootFolderName);
                if (IsValidDataRoot(candidate) && !result.Contains(candidate))
                    result.Add(candidate);
            }

            // LOCALAPPDATA 兜底位置（老版本数据在此；新插盘导致算法漂移后仍能找到老数据）
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string fallback = Path.Combine(localAppData, DataPathService.DataRootFolderName);
            if (IsValidDataRoot(fallback) && !result.Contains(fallback))
                result.Add(fallback);

            return result;
        }

        // ==================== 深扫（全盘递归，用户主动触发） ====================

        /// <summary>
        /// 全盘深扫：在所有就绪固定盘（含系统盘，排除可移动/网络盘）递归查找数据根目录。
        /// 异步、可取消；进度按 (已完成盘数, 总盘数) 上报。权限拒绝目录自动跳过。
        /// driveRoots 可注入扫描根集合（测试指定临时目录用）；null 时默认枚举所有固定盘。
        /// </summary>
        public static async Task<List<string>> SearchDeepAsync(CancellationToken ct, IProgress<(int Done, int Total)>? progress = null, IEnumerable<string>? driveRoots = null)
        {
            var roots = driveRoots?.ToList()
                ?? DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

            var found = new List<string>();
            int done = 0;
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                var results = await Task.Run(() => SearchDirIterative(new DirectoryInfo(root), ct), ct);
                foreach (var r in results)
                {
                    if (!found.Contains(r)) found.Add(r);
                }
                done++;
                progress?.Report((done, roots.Count));
            }
            return found;
        }

        /// <summary>迭代式深度遍历（避免深目录树栈溢出），只收集目录名为数据根名且有效的目录</summary>
        private static List<string> SearchDirIterative(DirectoryInfo root, CancellationToken ct)
        {
            var result = new List<string>();
            var stack = new Stack<DirectoryInfo>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                try
                {
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.Equals(sub.Name, DataPathService.DataRootFolderName, StringComparison.OrdinalIgnoreCase)
                            && IsValidDataRoot(sub.FullName))
                        {
                            result.Add(sub.FullName);
                            // 找到数据根后不深入其内部
                        }
                        else
                        {
                            stack.Push(sub);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { /* 权限拒绝目录跳过 */ }
                catch (IOException) { /* 占用/移除中的目录跳过 */ }
            }
            return result;
        }

        // ==================== 启动决策链 ====================

        /// <summary>
        /// 启动决策主链。返回最终数据根路径；返回 null 表示"疑似丢失"（有记录但失效且浅扫无果），需 UI 引导。
        /// </summary>
        public static string? ResolveRootPath()
        {
            return ResolveRootPath(FindCandidateRoots(), RecordFilePath);
        }

        /// <summary>
        /// 决策链（candidates / recordFilePath 可注入，测试用；生产走无参重载）。
        /// </summary>
        public static string? ResolveRootPath(IReadOnlyList<string> candidates, string? recordFilePath = null)
        {
            // 1️⃣ 上次记录有效 → 直接用（覆盖绝大多数正常启动）
            string? recorded = ReadRecordedRoot(recordFilePath);
            if (!string.IsNullOrEmpty(recorded) && IsValidDataRoot(recorded))
                return recorded;

            // 2️⃣ 浅扫命中 → 盘符漂移/新盘介入自动恢复，更新记录
            if (candidates != null && candidates.Count > 0)
            {
                string chosen = PickBestCandidate(candidates);
                WriteRecordedRoot(chosen, recordFilePath);
                return chosen;
            }

            // 3️⃣ 无记录 → 首启，用默认路径并写记录（静默初始化，不打扰）
            if (string.IsNullOrEmpty(recorded))
            {
                string defaultRoot = DataPathService.DefaultRootPath();
                WriteRecordedRoot(defaultRoot, recordFilePath);
                return defaultRoot;
            }

            // 4️⃣ 记录存在但失效 + 浅扫无果 → 疑似丢失
            return null;
        }

        /// <summary>多候选选择：带提示文件的优先；同级按 master.db 最后修改时间取新（拷贝残留中挑"最像真身"的）</summary>
        public static string PickBestCandidate(IReadOnlyList<string> candidates)
        {
            if (candidates == null || candidates.Count == 0) throw new ArgumentException("候选列表为空");
            if (candidates.Count == 1) return candidates[0];

            return candidates
                .OrderByDescending(c => File.Exists(Path.Combine(c, DataPathService.DataRootNoticeFileName)))
                .ThenByDescending(GetMasterDbLastWrite)
                .First();
        }

        private static DateTime GetMasterDbLastWrite(string dir)
        {
            string db = Path.Combine(dir, "ProjectsDate", "master.db");
            try
            {
                return File.Exists(db) ? File.GetLastWriteTime(db) : DateTime.MinValue;
            }
            catch
            {
                Log.Debug("[DataRoot] 读取 master.db 修改时间失败: {Dir}", dir);
                return DateTime.MinValue;
            }
        }
    }
}
