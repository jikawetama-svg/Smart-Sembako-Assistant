using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService(string? dbPath = null)
        {
            if (string.IsNullOrEmpty(dbPath))
            {
                _dbPath = RuntimePaths.MemoryDatabasePath;
            }
            else
            {
                if (!Path.IsPathRooted(dbPath))
                {
                    _dbPath = RuntimePaths.ResolveWritablePath(dbPath, dbPath);
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

                string createInboundEventsTable = @"
                    CREATE TABLE IF NOT EXISTS inbound_events (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        channel TEXT NOT NULL,
                        sender_id TEXT NOT NULL,
                        message_key TEXT NOT NULL,
                        message_id TEXT,
                        correlation_id TEXT NOT NULL,
                        payload_hash TEXT,
                        app_instance_id TEXT,
                        source_app_instance_id TEXT,
                        source_machine_name TEXT,
                        raw_sender_jid TEXT,
                        resolved_sender_jid TEXT,
                        upsert_type TEXT,
                        original_upsert_type TEXT,
                        sidecar_started_at TEXT,
                        message_timestamp_ms INTEGER,
                        text TEXT,
                        status TEXT NOT NULL,
                        last_error TEXT,
                        received_at TEXT NOT NULL,
                        processed_at TEXT,
                        UNIQUE(channel, message_key)
                    )";

                string createOutboundMessagesTable = @"
                    CREATE TABLE IF NOT EXISTS outbound_messages (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        correlation_id TEXT NOT NULL,
                        channel TEXT NOT NULL,
                        recipient_id TEXT NOT NULL,
                        text TEXT NOT NULL,
                        parse_mode TEXT,
                        media_url TEXT,
                        menu_keyboard_type TEXT,
                        message_kind TEXT DEFAULT 'text',
                        template_name TEXT,
                        template_language_code TEXT,
                        template_body_parameter_count INTEGER DEFAULT 0,
                        requires_confirmation INTEGER DEFAULT 0,
                        app_instance_id TEXT,
                        source_inbound_message_id TEXT,
                        source_inbound_received_at TEXT,
                        expires_at TEXT,
                        outbound_source_type TEXT DEFAULT 'manual_admin',
                        status TEXT NOT NULL,
                        attempt_count INTEGER DEFAULT 0,
                        next_attempt_at TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        external_message_id TEXT,
                        last_error TEXT,
                        last_status_event_at TEXT
                    )";

                string createMessageStatusEventsTable = @"
                    CREATE TABLE IF NOT EXISTS message_status_events (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        channel TEXT NOT NULL,
                        correlation_id TEXT,
                        external_message_id TEXT,
                        status TEXT NOT NULL,
                        raw_payload TEXT,
                        recorded_at TEXT NOT NULL
                    )";

                string createAutomationExecutionsTable = @"
                    CREATE TABLE IF NOT EXISTS automation_executions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        correlation_id TEXT NOT NULL UNIQUE,
                        trigger_type TEXT NOT NULL,
                        channel TEXT NOT NULL,
                        sender_id TEXT NOT NULL,
                        user_role TEXT NOT NULL,
                        status TEXT NOT NULL,
                        matched_rules TEXT,
                        details TEXT,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";

                string createPendingConfirmationsTable = @"
                    CREATE TABLE IF NOT EXISTS pending_confirmations (
                        confirmation_key TEXT PRIMARY KEY,
                        command TEXT NOT NULL,
                        product_id TEXT NOT NULL,
                        product_name TEXT NOT NULL,
                        quantity REAL NOT NULL,
                        price REAL,
                        correlation_id TEXT,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";

                string createRuntimeStateTable = @"
                    CREATE TABLE IF NOT EXISTS runtime_state (
                        state_key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";

                string createProductAliasesTable = @"
                    CREATE TABLE IF NOT EXISTS product_aliases (
                        alias_name TEXT PRIMARY KEY,
                        product_id TEXT NOT NULL,
                        product_name TEXT,
                        source TEXT,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";

                string createOcrReviewQueueTable = @"
                    CREATE TABLE IF NOT EXISTS ocr_review_queue (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        receipt_correlation_id TEXT NOT NULL,
                        sender_id TEXT,
                        supplier_name TEXT,
                        receipt_date TEXT,
                        raw_product_name TEXT NOT NULL,
                        quantity REAL NOT NULL,
                        unit_price REAL NOT NULL,
                        line_total REAL NOT NULL,
                        unit TEXT,
                        isi_per_box INTEGER,
                        status TEXT NOT NULL,
                        candidate_summary TEXT,
                        note TEXT,
                        resolved_product_id TEXT,
                        resolved_product_name TEXT,
                        created_at TEXT NOT NULL,
                        resolved_at TEXT
                    )";

                string createUnitConversionMappingsTable = @"
                    CREATE TABLE IF NOT EXISTS unit_conversion_mappings (
                        id TEXT PRIMARY KEY,
                        parent_product_id TEXT NOT NULL,
                        parent_product_name TEXT,
                        child_product_id TEXT NOT NULL,
                        child_product_name TEXT,
                        conversion_rate REAL NOT NULL,
                        family_name TEXT,
                        notes TEXT,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";

                string createOcrSessionTable = @"
                    CREATE TABLE IF NOT EXISTS OcrSession (
                        Id TEXT PRIMARY KEY,
                        SenderId TEXT NOT NULL,
                        Channel TEXT NOT NULL,
                        SupplierName TEXT,
                        ReceiptNumber TEXT,
                        ReceiptDate TEXT,
                        ItemsJson TEXT,
                        PageCount INTEGER DEFAULT 0,
                        IsComplete INTEGER DEFAULT 0,
                        CreatedAt TEXT NOT NULL,
                        ExpiresAt TEXT NOT NULL
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

                using (var command = new SqliteCommand(createInboundEventsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createOutboundMessagesTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                EnsureColumnExists(connection, "outbound_messages", "message_kind", "TEXT DEFAULT 'text'");
                EnsureColumnExists(connection, "outbound_messages", "menu_keyboard_type", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "template_name", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "template_language_code", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "template_body_parameter_count", "INTEGER DEFAULT 0");
                EnsureColumnExists(connection, "outbound_messages", "app_instance_id", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "source_inbound_message_id", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "source_inbound_received_at", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "expires_at", "TEXT");
                EnsureColumnExists(connection, "outbound_messages", "outbound_source_type", "TEXT DEFAULT 'manual_admin'");
                EnsureColumnExists(connection, "inbound_events", "app_instance_id", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "source_app_instance_id", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "source_machine_name", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "raw_sender_jid", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "resolved_sender_jid", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "upsert_type", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "original_upsert_type", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "sidecar_started_at", "TEXT");
                EnsureColumnExists(connection, "inbound_events", "message_timestamp_ms", "INTEGER");
                EnsureColumnExists(connection, "unit_conversion_mappings", "family_name", "TEXT");
                EnsureColumnExists(connection, "unit_conversion_mappings", "notes", "TEXT");

                using (var command = new SqliteCommand(createMessageStatusEventsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createAutomationExecutionsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createPendingConfirmationsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createRuntimeStateTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createProductAliasesTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createOcrReviewQueueTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                EnsureColumnExists(connection, "ocr_review_queue", "isi_per_box", "INTEGER");

                using (var command = new SqliteCommand(createUnitConversionMappingsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand(createOcrSessionTable, connection))
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
                    "CREATE INDEX IF NOT EXISTS idx_conversation_sessions_user_id ON conversation_sessions(user_id)",
                    "CREATE INDEX IF NOT EXISTS idx_inbound_events_received_at ON inbound_events(received_at)",
                    "CREATE INDEX IF NOT EXISTS idx_inbound_events_message_id ON inbound_events(message_id)",
                    "CREATE INDEX IF NOT EXISTS idx_inbound_events_sender_received ON inbound_events(sender_id, received_at)",
                    "CREATE INDEX IF NOT EXISTS idx_outbound_messages_status_attempt ON outbound_messages(status, next_attempt_at)",
                    "CREATE INDEX IF NOT EXISTS idx_outbound_messages_correlation_recipient ON outbound_messages(correlation_id, channel, recipient_id, status)",
                    "CREATE INDEX IF NOT EXISTS idx_outbound_messages_external_id ON outbound_messages(external_message_id)",
                    "CREATE INDEX IF NOT EXISTS idx_message_status_events_external_id ON message_status_events(external_message_id)",
                    "CREATE INDEX IF NOT EXISTS idx_automation_executions_created_at ON automation_executions(created_at)",
                    "CREATE INDEX IF NOT EXISTS idx_product_aliases_product_id ON product_aliases(product_id)",
                    "CREATE INDEX IF NOT EXISTS idx_ocr_review_queue_status_created ON ocr_review_queue(status, created_at)",
                    "CREATE INDEX IF NOT EXISTS idx_ocr_review_queue_correlation ON ocr_review_queue(receipt_correlation_id)",
                    "CREATE UNIQUE INDEX IF NOT EXISTS idx_unit_conversion_parent ON unit_conversion_mappings(parent_product_id)",
                    "CREATE INDEX IF NOT EXISTS idx_unit_conversion_child ON unit_conversion_mappings(child_product_id)",
                    "CREATE INDEX IF NOT EXISTS idx_ocr_session_sender_channel ON OcrSession(SenderId, Channel, IsComplete, ExpiresAt)"
                };

                foreach (string indexSql in createIndexes)
                {
                    using var command = new SqliteCommand(indexSql, connection);
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand("DELETE FROM OcrSession WHERE ExpiresAt < @now OR IsComplete = 1", connection))
                {
                    command.Parameters.AddWithValue("@now", ToDbTimestamp(DateTime.Now));
                    command.ExecuteNonQuery();
                }

                CleanupOldRuntimeData(connection);
            }
            catch (Exception ex)
            {
                throw new Exception($"Gagal inisialisasi database: {ex.Message}");
            }
        }

        private static string ToDbTimestamp(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static void CleanupOldRuntimeData(SqliteConnection connection)
        {
            ExecuteCleanup(
                connection,
                "DELETE FROM inbound_events WHERE processed_at IS NOT NULL AND processed_at < @cutoff",
                DateTime.Now.AddDays(-7));
            ExecuteCleanup(
                connection,
                "DELETE FROM outbound_messages WHERE status IN ('sent', 'dead_letter') AND updated_at < @cutoff",
                DateTime.Now.AddDays(-7));
            ExecuteCleanup(
                connection,
                "DELETE FROM logs WHERE level = 'Info' AND timestamp < @cutoff",
                DateTime.Now.AddDays(-30));
            ExecuteCleanup(
                connection,
                "DELETE FROM logs WHERE level IN ('Error', 'Warning') AND timestamp < @cutoff",
                DateTime.Now.AddDays(-90));
            ExecuteCleanup(
                connection,
                "DELETE FROM conversations WHERE timestamp < @cutoff",
                DateTime.Now.AddDays(-60));
        }

        private static void ExecuteCleanup(SqliteConnection connection, string sql, DateTime cutoff)
        {
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@cutoff", ToDbTimestamp(cutoff));
            command.ExecuteNonQuery();
        }

        private static DateTime ParseDbTimestamp(string? value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed : DateTime.Now;
        }

        private static string NormalizeAliasKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            bool lastWasSpace = false;
            foreach (char ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    lastWasSpace = false;
                }
                else if (char.IsWhiteSpace(ch) && !lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }

        private static OcrReviewQueueItem ReadOcrReviewQueueItem(SqliteDataReader reader)
        {
            string? supplierName = reader.IsDBNull(3) ? null : reader.GetString(3);
            decimal quantity = reader.GetDecimal(6);
            decimal unitPrice = reader.GetDecimal(7);
            decimal lineTotal = reader.GetDecimal(8);

            if (IsWingsSupplier(supplierName) && quantity > 0 && lineTotal > 0)
            {
                unitPrice = lineTotal / quantity;
            }

            return new OcrReviewQueueItem
            {
                Id = reader.GetInt64(0),
                ReceiptCorrelationId = reader.GetString(1),
                SenderId = reader.IsDBNull(2) ? null : reader.GetString(2),
                SupplierName = supplierName,
                ReceiptDate = reader.IsDBNull(4) ? null : ParseDbTimestamp(reader.GetString(4)),
                RawProductName = reader.GetString(5),
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
                Unit = reader.IsDBNull(9) ? null : reader.GetString(9),
                IsiPerBox = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Status = reader.GetString(11),
                CandidateSummary = reader.IsDBNull(12) ? null : reader.GetString(12),
                Note = reader.IsDBNull(13) ? null : reader.GetString(13),
                ResolvedProductId = reader.IsDBNull(14) ? null : reader.GetString(14),
                ResolvedProductName = reader.IsDBNull(15) ? null : reader.GetString(15),
                CreatedAt = ParseDbTimestamp(reader.GetString(16)),
                ResolvedAt = reader.IsDBNull(17) ? null : ParseDbTimestamp(reader.GetString(17))
            };
        }

        private static bool IsWingsSupplier(string? supplierName)
        {
            return !string.IsNullOrWhiteSpace(supplierName) &&
                   (supplierName.Contains("WINGS", StringComparison.OrdinalIgnoreCase) ||
                    supplierName.Contains("SAYAP MAS", StringComparison.OrdinalIgnoreCase));
        }

        private static UnitConversionMapping ReadUnitConversionMapping(SqliteDataReader reader)
        {
            return new UnitConversionMapping
            {
                Id = reader.GetString(0),
                ParentProductId = reader.GetString(1),
                ParentProductName = reader.IsDBNull(2) ? null : reader.GetString(2),
                ChildProductId = reader.GetString(3),
                ChildProductName = reader.IsDBNull(4) ? null : reader.GetString(4),
                ConversionRate = reader.GetDecimal(5),
                FamilyName = reader.IsDBNull(6) ? null : reader.GetString(6),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = ParseDbTimestamp(reader.GetString(8)),
                UpdatedAt = ParseDbTimestamp(reader.GetString(9))
            };
        }

        private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
        {
            using (var checkCommand = new SqliteCommand($"PRAGMA table_info({tableName})", connection))
            using (var reader = checkCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            using var alterCommand = new SqliteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}", connection);
            alterCommand.ExecuteNonQuery();
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

        public async Task<RbacUser?> GetUserByTelegramIdAsync(string telegramId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM users WHERE telegram_id = @telegramId AND is_active = 1";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@telegramId", telegramId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new RbacUser
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

        public async Task<RbacUser?> GetUserByWhatsappNumberAsync(string whatsappNumber)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = "SELECT * FROM users WHERE whatsapp_number = @whatsappNumber AND is_active = 1";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@whatsappNumber", whatsappNumber);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new RbacUser
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
            var users = new List<RbacUser>();

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

        #region Automation Runtime Methods

        public async Task<bool> TryRegisterInboundEventAsync(InboundMessage message, string correlationId)
        {
            string messageKey = !string.IsNullOrWhiteSpace(message.MessageId)
                ? message.MessageId!
                : !string.IsNullOrWhiteSpace(message.PayloadHash)
                    ? message.PayloadHash!
                    : Guid.NewGuid().ToString("N");

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO inbound_events (
                    channel, sender_id, message_key, message_id, correlation_id, payload_hash, app_instance_id,
                    source_app_instance_id, source_machine_name, raw_sender_jid, resolved_sender_jid,
                    upsert_type, original_upsert_type, sidecar_started_at, message_timestamp_ms,
                    text, status, received_at
                )
                VALUES (
                    @channel, @sender_id, @message_key, @message_id, @correlation_id, @payload_hash, @app_instance_id,
                    @source_app_instance_id, @source_machine_name, @raw_sender_jid, @resolved_sender_jid,
                    @upsert_type, @original_upsert_type, @sidecar_started_at, @message_timestamp_ms,
                    @text, @status, @received_at
                );";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@channel", message.Channel.ToString());
            command.Parameters.AddWithValue("@sender_id", message.SenderId);
            command.Parameters.AddWithValue("@message_key", messageKey);
            command.Parameters.AddWithValue("@message_id", message.MessageId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@correlation_id", correlationId);
            command.Parameters.AddWithValue("@payload_hash", message.PayloadHash ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@app_instance_id", message.AppInstanceId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@source_app_instance_id", message.SourceAppInstanceId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@source_machine_name", message.SourceMachineName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@raw_sender_jid", message.RawSenderJid ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@resolved_sender_jid", message.ResolvedSenderJid ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@upsert_type", message.UpsertType ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@original_upsert_type", message.OriginalUpsertType ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sidecar_started_at", message.SidecarStartedAt.HasValue ? ToDbTimestamp(message.SidecarStartedAt.Value) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@message_timestamp_ms", message.MessageTimestampMs.HasValue ? message.MessageTimestampMs.Value : (object)DBNull.Value);
            command.Parameters.AddWithValue("@text", message.Text ?? string.Empty);
            command.Parameters.AddWithValue("@status", "received");
            command.Parameters.AddWithValue("@received_at", ToDbTimestamp(message.Timestamp == default ? DateTime.Now : message.Timestamp));

            try
            {
                await command.ExecuteNonQueryAsync();
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return false;
            }
        }

        public async Task MarkInboundEventProcessedAsync(InboundMessage message, string status, string? error = null)
        {
            string messageKey = !string.IsNullOrWhiteSpace(message.MessageId)
                ? message.MessageId!
                : !string.IsNullOrWhiteSpace(message.PayloadHash)
                    ? message.PayloadHash!
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(messageKey))
            {
                return;
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE inbound_events
                SET status = @status,
                    last_error = @last_error,
                    processed_at = @processed_at
                WHERE channel = @channel AND message_key = @message_key";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@last_error", error ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@processed_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@channel", message.Channel.ToString());
            command.Parameters.AddWithValue("@message_key", messageKey);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> QueueOutboundMessageAsync(OutboundMessage message)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            DateTime now = DateTime.Now;
            string sql = @"
                INSERT INTO outbound_messages (
                    correlation_id, channel, recipient_id, text, parse_mode, media_url,
                    menu_keyboard_type, message_kind, template_name, template_language_code, template_body_parameter_count, requires_confirmation, app_instance_id,
                    source_inbound_message_id, source_inbound_received_at, expires_at, outbound_source_type,
                    status, attempt_count, next_attempt_at, created_at, updated_at
                )
                VALUES (
                    @correlation_id, @channel, @recipient_id, @text, @parse_mode, @media_url,
                    @menu_keyboard_type, @message_kind, @template_name, @template_language_code, @template_body_parameter_count, @requires_confirmation, @app_instance_id,
                    @source_inbound_message_id, @source_inbound_received_at, @expires_at, @outbound_source_type,
                    @status, 0, @next_attempt_at, @created_at, @updated_at
                );
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@correlation_id", message.CorrelationId ?? Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("@channel", message.Channel.ToString());
            command.Parameters.AddWithValue("@recipient_id", message.RecipientId);
            command.Parameters.AddWithValue("@text", message.Text);
            command.Parameters.AddWithValue("@parse_mode", message.ParseMode ?? string.Empty);
            command.Parameters.AddWithValue("@media_url", message.MediaUrl ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@menu_keyboard_type", message.MenuKeyboardType ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@message_kind", string.IsNullOrWhiteSpace(message.MessageKind) ? "text" : message.MessageKind);
            command.Parameters.AddWithValue("@template_name", message.TemplateName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@template_language_code", message.TemplateLanguageCode ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@template_body_parameter_count", Math.Max(0, message.TemplateBodyParameterCount));
            command.Parameters.AddWithValue("@requires_confirmation", message.RequiresConfirmation ? 1 : 0);
            command.Parameters.AddWithValue("@app_instance_id", message.AppInstanceId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@source_inbound_message_id", message.SourceInboundMessageId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@source_inbound_received_at", message.SourceInboundReceivedAt.HasValue ? ToDbTimestamp(message.SourceInboundReceivedAt.Value) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@expires_at", message.ExpiresAt.HasValue ? ToDbTimestamp(message.ExpiresAt.Value) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@outbound_source_type", string.IsNullOrWhiteSpace(message.OutboundSourceType) ? "manual_admin" : message.OutboundSourceType);
            command.Parameters.AddWithValue("@status", "queued");
            command.Parameters.AddWithValue("@next_attempt_at", ToDbTimestamp(now));
            command.Parameters.AddWithValue("@created_at", ToDbTimestamp(now));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(now));

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public async Task<List<OutboundMessageRecord>> GetDueOutboundMessagesAsync(string? channel = null, int limit = 20)
        {
            var messages = new List<OutboundMessageRecord>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                SELECT id, correlation_id, channel, recipient_id, text, parse_mode, media_url,
                       menu_keyboard_type, message_kind, template_name, template_language_code, template_body_parameter_count, requires_confirmation, app_instance_id,
                       source_inbound_message_id, source_inbound_received_at, expires_at, outbound_source_type,
                       status, attempt_count, next_attempt_at, created_at, updated_at, external_message_id,
                       last_error, last_status_event_at
                FROM outbound_messages
                WHERE status IN ('queued', 'retry')
                  AND next_attempt_at <= @now";

            if (!string.IsNullOrWhiteSpace(channel))
            {
                sql += " AND channel = @channel";
            }

            sql += " ORDER BY created_at ASC LIMIT @limit";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@now", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrWhiteSpace(channel))
            {
                command.Parameters.AddWithValue("@channel", channel);
            }

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(new OutboundMessageRecord
                {
                    Id = reader.GetInt64(0),
                    CorrelationId = reader.GetString(1),
                    Channel = Enum.TryParse<ChannelType>(reader.GetString(2), out var channelType) ? channelType : ChannelType.System,
                    RecipientId = reader.GetString(3),
                    Text = reader.GetString(4),
                    ParseMode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    MediaUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                    MenuKeyboardType = reader.IsDBNull(7) ? null : reader.GetString(7),
                    MessageKind = reader.IsDBNull(8) ? "text" : reader.GetString(8),
                    TemplateName = reader.IsDBNull(9) ? null : reader.GetString(9),
                    TemplateLanguageCode = reader.IsDBNull(10) ? null : reader.GetString(10),
                    TemplateBodyParameterCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    RequiresConfirmation = reader.GetInt32(12) == 1,
                    AppInstanceId = reader.IsDBNull(13) ? null : reader.GetString(13),
                    SourceInboundMessageId = reader.IsDBNull(14) ? null : reader.GetString(14),
                    SourceInboundReceivedAt = reader.IsDBNull(15) ? null : ParseDbTimestamp(reader.GetString(15)),
                    ExpiresAt = reader.IsDBNull(16) ? null : ParseDbTimestamp(reader.GetString(16)),
                    OutboundSourceType = reader.IsDBNull(17) ? "manual_admin" : reader.GetString(17),
                    Status = reader.GetString(18),
                    AttemptCount = reader.GetInt32(19),
                    NextAttemptAt = ParseDbTimestamp(reader.GetString(20)),
                    CreatedAt = ParseDbTimestamp(reader.GetString(21)),
                    UpdatedAt = ParseDbTimestamp(reader.GetString(22)),
                    ExternalMessageId = reader.IsDBNull(23) ? null : reader.GetString(23),
                    LastError = reader.IsDBNull(24) ? null : reader.GetString(24),
                    LastStatusEventAt = reader.IsDBNull(25) ? null : ParseDbTimestamp(reader.GetString(25))
                });
            }

            return messages;
        }

        public async Task<bool> HasSentOutboundForCorrelationRecipientAsync(OutboundMessageRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.CorrelationId) || string.IsNullOrWhiteSpace(record.RecipientId))
            {
                return false;
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                SELECT COUNT(1)
                FROM outbound_messages
                WHERE id <> @id
                  AND correlation_id = @correlation_id
                  AND channel = @channel
                  AND recipient_id = @recipient_id
                  AND status = 'sent'";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@id", record.Id);
            command.Parameters.AddWithValue("@correlation_id", record.CorrelationId);
            command.Parameters.AddWithValue("@channel", record.Channel.ToString());
            command.Parameters.AddWithValue("@recipient_id", record.RecipientId);
            long count = (long)(await command.ExecuteScalarAsync() ?? 0L);
            return count > 0;
        }

        public async Task MarkOutboundSentAsync(long id, string? externalMessageId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE outbound_messages
                SET status = 'sent',
                    external_message_id = COALESCE(@external_message_id, external_message_id),
                    last_error = NULL,
                    updated_at = @updated_at
                WHERE id = @id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@external_message_id", externalMessageId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task MarkOutboundRetryAsync(long id, string error, DateTime nextAttemptAt)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE outbound_messages
                SET status = 'retry',
                    attempt_count = attempt_count + 1,
                    last_error = @last_error,
                    next_attempt_at = @next_attempt_at,
                    updated_at = @updated_at
                WHERE id = @id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@last_error", error);
            command.Parameters.AddWithValue("@next_attempt_at", ToDbTimestamp(nextAttemptAt));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeferOutboundMessageAsync(long id, string reason, DateTime nextAttemptAt)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE outbound_messages
                SET status = 'queued',
                    last_error = @last_error,
                    next_attempt_at = @next_attempt_at,
                    updated_at = @updated_at
                WHERE id = @id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@last_error", reason);
            command.Parameters.AddWithValue("@next_attempt_at", ToDbTimestamp(nextAttemptAt));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task MarkOutboundDeadLetterAsync(long id, string error)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE outbound_messages
                SET status = 'dead_letter',
                    attempt_count = attempt_count + 1,
                    last_error = @last_error,
                    updated_at = @updated_at
                WHERE id = @id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@last_error", error);
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task RecordMessageStatusEventAsync(MessageStatusEventRecord statusEvent)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string insertSql = @"
                INSERT INTO message_status_events (
                    channel, correlation_id, external_message_id, status, raw_payload, recorded_at
                )
                VALUES (
                    @channel, @correlation_id, @external_message_id, @status, @raw_payload, @recorded_at
                )";

            using (var insertCommand = new SqliteCommand(insertSql, connection))
            {
                insertCommand.Parameters.AddWithValue("@channel", statusEvent.Channel);
                insertCommand.Parameters.AddWithValue("@correlation_id", statusEvent.CorrelationId ?? (object)DBNull.Value);
                insertCommand.Parameters.AddWithValue("@external_message_id", statusEvent.ExternalMessageId ?? (object)DBNull.Value);
                insertCommand.Parameters.AddWithValue("@status", statusEvent.Status);
                insertCommand.Parameters.AddWithValue("@raw_payload", statusEvent.RawPayload ?? (object)DBNull.Value);
                insertCommand.Parameters.AddWithValue("@recorded_at", ToDbTimestamp(statusEvent.RecordedAt));
                await insertCommand.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrWhiteSpace(statusEvent.ExternalMessageId))
            {
                string updateSql = @"
                    UPDATE outbound_messages
                    SET status = @status,
                        last_error = COALESCE(@last_error, last_error),
                        last_status_event_at = @recorded_at,
                        updated_at = @recorded_at
                    WHERE external_message_id = @external_message_id";

                using var updateCommand = new SqliteCommand(updateSql, connection);
                updateCommand.Parameters.AddWithValue("@status", statusEvent.Status);
                updateCommand.Parameters.AddWithValue("@last_error", statusEvent.ErrorDetails ?? (object)DBNull.Value);
                updateCommand.Parameters.AddWithValue("@recorded_at", ToDbTimestamp(statusEvent.RecordedAt));
                updateCommand.Parameters.AddWithValue("@external_message_id", statusEvent.ExternalMessageId);
                await updateCommand.ExecuteNonQueryAsync();
            }
        }

        public async Task<long> AddAutomationExecutionAsync(AutomationExecutionRecord execution)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO automation_executions (
                    correlation_id, trigger_type, channel, sender_id, user_role, status, matched_rules, details, created_at, updated_at
                )
                VALUES (
                    @correlation_id, @trigger_type, @channel, @sender_id, @user_role, @status, @matched_rules, @details, @created_at, @updated_at
                );
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@correlation_id", execution.CorrelationId);
            command.Parameters.AddWithValue("@trigger_type", execution.TriggerType);
            command.Parameters.AddWithValue("@channel", execution.Channel);
            command.Parameters.AddWithValue("@sender_id", execution.SenderId);
            command.Parameters.AddWithValue("@user_role", execution.UserRole);
            command.Parameters.AddWithValue("@status", execution.Status);
            command.Parameters.AddWithValue("@matched_rules", execution.MatchedRules ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@details", execution.Details ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@created_at", ToDbTimestamp(execution.CreatedAt));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(execution.UpdatedAt));

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        public async Task UpdateAutomationExecutionAsync(string correlationId, string status, string? matchedRules = null, string? details = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                UPDATE automation_executions
                SET status = @status,
                    matched_rules = COALESCE(@matched_rules, matched_rules),
                    details = COALESCE(@details, details),
                    updated_at = @updated_at
                WHERE correlation_id = @correlation_id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@matched_rules", matchedRules ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@details", details ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@correlation_id", correlationId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task SavePendingConfirmationAsync(PendingConfirmation confirmation)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO pending_confirmations (
                    confirmation_key, command, product_id, product_name, quantity, price, correlation_id, created_at, updated_at
                )
                VALUES (
                    @confirmation_key, @command, @product_id, @product_name, @quantity, @price, @correlation_id, @created_at, @updated_at
                )
                ON CONFLICT(confirmation_key) DO UPDATE SET
                    command = excluded.command,
                    product_id = excluded.product_id,
                    product_name = excluded.product_name,
                    quantity = excluded.quantity,
                    price = excluded.price,
                    correlation_id = excluded.correlation_id,
                    updated_at = excluded.updated_at";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@confirmation_key", confirmation.Key);
            command.Parameters.AddWithValue("@command", confirmation.Command);
            command.Parameters.AddWithValue("@product_id", confirmation.ProductId);
            command.Parameters.AddWithValue("@product_name", confirmation.ProductName);
            command.Parameters.AddWithValue("@quantity", confirmation.Quantity);
            command.Parameters.AddWithValue("@price", confirmation.Price ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@correlation_id", confirmation.CorrelationId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@created_at", ToDbTimestamp(confirmation.CreatedAt));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(confirmation.UpdatedAt));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<PendingConfirmation?> GetPendingConfirmationAsync(string key)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                SELECT confirmation_key, command, product_id, product_name, quantity, price, correlation_id, created_at, updated_at
                FROM pending_confirmations
                WHERE confirmation_key = @confirmation_key";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@confirmation_key", key);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PendingConfirmation
                {
                    Key = reader.GetString(0),
                    Command = reader.GetString(1),
                    ProductId = reader.GetString(2),
                    ProductName = reader.GetString(3),
                    Quantity = reader.GetDecimal(4),
                    Price = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    CorrelationId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    CreatedAt = ParseDbTimestamp(reader.GetString(7)),
                    UpdatedAt = ParseDbTimestamp(reader.GetString(8))
                };
            }

            return null;
        }

        public async Task DeletePendingConfirmationAsync(string key)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var command = new SqliteCommand("DELETE FROM pending_confirmations WHERE confirmation_key = @confirmation_key", connection);
            command.Parameters.AddWithValue("@confirmation_key", key);
            await command.ExecuteNonQueryAsync();
        }

        public async Task UpsertProductAliasAsync(ProductAliasEntry alias)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string normalizedAlias = NormalizeAliasKey(alias.AliasName);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                return;
            }

            string sql = @"
                INSERT INTO product_aliases (
                    alias_name, product_id, product_name, source, created_at, updated_at
                )
                VALUES (
                    @alias_name, @product_id, @product_name, @source, @created_at, @updated_at
                )
                ON CONFLICT(alias_name) DO UPDATE SET
                    product_id = excluded.product_id,
                    product_name = excluded.product_name,
                    source = excluded.source,
                    updated_at = excluded.updated_at";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@alias_name", normalizedAlias);
            command.Parameters.AddWithValue("@product_id", alias.ProductId);
            command.Parameters.AddWithValue("@product_name", alias.ProductName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@source", alias.Source ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@created_at", ToDbTimestamp(alias.CreatedAt));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(alias.UpdatedAt));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<ProductAliasEntry?> GetProductAliasAsync(string aliasName)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string normalizedAlias = NormalizeAliasKey(aliasName);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                return null;
            }

            const string sql = @"
                SELECT alias_name, product_id, product_name, source, created_at, updated_at
                FROM product_aliases
                WHERE alias_name = @alias_name";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@alias_name", normalizedAlias);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ProductAliasEntry
                {
                    AliasName = reader.GetString(0),
                    ProductId = reader.GetString(1),
                    ProductName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Source = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CreatedAt = ParseDbTimestamp(reader.GetString(4)),
                    UpdatedAt = ParseDbTimestamp(reader.GetString(5))
                };
            }

            return null;
        }

        public async Task DeleteProductAliasAsync(string aliasName)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string normalizedAlias = NormalizeAliasKey(aliasName);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                return;
            }

            using var command = new SqliteCommand("DELETE FROM product_aliases WHERE alias_name = @alias_name", connection);
            command.Parameters.AddWithValue("@alias_name", normalizedAlias);
            await command.ExecuteNonQueryAsync();
        }

        public async Task AddOcrReviewQueueItemsAsync(IEnumerable<OcrReviewQueueItem> items)
        {
            var materialized = items?.ToList() ?? new List<OcrReviewQueueItem>();
            if (!materialized.Any())
            {
                return;
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            const string sql = @"
                INSERT INTO ocr_review_queue (
                    receipt_correlation_id, sender_id, supplier_name, receipt_date, raw_product_name, quantity, unit_price,
                    line_total, unit, isi_per_box, status, candidate_summary, note, resolved_product_id, resolved_product_name, created_at, resolved_at
                )
                VALUES (
                    @receipt_correlation_id, @sender_id, @supplier_name, @receipt_date, @raw_product_name, @quantity, @unit_price,
                    @line_total, @unit, @isi_per_box, @status, @candidate_summary, @note, @resolved_product_id, @resolved_product_name, @created_at, @resolved_at
                )";

            foreach (var item in materialized)
            {
                using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@receipt_correlation_id", item.ReceiptCorrelationId);
                command.Parameters.AddWithValue("@sender_id", item.SenderId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@supplier_name", item.SupplierName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@receipt_date", item.ReceiptDate.HasValue ? ToDbTimestamp(item.ReceiptDate.Value) : (object)DBNull.Value);
                command.Parameters.AddWithValue("@raw_product_name", item.RawProductName);
                command.Parameters.AddWithValue("@quantity", item.Quantity);
                command.Parameters.AddWithValue("@unit_price", item.UnitPrice);
                command.Parameters.AddWithValue("@line_total", item.LineTotal);
                command.Parameters.AddWithValue("@unit", item.Unit ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@isi_per_box", item.IsiPerBox.HasValue ? item.IsiPerBox.Value : (object)DBNull.Value);
                command.Parameters.AddWithValue("@status", item.Status);
                command.Parameters.AddWithValue("@candidate_summary", item.CandidateSummary ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@note", item.Note ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@resolved_product_id", item.ResolvedProductId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@resolved_product_name", item.ResolvedProductName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@created_at", ToDbTimestamp(item.CreatedAt));
                command.Parameters.AddWithValue("@resolved_at", item.ResolvedAt.HasValue ? ToDbTimestamp(item.ResolvedAt.Value) : (object)DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task<List<OcrReviewQueueItem>> GetPendingOcrReviewQueueItemsAsync()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT id, receipt_correlation_id, sender_id, supplier_name, receipt_date, raw_product_name, quantity, unit_price,
                       line_total, unit, isi_per_box, status, candidate_summary, note, resolved_product_id, resolved_product_name, created_at, resolved_at
                FROM ocr_review_queue
                WHERE status = 'pending'
                ORDER BY created_at DESC, id DESC";

            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            var results = new List<OcrReviewQueueItem>();

            while (await reader.ReadAsync())
            {
                results.Add(ReadOcrReviewQueueItem(reader));
            }

            return results;
        }

        public async Task ResolveOcrReviewQueueItemAsync(long id, string productId, string productName, string? note = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                UPDATE ocr_review_queue
                SET status = 'resolved',
                    resolved_product_id = @resolved_product_id,
                    resolved_product_name = @resolved_product_name,
                    note = COALESCE(@note, note),
                    resolved_at = @resolved_at
                WHERE id = @id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@resolved_product_id", productId);
            command.Parameters.AddWithValue("@resolved_product_name", productName);
            command.Parameters.AddWithValue("@note", note ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@resolved_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteOcrReviewQueueItemAsync(long id)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var command = new SqliteCommand("DELETE FROM ocr_review_queue WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task CreateOcrSessionAsync(OcrSession session)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO OcrSession (
                    Id, SenderId, Channel, SupplierName, ReceiptNumber, ReceiptDate, ItemsJson, PageCount, IsComplete, CreatedAt, ExpiresAt
                ) VALUES (
                    @Id, @SenderId, @Channel, @SupplierName, @ReceiptNumber, @ReceiptDate, @ItemsJson, @PageCount, @IsComplete, @CreatedAt, @ExpiresAt
                )";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", session.Id);
            command.Parameters.AddWithValue("@SenderId", session.SenderId);
            command.Parameters.AddWithValue("@Channel", session.Channel);
            command.Parameters.AddWithValue("@SupplierName", session.SupplierName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ReceiptNumber", session.ReceiptNumber ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ReceiptDate", session.ReceiptDate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ItemsJson", session.ItemsJson ?? "[]");
            command.Parameters.AddWithValue("@PageCount", session.PageCount);
            command.Parameters.AddWithValue("@IsComplete", session.IsComplete ? 1 : 0);
            command.Parameters.AddWithValue("@CreatedAt", ToDbTimestamp(session.CreatedAt));
            command.Parameters.AddWithValue("@ExpiresAt", ToDbTimestamp(session.ExpiresAt));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<OcrSession?> GetActiveOcrSessionAsync(string senderId, string channel)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT Id, SenderId, Channel, SupplierName, ReceiptNumber, ReceiptDate, ItemsJson, PageCount, IsComplete, CreatedAt, ExpiresAt
                FROM OcrSession
                WHERE SenderId = @SenderId
                  AND Channel = @Channel
                  AND IsComplete = 0
                  AND ExpiresAt >= @Now
                ORDER BY CreatedAt DESC
                LIMIT 1";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@SenderId", senderId);
            command.Parameters.AddWithValue("@Channel", channel);
            command.Parameters.AddWithValue("@Now", ToDbTimestamp(DateTime.Now));
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new OcrSession
            {
                Id = reader.GetString(0),
                SenderId = reader.GetString(1),
                Channel = reader.GetString(2),
                SupplierName = reader.IsDBNull(3) ? null : reader.GetString(3),
                ReceiptNumber = reader.IsDBNull(4) ? null : reader.GetString(4),
                ReceiptDate = reader.IsDBNull(5) ? null : reader.GetString(5),
                ItemsJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6),
                PageCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                IsComplete = !reader.IsDBNull(8) && reader.GetInt32(8) == 1,
                CreatedAt = ParseDbTimestamp(reader.GetString(9)),
                ExpiresAt = ParseDbTimestamp(reader.GetString(10))
            };
        }

        public async Task AppendOcrSessionItemsAsync(string sessionId, IEnumerable<ReceiptItem> newItems, int pageCount, bool isComplete, string? supplierName = null, string? receiptNumber = null, string? receiptDate = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            var existingItems = new List<ReceiptItem>();
            using (var selectCommand = new SqliteCommand("SELECT ItemsJson FROM OcrSession WHERE Id = @Id LIMIT 1", connection))
            {
                selectCommand.Parameters.AddWithValue("@Id", sessionId);
                string? existingJson = (await selectCommand.ExecuteScalarAsync())?.ToString();
                if (!string.IsNullOrWhiteSpace(existingJson))
                {
                    existingItems = JsonSerializer.Deserialize<List<ReceiptItem>>(existingJson) ?? new List<ReceiptItem>();
                }
            }

            existingItems.AddRange(newItems ?? Enumerable.Empty<ReceiptItem>());
            string mergedItemsJson = JsonSerializer.Serialize(existingItems);

            const string sql = @"
                UPDATE OcrSession
                SET ItemsJson = @ItemsJson,
                    PageCount = @PageCount,
                    IsComplete = @IsComplete,
                    SupplierName = COALESCE(@SupplierName, SupplierName),
                    ReceiptNumber = COALESCE(@ReceiptNumber, ReceiptNumber),
                    ReceiptDate = COALESCE(@ReceiptDate, ReceiptDate),
                    ExpiresAt = @ExpiresAt
                WHERE Id = @Id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", sessionId);
            command.Parameters.AddWithValue("@ItemsJson", mergedItemsJson);
            command.Parameters.AddWithValue("@PageCount", pageCount);
            command.Parameters.AddWithValue("@IsComplete", isComplete ? 1 : 0);
            command.Parameters.AddWithValue("@SupplierName", supplierName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ReceiptNumber", receiptNumber ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ReceiptDate", receiptDate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ExpiresAt", ToDbTimestamp(DateTime.Now.AddMinutes(30)));
            await command.ExecuteNonQueryAsync();
        }

        public async Task CompleteOcrSessionAsync(string sessionId)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                UPDATE OcrSession
                SET IsComplete = 1,
                    ExpiresAt = @ExpiresAt
                WHERE Id = @Id";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", sessionId);
            command.Parameters.AddWithValue("@ExpiresAt", ToDbTimestamp(DateTime.Now));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<UnitConversionMapping>> GetAllUnitConversionsAsync()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT id, parent_product_id, parent_product_name, child_product_id, child_product_name, conversion_rate, family_name, notes, created_at, updated_at
                FROM unit_conversion_mappings
                ORDER BY COALESCE(parent_product_name, ''), parent_product_id";

            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            var results = new List<UnitConversionMapping>();
            while (await reader.ReadAsync())
            {
                results.Add(ReadUnitConversionMapping(reader));
            }

            return results;
        }

        public async Task<UnitConversionMapping?> GetConversionByParentIdAsync(string parentProductId)
        {
            if (string.IsNullOrWhiteSpace(parentProductId))
            {
                return null;
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            const string sql = @"
                SELECT id, parent_product_id, parent_product_name, child_product_id, child_product_name, conversion_rate, family_name, notes, created_at, updated_at
                FROM unit_conversion_mappings
                WHERE parent_product_id = @parent_product_id
                LIMIT 1";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@parent_product_id", parentProductId.Trim());
            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync()
                ? ReadUnitConversionMapping(reader)
                : null;
        }

        public async Task UpsertUnitConversionAsync(UnitConversionMapping mapping)
        {
            if (mapping == null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            if (string.IsNullOrWhiteSpace(mapping.ParentProductId))
            {
                throw new InvalidOperationException("ParentProductId wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(mapping.ChildProductId))
            {
                throw new InvalidOperationException("ChildProductId wajib diisi.");
            }

            if (mapping.ConversionRate <= 0)
            {
                throw new InvalidOperationException("ConversionRate harus lebih dari 0.");
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string id = string.IsNullOrWhiteSpace(mapping.Id)
                ? Guid.NewGuid().ToString("N")
                : mapping.Id.Trim();

            DateTime createdAt = mapping.CreatedAt == default ? DateTime.Now : mapping.CreatedAt;
            DateTime updatedAt = mapping.UpdatedAt == default ? DateTime.Now : mapping.UpdatedAt;

            const string sql = @"
                INSERT INTO unit_conversion_mappings (
                    id, parent_product_id, parent_product_name, child_product_id, child_product_name, conversion_rate, family_name, notes, created_at, updated_at
                )
                VALUES (
                    @id, @parent_product_id, @parent_product_name, @child_product_id, @child_product_name, @conversion_rate, @family_name, @notes, @created_at, @updated_at
                )
                ON CONFLICT(parent_product_id) DO UPDATE SET
                    id = excluded.id,
                    parent_product_name = excluded.parent_product_name,
                    child_product_id = excluded.child_product_id,
                    child_product_name = excluded.child_product_name,
                    conversion_rate = excluded.conversion_rate,
                    family_name = excluded.family_name,
                    notes = excluded.notes,
                    updated_at = excluded.updated_at";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@parent_product_id", mapping.ParentProductId.Trim());
            command.Parameters.AddWithValue("@parent_product_name", mapping.ParentProductName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@child_product_id", mapping.ChildProductId.Trim());
            command.Parameters.AddWithValue("@child_product_name", mapping.ChildProductName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@conversion_rate", mapping.ConversionRate);
            command.Parameters.AddWithValue("@family_name", mapping.FamilyName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@notes", mapping.Notes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@created_at", ToDbTimestamp(createdAt));
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(updatedAt));
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteUnitConversionAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var command = new SqliteCommand("DELETE FROM unit_conversion_mappings WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id.Trim());
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteUnitConversionByParentIdAsync(string parentProductId)
        {
            if (string.IsNullOrWhiteSpace(parentProductId))
            {
                return;
            }

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var command = new SqliteCommand("DELETE FROM unit_conversion_mappings WHERE parent_product_id = @parent_product_id", connection);
            command.Parameters.AddWithValue("@parent_product_id", parentProductId.Trim());
            await command.ExecuteNonQueryAsync();
        }

        public async Task SetRuntimeStateAsync(string key, string value)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO runtime_state (state_key, value, updated_at)
                VALUES (@state_key, @value, @updated_at)
                ON CONFLICT(state_key) DO UPDATE SET
                    value = excluded.value,
                    updated_at = excluded.updated_at";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@state_key", key);
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            await command.ExecuteNonQueryAsync();
        }

        public string? GetRuntimeState(string key)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = new SqliteCommand("SELECT value FROM runtime_state WHERE state_key = @state_key", connection);
            command.Parameters.AddWithValue("@state_key", key);
            return command.ExecuteScalar()?.ToString();
        }

        public int GetPendingOutboundCount()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = new SqliteCommand("SELECT COUNT(*) FROM outbound_messages WHERE status IN ('queued', 'retry')", connection);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        public int GetPendingWhatsAppLikeOutboundCount()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = new SqliteCommand(@"
                SELECT COUNT(*)
                FROM outbound_messages
                WHERE status IN ('queued', 'retry')
                  AND channel IN ('WhatsApp', 'Baileys')", connection);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        public async Task<OutboxCleanupResult> CancelPendingWhatsAppLikeOutboxAsync(
            string reason,
            DateTime? createdBefore = null)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            var result = new OutboxCleanupResult
            {
                WhatsAppCancelled = await CancelPendingOutboxByChannelAsync(connection, "WhatsApp", reason, createdBefore),
                BaileysCancelled = await CancelPendingOutboxByChannelAsync(connection, "Baileys", reason, createdBefore)
            };
            result.TotalCancelled = result.WhatsAppCancelled + result.BaileysCancelled;
            return result;
        }

        private static async Task<int> CancelPendingOutboxByChannelAsync(
            SqliteConnection connection,
            string channel,
            string reason,
            DateTime? createdBefore)
        {
            string sql = @"
                UPDATE outbound_messages
                SET status = 'dead_letter',
                    last_error = @reason,
                    updated_at = @updated_at
                WHERE status IN ('queued', 'retry')
                  AND channel = @channel";

            if (createdBefore.HasValue)
            {
                sql += " AND created_at < @created_before";
            }

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@reason", reason);
            command.Parameters.AddWithValue("@updated_at", ToDbTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("@channel", channel);
            if (createdBefore.HasValue)
            {
                command.Parameters.AddWithValue("@created_before", ToDbTimestamp(createdBefore.Value));
            }

            return await command.ExecuteNonQueryAsync();
        }

        #endregion
    }
}
