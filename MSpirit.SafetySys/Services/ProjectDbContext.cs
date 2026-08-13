using Dapper;
using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;
using MS_SafetyManagementSystem.Common;

namespace MS_SafetyManagementSystem.Services
{
    public class ProjectDbContext : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _projectId;
        private bool _walEnabled = false; // 确保只开启一次
        private bool _disposed = false;

        public string GetDbPath() => _dbPath;

        private const string CreateTablesSql = @"
            CREATE TABLE IF NOT EXISTS Groups (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                Module TEXT NOT NULL,
                ParentId TEXT NULL,
                Name TEXT NOT NULL,
                [Order] INTEGER NOT NULL,
                CreatedDate TEXT,
                Tag TEXT,
                FOREIGN KEY (ParentId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Records (
                Id TEXT PRIMARY KEY,
                GroupId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Content TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                DueDate TEXT,
                Status TEXT,
                ExtraData TEXT,
                ResponsibleUnit TEXT,
                ResponsiblePerson TEXT,
                Location TEXT,
                HazardType TEXT,
                HazardLevel TEXT,
                Deadline TEXT,
                CompletedDate TEXT,
                Reviewer TEXT,
                Description TEXT,
                Measure TEXT,
                YearMonth TEXT,
                DisplayIndex INTEGER,
                FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Attachments (
                Id TEXT PRIMARY KEY,
                RecordId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                FileSize INTEGER,
                MimeType TEXT,
                UploadedAt TEXT,
                AttachmentType TEXT,
                DisplayOrder INTEGER,
                ThumbnailPath TEXT,
                CertNumber TEXT,
                CertStart TEXT,
                CertEnd TEXT,
                CertName TEXT
            );

            CREATE TABLE IF NOT EXISTS PersonnelRecords (
                Id TEXT PRIMARY KEY,
                GroupId TEXT NOT NULL,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                DisplayIndex INTEGER NOT NULL DEFAULT 0,
                YearMonth TEXT,
                Gender TEXT,
                Age TEXT,
                EmployeeID TEXT,
                Position TEXT,
                PhoneNumber TEXT,
                EducationLevel TEXT,
                NativePlace TEXT,
                HomeAddress TEXT,
                BirthDate TEXT,
                EntryTime TEXT,
                EduTime TEXT,
                ExitTime TEXT,
                ThreeCategoryPersonnel INTEGER DEFAULT 0,
                SpecialOperationsPersonnel INTEGER DEFAULT 0,
                Remark TEXT,
                IDCardDocNumber TEXT,
                CertDocNumber TEXT,
                IDCardStart TEXT,
                IDCardEnd TEXT,
                CertStart TEXT,
                CertEnd TEXT,
                CertName TEXT,
                Cert1Name TEXT, Cert1DocNumber TEXT, Cert1Start TEXT, Cert1End TEXT,
                Cert2Name TEXT, Cert2DocNumber TEXT, Cert2Start TEXT, Cert2End TEXT,
                Cert3Name TEXT, Cert3DocNumber TEXT, Cert3Start TEXT, Cert3End TEXT,
                Cert4Name TEXT, Cert4DocNumber TEXT, Cert4Start TEXT, Cert4End TEXT,
                Cert5Name TEXT, Cert5DocNumber TEXT, Cert5Start TEXT, Cert5End TEXT,
                FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE INDEX idx_groups_project_module ON Groups(ProjectId, Module);
            CREATE INDEX idx_records_group ON Records(GroupId);
            CREATE INDEX idx_attachments_record ON Attachments(RecordId);
            CREATE INDEX idx_personnel_group ON PersonnelRecords(GroupId);
        ";

        public ProjectDbContext(string projectFolderPath, string projectId)
        {
            _projectId = projectId;
            _dbPath = Path.Combine(projectFolderPath, "data.db");
            EnsureDatabase();
            EnableWalMode(); // 开启 WAL 模式
        }

        // 保留无 projectId 的构造函数（兼容旧代码），但此时 _projectId 为空字符串
        public ProjectDbContext(string projectFolderPath) : this(projectFolderPath, "")
        {
        }

        /// <summary>
        /// 安全获取 ProjectsDate 根目录（附件存储根目录）
        /// </summary>
        /// <exception cref="InvalidOperationException">路径无效时抛出</exception>
        private string GetProjectsDateRoot()
        {
            string? dbPath = GetDbPath();
            if (string.IsNullOrEmpty(dbPath))
                throw new InvalidOperationException("数据库路径为空，无法获取根目录。");

            string? projectFolder = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(projectFolder))
                throw new InvalidOperationException("无法获取项目文件夹路径。");

            string? projectsDateRoot = Path.GetDirectoryName(projectFolder);
            if (string.IsNullOrEmpty(projectsDateRoot))
                throw new InvalidOperationException("无法获取 ProjectsDate 根目录。");

            return projectsDateRoot;
        }

        public void Dispose()
        {
            // 当前没有需要显式释放的资源
        }

        public static void ClearConnectionPool(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            SqliteConnection.ClearPool(conn);
        }

        private void EnsureDatabase()
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
                // 检查并添加 Groups 表的 Tag 列
                using var cmd = new SqliteCommand("PRAGMA table_info(Groups);", connection);
                using var reader = cmd.ExecuteReader();
                bool hasTagColumn = false;
                while (reader.Read())
                {
                    if (reader.GetString(1) == "Tag")
                    {
                        hasTagColumn = true;
                        break;
                    }
                }
                if (!hasTagColumn)
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Groups ADD COLUMN Tag TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Groups 表添加 Tag 列");
                }

                // 检查并添加 Records 表的列
                using var cmdColumns = new SqliteCommand("PRAGMA table_info(Records);", connection);
                using var readerColumns = cmdColumns.ExecuteReader();
                var existingColumns = new List<string>();
                while (readerColumns.Read())
                {
                    existingColumns.Add(readerColumns.GetString(1));
                }

                var columnsToAdd = new Dictionary<string, string>
        {
            { "ResponsibleUnit", "TEXT" },
            { "ResponsiblePerson", "TEXT" },
            { "Location", "TEXT" },
            { "HazardType", "TEXT" },
            { "HazardLevel", "TEXT" },
            { "Deadline", "TEXT" },
            { "CompletedDate", "TEXT" },
            { "Reviewer", "TEXT" },
            { "Description", "TEXT" },
            { "Measure", "TEXT" }
        };
                foreach (var col in columnsToAdd)
                {
                    if (!existingColumns.Contains(col.Key))
                    {
                        using var alterCmd = new SqliteCommand($"ALTER TABLE Records ADD COLUMN {col.Key} {col.Value};", connection);
                        alterCmd.ExecuteNonQuery();
                        Log.Information($"已为 Records 表添加列 {col.Key}");
                    }
                }

                // 检查并添加 Records 表的 YearMonth 列
                if (!existingColumns.Contains("YearMonth"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Records ADD COLUMN YearMonth TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Records 表添加 YearMonth 列");
                }

                // 检查并添加 Records 表的 DisplayIndex 列
                if (!existingColumns.Contains("DisplayIndex"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Records ADD COLUMN DisplayIndex INTEGER;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Records 表添加 DisplayIndex 列");
                }

                // 检查 Attachments 表的列
                using var cmdAttach = new SqliteCommand("PRAGMA table_info(Attachments);", connection);
                using var readerAttach = cmdAttach.ExecuteReader();
                var existingAttachColumns = new List<string>();
                while (readerAttach.Read())
                {
                    existingAttachColumns.Add(readerAttach.GetString(1));
                }

                if (!existingAttachColumns.Contains("AttachmentType"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN AttachmentType TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 AttachmentType 列");
                }
                if (!existingAttachColumns.Contains("DisplayOrder"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN DisplayOrder INTEGER;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 DisplayOrder 列");
                }
                if (!existingAttachColumns.Contains("ThumbnailPath"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN ThumbnailPath TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 ThumbnailPath 列");
                }
                if (!existingAttachColumns.Contains("CertNumber"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN CertNumber TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 CertNumber 列");
                }
                if (!existingAttachColumns.Contains("CertStart"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN CertStart TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 CertStart 列");
                }
                if (!existingAttachColumns.Contains("CertEnd"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN CertEnd TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 CertEnd 列");
                }
                if (!existingAttachColumns.Contains("CertName"))
                {
                    using var alterCmd = new SqliteCommand("ALTER TABLE Attachments ADD COLUMN CertName TEXT;", connection);
                    alterCmd.ExecuteNonQuery();
                    Log.Information("已为 Attachments 表添加 CertName 列");
                }

                // 检查 PersonnelRecords 表是否存在
                using var cmdPersonnel = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='PersonnelRecords';", connection);
                var personnelTableExists = cmdPersonnel.ExecuteScalar() != null;
                if (!personnelTableExists)
                {
                    using var createCmd = new SqliteCommand(@"
                        CREATE TABLE IF NOT EXISTS PersonnelRecords (
                            Id TEXT PRIMARY KEY,
                            GroupId TEXT NOT NULL,
                            Title TEXT NOT NULL,
                            CreatedAt TEXT NOT NULL,
                            UpdatedAt TEXT,
                            DisplayIndex INTEGER NOT NULL DEFAULT 0,
                            YearMonth TEXT,
                            Gender TEXT, Age TEXT, EmployeeID TEXT,
                            Position TEXT, PhoneNumber TEXT,
                            EducationLevel TEXT, NativePlace TEXT, HomeAddress TEXT, BirthDate TEXT,
                            EntryTime TEXT, EduTime TEXT, ExitTime TEXT,
                            ThreeCategoryPersonnel INTEGER DEFAULT 0,
                            SpecialOperationsPersonnel INTEGER DEFAULT 0,
                            Remark TEXT,
                            IDCardDocNumber TEXT, CertDocNumber TEXT,
                            IDCardStart TEXT, IDCardEnd TEXT,
                            CertStart TEXT, CertEnd TEXT,
                            CertName TEXT,
                            Cert1Name TEXT, Cert1DocNumber TEXT, Cert1Start TEXT, Cert1End TEXT,
                            Cert2Name TEXT, Cert2DocNumber TEXT, Cert2Start TEXT, Cert2End TEXT,
                            Cert3Name TEXT, Cert3DocNumber TEXT, Cert3Start TEXT, Cert3End TEXT,
                            Cert4Name TEXT, Cert4DocNumber TEXT, Cert4Start TEXT, Cert4End TEXT,
                            Cert5Name TEXT, Cert5DocNumber TEXT, Cert5Start TEXT, Cert5End TEXT,
                            FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
                        );
                        CREATE INDEX IF NOT EXISTS idx_personnel_group ON PersonnelRecords(GroupId);
                    ", connection);
                    createCmd.ExecuteNonQuery();
                    Log.Information("已为现有数据库创建 PersonnelRecords 表");
                }
                else
                {
                    // 检查并添加 PersonnelRecords 表的 CertName 列
                    using var cmdPersonnelColumns = new SqliteCommand("PRAGMA table_info(PersonnelRecords);", connection);
                    using var readerPersonnelColumns = cmdPersonnelColumns.ExecuteReader();
                    var existingPersonnelColumns = new List<string>();
                    while (readerPersonnelColumns.Read())
                    {
                        existingPersonnelColumns.Add(readerPersonnelColumns.GetString(1));
                    }

                    if (!existingPersonnelColumns.Contains("CertName"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN CertName TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                        Log.Information("已为 PersonnelRecords 表添加 CertName 列");
                    }

                    // 迁移 5 个证书槽位字段
                    if (!existingPersonnelColumns.Contains("Cert1Name"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert1Name TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert1DocNumber"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert1DocNumber TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert1Start"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert1Start TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert1End"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert1End TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert2Name"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert2Name TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert2DocNumber"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert2DocNumber TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert2Start"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert2Start TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert2End"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert2End TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert3Name"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert3Name TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert3DocNumber"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert3DocNumber TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert3Start"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert3Start TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert3End"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert3End TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert4Name"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert4Name TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert4DocNumber"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert4DocNumber TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert4Start"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert4Start TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert4End"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert4End TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert5Name"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert5Name TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert5DocNumber"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert5DocNumber TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert5Start"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert5Start TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    if (!existingPersonnelColumns.Contains("Cert5End"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN Cert5End TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                    }
                    Log.Information("已为 PersonnelRecords 表添加 5 个证书槽位字段");

                    // 迁移 2：添加 BirthDate 列，并从 HomeAddress 复制数据
                    if (!existingPersonnelColumns.Contains("BirthDate"))
                    {
                        using var alterCmd = new SqliteCommand("ALTER TABLE PersonnelRecords ADD COLUMN BirthDate TEXT;", connection);
                        alterCmd.ExecuteNonQuery();
                        // 将现有 HomeAddress 数据（实际是出生年月）复制到 BirthDate
                        using var copyCmd = new SqliteCommand(
                            "UPDATE PersonnelRecords SET BirthDate = HomeAddress WHERE BirthDate IS NULL AND HomeAddress IS NOT NULL AND HomeAddress != '';", connection);
                        int copied = copyCmd.ExecuteNonQuery();
                        Log.Information("已为 PersonnelRecords 表添加 BirthDate 列，迁移 {Count} 条数据", copied);
                    }
                }

                // 检查 Attachments 表是否仍有旧的外键约束（指向 Records 表）
                // 如果存在，则需要重建表移除 FK，否则人员模块附件插入会失败
                using var cmdFK = new SqliteCommand("PRAGMA foreign_key_list(Attachments);", connection);
                using var readerFK = cmdFK.ExecuteReader();
                bool hasRecordFK = false;
                while (readerFK.Read())
                {
                    var table = readerFK.GetString(2); // "table" 列
                    var from = readerFK.GetString(3);  // "from" 列
                    if (table == "Records" && from == "RecordId")
                    {
                        hasRecordFK = true;
                        break;
                    }
                }
                if (hasRecordFK)
                {
                    Log.Information("检测到 Attachments 表存在旧外键约束，开始迁移...");

                    // 分步执行，每一步独立检测，避免批量 SQL 中断导致数据库不一致
                    using var stepCmd = connection.CreateCommand();

                    // Step 0: 先清理可能的残留
                    stepCmd.CommandText = "DROP TABLE IF EXISTS Attachments_v2";
                    stepCmd.ExecuteNonQuery();

                    // Step 1: 关闭外键检查
                    stepCmd.CommandText = "PRAGMA foreign_keys = OFF";
                    stepCmd.ExecuteNonQuery();

                    // Step 2: 创建新表（无 FK），包含所有可能已添加的列
                    // 注意列顺序必须与原 Attachments 表一致（ALTER TABLE 追加到末尾）
                    stepCmd.CommandText = @"
                        CREATE TABLE Attachments_v2 (
                            Id TEXT PRIMARY KEY,
                            RecordId TEXT NOT NULL,
                            FileName TEXT NOT NULL,
                            RelativePath TEXT NOT NULL,
                            FileSize INTEGER,
                            MimeType TEXT,
                            UploadedAt TEXT,
                            AttachmentType TEXT,
                            DisplayOrder INTEGER,
                            ThumbnailPath TEXT,
                            CertNumber TEXT,
                            CertStart TEXT,
                            CertEnd TEXT,
                            CertName TEXT
                        )";
                    stepCmd.ExecuteNonQuery();
                    Log.Information("迁移 Step 1/4: 创建 Attachments_v2");

                    // Step 3: 复制数据
                    stepCmd.CommandText = "INSERT INTO Attachments_v2 SELECT * FROM Attachments";
                    int copiedRows = stepCmd.ExecuteNonQuery();
                    Log.Information("迁移 Step 2/4: 已复制 {Rows} 行数据", copiedRows);

                    // Step 4: 删除旧表
                    stepCmd.CommandText = "DROP TABLE Attachments";
                    stepCmd.ExecuteNonQuery();
                    Log.Information("迁移 Step 3/4: 已删除旧 Attachments 表");

                    // Step 5: 重命名新表
                    stepCmd.CommandText = "ALTER TABLE Attachments_v2 RENAME TO Attachments";
                    stepCmd.ExecuteNonQuery();
                    Log.Information("迁移 Step 4/4: 已重命名 Attachments_v2 -> Attachments");

                    // Step 6: 恢复外键检查
                    stepCmd.CommandText = "PRAGMA foreign_keys = ON";
                    stepCmd.ExecuteNonQuery();

                    Log.Information("Attachments 表外键约束已移除");
                }
            }
        }

        private void EnableWalMode()
        {
            if (_walEnabled) return;
            using var conn = OpenConnection();
            var mode = conn.ExecuteScalar<string>("PRAGMA journal_mode=WAL;");
            Log.Information("数据库 WAL 模式已启用，当前模式: {Mode}", mode);
            _walEnabled = true;
        }

        public SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout = 3000";
            cmd.ExecuteNonQuery();
            return conn;
        }

        public Group? GetGroupById(string id)
        {
            using var conn = OpenConnection();
            return conn.QueryFirstOrDefault<Group>("SELECT * FROM Groups WHERE Id = @Id", new { Id = id });
        }

        /// <summary>
        /// 递归查找指定分组的顶层（根）分组 ID
        /// </summary>
        public string GetRootGroupId(string groupId)
        {
            var group = GetGroupById(groupId);
            if (group == null) return groupId;
            return string.IsNullOrEmpty(group.ParentId)
                ? group.Id
                : GetRootGroupId(group.ParentId);
        }

        public bool UpdateAttachment(Attachment attachment)
        {
            using var conn = OpenConnection();
            int rows = conn.Execute(@"UPDATE Attachments SET 
                RelativePath = @RelativePath, ThumbnailPath = @ThumbnailPath, 
                DisplayOrder = @DisplayOrder, CertNumber = @CertNumber, 
                CertName = @CertName, CertStart = @CertStart, CertEnd = @CertEnd 
                WHERE Id = @Id", attachment);
            return rows > 0;
        }

        #region Groups
        public List<Group> GetGroups(string module, string? parentId = null)
        {
            Log.Information("GetGroups: module={Module}, parentId={ParentId}, _projectId={ProjectId}", module, parentId, _projectId);
            using var conn = OpenConnection();
            string sql = "SELECT * FROM Groups WHERE Module = @Module AND ProjectId = @ProjectId";
            if (parentId == null)
                sql += " AND ParentId IS NULL";
            else
                sql += " AND ParentId = @ParentId";
            // 隐患模块按名称降序（年月倒序），人员模块按 Order 升序
            if (module == ModuleNames.Hazard)
                sql += " ORDER BY Name DESC";
            else
                sql += " ORDER BY [Order]";
            return conn.Query<Group>(sql, new { Module = module, ProjectId = _projectId, ParentId = parentId }).ToList();
        }

        public bool AddGroup(Group group)
        {
            try
            {
                using var conn = OpenConnection();
                using var transaction = conn.BeginTransaction();

                string sql = @"SELECT COALESCE(MAX([Order]), -1) FROM Groups 
               WHERE Module = @Module AND ProjectId = @ProjectId AND ParentId IS NULL";
                if (!string.IsNullOrEmpty(group.ParentId))
                    sql = @"SELECT COALESCE(MAX([Order]), -1) FROM Groups 
            WHERE Module = @Module AND ProjectId = @ProjectId AND ParentId = @ParentId";
                int maxOrder = conn.ExecuteScalar<int>(sql, new { group.Module, group.ProjectId, group.ParentId }, transaction);
                group.Order = maxOrder + 1;
                int rows = conn.Execute(@"INSERT INTO Groups (Id, ProjectId, Module, ParentId, Name, [Order], CreatedDate, Tag) 
                          VALUES (@Id, @ProjectId, @Module, @ParentId, @Name, @Order, @CreatedDate, @Tag)", group, transaction);

                transaction.Commit();

                Log.Information("AddGroup: 分组 {Name} 插入成功，影响行数 {Rows}", group.Name, rows);

                // 验证写入
                var exists = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Groups WHERE Id = @Id", new { group.Id });
                Log.Information("AddGroup 验证: 分组 {Name} 是否存在? {Exists}", group.Name, exists > 0);

                // 强制执行 WAL checkpoint，确保数据落盘
                conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                Log.Information("AddGroup: 已执行 WAL checkpoint");

                return rows > 0;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                Log.Error(ex, "AddGroup 捕获到 SqliteException: ErrorCode={ErrorCode}, SqliteErrorCode={SqliteErrorCode}, SqliteExtendedErrorCode={SqliteExtendedErrorCode}, 分组名称={Name}",
                    ex.ErrorCode, ex.SqliteErrorCode, ex.SqliteExtendedErrorCode, group.Name);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AddGroup 捕获到一般异常，分组名称: {Name}", group.Name);
                throw;
            }
        }

        public bool UpdateGroup(Group group)
        {
            using var conn = OpenConnection();
            int rows = conn.Execute("UPDATE Groups SET Name = @Name, [Order] = @Order, ParentId = @ParentId, Tag = @Tag WHERE Id = @Id", group);
            return rows > 0;
        }

        public bool IsTagDuplicate(string parentId, string name, string tag, string? excludeGroupId = null)
        {
            using var conn = OpenConnection();
            string sql = "SELECT COUNT(1) FROM Groups WHERE ParentId = @ParentId AND Name = @Name AND Tag = @Tag";
            if (!string.IsNullOrEmpty(excludeGroupId))
                sql += " AND Id != @ExcludeId";
            var parameters = new { ParentId = parentId, Name = name, Tag = tag, ExcludeId = excludeGroupId };
            return conn.ExecuteScalar<int>(sql, parameters) > 0;
        }

        public bool DeleteGroup(string id)
        {
            var group = GetGroupById(id);
            if (group == null) return false;

            var allGroupIds = new List<string> { id };
            // 使用单一连接获取所有子孙分组 ID
            using (var conn = OpenConnection())
            {
                GetAllDescendantGroupIds(conn, id, allGroupIds);
            }

            // 2. 获取所有要删除的记录 ID（Records + PersonnelRecords）
            var allRecordIds = new List<string>();
            var allPersonnelRecordIds = new List<string>();
            using (var conn = OpenConnection())
            {
                foreach (var gid in allGroupIds)
                {
                    var recordIds = conn.Query<string>("SELECT Id FROM Records WHERE GroupId = @GroupId", new { GroupId = gid }).ToList();
                    allRecordIds.AddRange(recordIds);
                    var prIds = conn.Query<string>("SELECT Id FROM PersonnelRecords WHERE GroupId = @GroupId", new { GroupId = gid }).ToList();
                    allPersonnelRecordIds.AddRange(prIds);
                }
            }

            // 3. 删除 Records 的物理文件夹（Card_xxx）
            string projectsDateRoot = GetProjectsDateRoot();   // 安全获取根目录
            foreach (var recordId in allRecordIds)
            {
                var attachments = GetAttachmentsByRecord(recordId);
                if (attachments.Any())
                {
                    var firstAtt = attachments.First();
                    var folderPath = Path.GetDirectoryName(firstAtt.RelativePath);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        var fullFolder = Path.Combine(projectsDateRoot, folderPath);
                        if (Directory.Exists(fullFolder))
                        {
                            try { Directory.Delete(fullFolder, true); }
                            catch (Exception ex) { Log.Warning(ex, "删除卡片文件夹失败: {Folder}", fullFolder); }
                        }
                    }
                }
            }

            // 3.5 删除 PersonnelRecords 的物理文件夹（Card_xxx）
            foreach (var prId in allPersonnelRecordIds)
            {
                var attachments = GetAttachmentsByRecord(prId);
                if (attachments.Any())
                {
                    var firstAtt = attachments.First();
                    var folderPath = Path.GetDirectoryName(firstAtt.RelativePath);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        var fullFolder = Path.Combine(projectsDateRoot, folderPath);
                        if (Directory.Exists(fullFolder))
                        {
                            try { Directory.Delete(fullFolder, true); }
                            catch (Exception ex) { Log.Warning(ex, "删除人员卡片文件夹失败: {Folder}", fullFolder); }
                        }
                    }
                }
                // 同时删除 Attachments 数据库记录
                using (var conn = OpenConnection())
                    conn.Execute("DELETE FROM Attachments WHERE RecordId = @RecordId", new { RecordId = prId });
            }

            // 4. 如果是 Personnel 顶级分组，删除 Personnel_{groupId} 文件夹
            if (string.IsNullOrEmpty(group.ParentId) && group.Module == ModuleNames.Personnel)
            {
                string personnelFolderName = $"Personnel_{id}";
                string fullPersonnelFolder = Path.Combine(projectsDateRoot, $"Project_{_projectId}", personnelFolderName);
                if (Directory.Exists(fullPersonnelFolder))
                {
                    try { Directory.Delete(fullPersonnelFolder, true); }
                    catch (Exception ex) { Log.Warning(ex, "删除 Personnel 分组文件夹失败: {Folder}", fullPersonnelFolder); }
                }
            }

            // 5. 如果是顶级分组且为 Hazard 模块，删除年月文件夹
            if (string.IsNullOrEmpty(group.ParentId) && group.Module != ModuleNames.Personnel)
            {
                var match = System.Text.RegularExpressions.Regex.Match(group.Name, @"(\d{4})年(\d{1,2})月");
                if (match.Success)
                {
                    string yearMonth = $"{match.Groups[1].Value}-{int.Parse(match.Groups[2].Value):D2}";
                    string module = group.Module;
                    string yearMonthFolderName = $"{module}_{yearMonth}";
                    string fullYearMonthFolder = Path.Combine(projectsDateRoot, $"Project_{_projectId}", yearMonthFolderName);
                    if (Directory.Exists(fullYearMonthFolder))
                    {
                        try { Directory.Delete(fullYearMonthFolder, true); }
                        catch (Exception ex) { Log.Warning(ex, "删除年月文件夹失败: {Folder}", fullYearMonthFolder); }
                    }
                }
            }

            // 5. 执行数据库删除（重新打开一个连接，或者使用之前的连接，但为了避免作用域问题，单独开一个也可以，只要不使用同名变量）
            using (var conn = OpenConnection())
            {
                int rows = conn.Execute("DELETE FROM Groups WHERE Id = @Id", new { Id = id });
                return rows > 0;
            }
        }

        private void GetAllDescendantGroupIds(SqliteConnection conn, string parentId, List<string> ids)
        {
            var children = conn.Query<string>("SELECT Id FROM Groups WHERE ParentId = @ParentId", new { ParentId = parentId }).ToList();
            foreach (var childId in children)
            {
                ids.Add(childId);
                GetAllDescendantGroupIds(conn, childId, ids);
            }
        }

        #endregion

        #region Records
        public List<Record> GetRecordsByGroup(string groupId)
        {
            using var conn = OpenConnection();
            return conn.Query<Record>("SELECT * FROM Records WHERE GroupId = @GroupId ORDER BY DisplayIndex ASC, Id ASC", new { GroupId = groupId }).ToList();
        }

        public List<Record> GetRecordsByGroupPaged(string groupId, int offset, int limit)
        {
            using var conn = OpenConnection();
            return conn.Query<Record>(
                "SELECT * FROM Records WHERE GroupId = @GroupId ORDER BY DisplayIndex ASC, Id ASC LIMIT @Limit OFFSET @Offset",
                new { GroupId = groupId, Offset = offset, Limit = limit }).ToList();
        }

        public int GetRecordsCountByGroup(string groupId)
        {
            using var conn = OpenConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Records WHERE GroupId = @GroupId", new { GroupId = groupId });
        }

        public int GetMaxDisplayIndex(string groupId)
        {
            using var conn = OpenConnection();
            return conn.ExecuteScalar<int>("SELECT IFNULL(MAX(DisplayIndex), 0) FROM Records WHERE GroupId = @GroupId", new { GroupId = groupId });
        }

        public Record? GetRecordById(string id)
        {
            using var conn = OpenConnection();
            return conn.QueryFirstOrDefault<Record>("SELECT * FROM Records WHERE Id = @Id", new { Id = id });
        }

        public bool AddRecord(Record record)
        {
            using var conn = OpenConnection();
            record.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int rows = conn.Execute(@"INSERT INTO Records 
        (Id, GroupId, Title, Content, CreatedAt, UpdatedAt, DueDate, Status, ExtraData,
         ResponsibleUnit, ResponsiblePerson, Location, HazardType, HazardLevel,
         Deadline, CompletedDate, Reviewer, Description, Measure, YearMonth, DisplayIndex)
        VALUES 
        (@Id, @GroupId, @Title, @Content, @CreatedAt, @UpdatedAt, @DueDate, @Status, @ExtraData,
         @ResponsibleUnit, @ResponsiblePerson, @Location, @HazardType, @HazardLevel,
         @Deadline, @CompletedDate, @Reviewer, @Description, @Measure, @YearMonth, @DisplayIndex)", record);
            return rows > 0;
        }

        public bool UpdateRecord(Record record)
        {
            bool success = false;
            DatabaseHelper.ExecuteWithRetry(conn =>
            {
                record.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                int rows = conn.Execute(@"UPDATE Records SET 
            Title = @Title, Content = @Content, UpdatedAt = @UpdatedAt, DueDate = @DueDate, 
            Status = @Status, ExtraData = @ExtraData,
            ResponsibleUnit = @ResponsibleUnit, ResponsiblePerson = @ResponsiblePerson, 
            Location = @Location, HazardType = @HazardType, HazardLevel = @HazardLevel,
            Deadline = @Deadline, CompletedDate = @CompletedDate, Reviewer = @Reviewer,
            Description = @Description, Measure = @Measure, YearMonth = @YearMonth,
            DisplayIndex = @DisplayIndex, GroupId = @GroupId
            WHERE Id = @Id", record);
                success = rows > 0;
            }, _dbPath, "UpdateRecord");
            return success;
        }

        public bool DeleteRecord(string id)
        {
            var record = GetRecordById(id);
            if (record == null) return false;

            // 1. 删除所有附件物理文件和数据库记录
            var attachments = GetAttachmentsByRecord(id);
            string projectsDateRoot = GetProjectsDateRoot();   // 安全获取根目录
            foreach (var att in attachments)
            {
                string fullPath = Path.Combine(projectsDateRoot, att.RelativePath);
                if (File.Exists(fullPath)) File.Delete(fullPath);
                if (!string.IsNullOrEmpty(att.ThumbnailPath))
                {
                    string thumbFullPath = Path.Combine(projectsDateRoot, att.ThumbnailPath);
                    if (File.Exists(thumbFullPath)) File.Delete(thumbFullPath);
                }
            }
            DeleteAttachmentsByRecordId(id);

            // 2. 直接删除卡片所在的整个文件夹（无论是否有内容）
            string module = GetGroupModule(record.GroupId) ?? "Hazard";
            string yearMonth = record.YearMonth ?? "Unknown";
            string cardFolderName = $"Card_{id}";
            string fullCardFolder = Path.Combine(projectsDateRoot, $"Project_{_projectId}", $"{module}_{yearMonth}", cardFolderName);
            if (Directory.Exists(fullCardFolder))
            {
                try
                {
                    Directory.Delete(fullCardFolder, true);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "删除卡片文件夹失败: {Folder}", fullCardFolder);
                }
            }

            // 3. 删除数据库中的记录
            return DeleteRecordFromDb(id);
        }

        private void DeleteAttachmentsByRecordId(string recordId)
        {
            using var conn = OpenConnection();
            conn.Execute("DELETE FROM Attachments WHERE RecordId = @RecordId", new { RecordId = recordId });
        }

        private bool DeleteRecordFromDb(string id)
        {
            using var conn = OpenConnection();
            int rows = conn.Execute("DELETE FROM Records WHERE Id = @Id", new { Id = id });
            return rows > 0;
        }

        // 辅助方法：根据 GroupId 获取模块名
        private string? GetGroupModule(string groupId)
        {
            // 调用修改后的 GetAllGroups 方法（传入空字符串获取所有分组）
            var groups = GetAllGroups("");
            var group = groups.FirstOrDefault(g => g.Id == groupId);
            return group?.Module;
        }

        // 辅助方法：根据 GroupId 获取模块名
        public List<Group> GetAllGroups(string module)
        {
            using var conn = OpenConnection();
            string sql = "SELECT * FROM Groups WHERE ProjectId = @ProjectId";
            if (!string.IsNullOrEmpty(module))
                sql += " AND Module = @Module";
            sql += " ORDER BY [Order]";
            return conn.Query<Group>(sql, new { ProjectId = _projectId, Module = module }).ToList();
        }
        #endregion

        #region PersonnelRecords
        public List<PersonnelRecord> GetPersonnelRecordsByGroup(string groupId)
        {
            using var conn = OpenConnection();
            return conn.Query<PersonnelRecord>("SELECT * FROM PersonnelRecords WHERE GroupId = @GroupId ORDER BY DisplayIndex ASC, Id ASC", new { GroupId = groupId }).ToList();
        }
        /// <summary>
        /// 获取当前项目全量人员记录（按所属分组归属项目过滤），供搜索建议下拉使用
        /// </summary>
        public List<PersonnelRecord> GetAllPersonnelRecords(string projectId)
        {
            using var conn = OpenConnection();
            return conn.Query<PersonnelRecord>(
                @"SELECT p.* FROM PersonnelRecords p
                  INNER JOIN Groups g ON p.GroupId = g.Id
                  WHERE g.ProjectId = @ProjectId
                  ORDER BY p.DisplayIndex ASC, p.Id ASC",
                new { ProjectId = projectId }).ToList();
        }


        public List<PersonnelRecord> GetPersonnelRecordsByGroupPaged(string groupId, int offset, int limit)
        {
            using var conn = OpenConnection();
            return conn.Query<PersonnelRecord>(
                "SELECT * FROM PersonnelRecords WHERE GroupId = @GroupId ORDER BY DisplayIndex ASC, Id ASC LIMIT @Limit OFFSET @Offset",
                new { GroupId = groupId, Offset = offset, Limit = limit }).ToList();
        }

        public int GetPersonnelRecordsCountByGroup(string groupId)
        {
            using var conn = OpenConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM PersonnelRecords WHERE GroupId = @GroupId", new { GroupId = groupId });
        }

        public int GetMaxPersonnelDisplayIndex(string groupId)
        {
            using var conn = OpenConnection();
            return conn.ExecuteScalar<int>("SELECT IFNULL(MAX(DisplayIndex), 0) FROM PersonnelRecords WHERE GroupId = @GroupId", new { GroupId = groupId });
        }

        public PersonnelRecord? GetPersonnelRecord(string id)
        {
            using var conn = OpenConnection();
            return conn.QueryFirstOrDefault<PersonnelRecord>("SELECT * FROM PersonnelRecords WHERE Id = @Id", new { Id = id });
        }

        public bool AddPersonnelRecord(PersonnelRecord record)
        {
            using var conn = OpenConnection();
            record.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int rows = conn.Execute(@"
                INSERT INTO PersonnelRecords
                (Id, GroupId, Title, CreatedAt, UpdatedAt, DisplayIndex, YearMonth,
                 Gender, EmployeeID, Position, PhoneNumber,
                 EducationLevel, NativePlace, HomeAddress, BirthDate,
                 EntryTime, EduTime, ExitTime,
                 ThreeCategoryPersonnel, SpecialOperationsPersonnel, Remark,
                 IDCardDocNumber, CertDocNumber, IDCardStart, IDCardEnd, CertStart, CertEnd,
                 CertName,
                 Cert1Name, Cert1DocNumber, Cert1Start, Cert1End,
                 Cert2Name, Cert2DocNumber, Cert2Start, Cert2End,
                 Cert3Name, Cert3DocNumber, Cert3Start, Cert3End,
                 Cert4Name, Cert4DocNumber, Cert4Start, Cert4End,
                 Cert5Name, Cert5DocNumber, Cert5Start, Cert5End)
                VALUES
                (@Id, @GroupId, @Title, @CreatedAt, @UpdatedAt, @DisplayIndex, @YearMonth,
                 @Gender, @EmployeeID, @Position, @PhoneNumber,
                 @EducationLevel, @NativePlace, @HomeAddress, @BirthDate,
                 @EntryTime, @EduTime, @ExitTime,
                 @ThreeCategoryPersonnel, @SpecialOperationsPersonnel, @Remark,
                 @IDCardDocNumber, @CertDocNumber, @IDCardStart, @IDCardEnd, @CertStart, @CertEnd,
                 @CertName,
                 @Cert1Name, @Cert1DocNumber, @Cert1Start, @Cert1End,
                 @Cert2Name, @Cert2DocNumber, @Cert2Start, @Cert2End,
                 @Cert3Name, @Cert3DocNumber, @Cert3Start, @Cert3End,
                 @Cert4Name, @Cert4DocNumber, @Cert4Start, @Cert4End,
                 @Cert5Name, @Cert5DocNumber, @Cert5Start, @Cert5End)", record);
            return rows > 0;
        }

        public bool UpdatePersonnelRecord(PersonnelRecord record)
        {
            using var conn = OpenConnection();
            record.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int rows = conn.Execute(@"
                UPDATE PersonnelRecords SET
                    GroupId = @GroupId, Title = @Title, UpdatedAt = @UpdatedAt,
                    DisplayIndex = @DisplayIndex, YearMonth = @YearMonth,
                    Gender = @Gender, EmployeeID = @EmployeeID,
                    Position = @Position, PhoneNumber = @PhoneNumber,
                    EducationLevel = @EducationLevel, NativePlace = @NativePlace,
                    HomeAddress = @HomeAddress, BirthDate = @BirthDate,
                    EntryTime = @EntryTime, EduTime = @EduTime, ExitTime = @ExitTime,
                    ThreeCategoryPersonnel = @ThreeCategoryPersonnel,
                    SpecialOperationsPersonnel = @SpecialOperationsPersonnel,
                    Remark = @Remark,
                    IDCardDocNumber = @IDCardDocNumber, CertDocNumber = @CertDocNumber,
                    IDCardStart = @IDCardStart, IDCardEnd = @IDCardEnd,
                    CertStart = @CertStart, CertEnd = @CertEnd,
                    CertName = @CertName,
                    Cert1Name = @Cert1Name, Cert1DocNumber = @Cert1DocNumber,
                    Cert1Start = @Cert1Start, Cert1End = @Cert1End,
                    Cert2Name = @Cert2Name, Cert2DocNumber = @Cert2DocNumber,
                    Cert2Start = @Cert2Start, Cert2End = @Cert2End,
                    Cert3Name = @Cert3Name, Cert3DocNumber = @Cert3DocNumber,
                    Cert3Start = @Cert3Start, Cert3End = @Cert3End,
                    Cert4Name = @Cert4Name, Cert4DocNumber = @Cert4DocNumber,
                    Cert4Start = @Cert4Start, Cert4End = @Cert4End,
                    Cert5Name = @Cert5Name, Cert5DocNumber = @Cert5DocNumber,
                    Cert5Start = @Cert5Start, Cert5End = @Cert5End
                WHERE Id = @Id", record);
            return rows > 0;
        }

        public bool DeletePersonnelRecord(string id)
        {
            using var conn = OpenConnection();
            int rows = conn.Execute("DELETE FROM PersonnelRecords WHERE Id = @Id", new { Id = id });
            return rows > 0;
        }
        #endregion

        #region Attachments
        public List<Attachment> GetAttachmentsByRecord(string recordId)
        {
            using var conn = OpenConnection();
            return conn.Query<Attachment>("SELECT * FROM Attachments WHERE RecordId = @RecordId", new { RecordId = recordId }).ToList();
        }

        /// <summary>
        /// 批量获取多个记录的所有附件
        /// </summary>
        public List<Attachment> GetAttachmentsByRecordIds(List<string> recordIds)
        {
            if (recordIds == null || recordIds.Count == 0)
                return new List<Attachment>();

            using var conn = OpenConnection();
            // 使用 Dapper 的 IN 参数化查询，安全高效
            return conn.Query<Attachment>("SELECT * FROM Attachments WHERE RecordId IN @RecordIds", new { RecordIds = recordIds }).ToList();
        }

        public bool AddAttachment(Attachment attachment)
        {
            using var conn = OpenConnection();
            attachment.UploadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int rows = conn.Execute(@"INSERT INTO Attachments 
        (Id, RecordId, FileName, RelativePath, FileSize, MimeType, UploadedAt, AttachmentType, DisplayOrder, ThumbnailPath, CertNumber, CertName, CertStart, CertEnd) 
        VALUES (@Id, @RecordId, @FileName, @RelativePath, @FileSize, @MimeType, @UploadedAt, @AttachmentType, @DisplayOrder, @ThumbnailPath, @CertNumber, @CertName, @CertStart, @CertEnd)", attachment);
            return rows > 0;
        }

        public bool DeleteAttachment(string id)
        {
            using var conn = OpenConnection();
            int rows = conn.Execute("DELETE FROM Attachments WHERE Id = @Id", new { Id = id });
            return rows > 0;
        }
        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // 强制清除该数据库的连接池，释放所有连接
                ClearConnectionPool(_dbPath);
            }
            _disposed = true;
        }
    }
}
