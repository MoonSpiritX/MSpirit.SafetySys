// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MS_SafetyManagementSystem.Models;   // 添加这一行

namespace MS_SafetyManagementSystem.Services
{
    public class MasterDbContext
    {
        private readonly string _dbPath;
        private const string CreateTablesSql = @"
            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Created TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
        ";

        public MasterDbContext(string rootPath)
        {
            // 2026-08-10 目录调整：master.db 移入 ProjectsDate（与各项目库同层），Data 目录已废弃
            var dataDir = Path.Combine(rootPath, "ProjectsDate");
            Directory.CreateDirectory(dataDir);
            _dbPath = Path.Combine(dataDir, "master.db");
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            try
            {
                bool exists = File.Exists(_dbPath);
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();
                if (!exists)
                {
                    using var cmd = new SqliteCommand(CreateTablesSql, connection);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    // 检查是否存在旧的 Order 列，若存在则重命名为 SortOrder
                    try
                    {
                        // 查询表结构，看是否有列名为 Order
                        using var cmd = new SqliteCommand("PRAGMA table_info(Projects);", connection);
                        using var reader = cmd.ExecuteReader();
                        bool hasOrderColumn = false;
                        while (reader.Read())
                        {
                            string colName = reader.GetString(1);
                            if (colName == "Order")
                            {
                                hasOrderColumn = true;
                                break;
                            }
                        }
                        if (hasOrderColumn)
                        {
                            using var renameCmd = new SqliteCommand("ALTER TABLE Projects RENAME COLUMN [Order] TO SortOrder;", connection);
                            renameCmd.ExecuteNonQuery();
                            Log.Information("已将 Projects 表中的 Order 列重命名为 SortOrder");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "重命名列 Order 失败，可能列不存在或已重命名");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "确保主数据库失败，路径: {DbPath}", _dbPath);
                throw;
            }
        }

        public SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            return conn;
        }

        // 注意：此方法可能属于 ProjectDbContext，但保留在此处以兼容已有代码
        public List<Attachment> GetAttachmentsByRecordIds(List<string> recordIds)
        {
            if (recordIds == null || recordIds.Count == 0) return new List<Attachment>();
            using var conn = OpenConnection();
            var ids = string.Join(",", recordIds.Select(id => $"'{id}'"));
            var sql = $"SELECT * FROM Attachments WHERE RecordId IN ({ids})";
            return conn.Query<Attachment>(sql).ToList();
        }

        #region Projects
        public List<ProjectInfo> GetAllProjects()
        {
            try
            {
                using var conn = OpenConnection();
                return conn.Query<ProjectInfo>("SELECT Id, Name, Created, SortOrder FROM Projects ORDER BY SortOrder").ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取所有项目失败");
                throw;
            }
        }

        public ProjectInfo? GetProjectById(string id)
        {
            try
            {
                using var conn = OpenConnection();
                return conn.QueryFirstOrDefault<ProjectInfo>("SELECT Id, Name, Created, SortOrder FROM Projects WHERE Id = @Id", new { Id = id });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "根据Id获取项目失败, Id: {Id}", id);
                throw;
            }
        }

        public bool AddProject(ProjectInfo project)
        {
            try
            {
                using var conn = OpenConnection();
                int maxOrder = conn.ExecuteScalar<int>("SELECT COALESCE(MAX(SortOrder), -1) FROM Projects");
                project.SortOrder = maxOrder + 1;
                int rows = conn.Execute("INSERT INTO Projects (Id, Name, Created, SortOrder) VALUES (@Id, @Name, @Created, @SortOrder)", project);
                return rows > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "添加项目失败, Name: {Name}", project.Name);
                return false;
            }
        }

        public bool UpdateProject(ProjectInfo project)
        {
            try
            {
                using var conn = OpenConnection();
                int rows = conn.Execute("UPDATE Projects SET Name = @Name, Created = @Created, SortOrder = @SortOrder WHERE Id = @Id", project);
                return rows > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "更新项目失败, Id: {Id}", project.Id);
                return false;
            }
        }

        public bool DeleteProject(string id)
        {
            try
            {
                using var conn = OpenConnection();
                int rows = conn.Execute("DELETE FROM Projects WHERE Id = @Id", new { Id = id });
                return rows > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "删除项目失败, Id: {Id}", id);
                return false;
            }
        }

        public void ReorderProjects(List<string> orderedIds)
        {
            if (orderedIds == null || orderedIds.Count == 0)
                return;

            try
            {
                using var conn = OpenConnection();
                using var transaction = conn.BeginTransaction();
                for (int i = 0; i < orderedIds.Count; i++)
                {
                    conn.Execute("UPDATE Projects SET SortOrder = @Order WHERE Id = @Id", new { Order = i, Id = orderedIds[i] }, transaction);
                }
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "重新排序项目失败, orderedIds: {Ids}", string.Join(",", orderedIds));
                throw;
            }
        }
        #endregion

        #region Settings
        public string? GetSetting(string key)
        {
            try
            {
                using var conn = OpenConnection();
                return conn.QueryFirstOrDefault<string?>("SELECT Value FROM Settings WHERE Key = @Key", new { Key = key });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取设置失败, Key: {Key}", key);
                return null;
            }
        }

        public void SetSetting(string key, string value)
        {
            try
            {
                using var conn = OpenConnection();
                var exists = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Settings WHERE Key = @Key", new { Key = key }) > 0;
                if (exists)
                    conn.Execute("UPDATE Settings SET Value = @Value WHERE Key = @Key", new { Value = value, Key = key });
                else
                    conn.Execute("INSERT INTO Settings (Key, Value) VALUES (@Key, @Value)", new { Key = key, Value = value });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存设置失败, Key: {Key}, Value: {Value}", key, value);
            }
        }
        #endregion
    }
}