// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using Microsoft.Data.Sqlite;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    public static class DatabaseHelper
    {
        private const int MaxRetryCount = 3;
        private const int RetryDelayMs = 100;

        public static void ExecuteWithRetry(Action<SqliteConnection> action, string dbPath, string operationName)
        {
            int retry = 0;
            while (true)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath}");
                    conn.Open();
                    action(conn);
                    return;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && retry < MaxRetryCount) // 5 = SQLITE_BUSY
                {
                    retry++;
                    Log.Warning(ex, "数据库忙，正在重试 ({Retry}/{Max}) 操作: {Operation}", retry, MaxRetryCount, operationName);
                    Thread.Sleep(RetryDelayMs);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "数据库操作失败: {Operation}", operationName);
                    throw;
                }
            }
        }
    }
}