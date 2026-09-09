// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 项目管理器：负责项目列表的增删改查、排序、当前项目存储，并提供项目相关路径。
    /// 底层使用 SQLite (master.db) 存储项目列表。
    /// </summary>
    public class ConfigManager
    {
        private readonly string _rootPath;
        private readonly string _projectsDataFolder;
        private readonly MasterDbContext _masterDb;
        public event Action<string>? CurrentProjectWillBeDeleted; 

        public ConfigManager(string rootPath)
        {
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            _projectsDataFolder = Path.Combine(_rootPath, "ProjectsDate");
            Directory.CreateDirectory(_projectsDataFolder);

            _masterDb = new MasterDbContext(rootPath);
        }
        public bool ValidateCurrentProject()
        {
            string currentId = GetCurrentProjectId();
            if (string.IsNullOrEmpty(currentId))
            {
                // 没有当前项目，尝试获取第一个项目
                var projects = GetProjectList();
                if (projects.Count == 0)
                    return false;
                SetCurrentProjectId(projects[0].Id);
                return true;
            }

            // 检查文件夹是否存在
            string projectFolder = GetProjectFolder(currentId);
            if (!Directory.Exists(projectFolder))
            {
                // 物理文件夹丢失，删除数据库记录
                CurrentProjectWillBeDeleted?.Invoke(currentId);
                _masterDb.DeleteProject(currentId);

                var remaining = GetProjectList();
                if (remaining.Count == 0)
                {
                    SetCurrentProjectId("");
                    return false;
                }
                else
                {
                    SetCurrentProjectId(remaining[0].Id);
                    return true;
                }
            }
            return true;
        }


        public List<ProjectInfo> GetProjectList()
        {
            return _masterDb.GetAllProjects();
        }

        public ProjectInfo? GetProjectById(string id)
        {
            return _masterDb.GetProjectById(id);
        }

        public bool AddProject(string name)
        {
            // 2026-08-15 修复：先 Trim 再判重，避免"同名项目 "（尾随空格）绕过重名检查创建重名项目
            name = name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (_masterDb.GetAllProjects().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return false;

            string id = Guid.NewGuid().ToString("N");
            var newProject = new ProjectInfo
            {
                Id = id,
                Name = name.Trim(),
                Created = DateTime.Now.ToString("yyyy-MM-dd"),
                SortOrder = 0
            };

            if (!_masterDb.AddProject(newProject))
                return false;

            string projectFolder = GetProjectFolder(id);
            Directory.CreateDirectory(projectFolder);
            var _ = new ProjectDbContext(projectFolder);
            string cacheFolder = GetProjectCacheFolder(id);
            Directory.CreateDirectory(cacheFolder);

            return true;
        }

        /// <summary>
        /// 以指定 ID 注册项目记录（存档导入用）：ID 已存在或名称重复时返回 false。
        /// 仅建记录与文件夹，不建空库（导入方会用包内 data.db 覆盖）。
        /// </summary>
        public bool AddProjectRecord(string id, string name, string created)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return false;
            if (_masterDb.GetProjectById(id) != null) return false;
            if (_masterDb.GetAllProjects().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return false;

            var proj = new ProjectInfo
            {
                Id = id,
                Name = name.Trim(),
                Created = string.IsNullOrWhiteSpace(created) ? DateTime.Now.ToString("yyyy-MM-dd") : created,
                SortOrder = 0
            };
            if (!_masterDb.AddProject(proj)) return false;

            Directory.CreateDirectory(GetProjectFolder(id));
            return true;
        }

        public bool DeleteProject(string id)
        {
            var project = GetProjectById(id);
            if (project == null) return false;

            int deletedIndex = GetProjectList().FindIndex(p => p.Id == id);
            bool isCurrentProject = (_masterDb.GetSetting("CurrentProjectId") == id);

            if (isCurrentProject)
            {
                CurrentProjectWillBeDeleted?.Invoke(id);
            }

            if (!_masterDb.DeleteProject(id))
                return false;

            string projectFolder = GetProjectFolder(id);
            if (Directory.Exists(projectFolder))
            {
                string dbPath = Path.Combine(projectFolder, "data.db");
                if (File.Exists(dbPath))
                {
                    // 2026-08-15 修复：释放 data.db 句柄（此前 tempConn 用默认连接池，
                    // Open/Close 后连接回池仍持有文件句柄 → Directory.Delete 必然失败，
                    // 文件夹只能等下次启动清理。现先 ClearPool 清残留池连接，再用
                    // Pooling=False 连接执行 checkpoint，Close 后立即关闭句柄）
                    try
                    {
                        using (var probe = new SqliteConnection($"Data Source={dbPath}"))
                        {
                            SqliteConnection.ClearPool(probe);
                        }
                        using var tempConn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                        tempConn.Open();
                        using var cmd = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", tempConn);
                        cmd.ExecuteNonQuery();
                        tempConn.Close();
                    }
                    catch { /* 连接池清理失败不影响删除流程 */ }
                    // 等待一下，确保文件句柄释放
                    System.Threading.Thread.Sleep(50);
                }
                // 删除文件夹
                try { Directory.Delete(projectFolder, true); }
                catch (Exception ex)
                {
                    // 删除失败（通常是 data.db/WAL 句柄被占用）：记录到 setting.json，启动时重试清理
                    Log.Error(ex, "删除项目文件夹失败，已登记待清理: {Folder}", projectFolder);
                    try { new SettingFileService(_rootPath).AddPendingDeleteDir(projectFolder); }
                    catch (Exception ex2) { Log.Warning(ex2, "登记待清理目录失败"); }
                }
            }

            if (isCurrentProject)
            {
                var remaining = GetProjectList();
                if (remaining.Count == 0)
                {
                    _masterDb.SetSetting("CurrentProjectId", "");
                }
                else
                {
                    int newIndex = (deletedIndex < remaining.Count) ? deletedIndex : remaining.Count - 1;
                    _masterDb.SetSetting("CurrentProjectId", remaining[newIndex].Id);
                }
            }

            return true;
        }

        public bool RenameProject(string id, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return false;
            var project = GetProjectById(id);
            if (project == null) return false;
            if (GetProjectList().Any(p => p.Id != id && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return false;

            project.Name = newName.Trim();
            return _masterDb.UpdateProject(project);
        }

        private void ReorderProjects(List<string> orderedIds)
        {
            _masterDb.ReorderProjects(orderedIds);
        }

        public void MoveProject(string id, int delta)
        {
            var projects = GetProjectList();
            int idx = projects.FindIndex(p => p.Id == id);
            if (idx < 0) return;
            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= projects.Count) return;

            var orderedIds = projects.Select(p => p.Id).ToList();
            var item = orderedIds[idx];
            orderedIds.RemoveAt(idx);
            orderedIds.Insert(newIdx, item);
            ReorderProjects(orderedIds);
        }

        public void MoveToTop(string id)
        {
            var projects = GetProjectList();
            int idx = projects.FindIndex(p => p.Id == id);
            if (idx <= 0) return;
            var orderedIds = projects.Select(p => p.Id).ToList();
            var item = orderedIds[idx];
            orderedIds.RemoveAt(idx);
            orderedIds.Insert(0, item);
            ReorderProjects(orderedIds);
        }

        public void MoveToBottom(string id)
        {
            var projects = GetProjectList();
            int idx = projects.FindIndex(p => p.Id == id);
            if (idx < 0 || idx == projects.Count - 1) return;
            var orderedIds = projects.Select(p => p.Id).ToList();
            var item = orderedIds[idx];
            orderedIds.RemoveAt(idx);
            orderedIds.Add(item);
            ReorderProjects(orderedIds);
        }

        public string GetCurrentProjectId()
        {
            return _masterDb.GetSetting("CurrentProjectId") ?? "";
        }

        public void SetCurrentProjectId(string id)
        {
            if (string.IsNullOrEmpty(id) || GetProjectById(id) != null)
                _masterDb.SetSetting("CurrentProjectId", id);
            else
                _masterDb.SetSetting("CurrentProjectId", "");
        }

        public string GetProjectFolder(string id)
        {
            return Path.Combine(_projectsDataFolder, $"Project_{id}");
        }

        public string GetModuleDataPath(string id, string moduleName)
        {
            // 保留此方法以兼容旧调用，实际不再使用 JSON 文件
            return Path.Combine(GetProjectFolder(id), $"{moduleName}.json");
        }

        public string GetProjectCacheFolder(string id)
        {
            return Path.Combine(GetProjectFolder(id), "Cache");
        }
    }

    public class ProjectInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Created { get; set; } = "";
        public int SortOrder { get; set; }  // 原 Order 改为 SortOrder
    }
}
