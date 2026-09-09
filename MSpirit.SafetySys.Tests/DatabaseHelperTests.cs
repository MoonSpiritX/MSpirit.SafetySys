// DatabaseHelper 测试：SQLITE_BUSY 重试与异常透传（B 层数据安全）
// 用 SqliteException.ThrowExceptionForRC(5) 模拟 BUSY，避免真实锁竞争
// （Microsoft.Data.Sqlite 默认 30s busy_timeout 会让真实锁测试变慢/不可控）

using Dapper;
using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class DatabaseHelperTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;

    public DatabaseHelperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "DbHelper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, Value TEXT);");
        conn.Execute("INSERT INTO T (Value) VALUES ('初始');");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    /// <summary>构造一个 SQLITE_BUSY 异常（错误码 5）</summary>
    private static SqliteException BusyException() =>
        Assert.Throws<SqliteException>(() => SqliteException.ThrowExceptionForRC(5, null));

    [Fact]
    public void ExecuteWithRetry_正常执行_一次性成功()
    {
        bool called = false;
        DatabaseHelper.ExecuteWithRetry(conn =>
        {
            called = true;
            conn.Execute("INSERT INTO T (Value) VALUES ('x');");
        }, _dbPath, "测试操作");

        Assert.True(called);
        using var check = new SqliteConnection($"Data Source={_dbPath}");
        check.Open();
        Assert.Equal(2, check.ExecuteScalar<int>("SELECT COUNT(*) FROM T;"));
    }

    [Fact]
    public void ExecuteWithRetry_数据库忙_重试后成功()
    {
        int attempts = 0;
        DatabaseHelper.ExecuteWithRetry(conn =>
        {
            attempts++;
            if (attempts == 1)
                throw BusyException(); // 第一次 BUSY → 触发重试
            conn.Execute("INSERT INTO T (Value) VALUES ('重试后写入');");
        }, _dbPath, "busy测试");

        Assert.True(attempts >= 2, $"应至少重试一次（实际 {attempts} 次）");

        using var check = new SqliteConnection($"Data Source={_dbPath}");
        check.Open();
        Assert.Equal(2, check.ExecuteScalar<int>("SELECT COUNT(*) FROM T;"));
    }

    [Fact]
    public void ExecuteWithRetry_重试超限_抛出BUSY()
    {
        int attempts = 0;
        var ex = Assert.Throws<SqliteException>(() =>
            DatabaseHelper.ExecuteWithRetry(conn =>
            {
                attempts++;
                throw BusyException(); // 持续 BUSY
            }, _dbPath, "持续锁测试"));

        Assert.Equal(5, ex.SqliteErrorCode); // SQLITE_BUSY
        Assert.Equal(4, attempts); // 1 次初始 + 3 次重试
    }

    [Fact]
    public void ExecuteWithRetry_非BUSY异常_直接抛出不重试()
    {
        int attempts = 0;
        // 打开一个不存在的库路径 → conn.Open() 抛非 BUSY 异常（SQLITE_CANTOPEN），不重试直接抛出
        Assert.ThrowsAny<System.Exception>(() =>
            DatabaseHelper.ExecuteWithRetry(conn =>
            {
                attempts++;
                conn.Execute("SELECT 1;");
            }, Path.Combine(_root, "不存在", "x.db"), "坏路径操作"));

        Assert.Equal(0, attempts); // 连接打开即失败，action 未执行、不重试
    }
}
