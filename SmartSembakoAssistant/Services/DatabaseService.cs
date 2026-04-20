using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService(string? dbPath = null)
        {
            // Gunakan absolute path berbasis AppDomain base directory
            if (string.IsNullOrEmpty(dbPath))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dataDir = Path.Combine(baseDir, "data");
                
                // Pastikan folder data ada
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
                
                _dbPath = Path.Combine(dataDir, "memory.db");
            }
            else
            {
                // Jika path relatif, convert ke absolute
                if (!Path.IsPathRooted(dbPath))
                {
                    _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
                }
                else
                {
                    _dbPath = dbPath;
                }
            }
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                // Pastikan folder data ada
                string? directory = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                // Tabel conversations
                string createConversationsTable = @"
                    CREATE TABLE IF NOT EXISTS conversations (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        chat_id INTEGER NOT NULL,
                        user_name TEXT,
                        role TEXT NOT NULL,
                        message TEXT NOT NULL,
                        timestamp TEXT NOT NULL,
                        message_type TEXT
                    )";

                // Tabel long_term_memory
                string createLongTermMemoryTable = @"
                    CREATE TABLE IF NOT EXISTS long_term_memory (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        category TEXT NOT NULL,
                        summary TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        updated_at TEXT,
                        usage_count INTEGER DEFAULT 1
                    )";

                // Tabel logs
                string createLogsTable = @"
                    CREATE TABLE IF NOT EXISTS logs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp TEXT NOT NULL,
                        level TEXT NOT NULL,
                        category TEXT NOT NULL,
                        message TEXT NOT NULL,
                        details TEXT,
                        user_id TEXT
                    )";

                // Tabel app_config
                string createAppConfigTable = @"
                    CREATE TABLE IF NOT EXISTS app_config (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        key TEXT NOT NULL UNIQUE,
                        value TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";

                // Tabel users untuk RBAC
                string createUsersTable = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL,
                        telegram_id TEXT,
                        whatsapp_number TEXT,
                        role_id INTEGER NOT NULL,
                        is_active INTEGER DEFAULT 1,
                        created_at TEXT NOT NULL,
                        FOREIGN KEY (role_id) REFERENCES roles(id)
                    )";

                // Tabel roles
                string createRolesTable = @"
                    CREATE TABLE IF NOT EXISTS roles (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        role_name TEXT NOT NULL UNIQUE
                    )";

                // Tabel permissions
                string createPermissionsTable = @"
                    CREATE TABLE IF NOT EXISTS permissions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        role_id INTEGER NOT NULL,
                        can_ocr INTEGER DEFAULT 0,
                        can_purchase INTEGER DEFAULT 0,
                        can_edit_stock INTEGER DEFAULT 0,
                        can_view_report INTEGER DEFAULT 0,
                        can_manage_users INTEGER DEFAULT 0,
                        FOREIGN KEY (role_id) REFERENCES roles(id)
                    )";

                // Tabel conversation_sessions untuk state machine
                string createConversationSessionsTable = @"
                    CREATE TABLE IF NOT EXISTS conversation_sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        user_id TEXT NOT NULL,
                        state TEXT NOT NULL,
                        receipt_id TEXT,
                        current_unknown_index INTEGER DEFAULT 0,
                        temp_product_name TEXT,
                        updated_at TEXT NOT NULL
                    )";

                // Tabel inventory_logs untuk tracking perubahan stok
                string createInventoryLogsTable = @"
                    CREATE TABLE IF NOT EXISTS inventory_logs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        product_id TEXT NOT NULL,
                        product_name TEXT NOT NULL,
                        old_stock REAL NOT NULL,
                        new_stock REAL NOT NULL,
                        adjustment REAL NOT NULL,
                        reason TEXT NOT NULL,
                        user_id TEXT NOT NULL,
                        channel TEXT NOT NULL,
                        timestamp TEXT NOT NULL
                    )";

                using (var command = new SqliteCommand(createConversationsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createConversationSessionsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createInventoryLogsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createLongTermMemoryTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createLogsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createAppConfigTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createUsersTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createRolesTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createPermissionsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createConversationSessionsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create indexes untuk performa
                string[] createIndexes = new[]
                {
                    "CREATE INDEX IF NOT EXISTS idx_conversations_chat_id ON conversations(chat_id)",
                    "CREATE INDEX IF NOT EXISTS idx_conversations_timestamp ON conversations(timestamp)",
                    "CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON logs(timestamp)",
                    "CREATE INDEX IF NOT EXISTS idx_logs_category ON logs(category)",
                    "CREATE INDEX IF NOT EXISTS idx_users_telegram_id ON users(telegram_id)",
                    "CREATE INDEX IF NOT EXISTS idx_users_whatsapp_number ON users(whatsapp_number)",
                    "CREATE INDEX IF NOT EXISTS idx_conversation_sessions_user_id ON conversation_sessions(user_id)"
                };

                foreach (string indexSql in createIndexes)
                {
                    using var command = new SqliteCommand(indexSql, connection);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Gagal inisialisasi database: {ex.Message}");
            }
        }

        #region Conversation Methods

        public async Task<long> AddConversationAsync(Conversation conversation)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO conversations (chat_id, user_name, role, message, timestamp, message_type)
                VALUES (@chat_id, @user_name, @role, @message, @timestamp, @message_type);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@chat_id", conversation.ChatId);
            command.Parameters.AddWithValue("@user_name", conversation.UserName ?? "Unknown");
            command.Parameters.AddWithValue("@role", conversation.Role ?? "user");
            command.Parameters.AddWithValue("@message", conversation.Message ?? "");
            command.Parameters.AddWithValue("@timestamp", conversation.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@message_type", conversation.MessageType ?? "text");

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public async Task<List<Conversation>> GetRecentConversationsAsync(long? chatId = null, int count = 10)
        {
            var conversations = new List<Conversation>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                SELECT id, chat_id, user_name, role, message, timestamp, message_type
                FROM conversations
                WHERE 1=1";
            
            if (chatId.HasValue)
            {
                sql += " AND chat_id = @chat_id";
            }
            
            sql += " ORDER BY timestamp DESC LIMIT @count";

            using var command = new SqliteCommand(sql, connection);
            if (chatId.HasValue)
            {
                command.Parameters.AddWithValue("@chat_id", chatId.Value);
            }
            command.Parameters.AddWithValue("@count", count);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                conversations.Add(new Conversation
                {
                    Id = reader.GetInt64(0),
                    ChatId = reader.GetInt64(1),
                    UserName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Role = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Message = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Timestamp = reader.GetDateTime(5),
                    MessageType = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            conversations.Reverse(); // Balik ke urutan kronologis
            return conversations;
        }

        public async Task ClearOldConversationsAsync(int daysToKeep = 30)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                DELETE FROM conversations
                WHERE timestamp < datetime('now', @days)";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@days", $"-{daysToKeep} days");

            await command.ExecuteNonQueryAsync();
        }

        #endregion

        #region Long-Term Memory Methods

        public async Task<long> AddLongTermMemoryAsync(LongTermMemory memory)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO long_term_memory (category, summary, created_at, updated_at, usage_count)
                VALUES (@category, @summary, @created_at, @updated_at, @usage_count);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@category", memory.Category ?? "");
            command.Parameters.AddWithValue("@summary", memory.Summary ?? "");
            command.Parameters.AddWithValue("@created_at", memory.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@updated_at", memory.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@usage_count", memory.UsageCount);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public async Task<List<LongTermMemory>> GetLongTermMemoriesAsync(string? category = null, int limit = 20)
        {
            var memories = new List<LongTermMemory>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                SELECT id, category, summary, created_at, updated_at, usage_count
                FROM long_term_memory";

            if (!string.IsNullOrEmpty(category))
            {
                sql += " WHERE category = @category";
            }

            sql += " ORDER BY usage_count DESC, updated_at DESC LIMIT @limit";

            using var command = new SqliteCommand(sql, connection);
            if (!string.IsNullOrEmpty(category))
            {
                command.Parameters.AddWithValue("@category", category);
            }
            command.Parameters.AddWithValue("@limit", limit);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                memories.Add(new LongTermMemory
                {
                    Id = reader.GetInt64(0),
                    Category = reader.GetString(1),
                    Summary = reader.GetString(2),
                    CreatedAt = reader.GetDateTime(3),
                    UpdatedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    UsageCount = reader.GetInt32(5)
                });
            }

            return memories;
        }

        public async Task UpdateLongTermMemoryUsageAsync(long id)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE long_term_memory
                SET usage_count = usage_count + 1, updated_at = datetime('now')
                WHERE id = @id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();
        }

        #endregion

        #region Log Methods

        public async Task<long> AddLogAsync(LogEntry log)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO logs (timestamp, level, category, message, details, user_id)
                VALUES (@timestamp, @level, @category, @message, @details, @user_id);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@timestamp", log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@level", log.Level ?? "Info");
            command.Parameters.AddWithValue("@category", log.Category ?? "System");
            command.Parameters.AddWithValue("@message", log.Message ?? "");
            command.Parameters.AddWithValue("@details", log.Details ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@user_id", log.UserId ?? (object)DBNull.Value);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public async Task<List<LogEntry>> GetLogsAsync(string? category = null, string? level = null, int limit = 100)
        {
            var logs = new List<LogEntry>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                SELECT id, timestamp, level, category, message, details, user_id
                FROM logs
                WHERE 1=1";

            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(category))
            {
                sql += " AND category = @category";
                parameters.Add("@category", category);
            }

            if (!string.IsNullOrEmpty(level))
            {
                sql += " AND level = @level";
                parameters.Add("@level", level);
            }

            sql += " ORDER BY timestamp DESC LIMIT @limit";
            parameters.Add("@limit", limit);

            using var command = new SqliteCommand(sql, connection);
            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                logs.Add(new LogEntry
                {
                    Id = reader.GetInt64(0),
                    Timestamp = reader.GetDateTime(1),
                    Level = reader.GetString(2),
                    Category = reader.GetString(3),
                    Message = reader.GetString(4),
                    Details = reader.IsDBNull(5) ? null : reader.GetString(5),
                    UserId = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            return logs;
        }

        public async Task ClearOldLogsAsync(int daysToKeep = 30)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                DELETE FROM logs
                WHERE timestamp < datetime('now', @days)";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@days", $"-{daysToKeep} days");

            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteLogsBeforeAsync(DateTime cutoffDate)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "DELETE FROM logs WHERE timestamp < @cutoff";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@cutoff", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));

            return await command.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAllLogsAsync()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "DELETE FROM logs";
            using var command = new SqliteCommand(sql, connection);

            return await command.ExecuteNonQueryAsync();
        }

        #endregion

        #region App Config Methods

        public async Task<string?> GetAppConfigValueAsync(string key)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT value FROM app_config WHERE key = @key";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@key", key);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }

        public async Task SetAppConfigValueAsync(string key, string value)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO app_config (key, value, updated_at)
                VALUES (@key, @value, datetime('now'))
                ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = datetime('now')";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value);

            await command.ExecuteNonQueryAsync();
        }

        #endregion

        #region RBAC Methods

        public async Task<long> AddUserAsync(RbacUser user)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO users (name, telegram_id, whatsapp_number, role_id, is_active, created_at)
                VALUES (@name, @telegramId, @whatsappNumber, @roleId, @isActive, @createdAt);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@name", user.Name);
            command.Parameters.AddWithValue("@telegramId", user.TelegramId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@whatsappNumber", user.WhatsappNumber ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@roleId", user.RoleId);
            command.Parameters.AddWithValue("@isActive", user.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)await command.ExecuteScalarAsync();
        }

        public async Task<User?> GetUserByTelegramIdAsync(string telegramId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM users WHERE telegram_id = @telegramId AND is_active = 1";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@telegramId", telegramId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    TelegramId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    WhatsappNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RoleId = reader.GetInt64(4),
                    IsActive = reader.GetInt32(5) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                };
            }

            return null;
        }

        public async Task<User?> GetUserByWhatsappNumberAsync(string whatsappNumber)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM users WHERE whatsapp_number = @whatsappNumber AND is_active = 1";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@whatsappNumber", whatsappNumber);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    TelegramId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    WhatsappNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RoleId = reader.GetInt64(4),
                    IsActive = reader.GetInt32(5) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                };
            }

            return null;
        }

        public async Task<List<RbacUser>> GetAllUsersAsync()
        {
            var users = new List<User>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM users ORDER BY name";

            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(new RbacUser
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    TelegramId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    WhatsappNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RoleId = reader.GetInt64(4),
                    IsActive = reader.GetInt32(5) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                });
            }

            return users;
        }

        public async Task<long> AddRoleAsync(Role role)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO roles (role_name)
                VALUES (@roleName);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@roleName", role.RoleName);

            return (long)await command.ExecuteScalarAsync();
        }

        public async Task<List<Role>> GetAllRolesAsync()
        {
            var roles = new List<Role>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM roles ORDER BY role_name";

            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                roles.Add(new Role
                {
                    Id = reader.GetInt64(0),
                    RoleName = reader.GetString(1)
                });
            }

            return roles;
        }

        public async Task<Permission?> GetPermissionsByRoleIdAsync(long roleId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM permissions WHERE role_id = @roleId";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@roleId", roleId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Permission
                {
                    Id = reader.GetInt64(0),
                    RoleId = reader.GetInt64(1),
                    CanOcr = reader.GetInt32(2) == 1,
                    CanPurchase = reader.GetInt32(3) == 1,
                    CanEditStock = reader.GetInt32(4) == 1,
                    CanViewReport = reader.GetInt32(5) == 1,
                    CanManageUsers = reader.GetInt32(6) == 1
                };
            }

            return null;
        }

        public async Task<long> AddPermissionAsync(Permission permission)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO permissions (role_id, can_ocr, can_purchase, can_edit_stock, can_view_report, can_manage_users)
                VALUES (@roleId, @canOcr, @canPurchase, @canEditStock, @canViewReport, @canManageUsers);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@roleId", permission.RoleId);
            command.Parameters.AddWithValue("@canOcr", permission.CanOcr ? 1 : 0);
            command.Parameters.AddWithValue("@canPurchase", permission.CanPurchase ? 1 : 0);
            command.Parameters.AddWithValue("@canEditStock", permission.CanEditStock ? 1 : 0);
            command.Parameters.AddWithValue("@canViewReport", permission.CanViewReport ? 1 : 0);
            command.Parameters.AddWithValue("@canManageUsers", permission.CanManageUsers ? 1 : 0);

            return (long)await command.ExecuteScalarAsync();
        }

        public async Task<long> AddConversationSessionAsync(ConversationSession session)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO conversation_sessions (user_id, state, receipt_id, current_unknown_index, temp_product_name, updated_at)
                VALUES (@userId, @state, @receiptId, @currentUnknownIndex, @tempProductName, @updatedAt);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@userId", session.UserId);
            command.Parameters.AddWithValue("@state", session.State);
            command.Parameters.AddWithValue("@receiptId", session.ReceiptId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@currentUnknownIndex", session.CurrentUnknownIndex);
            command.Parameters.AddWithValue("@tempProductName", session.TempProductName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)await command.ExecuteScalarAsync();
        }

        public async Task<ConversationSession?> GetConversationSessionAsync(string userId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM conversation_sessions WHERE user_id = @userId";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ConversationSession
                {
                    Id = reader.GetInt64(0),
                    UserId = reader.GetString(1),
                    State = reader.GetString(2),
                    ReceiptId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CurrentUnknownIndex = reader.GetInt32(4),
                    TempProductName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    UpdatedAt = DateTime.Parse(reader.GetString(6))
                };
            }

            return null;
        }

        public async Task UpdateConversationSessionAsync(ConversationSession session)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE conversation_sessions
                SET state = @state, receipt_id = @receiptId, current_unknown_index = @currentUnknownIndex,
                    temp_product_name = @tempProductName, updated_at = @updatedAt
                WHERE user_id = @userId";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@userId", session.UserId);
            command.Parameters.AddWithValue("@state", session.State);
            command.Parameters.AddWithValue("@receiptId", session.ReceiptId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@currentUnknownIndex", session.CurrentUnknownIndex);
            command.Parameters.AddWithValue("@tempProductName", session.TempProductName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> AddInventoryLogAsync(InventoryLog log)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO inventory_logs (product_id, product_name, old_stock, new_stock, adjustment, reason, user_id, channel, timestamp)
                VALUES (@productId, @productName, @oldStock, @newStock, @adjustment, @reason, @userId, @channel, @timestamp);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@productId", log.ProductId);
            command.Parameters.AddWithValue("@productName", log.ProductName);
            command.Parameters.AddWithValue("@oldStock", log.OldStock);
            command.Parameters.AddWithValue("@newStock", log.NewStock);
            command.Parameters.AddWithValue("@adjustment", log.Adjustment);
            command.Parameters.AddWithValue("@reason", log.Reason);
            command.Parameters.AddWithValue("@userId", log.UserId);
            command.Parameters.AddWithValue("@channel", log.Channel);
            command.Parameters.AddWithValue("@timestamp", log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

            return (long)await command.ExecuteScalarAsync();
        }

        #endregion
    }
}
