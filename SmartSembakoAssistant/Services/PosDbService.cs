using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class PosDbService
    {
        private readonly string _dbPath;
        private readonly LoggingService? _loggingService;
        private readonly ProductSchemaMetadata _productSchema;

        public string SchemaStatus => _productSchema.Status;
        public DateTime? LastSchemaValidatedAt { get; private set; }
        public string? LastSchemaActionHint => _productSchema.ActionHint;

        public PosDbService(string dbPath, LoggingService? loggingService = null)
        {
            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException($"Database pos.db tidak ditemukan di path: {dbPath}");
            }

            _dbPath = dbPath;
            _loggingService = loggingService;
            _productSchema = LoadProductSchemaMetadata();
        }

        #region Helper Methods

        private decimal? SafeConvertToDecimal(SqliteDataReader reader, int columnIndex)
        {
            if (reader.IsDBNull(columnIndex))
                return null;

            var value = reader.GetValue(columnIndex);
            
            // Handle DBNull
            if (value == DBNull.Value)
                return null;

            // Handle string yang mungkin kosong
            if (value is string strValue)
            {
                if (string.IsNullOrWhiteSpace(strValue))
                    return null;
                
                if (decimal.TryParse(strValue, out decimal result))
                    return result;
                
                return null;
            }

            // Handle numeric types
            try
            {
                return Convert.ToDecimal(value);
            }
            catch
            {
                return null;
            }
        }

        private DateTime? SafeConvertToDateTime(SqliteDataReader reader, int columnIndex)
        {
            if (reader.IsDBNull(columnIndex))
            {
                return null;
            }

            try
            {
                var value = reader.GetValue(columnIndex);
                if (value is DateTime dateTime)
                {
                    return dateTime;
                }

                if (DateTime.TryParse(value?.ToString(), out var parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // Ignore conversion issue and return null
            }

            return null;
        }

        private async Task<List<string>> GetAvailableTablesAsync(SqliteConnection connection)
        {
            var tables = new List<string>();

            try
            {
                string sql = "SELECT name FROM sqlite_master WHERE type='table'";
                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            catch
            {
                // Ignore errors
            }

            return tables;
        }

        private string? FindTable(List<string> tables, string[] possibleNames)
        {
            // Exact match first
            foreach (var name in possibleNames)
            {
                var found = tables.FirstOrDefault(t =>
                    t.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                    return found;
            }

            // Partial match
            foreach (var name in possibleNames)
            {
                var found = tables.FirstOrDefault(t =>
                    t.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                    return found;
            }

            return null;
        }

        private bool TableExists(List<string> tables, string tableName)
        {
            return tables.Any(t => t.Equals(tableName, StringComparison.OrdinalIgnoreCase));
        }

        private string ValidateTableName(string tableName)
        {
            // Validasi nama tabel untuk mencegah SQL injection
            if (!Regex.IsMatch(tableName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            {
                throw new ArgumentException($"Invalid table name: {tableName}");
            }
            return tableName;
        }

        private static string NormalizeLookupText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(" ", value
                .ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim('.', ',', ':', ';', '(', ')', '[', ']', '"', '\''))
                .Where(token => !string.IsNullOrWhiteSpace(token)));
        }

        private ProductSchemaMetadata LoadProductSchemaMetadata()
        {
            var metadata = new ProductSchemaMetadata();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                using var command = new SqliteCommand("PRAGMA table_info(Product)", connection);
                using var reader = command.ExecuteReader();
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    if (!reader.IsDBNull(1))
                    {
                        columns.Add(reader.GetString(1));
                    }
                }

                metadata.HasMeasurementUnit = columns.Contains("MeasurementUnit");
                metadata.HasUnit = columns.Contains("Unit");
                metadata.Status = metadata.HasMeasurementUnit
                    ? "Schema Product kompatibel: memakai kolom MeasurementUnit."
                    : metadata.HasUnit
                        ? "Schema Product kompatibel: memakai kolom Unit."
                        : "Schema Product tidak memiliki kolom satuan; aplikasi akan fallback ke 'Pcs'.";
                metadata.ActionHint = metadata.HasMeasurementUnit || metadata.HasUnit
                    ? "Schema stok kompatibel."
                    : "Kolom satuan tidak ditemukan di Product. Unit akan ditampilkan sebagai Pcs.";
                LastSchemaValidatedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                metadata.Status = $"Schema Product belum tervalidasi: {ex.Message}";
                metadata.ActionHint = "Periksa file pos.db dan pastikan tabel Product dapat dibaca.";
                LastSchemaValidatedAt = DateTime.Now;
            }

            return metadata;
        }

        private string GetProductUnitSql(string alias)
        {
            if (_productSchema.HasMeasurementUnit)
            {
                return $"{alias}.MeasurementUnit";
            }

            if (_productSchema.HasUnit)
            {
                return $"{alias}.Unit";
            }

            return "'Pcs'";
        }

        private static string MapAccessLevelToRole(int accessLevel)
        {
            return accessLevel switch
            {
                0 => "Cashier",
                8 => "Admin",
                20 => "Owner",
                _ => $"Level {accessLevel}"
            };
        }

        private static string GetDocumentTypeLabel(int documentTypeId)
        {
            return documentTypeId switch
            {
                1 => "Pembelian",
                2 => "Penjualan",
                3 => "Inventory",
                4 => "Refund",
                5 => "Retur Stok",
                6 => "Loss",
                _ => $"Tipe {documentTypeId}"
            };
        }

        private sealed class ProductSchemaMetadata
        {
            public bool HasMeasurementUnit { get; set; }
            public bool HasUnit { get; set; }
            public string Status { get; set; } = "Schema belum diperiksa.";
            public string? ActionHint { get; set; }
        }

        private sealed class ValidatedProductData
        {
            public int ProductId { get; set; }
            public decimal Cost { get; set; }
            public decimal Price { get; set; }
        }

        private sealed class StockSnapshot
        {
            public bool Exists { get; set; }
            public long StockId { get; set; }
            public decimal Quantity { get; set; }
        }

        private const string PreferredWarehouseIdSql = "COALESCE((SELECT Id FROM Warehouse WHERE Id = 1 LIMIT 1), (SELECT Id FROM Warehouse ORDER BY Id LIMIT 1))";

        #endregion

        #region Product Methods

        public async Task<List<Product>> GetAllProductsAsync()
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);

                // Aronium: Product + Stock (terpisah)
                string? productTable = FindTable(tables, new[] { "Product", "products", "item", "items", "barang" });
                string? stockTable = FindTable(tables, new[] { "Stock", "stock", "inventory" });

                if (string.IsNullOrEmpty(productTable))
                {
                    if (_loggingService != null)
                    {
                        await _loggingService.LogWarningAsync(
                            $"Tabel produk tidak ditemukan. Tabel tersedia: {string.Join(", ", tables)}",
                            "Database");
                    }
                    return products;
                }

                // Build query dengan JOIN ke Stock dan ProductGroup jika tabel ada
                string sql;
                if (!string.IsNullOrEmpty(stockTable))
                {
                    sql = $@"
                        SELECT 
                            p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                            COALESCE(s.Quantity, 0) as Stock,
                            p.Cost, p.Price,
                            p.IsEnabled
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN {ValidateTableName(stockTable)} s
                            ON p.Id = s.ProductId
                           AND s.WarehouseId = {PreferredWarehouseIdSql}
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        ORDER BY p.Name";
                }
                else
                {
                    sql = $@"
                        SELECT 
                            p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                            NULL as Stock,
                            p.Cost, p.Price,
                            p.IsEnabled
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        ORDER BY p.Name";
                }

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5),
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading products: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data produk dari pos.db: {ex.Message}", ex);
            }

            return products;
        }

        public async Task<Product?> GetProductByIdAsync(string productId)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products", "item" });
                string? stockTable = FindTable(tables, new[] { "Stock", "stock", "inventory" });

                if (string.IsNullOrEmpty(productTable))
                    return null;

                string sql;
                if (!string.IsNullOrEmpty(stockTable))
                {
                    sql = $@"
                        SELECT 
                            p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                            COALESCE(s.Quantity, 0) as Stock,
                            p.Cost, p.Price,
                            p.IsEnabled
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN {ValidateTableName(stockTable)} s
                            ON p.Id = s.ProductId
                           AND s.WarehouseId = {PreferredWarehouseIdSql}
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        WHERE p.Id = @id";
                }
                else
                {
                    sql = $@"
                        SELECT 
                            p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                            NULL as Stock,
                            p.Cost, p.Price,
                            p.IsEnabled
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        WHERE p.Id = @id";
                }

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@id", productId);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5),
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    };
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading product {productId}: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data produk: {ex.Message}", ex);
            }

            return null;
        }

        public async Task<List<Product>> GetLowStockProductsAsync(int threshold = 20)
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products", "item" });
                string? stockTable = FindTable(tables, new[] { "Stock", "stock", "inventory" });

                if (string.IsNullOrEmpty(productTable))
                    return products;

                string sql;
                if (!string.IsNullOrEmpty(stockTable))
                {
                    sql = $@"
                        SELECT 
                            p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                            COALESCE(s.Quantity, 0) as Stock,
                            p.Cost, p.Price,
                            p.IsEnabled
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN {ValidateTableName(stockTable)} s
                            ON p.Id = s.ProductId
                           AND s.WarehouseId = {PreferredWarehouseIdSql}
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        WHERE s.Quantity <= @threshold
                        AND p.IsEnabled = 1
                        ORDER BY s.Quantity ASC";
                }
                else
                {
                    // Jika tidak ada tabel Stock, kembalikan kosong
                    return products;
                }

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@threshold", threshold);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5),
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading low stock products: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data stok rendah: {ex.Message}", ex);
            }

            return products;
        }

        #endregion

        #region Expiry Methods

        public async Task<List<Product>> GetExpiringProductsAsync(int daysBefore = 30)
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);

                // Cek apakah ada tabel expiry-related
                string? expiryTable = FindTable(tables, new[] { "DocumentItemExpirationDate", "expiry", "expiration", "batch" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(expiryTable) || string.IsNullOrEmpty(productTable))
                {
                    // Tabel expiry tidak ada, kembalikan kosong
                    if (_loggingService != null)
                    {
                        await _loggingService.LogInfoAsync(
                            "Tabel expiry tidak ditemukan, skip expiry check", "Database");
                    }
                    return products;
                }

                // Cek kolom yang tersedia di tabel expiry
                var columns = await GetTableColumnsAsync(connection, expiryTable);
                bool hasBatchNumber = columns.Any(c => c.Equals("BatchNumber", StringComparison.OrdinalIgnoreCase));
                bool hasExpirationDate = columns.Any(c => c.Equals("ExpirationDate", StringComparison.OrdinalIgnoreCase) || c.Equals("ExpiryDate", StringComparison.OrdinalIgnoreCase));
                
                // Cari kolom foreign key ke Product (bisa ProductId, DocumentItemId, atau lainnya)
                string? fkColumn = columns.FirstOrDefault(c => 
                    c.Contains("ProductId", StringComparison.OrdinalIgnoreCase) || 
                    c.Contains("Product_Id", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("ItemId", StringComparison.OrdinalIgnoreCase));

                if (!hasExpirationDate || string.IsNullOrEmpty(fkColumn))
                {
                    if (_loggingService != null)
                    {
                        await _loggingService.LogInfoAsync(
                            $"Kolom yang diperlukan tidak ditemukan di tabel expiry (DateColumn: {hasExpirationDate}, FK: {fkColumn})", "Database");
                    }
                    return products;
                }

                // Gunakan kolom yang tersedia
                string dateColumn = hasExpirationDate ? "ExpirationDate" : "ExpiryDate";
                string batchColumn = hasBatchNumber ? "e.BatchNumber" : "NULL";

                // Aronium schema: DocumentItemExpirationDate (mungkin pakai kolom berbeda)
                string sql = $@"
                    SELECT DISTINCT
                        p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                        NULL as Stock,
                        p.Cost, p.Price,
                        p.IsEnabled,
                        e.{dateColumn} as ExpiryDate,
                        {batchColumn} as BatchNumber
                    FROM {ValidateTableName(productTable)} p
                    INNER JOIN {ValidateTableName(expiryTable)} e ON p.Id = e.{fkColumn}
                    LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                    WHERE e.{dateColumn} <= date('now', '+{daysBefore} days')
                    AND p.IsEnabled = 1
                    ORDER BY e.{dateColumn} ASC";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5),
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8)),
                        ExpiryDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        BatchNumber = reader.IsDBNull(10) ? null : reader.GetValue(10).ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading expiring products: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data expiry: {ex.Message}", ex);
            }

            return products;
        }

        /// <summary>
        /// Mendapatkan daftar kolom dari sebuah tabel
        /// </summary>
        private async Task<List<string>> GetTableColumnsAsync(
            SqliteConnection connection,
            string tableName,
            SqliteTransaction? transaction = null)
        {
            var columns = new List<string>();
            try
            {
                string sql = $"PRAGMA table_info({ValidateTableName(tableName)})";
                using var command = new SqliteCommand(sql, connection);
                if (transaction != null)
                {
                    command.Transaction = transaction;
                }

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1)); // Column name ada di index 1
                }
            }
            catch
            {
                // Ignore errors
            }

            return columns;
        }

        #endregion

        #region Transaction Methods

        public async Task<List<Transaction>> GetRecentTransactionsAsync(int limit = 50)
        {
            var transactions = new List<Transaction>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi", "transaction" });
                string? paymentTable = FindTable(tables, new[] { "Payment", "payments", "pembayaran" });

                if (string.IsNullOrEmpty(documentTable))
                    return transactions;

                string sql;
                if (!string.IsNullOrEmpty(paymentTable))
                {
                    // Ambil payment method dari Payment table
                    sql = $@"
                        SELECT 
                            d.Id, d.Date, d.UserId, d.Total,
                            pt.Name as PaymentMethod
                        FROM {ValidateTableName(documentTable)} d
                        LEFT JOIN {ValidateTableName(paymentTable)} pay ON d.Id = pay.DocumentId
                        LEFT JOIN PaymentType pt ON pay.PaymentTypeId = pt.Id
                        ORDER BY d.Date DESC
                        LIMIT @limit";
                }
                else
                {
                    sql = $@"
                        SELECT 
                            d.Id, d.Date, d.UserId, d.Total,
                            NULL as PaymentMethod
                        FROM {ValidateTableName(documentTable)} d
                        ORDER BY d.Date DESC
                        LIMIT @limit";
                }

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    transactions.Add(new Transaction
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Date = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                        UserId = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Total = SafeConvertToDecimal(reader, 3),
                        PaymentMethod = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading transactions: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data transaksi: {ex.Message}", ex);
            }

            return transactions;
        }

        public async Task<List<Transaction>> GetRecentSalesTransactionsAsync(int limit = 50)
        {
            var transactions = new List<Transaction>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi", "transaction" });
                if (string.IsNullOrEmpty(documentTable))
                {
                    return transactions;
                }

                string sql = $@"
                    SELECT d.Id, d.Number, d.Date, d.UserId, d.Total
                    FROM {ValidateTableName(documentTable)} d
                    WHERE d.DocumentTypeId = 2
                    ORDER BY d.Date DESC, d.Id DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    transactions.Add(new Transaction
                    {
                        Id = reader.IsDBNull(1) ? reader.GetValue(0).ToString() : reader.GetString(1),
                        Date = SafeConvertToDateTime(reader, 2),
                        UserId = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Total = SafeConvertToDecimal(reader, 4)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading recent sales transactions: {ex.Message}", "Database", ex.ToString());
                }
            }

            return transactions;
        }

        public async Task<decimal> GetTodayRevenueAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(documentTable))
                    return 0;

                // Aronium: DocumentTypeId = 2 adalah Sales (kode 200)
                string sql = $@"
                    SELECT COALESCE(SUM(Total), 0)
                    FROM {ValidateTableName(documentTable)}
                    WHERE date(Date) = date('now', 'localtime')
                    AND DocumentTypeId = 2";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error calculating today's revenue: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal menghitung revenue hari ini: {ex.Message}", ex);
            }
        }

        public async Task<decimal> GetTodayProfitAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return 0;

                // Aronium: DocumentTypeId = 2 adalah Sales (kode 200)
                // Profit = (Price - ProductCost) * Quantity
                string sql = $@"
                    SELECT COALESCE(SUM(
                        (di.Price - di.ProductCost) * di.Quantity
                    ), 0)
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    WHERE date(d.Date) = date('now', 'localtime')
                    AND d.DocumentTypeId = 2";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error calculating today's profit: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal menghitung profit hari ini: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Revenue kemarin (DocumentTypeId = 2 / sales only)
        /// </summary>
        public async Task<decimal> GetYesterdayRevenueAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(documentTable))
                    return 0;

                string sql = $@"
                    SELECT COALESCE(SUM(Total), 0)
                    FROM {ValidateTableName(documentTable)}
                    WHERE date(Date) = date('now', '-1 day', 'localtime')
                    AND DocumentTypeId = 2";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error calculating yesterday's revenue: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal menghitung revenue kemarin: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Rata-rata transaksi per hari (DocumentTypeId = 2 / sales only, 7 hari terakhir)
        /// </summary>
        public async Task<decimal> GetAverageDailyTransactionsAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(documentTable))
                    return 0;

                string sql = $@"
                    SELECT COUNT(*) * 1.0 / 7.0
                    FROM {ValidateTableName(documentTable)}
                    WHERE date(Date) >= date('now', '-7 days', 'localtime')
                    AND DocumentTypeId = 2";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error calculating average daily transactions: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal menghitung rata-rata transaksi harian: {ex.Message}", ex);
            }
        }

        #endregion

        #region User Methods

        public async Task<List<User>> GetAllUsersAsync()
        {
            var users = new List<User>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? userTable = FindTable(tables, new[] { "User", "users", "pengguna" });

                if (string.IsNullOrEmpty(userTable))
                    return users;

                // Aronium: User (Id, FirstName, LastName, Username, AccessLevel, IsEnabled, Email)
                string sql = $@"
                    SELECT Id, Username, FirstName, LastName, AccessLevel, IsEnabled
                    FROM {ValidateTableName(userTable)}
                    ORDER BY Username";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var firstName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var accessLevel = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

                    users.Add(new User
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Username = reader.IsDBNull(1) ? null : reader.GetString(1),
                        FullName = string.IsNullOrWhiteSpace(lastName) 
                            ? firstName 
                            : $"{firstName} {lastName}",
                        Role = MapAccessLevelToRole(accessLevel),
                        RoleId = accessLevel,
                        IsActive = reader.IsDBNull(5) || reader.GetBoolean(5)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading users: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data user: {ex.Message}", ex);
            }

            return users;
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? userTable = FindTable(tables, new[] { "User", "users", "pengguna" });

                if (string.IsNullOrEmpty(userTable))
                    return null;

                string sql = $@"
                    SELECT Id, Username, FirstName, LastName, AccessLevel, IsEnabled
                    FROM {ValidateTableName(userTable)}
                    WHERE Id = @id";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@id", userId);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var firstName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var accessLevel = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

                    string role = accessLevel switch
                    {
                        0 => "Cashier",
                        1 => "Admin",
                        2 => "Manager",
                        3 => "Owner",
                        _ => "User"
                    };

                    return new User
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Username = reader.IsDBNull(1) ? null : reader.GetString(1),
                        FullName = string.IsNullOrWhiteSpace(lastName) 
                            ? firstName 
                            : $"{firstName} {lastName}",
                        Role = role,
                        IsActive = reader.IsDBNull(5) || reader.GetBoolean(5)
                    };
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading user {userId}: {ex.Message}", "Database", ex.ToString());
                }
                throw new Exception($"Gagal membaca data user: {ex.Message}", ex);
            }

            return null;
        }

        public async Task<List<User>> GetUsersAsync(string? query, int limit)
        {
            var users = new List<User>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? userTable = FindTable(tables, new[] { "User", "users", "pengguna" });
                if (string.IsNullOrEmpty(userTable))
                {
                    return users;
                }

                string sql = $@"
                    SELECT Id, Username, FirstName, LastName, AccessLevel, IsEnabled
                    FROM {ValidateTableName(userTable)}
                    WHERE (@query = '' OR
                           COALESCE(Username, '') LIKE '%' || @query || '%' OR
                           COALESCE(FirstName, '') LIKE '%' || @query || '%' OR
                           COALESCE(LastName, '') LIKE '%' || @query || '%')
                    ORDER BY COALESCE(Username, FirstName, LastName), Id
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@query", query?.Trim() ?? string.Empty);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string firstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    string lastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    int accessLevel = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));

                    users.Add(new User
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Username = reader.IsDBNull(1) ? null : reader.GetString(1),
                        FullName = string.Join(" ", new[] { firstName, lastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
                        Role = MapAccessLevelToRole(accessLevel),
                        RoleId = accessLevel,
                        IsActive = reader.IsDBNull(5) || Convert.ToBoolean(reader.GetValue(5))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading users with filter: {ex.Message}", "Database", ex.ToString());
                }
            }

            return users;
        }

        #endregion

        #region Advanced Analytics

        /// <summary>
        /// Mendapatkan nama toko dari tabel Company
        /// </summary>
        public async Task<string?> GetShopNameAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? companyTable = FindTable(tables, new[] { "Company", "company", "toko", "store" });

                if (string.IsNullOrEmpty(companyTable))
                    return "Toko Sembako";

                string sql = $@"
                    SELECT Name 
                    FROM {ValidateTableName(companyTable)}
                    LIMIT 1";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return result?.ToString() ?? "Toko Sembako";
            }
            catch
            {
                return "Toko Sembako";
            }
        }

        /// <summary>
        /// Mendapatkan produk terlaris berdasarkan tanggal spesifik
        /// </summary>
        public async Task<List<Product>> GetTopSellingProductsByDateAsync(DateTime date, int limit = 5)
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable) || string.IsNullOrEmpty(productTable))
                    return products;

                string dateStr = date.ToString("yyyy-MM-dd");
                string sql = $@"
                    SELECT
                        p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                        SUM(di.Quantity) as TotalSold,
                        p.Cost, p.Price,
                        p.IsEnabled
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    INNER JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id
                    LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                    WHERE date(d.Date) = @dateStr
                    GROUP BY p.Id
                    ORDER BY TotalSold DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@dateStr", dateStr);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5), // TotalSold
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading top selling products by date: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        /// <summary>
        /// Mendapatkan pelanggan teratas berdasarkan frekuensi belanja
        /// </summary>
        public async Task<List<CustomerInfo>> GetCustomersAsync(string? query, int? limit = 10, bool onlyCustomers = true)
        {
            var customers = new List<CustomerInfo>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(customerTable))
                {
                    return customers;
                }

                string customerFlagCondition = onlyCustomers ? "c.IsCustomer = 1" : "1 = 1";
                string limitClause = limit.HasValue ? "\n                        LIMIT @limit" : string.Empty;
                string sql = string.IsNullOrEmpty(documentTable)
                    ? $@"
                        SELECT
                            c.Id,
                            c.Name,
                            c.Email,
                            c.PhoneNumber,
                            0 as PurchaseCount,
                            0 as TotalSpent,
                            NULL as LastPurchaseDate
                        FROM {ValidateTableName(customerTable)} c
                        WHERE c.IsEnabled = 1
                        AND {customerFlagCondition}
                        AND (@query = '' OR COALESCE(c.Name, '') LIKE '%' || @query || '%')
                        ORDER BY c.Name{limitClause}"
                    : $@"
                        SELECT
                            c.Id,
                            c.Name,
                            c.Email,
                            c.PhoneNumber,
                            COALESCE(SUM(CASE WHEN d.DocumentTypeId = 2 THEN 1 ELSE 0 END), 0) as PurchaseCount,
                            COALESCE(SUM(CASE WHEN d.DocumentTypeId = 2 THEN d.Total ELSE 0 END), 0) as TotalSpent,
                            MAX(CASE WHEN d.DocumentTypeId = 2 THEN d.Date END) as LastPurchaseDate
                        FROM {ValidateTableName(customerTable)} c
                        LEFT JOIN {ValidateTableName(documentTable)} d ON c.Id = d.CustomerId
                        WHERE c.IsEnabled = 1
                        AND {customerFlagCondition}
                        AND (@query = '' OR COALESCE(c.Name, '') LIKE '%' || @query || '%')
                        GROUP BY c.Id, c.Name, c.Email, c.PhoneNumber
                        ORDER BY c.Name{limitClause}";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@query", query?.Trim() ?? string.Empty);
                if (limit.HasValue)
                {
                    command.Parameters.AddWithValue("@limit", limit.Value);
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    customers.Add(new CustomerInfo
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                        PurchaseCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                        TotalSpent = SafeConvertToDecimal(reader, 5) ?? 0,
                        LastPurchaseDate = SafeConvertToDateTime(reader, 6)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading customers: {ex.Message}", "Database", ex.ToString());
                }
            }

            return customers;
        }

        public async Task<List<CustomerInfo>> GetSuppliersAsync(string? query, int? limit = 10)
        {
            var suppliers = new List<CustomerInfo>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });

                if (string.IsNullOrEmpty(customerTable))
                {
                    return suppliers;
                }

                string limitClause = limit.HasValue ? "\n                    LIMIT @limit" : string.Empty;
                string sql = $@"
                    SELECT Id, Name, Email, PhoneNumber
                    FROM {ValidateTableName(customerTable)}
                    WHERE IsEnabled = 1
                    AND IsSupplier = 1
                    AND (@query = '' OR COALESCE(Name, '') LIKE '%' || @query || '%')
                    ORDER BY Name{limitClause}";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@query", query?.Trim() ?? string.Empty);
                if (limit.HasValue)
                {
                    command.Parameters.AddWithValue("@limit", limit.Value);
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    suppliers.Add(new CustomerInfo
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Phone = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading suppliers: {ex.Message}", "Database", ex.ToString());
                }
            }

            return suppliers;
        }

        public async Task<List<CustomerInfo>> GetTopCustomersAsync(int limit = 10)
        {
            var customers = new List<CustomerInfo>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(customerTable) || string.IsNullOrEmpty(documentTable))
                    return customers;

                string sql = $@"
                    SELECT 
                        c.Id, c.Name, c.Email, c.PhoneNumber,
                        COUNT(d.Id) as PurchaseCount,
                        COALESCE(SUM(d.Total), 0) as TotalSpent,
                        MAX(d.Date) as LastPurchaseDate
                    FROM {ValidateTableName(customerTable)} c
                    INNER JOIN {ValidateTableName(documentTable)} d ON c.Id = d.CustomerId
                    WHERE d.DocumentTypeId = 2
                    GROUP BY c.Id
                    ORDER BY PurchaseCount DESC, TotalSpent DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    customers.Add(new CustomerInfo
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                        PurchaseCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        TotalSpent = SafeConvertToDecimal(reader, 5) ?? 0,
                        LastPurchaseDate = SafeConvertToDateTime(reader, 6)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading top customers: {ex.Message}", "Database", ex.ToString());
                }
            }

            return customers;
        }

        /// <summary>
        /// Mendapatkan total pelanggan unik
        /// </summary>
        public async Task<int> GetTotalCustomersAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });

                if (string.IsNullOrEmpty(customerTable))
                    return 0;

                string sql = $@"
                    SELECT COUNT(*)
                    FROM {ValidateTableName(customerTable)}
                    WHERE IsEnabled = 1
                    AND IsCustomer = 1";
                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Mendapatkan riwayat transaksi detail per pelanggan
        /// </summary>
        public async Task<List<CustomerTransaction>> GetCustomerTransactionsAsync(string customerId, int limit = 10)
        {
            var transactions = new List<CustomerTransaction>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return transactions;

                string productJoin = string.IsNullOrEmpty(productTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id";
                string productSelect = string.IsNullOrEmpty(productTable)
                    ? "NULL as ProductName"
                    : "p.Name as ProductName";

                string sql = $@"
                    SELECT 
                        d.Id, d.Number, d.Date, d.Total,
                        di.ProductId, di.Quantity, di.Price, di.Total as ItemTotal,
                        {productSelect}
                    FROM {ValidateTableName(documentTable)} d
                    INNER JOIN {ValidateTableName(documentItemTable)} di ON d.Id = di.DocumentId
                    {productJoin}
                    WHERE d.CustomerId = @customerId
                    AND d.DocumentTypeId = 2
                    ORDER BY d.Date DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@customerId", customerId);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    transactions.Add(new CustomerTransaction
                    {
                        TransactionId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        DocumentNumber = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Date = SafeConvertToDateTime(reader, 2),
                        Total = SafeConvertToDecimal(reader, 3) ?? 0,
                        ProductName = reader.IsDBNull(8) ? "Unknown" : reader.GetString(8),
                        Quantity = SafeConvertToDecimal(reader, 5) ?? 0,
                        Price = SafeConvertToDecimal(reader, 6) ?? 0,
                        ItemTotal = SafeConvertToDecimal(reader, 7) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading customer transactions: {ex.Message}", "Database", ex.ToString());
                }
            }

            return transactions;
        }

        public async Task<List<CustomerDocumentSummary>> GetCustomerRecentDocumentsAsync(string customerId, int limit = 10)
        {
            var documents = new List<CustomerDocumentSummary>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? paymentTable = FindTable(tables, new[] { "Payment", "payments", "pembayaran" });
                string? paymentTypeTable = FindTable(tables, new[] { "PaymentType", "payment_types", "jenis_pembayaran" });

                if (string.IsNullOrEmpty(documentTable))
                {
                    return documents;
                }

                bool hasPaymentTable = !string.IsNullOrEmpty(paymentTable);
                string paymentJoinSql = string.Empty;
                string paidAmountExpr = "0";

                if (hasPaymentTable)
                {
                    paymentJoinSql = $"LEFT JOIN {ValidateTableName(paymentTable!)} p ON d.Id = p.DocumentId";
                    if (!string.IsNullOrEmpty(paymentTypeTable))
                    {
                        paymentJoinSql += $" LEFT JOIN {ValidateTableName(paymentTypeTable)} pt ON p.PaymentTypeId = pt.Id";
                        paidAmountExpr = "COALESCE(SUM(CASE WHEN pt.MarkAsPaid = 1 THEN p.Amount ELSE 0 END), 0)";
                    }
                    else
                    {
                        paidAmountExpr = "COALESCE(SUM(p.Amount), 0)";
                    }
                }

                string outstandingExpr = hasPaymentTable
                    ? $"MAX(d.Total - ({paidAmountExpr}), 0)"
                    : "CASE WHEN d.PaidStatus = 1 THEN d.Total ELSE 0 END";

                string sql = $@"
                    SELECT
                        d.Id,
                        d.Number,
                        d.Date,
                        d.Total,
                        {paidAmountExpr} as PaidAmount,
                        {outstandingExpr} as OutstandingBalance
                    FROM {ValidateTableName(documentTable)} d
                    {paymentJoinSql}
                    WHERE d.CustomerId = @customerId
                    AND d.DocumentTypeId = 2
                    GROUP BY d.Id, d.Number, d.Date, d.Total, d.PaidStatus
                    ORDER BY d.Date DESC, d.Id DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@customerId", customerId);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    documents.Add(new CustomerDocumentSummary
                    {
                        DocumentId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        DocumentNumber = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Date = SafeConvertToDateTime(reader, 2),
                        Total = SafeConvertToDecimal(reader, 3) ?? 0,
                        PaidAmount = SafeConvertToDecimal(reader, 4) ?? 0,
                        OutstandingBalance = SafeConvertToDecimal(reader, 5) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading customer recent documents: {ex.Message}", "Database", ex.ToString());
                }
            }

            return documents;
        }

        public async Task<List<CustomerReceivable>> GetCustomerReceivablesAsync()
        {
            var receivables = new List<CustomerReceivable>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? paymentTable = FindTable(tables, new[] { "Payment", "payments", "pembayaran" });
                string? paymentTypeTable = FindTable(tables, new[] { "PaymentType", "payment_types", "jenis_pembayaran" });

                if (string.IsNullOrEmpty(customerTable) || string.IsNullOrEmpty(documentTable))
                {
                    return receivables;
                }

                bool hasPaymentTable = !string.IsNullOrEmpty(paymentTable);
                string paymentJoinSql = string.Empty;
                string paidAmountExpr = "0";

                if (hasPaymentTable)
                {
                    paymentJoinSql = $"LEFT JOIN {ValidateTableName(paymentTable!)} p ON d.Id = p.DocumentId";
                    if (!string.IsNullOrEmpty(paymentTypeTable))
                    {
                        paymentJoinSql += $" LEFT JOIN {ValidateTableName(paymentTypeTable)} pt ON p.PaymentTypeId = pt.Id";
                        paidAmountExpr = "COALESCE(SUM(CASE WHEN pt.MarkAsPaid = 1 THEN p.Amount ELSE 0 END), 0)";
                    }
                    else
                    {
                        paidAmountExpr = "COALESCE(SUM(p.Amount), 0)";
                    }
                }

                string outstandingExpr = hasPaymentTable
                    ? $"MAX(d.Total - ({paidAmountExpr}), 0)"
                    : "CASE WHEN d.PaidStatus = 1 THEN d.Total ELSE 0 END";

                string sql = $@"
                    SELECT
                        c.Id,
                        c.Name,
                        c.PhoneNumber,
                        COUNT(rd.DocumentId) as InvoiceCount,
                        COALESCE(SUM(rd.OutstandingBalance), 0) as TotalOwed,
                        MIN(rd.DueDate) as OldestDueDate,
                        MAX(rd.Date) as LastTransactionDate
                    FROM {ValidateTableName(customerTable)} c
                    INNER JOIN (
                        SELECT
                            d.Id as DocumentId,
                            d.CustomerId,
                            d.Date,
                            d.DueDate,
                            {outstandingExpr} as OutstandingBalance
                        FROM {ValidateTableName(documentTable)} d
                        {paymentJoinSql}
                        WHERE d.DocumentTypeId = 2
                        AND d.PaidStatus = 1
                        AND d.CustomerId IS NOT NULL
                        GROUP BY d.Id, d.CustomerId, d.Date, d.DueDate, d.Total, d.PaidStatus
                    ) rd ON c.Id = rd.CustomerId
                    WHERE rd.OutstandingBalance > 0
                    GROUP BY c.Id, c.Name, c.PhoneNumber
                    ORDER BY TotalOwed DESC, InvoiceCount DESC, c.Name";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    receivables.Add(new CustomerReceivable
                    {
                        CustomerId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        CustomerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
                        InvoiceCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                        TotalOwed = SafeConvertToDecimal(reader, 4) ?? 0,
                        OldestDueDate = SafeConvertToDateTime(reader, 5),
                        LastTransactionDate = SafeConvertToDateTime(reader, 6)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading customer receivables: {ex.Message}", "Database", ex.ToString());
                }
            }

            return receivables;
        }

        public async Task<List<ReceivableInvoice>> GetCustomerReceivableDetailAsync(string customerId)
        {
            var invoices = new List<ReceivableInvoice>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? paymentTable = FindTable(tables, new[] { "Payment", "payments", "pembayaran" });
                string? paymentTypeTable = FindTable(tables, new[] { "PaymentType", "payment_types", "jenis_pembayaran" });
                if (string.IsNullOrEmpty(documentTable))
                {
                    return invoices;
                }

                bool hasPaymentTable = !string.IsNullOrEmpty(paymentTable);
                string paymentJoinSql = string.Empty;
                string paidAmountExpr = "0";

                if (hasPaymentTable)
                {
                    paymentJoinSql = $"LEFT JOIN {ValidateTableName(paymentTable!)} p ON d.Id = p.DocumentId";
                    if (!string.IsNullOrEmpty(paymentTypeTable))
                    {
                        paymentJoinSql += $" LEFT JOIN {ValidateTableName(paymentTypeTable)} pt ON p.PaymentTypeId = pt.Id";
                        paidAmountExpr = "COALESCE(SUM(CASE WHEN pt.MarkAsPaid = 1 THEN p.Amount ELSE 0 END), 0)";
                    }
                    else
                    {
                        paidAmountExpr = "COALESCE(SUM(p.Amount), 0)";
                    }
                }

                string outstandingExpr = hasPaymentTable
                    ? $"MAX(d.Total - ({paidAmountExpr}), 0)"
                    : "CASE WHEN d.PaidStatus = 1 THEN d.Total ELSE 0 END";

                string sql = $@"
                    SELECT
                        d.Number,
                        d.Date,
                        d.DueDate,
                        d.Total as InvoiceTotal,
                        {paidAmountExpr} as PaidAmount,
                        {outstandingExpr} as OutstandingBalance
                    FROM {ValidateTableName(documentTable)} d
                    {paymentJoinSql}
                    WHERE d.CustomerId = @customerId
                    AND d.DocumentTypeId = 2
                    AND d.PaidStatus = 1
                    GROUP BY d.Id, d.Number, d.Date, d.DueDate, d.Total, d.PaidStatus
                    HAVING OutstandingBalance > 0
                    ORDER BY d.Date DESC, d.Id DESC";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@customerId", customerId);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    invoices.Add(new ReceivableInvoice
                    {
                        DocumentNumber = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Date = SafeConvertToDateTime(reader, 1),
                        DueDate = SafeConvertToDateTime(reader, 2),
                        InvoiceTotal = SafeConvertToDecimal(reader, 3) ?? 0,
                        PaidAmount = SafeConvertToDecimal(reader, 4) ?? 0,
                        OutstandingBalance = SafeConvertToDecimal(reader, 5) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading customer receivable detail: {ex.Message}", "Database", ex.ToString());
                }
            }

            return invoices;
        }

        public async Task<decimal> GetTotalReceivableAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? paymentTable = FindTable(tables, new[] { "Payment", "payments", "pembayaran" });
                string? paymentTypeTable = FindTable(tables, new[] { "PaymentType", "payment_types", "jenis_pembayaran" });
                if (string.IsNullOrEmpty(documentTable))
                {
                    return 0;
                }

                bool hasPaymentTable = !string.IsNullOrEmpty(paymentTable);
                string paymentJoinSql = string.Empty;
                string paidAmountExpr = "0";

                if (hasPaymentTable)
                {
                    paymentJoinSql = $"LEFT JOIN {ValidateTableName(paymentTable!)} p ON d.Id = p.DocumentId";
                    if (!string.IsNullOrEmpty(paymentTypeTable))
                    {
                        paymentJoinSql += $" LEFT JOIN {ValidateTableName(paymentTypeTable)} pt ON p.PaymentTypeId = pt.Id";
                        paidAmountExpr = "COALESCE(SUM(CASE WHEN pt.MarkAsPaid = 1 THEN p.Amount ELSE 0 END), 0)";
                    }
                    else
                    {
                        paidAmountExpr = "COALESCE(SUM(p.Amount), 0)";
                    }
                }

                string outstandingExpr = hasPaymentTable
                    ? $"MAX(d.Total - ({paidAmountExpr}), 0)"
                    : "CASE WHEN d.PaidStatus = 1 THEN d.Total ELSE 0 END";

                string sql = $@"
                    SELECT COALESCE(SUM(OutstandingBalance), 0)
                    FROM (
                        SELECT
                            {outstandingExpr} as OutstandingBalance
                        FROM {ValidateTableName(documentTable)} d
                        {paymentJoinSql}
                        WHERE d.DocumentTypeId = 2
                        AND d.PaidStatus = 1
                        GROUP BY d.Id, d.Total, d.PaidStatus
                    ) receivables
                    WHERE OutstandingBalance > 0";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<int> GetTotalSuppliersAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                if (string.IsNullOrEmpty(customerTable))
                {
                    return 0;
                }

                string sql = $@"
                    SELECT COUNT(*)
                    FROM {ValidateTableName(customerTable)}
                    WHERE IsEnabled = 1
                    AND IsSupplier = 1";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<int> GetProductCountAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products" });
                if (string.IsNullOrEmpty(productTable))
                {
                    return 0;
                }

                string sql = $@"
                    SELECT COUNT(*)
                    FROM {ValidateTableName(productTable)}
                    WHERE IsEnabled = 1";

                using var command = new SqliteCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<ProductSalesData?> GetProductSalesSummaryAsync(string productId)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                {
                    return null;
                }

                string productNameSql = string.IsNullOrEmpty(productTable)
                    ? "NULL as ProductName"
                    : "p.Name as ProductName";
                string productJoinSql = string.IsNullOrEmpty(productTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id";

                string sql = $@"
                    SELECT
                        di.ProductId,
                        {productNameSql},
                        COALESCE(SUM(di.Quantity), 0) as TotalQty,
                        COALESCE(SUM(di.Total), 0) as TotalRevenue,
                        COALESCE(SUM((di.Price - di.ProductCost) * di.Quantity), 0) as TotalProfit,
                        MAX(d.Date) as LastSaleDate
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    {productJoinSql}
                    WHERE di.ProductId = @productId
                    AND d.DocumentTypeId = 2";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@productId", productId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new ProductSalesData
                    {
                        ProductId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        ProductName = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        QuantitySold = SafeConvertToDecimal(reader, 2) ?? 0,
                        Revenue = SafeConvertToDecimal(reader, 3) ?? 0,
                        Profit = SafeConvertToDecimal(reader, 4) ?? 0,
                        LastSaleDate = SafeConvertToDateTime(reader, 5)
                    };
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading product sales summary: {ex.Message}", "Database", ex.ToString());
                }
            }

            return null;
        }

        public async Task<List<ProductSalesTransaction>> GetProductSalesTransactionsAsync(string productId, int limit)
        {
            var transactions = new List<ProductSalesTransaction>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                string? userTable = FindTable(tables, new[] { "User", "users", "pengguna" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                {
                    return transactions;
                }

                string customerJoin = string.IsNullOrEmpty(customerTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(customerTable)} c ON d.CustomerId = c.Id";
                string userJoin = string.IsNullOrEmpty(userTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(userTable)} u ON d.UserId = u.Id";
                string customerSelect = string.IsNullOrEmpty(customerTable) ? "NULL as CustomerName" : "c.Name as CustomerName";
                string userSelect = string.IsNullOrEmpty(userTable)
                    ? "NULL as UserName"
                    : "TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) as UserName";

                string sql = $@"
                    SELECT
                        d.Id,
                        d.Number,
                        d.Date,
                        {customerSelect},
                        {userSelect},
                        di.Quantity,
                        di.Price,
                        di.ProductCost,
                        di.Total
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    {customerJoin}
                    {userJoin}
                    WHERE di.ProductId = @productId
                    AND d.DocumentTypeId = 2
                    ORDER BY d.Date DESC, d.Id DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@productId", productId);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    decimal qty = SafeConvertToDecimal(reader, 5) ?? 0;
                    decimal price = SafeConvertToDecimal(reader, 6) ?? 0;
                    decimal productCost = SafeConvertToDecimal(reader, 7) ?? 0;

                    transactions.Add(new ProductSalesTransaction
                    {
                        DocumentId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        DocumentNumber = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Date = SafeConvertToDateTime(reader, 2),
                        CustomerName = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        UserName = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Quantity = qty,
                        Price = price,
                        ProductCost = productCost,
                        Total = SafeConvertToDecimal(reader, 8) ?? 0,
                        Profit = (price - productCost) * qty
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading product sales transactions: {ex.Message}", "Database", ex.ToString());
                }
            }

            return transactions;
        }

        public async Task<DocumentInfo?> GetDocumentByNumberAsync(string documentNumber)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                string? userTable = FindTable(tables, new[] { "User", "users", "pengguna" });

                if (string.IsNullOrEmpty(documentTable))
                {
                    return null;
                }

                string customerJoin = string.IsNullOrEmpty(customerTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(customerTable)} c ON d.CustomerId = c.Id";
                string userJoin = string.IsNullOrEmpty(userTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(userTable)} u ON d.UserId = u.Id";
                string customerSelect = string.IsNullOrEmpty(customerTable) ? "NULL as CustomerName" : "c.Name as CustomerName";
                string userSelect = string.IsNullOrEmpty(userTable)
                    ? "NULL as UserName"
                    : "TRIM(COALESCE(u.FirstName, '') || ' ' || COALESCE(u.LastName, '')) as UserName";

                string sql = $@"
                    SELECT
                        d.Id,
                        d.Number,
                        d.DocumentTypeId,
                        d.Date,
                        d.UserId,
                        {userSelect},
                        d.CustomerId,
                        {customerSelect},
                        d.Total
                    FROM {ValidateTableName(documentTable)} d
                    {customerJoin}
                    {userJoin}
                    WHERE d.Number = @documentNumber
                    LIMIT 1";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@documentNumber", documentNumber.Trim());

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int documentTypeId = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                    return new DocumentInfo
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Number = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        DocumentTypeId = documentTypeId,
                        DocumentTypeLabel = GetDocumentTypeLabel(documentTypeId),
                        Date = SafeConvertToDateTime(reader, 3),
                        UserId = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        UserName = reader.IsDBNull(5) ? null : reader.GetValue(5).ToString(),
                        CustomerId = reader.IsDBNull(6) ? null : reader.GetValue(6).ToString(),
                        CustomerName = reader.IsDBNull(7) ? null : reader.GetValue(7).ToString(),
                        Total = SafeConvertToDecimal(reader, 8) ?? 0
                    };
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading document by number: {ex.Message}", "Database", ex.ToString());
                }
            }

            return null;
        }

        public async Task<List<DocumentItemInfo>> GetDocumentItemsAsync(string documentId)
        {
            var items = new List<DocumentItemInfo>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentItemTable))
                {
                    return items;
                }

                string productJoin = string.IsNullOrEmpty(productTable)
                    ? string.Empty
                    : $"LEFT JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id";
                string productSelect = string.IsNullOrEmpty(productTable) ? "NULL as ProductName" : "p.Name as ProductName";
                string unitSelect = string.IsNullOrEmpty(productTable) ? "NULL as Unit" : "p.MeasurementUnit as Unit";

                string sql = $@"
                    SELECT
                        di.ProductId,
                        {productSelect},
                        {unitSelect},
                        di.Quantity,
                        di.Price,
                        di.ProductCost,
                        di.Total
                    FROM {ValidateTableName(documentItemTable)} di
                    {productJoin}
                    WHERE di.DocumentId = @documentId
                    ORDER BY di.Id";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@documentId", documentId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new DocumentItemInfo
                    {
                        ProductId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        ProductName = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Unit = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Quantity = SafeConvertToDecimal(reader, 3) ?? 0,
                        Price = SafeConvertToDecimal(reader, 4) ?? 0,
                        ProductCost = SafeConvertToDecimal(reader, 5) ?? 0,
                        Total = SafeConvertToDecimal(reader, 6) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading document items: {ex.Message}", "Database", ex.ToString());
                }
            }

            return items;
        }

        /// <summary>
        /// Mendapatkan produk terlaris hari ini berdasarkan jumlah penjualan
        /// </summary>
        public async Task<List<Product>> GetTopSellingProductsAsync(int limit = 5)
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable) || string.IsNullOrEmpty(productTable))
                    return products;

                string sql = $@"
                    SELECT 
                        p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                        SUM(di.Quantity) as TotalSold,
                        p.Cost, p.Price,
                        p.IsEnabled
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    INNER JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id
                    LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                    WHERE date(d.Date) = date('now', 'localtime')
                    AND d.DocumentTypeId = 2
                    GROUP BY p.Id
                    ORDER BY TotalSold DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5), // TotalSold
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading top selling products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        /// <summary>
        /// Mendapatkan produk Slow Moving (Stok > 0 tapi tidak laku 7 hari terakhir)
        /// </summary>
        public async Task<List<Product>> GetSlowMovingProductsAsync()
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products" });
                string? stockTable = FindTable(tables, new[] { "Stock", "stock" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(productTable) || string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return products;

                // Cari produk yang stoknya > 0 TAPI tidak ada di transaksi 7 hari terakhir
                string sql = $@"
                    SELECT 
                        p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                        COALESCE(s.Quantity, 0) as Stock,
                        p.Cost, p.Price,
                        p.IsEnabled
                    FROM {ValidateTableName(productTable)} p
                    LEFT JOIN {ValidateTableName(stockTable)} s
                        ON p.Id = s.ProductId
                       AND s.WarehouseId = {PreferredWarehouseIdSql}
                    LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                    WHERE p.IsEnabled = 1
                    AND (s.Quantity > 0 OR s.Quantity IS NULL)
                    AND p.Id NOT IN (
                        SELECT DISTINCT di.ProductId 
                        FROM {ValidateTableName(documentItemTable)} di
                        INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                        WHERE date(d.Date) >= date('now', '-7 days', 'localtime')
                    )
                    ORDER BY p.Name
                    LIMIT 20"; // Batasi 20 item teratas

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        Stock = SafeConvertToDecimal(reader, 5),
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading slow moving products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        /// <summary>
        /// Mendapatkan daftar Tier A produk aktif tanpa modal yang laku 30 hari terakhir.
        /// </summary>
        public async Task<List<ZeroCostProductInsight>> GetZeroCostProductsAsync()
        {
            var products = new List<ZeroCostProductInsight>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(productTable) ||
                    string.IsNullOrEmpty(documentTable) ||
                    string.IsNullOrEmpty(documentItemTable))
                    return products;

                string sql = $@"
                    SELECT
                        p.Id,
                        p.Name,
                        p.Price,
                        {GetProductUnitSql("p")} as Unit,
                        pg.Name as Category,
                        ROUND(COALESCE(SUM(di.Quantity), 0), 1) as Qty30Days,
                        CAST(COALESCE(SUM(di.Total), 0) as INTEGER) as Revenue30Days,
                        MAX(d.Date) as LastSaleDate
                    FROM {ValidateTableName(productTable)} p
                    LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                    INNER JOIN {ValidateTableName(documentItemTable)} di ON di.ProductId = p.Id
                    INNER JOIN {ValidateTableName(documentTable)} d ON d.Id = di.DocumentId
                    WHERE d.DocumentTypeId = 2
                      AND date(d.Date) >= date('now', '-30 days')
                      AND (p.Cost = 0 OR p.Cost IS NULL)
                      AND p.Price > 0
                      AND p.IsEnabled = 1
                    GROUP BY p.Id, p.Name, p.Price, {GetProductUnitSql("p")}, pg.Name
                    ORDER BY Revenue30Days DESC";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new ZeroCostProductInsight
                    {
                        ProductId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        ProductName = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        SellingPrice = SafeConvertToDecimal(reader, 2) ?? 0,
                        Unit = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Category = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                        QuantitySold30Days = SafeConvertToDecimal(reader, 5) ?? 0,
                        Revenue30Days = SafeConvertToDecimal(reader, 6) ?? 0,
                        LastSaleDate = SafeConvertToDateTime(reader, 7)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading zero cost products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        public async Task<List<ZeroCostExportRow>> GetNoCostProductsForExportAsync(bool includeAllZeroCostProducts)
        {
            var rows = new List<ZeroCostExportRow>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(productTable) ||
                    string.IsNullOrEmpty(documentTable) ||
                    string.IsNullOrEmpty(documentItemTable))
                {
                    return rows;
                }

                string sql = includeAllZeroCostProducts
                    ? $@"
                        SELECT
                            p.Name,
                            p.Price,
                            {GetProductUnitSql("p")} as Unit,
                            pg.Name as Category,
                            ROUND(COALESCE(SUM(CASE WHEN d.DocumentTypeId = 2 THEN di.Quantity ELSE 0 END), 0), 1) as QtySoldTotal,
                            MAX(CASE WHEN d.DocumentTypeId = 2 THEN d.Date END) as LastSaleDate
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        LEFT JOIN {ValidateTableName(documentItemTable)} di ON di.ProductId = p.Id
                        LEFT JOIN {ValidateTableName(documentTable)} d ON d.Id = di.DocumentId
                        WHERE (p.Cost = 0 OR p.Cost IS NULL)
                          AND p.Price > 0
                          AND p.IsEnabled = 1
                        GROUP BY p.Id, p.Name, p.Price, {GetProductUnitSql("p")}, pg.Name
                        ORDER BY QtySoldTotal DESC, p.Name"
                    : $@"
                        SELECT
                            p.Name,
                            p.Price,
                            {GetProductUnitSql("p")} as Unit,
                            pg.Name as Category,
                            ROUND(COALESCE(SUM(di.Quantity), 0), 1) as QtySold,
                            CAST(COALESCE(SUM(di.Total), 0) as INTEGER) as Revenue,
                            MAX(d.Date) as LastSaleDate
                        FROM {ValidateTableName(productTable)} p
                        LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                        INNER JOIN {ValidateTableName(documentItemTable)} di ON di.ProductId = p.Id
                        INNER JOIN {ValidateTableName(documentTable)} d ON d.Id = di.DocumentId
                        WHERE d.DocumentTypeId = 2
                          AND date(d.Date) >= date('now', '-30 days')
                          AND (p.Cost = 0 OR p.Cost IS NULL)
                          AND p.Price > 0
                          AND p.IsEnabled = 1
                        GROUP BY p.Id, p.Name, p.Price, {GetProductUnitSql("p")}, pg.Name
                        ORDER BY Revenue DESC";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    rows.Add(new ZeroCostExportRow
                    {
                        ProductName = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        SellingPrice = SafeConvertToDecimal(reader, 1) ?? 0,
                        Unit = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        QuantitySold = SafeConvertToDecimal(reader, 4) ?? 0,
                        Revenue = includeAllZeroCostProducts ? 0 : SafeConvertToDecimal(reader, 5) ?? 0,
                        LastSaleDate = SafeConvertToDateTime(reader, includeAllZeroCostProducts ? 5 : 6)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading zero cost export products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return rows;
        }

        /// <summary>
        /// Mendapatkan laporan penjualan per User/Kasir hari ini
        /// </summary>
        public async Task<List<SalesReportItem>> GetSalesPerUserAsync()
        {
            var reports = new List<SalesReportItem>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? userTable = FindTable(tables, new[] { "User", "users" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(userTable))
                    return reports;

                string sql = $@"
                    SELECT 
                        u.FirstName, u.LastName, u.Username,
                        COUNT(d.Id) as TransactionCount,
                        COALESCE(SUM(d.Total), 0) as TotalSales
                    FROM {ValidateTableName(documentTable)} d
                    INNER JOIN {ValidateTableName(userTable)} u ON d.UserId = u.Id
                    WHERE date(d.Date) = date('now', 'localtime')
                    GROUP BY u.Id
                    ORDER BY TotalSales DESC";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var firstName = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var lastName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var username = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    
                    reports.Add(new SalesReportItem
                    {
                        Name = string.IsNullOrWhiteSpace(lastName) ? (string.IsNullOrWhiteSpace(username) ? firstName : username) : $"{firstName} {lastName}",
                        TransactionCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        TotalSales = SafeConvertToDecimal(reader, 4) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading sales per user: {ex.Message}", "Database", ex.ToString());
                }
            }

            return reports;
        }

        /// <summary>
        /// Mendapatkan Dead Stock (Tidak laku > 14 hari)
        /// </summary>
        public async Task<List<Product>> GetDeadStockProductsAsync()
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? stockTable = FindTable(tables, new[] { "Stock", "stock" });

                if (string.IsNullOrEmpty(productTable) || string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return products;

                string sql = $@"
                    SELECT 
                        p.Id, p.Name, p.Code, pg.Name as Category, p.MeasurementUnit,
                        COALESCE(s.Quantity, 0) as Stock,
                        p.Cost, p.Price,
                        p.IsEnabled
                    FROM {ValidateTableName(productTable)} p
                    LEFT JOIN {ValidateTableName(stockTable)} s
                        ON p.Id = s.ProductId
                       AND s.WarehouseId = {PreferredWarehouseIdSql}
                    LEFT JOIN ProductGroup pg ON p.ProductGroupId = pg.Id
                    WHERE p.IsEnabled = 1
                    AND p.Id NOT IN (
                        SELECT DISTINCT di.ProductId 
                        FROM {ValidateTableName(documentItemTable)} di
                        INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                        WHERE date(d.Date) >= date('now', '-14 days', 'localtime')
                    )
                    ORDER BY p.Name
                    LIMIT 20";

                using var command = new SqliteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var unit = reader.IsDBNull(4) ? null : reader.GetValue(4).ToString();
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                        Sku = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Category = reader.IsDBNull(3) ? null : reader.GetValue(3).ToString(),
                        Unit = string.IsNullOrWhiteSpace(unit) ? "Pcs" : unit, // Default ke Pcs jika kosong
                        Stock = SafeConvertToDecimal(reader, 5),
                        PurchasePrice = SafeConvertToDecimal(reader, 6),
                        SellingPrice = SafeConvertToDecimal(reader, 7),
                        IsActive = reader.IsDBNull(8) || Convert.ToBoolean(reader.GetValue(8))
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading dead stock products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        #endregion

        #region Restock Engine

        private async Task EnsureUserExistsAsync(SqliteConnection connection, SqliteTransaction transaction, int userId)
        {
            using var checkUserCmd = new SqliteCommand("SELECT Id FROM [User] WHERE Id = @id", connection, transaction);
            checkUserCmd.Parameters.AddWithValue("@id", userId);
            var userExists = await checkUserCmd.ExecuteScalarAsync();
            if (userExists == null)
            {
                throw new InvalidOperationException($"UserId {userId} tidak ditemukan.");
            }
        }

        private async Task<int> ResolveWarehouseIdAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            int warehouseId = 1;
            using var checkWarehouseCmd = new SqliteCommand("SELECT Id FROM Warehouse WHERE Id = @id", connection, transaction);
            checkWarehouseCmd.Parameters.AddWithValue("@id", warehouseId);
            var warehouseExists = await checkWarehouseCmd.ExecuteScalarAsync();
            if (warehouseExists != null)
            {
                return warehouseId;
            }

            using var getFirstWarehouseCmd = new SqliteCommand("SELECT Id FROM Warehouse ORDER BY Id LIMIT 1", connection, transaction);
            var firstWarehouse = await getFirstWarehouseCmd.ExecuteScalarAsync();
            if (firstWarehouse == null)
            {
                throw new InvalidOperationException("Tidak ada warehouse di database. Buat warehouse terlebih dahulu di Aronium.");
            }

            return Convert.ToInt32(firstWarehouse);
        }

        private async Task<int> ResolvePurchaseCustomerIdAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            int customerId = 1;
            using var checkCustomerCmd = new SqliteCommand("SELECT Id FROM Customer WHERE Id = @id", connection, transaction);
            checkCustomerCmd.Parameters.AddWithValue("@id", customerId);
            var customerExists = await checkCustomerCmd.ExecuteScalarAsync();
            if (customerExists != null)
            {
                return customerId;
            }

            using var getFirstCustomerCmd = new SqliteCommand("SELECT Id FROM Customer ORDER BY Id LIMIT 1", connection, transaction);
            var firstCustomer = await getFirstCustomerCmd.ExecuteScalarAsync();
            if (firstCustomer == null)
            {
                throw new InvalidOperationException("Tidak ada customer di database. Buat customer terlebih dahulu di Aronium.");
            }

            return Convert.ToInt32(firstCustomer);
        }

        private async Task<int> ResolveOrCreateSupplierCustomerIdAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string supplierName)
        {
            string trimmedName = supplierName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                return await ResolvePurchaseCustomerIdAsync(connection, transaction);
            }

            const string findSupplierSql = @"
                SELECT Id, Name, IsSupplier
                FROM Customer
                WHERE IsEnabled = 1
                AND (
                    Name = @name
                    OR LOWER(TRIM(Name)) = LOWER(TRIM(@name))
                    OR REPLACE(LOWER(TRIM(Name)), ' ', '') = REPLACE(LOWER(TRIM(@name)), ' ', '')
                )
                ORDER BY IsSupplier DESC, Id
                LIMIT 10";

            var candidates = new List<(int Id, string Name, bool IsSupplier)>();
            using (var findCmd = new SqliteCommand(findSupplierSql, connection, transaction))
            {
                findCmd.Parameters.AddWithValue("@name", trimmedName);
                using var reader = await findCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    candidates.Add((
                        reader.GetInt32(0),
                        reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        !reader.IsDBNull(2) && Convert.ToBoolean(reader.GetValue(2))));
                }
            }

            string normalizedTarget = NormalizeLookupText(trimmedName);
            var exactMatch = candidates.FirstOrDefault(candidate =>
                NormalizeLookupText(candidate.Name) == normalizedTarget);

            if (exactMatch != default)
            {
                if (!exactMatch.IsSupplier)
                {
                    using var updateCmd = new SqliteCommand(
                        "UPDATE Customer SET IsSupplier = 1, DateUpdated = DATETIME('now') WHERE Id = @id",
                        connection,
                        transaction);
                    updateCmd.Parameters.AddWithValue("@id", exactMatch.Id);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                return exactMatch.Id;
            }

            const string insertSupplierSql = @"
                INSERT INTO Customer
                    (Name, IsEnabled, IsCustomer, IsSupplier, DueDatePeriod, IsTaxExempt)
                VALUES
                    (@name, 1, 0, 1, 0, 0)";

            using (var insertCmd = new SqliteCommand(insertSupplierSql, connection, transaction))
            {
                insertCmd.Parameters.AddWithValue("@name", trimmedName);
                await insertCmd.ExecuteNonQueryAsync();
            }

            using var idCmd = new SqliteCommand("SELECT last_insert_rowid()", connection, transaction);
            int supplierId = Convert.ToInt32(await idCmd.ExecuteScalarAsync());

            if (_loggingService != null)
            {
                await _loggingService.LogInfoAsync(
                    $"Supplier baru dibuat otomatis dari OCR: {trimmedName} (CustomerId={supplierId}).",
                    "OCR");
            }

            return supplierId;
        }

        public async Task<string?> GetOrCreateSupplierAsync(string supplierName)
        {
            if (string.IsNullOrWhiteSpace(supplierName))
            {
                return null;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();
                int supplierId = await ResolveOrCreateSupplierCustomerIdAsync(connection, transaction, supplierName);
                transaction.Commit();
                return supplierId.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error resolving OCR supplier '{supplierName}': {ex.Message}",
                        "OCR",
                        ex.ToString());
                }

                return null;
            }
        }

        private static async Task<ValidatedProductData> LoadValidatedProductDataAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int productId)
        {
            const string sql = "SELECT Id, IsEnabled, Cost, Price FROM Product WHERE Id = @id";
            using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@id", productId);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException($"ProductId {productId} tidak ditemukan di database.");
            }

            bool isEnabled = reader.IsDBNull(1) || Convert.ToBoolean(reader[1]);
            if (!isEnabled)
            {
                throw new InvalidOperationException($"Produk dengan ID {productId} tidak aktif (IsEnabled = 0). Pilih produk lain.");
            }

            return new ValidatedProductData
            {
                ProductId = productId,
                Cost = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2),
                Price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3)
            };
        }

        private static async Task<StockSnapshot> GetStockSnapshotAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int productId,
            int warehouseId)
        {
            const string sql = "SELECT Id, Quantity FROM Stock WHERE ProductId = @prodId AND WarehouseId = @warehouseId";
            using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@prodId", productId);
            command.Parameters.AddWithValue("@warehouseId", warehouseId);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return new StockSnapshot();
            }

            return new StockSnapshot
            {
                Exists = true,
                StockId = reader.GetInt64(0),
                Quantity = reader.GetDecimal(1)
            };
        }

        private static async Task<(decimal LedgerStock, bool HasHistory)> GetLedgerStockInTransactionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int productId,
            int warehouseId)
        {
            const string sql = @"
                SELECT COALESCE(SUM(di.Quantity), 0), COUNT(di.Id)
                FROM DocumentItem di
                INNER JOIN Document d ON d.Id = di.DocumentId
                WHERE di.ProductId = @productId
                  AND d.WarehouseId = @warehouseId";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@productId", productId);
            cmd.Parameters.AddWithValue("@warehouseId", warehouseId);
            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                decimal ledgerStock = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
                long rowCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                return (ledgerStock, rowCount > 0);
            }

            return (0, false);
        }

        /// <summary>
        /// Menghitung stok berdasarkan akumulasi histori DocumentItem (metode ledger Aronium).
        /// Jika belum ada histori, caller bisa fallback ke Stock.Quantity.
        /// </summary>
        public async Task<(decimal LedgerStock, bool HasHistory)> GetLedgerStockAsync(
            int productId,
            int warehouseId = 1)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();
                return await GetLedgerStockInTransactionAsync(connection, transaction, productId, warehouseId);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error getting ledger stock for product {productId}: {ex.Message}",
                        "Inventory",
                        ex.ToString());
                }
            }

            return (0, false);
        }

        private static async Task UpdateOrInsertStockAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int productId,
            int warehouseId,
            decimal newQuantity,
            StockSnapshot snapshot)
        {
            if (snapshot.Exists)
            {
                using var updateCmd = new SqliteCommand("UPDATE Stock SET Quantity = @newQty WHERE Id = @stockId", connection, transaction);
                updateCmd.Parameters.AddWithValue("@newQty", newQuantity);
                updateCmd.Parameters.AddWithValue("@stockId", snapshot.StockId);
                await updateCmd.ExecuteNonQueryAsync();
                return;
            }

            using var insertCmd = new SqliteCommand(
                "INSERT INTO Stock (ProductId, WarehouseId, Quantity) VALUES (@prodId, @warehouseId, @qty)",
                connection,
                transaction);
            insertCmd.Parameters.AddWithValue("@prodId", productId);
            insertCmd.Parameters.AddWithValue("@warehouseId", warehouseId);
            insertCmd.Parameters.AddWithValue("@qty", newQuantity);
            await insertCmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Membuat dokumen Purchase (Restock) baru di Aronium.
        /// Meniru mekanisme Aronium Lite agar aman dan tercatat.
        /// </summary>
        public async Task<RestockResult> CreatePurchaseDocumentAsync(
            int productId,
            decimal quantity,
            decimal price,
            int userId = 1,
            string? note = null)
        {
            var result = new RestockResult { Success = false };

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                // Enable foreign key enforcement
                using (var fkCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    await fkCmd.ExecuteNonQueryAsync();
                }

                // Gunakan Transaction untuk memastikan konsistensi data
                using var transaction = connection.BeginTransaction();

                try
                {
                    // VALIDASI: Cek apakah ProductId ada
                    var checkProductCmd = new SqliteCommand("SELECT Id, IsEnabled FROM Product WHERE Id = @id", connection, transaction);
                    checkProductCmd.Parameters.AddWithValue("@id", productId);
                    using var productReader = await checkProductCmd.ExecuteReaderAsync();
                    if (!await productReader.ReadAsync())
                    {
                        result.Error = $"ProductId {productId} tidak ditemukan di database.";
                        return result;
                    }
                    
                    bool isEnabled = productReader.IsDBNull(1) ? true : Convert.ToBoolean(productReader[1]);
                    if (!isEnabled)
                    {
                        result.Error = $"Produk dengan ID {productId} tidak aktif (IsEnabled = 0). Pilih produk lain.";
                        return result;
                    }
                    productReader.Close();

                    // VALIDASI: Cek apakah UserId ada
                    var checkUserCmd = new SqliteCommand("SELECT Id FROM [User] WHERE Id = @id", connection, transaction);
                    checkUserCmd.Parameters.AddWithValue("@id", userId);
                    var userExists = await checkUserCmd.ExecuteScalarAsync();
                    if (userExists == null)
                    {
                        result.Error = $"UserId {userId} tidak ditemukan. Coba gunakan UserId lain.";
                        return result;
                    }

                    // VALIDASI: Cek WarehouseId yang tersedia
                    int warehouseId = 1; // Default
                    var checkWarehouseCmd = new SqliteCommand("SELECT Id FROM Warehouse WHERE Id = @id", connection, transaction);
                    checkWarehouseCmd.Parameters.AddWithValue("@id", warehouseId);
                    var warehouseExists = await checkWarehouseCmd.ExecuteScalarAsync();
                    
                    if (warehouseExists == null)
                    {
                        // Cari warehouse pertama yang ada
                        var getFirstWarehouseCmd = new SqliteCommand("SELECT Id FROM Warehouse ORDER BY Id LIMIT 1", connection, transaction);
                        var firstWarehouse = await getFirstWarehouseCmd.ExecuteScalarAsync();
                        
                        if (firstWarehouse == null)
                        {
                            result.Error = "Tidak ada warehouse di database. Buat warehouse terlebih dahulu di Aronium.";
                            return result;
                        }
                        
                        warehouseId = Convert.ToInt32(firstWarehouse);
                    }

                    // VALIDASI: Cek apakah CustomerId "Walk-in customer" ada
                    int customerId = 1; // Default Walk-in customer
                    var checkCustomerCmd = new SqliteCommand("SELECT Id FROM Customer WHERE Id = @id", connection, transaction);
                    checkCustomerCmd.Parameters.AddWithValue("@id", customerId);
                    var customerExists = await checkCustomerCmd.ExecuteScalarAsync();
                    
                    if (customerExists == null)
                    {
                        // Cari customer pertama yang ada
                        var getFirstCustomerCmd = new SqliteCommand("SELECT Id FROM Customer ORDER BY Id LIMIT 1", connection, transaction);
                        var firstCustomer = await getFirstCustomerCmd.ExecuteScalarAsync();
                        
                        if (firstCustomer == null)
                        {
                            result.Error = "Tidak ada customer di database. Buat customer terlebih dahulu di Aronium.";
                            return result;
                        }
                        
                        customerId = Convert.ToInt32(firstCustomer);
                    }

                    // 1. Generate Nomor Dokumen (Format: YY-100-NNNNNN)
                    // IMPORTANT: DocumentTypeId untuk Purchase adalah 1 (BUKAN 100!)
                    int purchaseTypeId = 1; // Purchase = 1 berdasarkan database Aronium
                    
                    // PENTING: Gunakan PRAGMA writable_schema untuk menghindari race condition
                    // dengan Aronium yang mungkin juga sedang membuat dokumen
                    string docNumber = await GenerateNextDocumentNumberAsync(connection, transaction, purchaseTypeId);
                    
                    // PENTING: Format tanggal harus persis seperti Aronium
                    // Date: "YYYY-MM-DD 00:00:00"
                    // StockDate, DateCreated, DateUpdated: "YYYY-MM-DD HH:mm:ss.ffffff"
                    string today = DateTime.Now.ToString("yyyy-MM-dd") + " 00:00:00";
                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
                    decimal total = quantity * price;

                    // 2. Insert Document (Header)
                    // Schema Aronium Verified: Id, Number, UserId, CustomerId, OrderNumber, Date, StockDate, Total, IsClockedOut, DocumentTypeId, WarehouseId, ReferenceDocumentNumber, DateCreated, DateUpdated, InternalNote, Note, DueDate, Discount, DiscountType, PaidStatus, DiscountApplyRule
                    // WAJIB diisi: Number, UserId, Date, StockDate, Total, DocumentTypeId, WarehouseId, DateCreated, DateUpdated, Discount, DiscountType, PaidStatus, DiscountApplyRule, DueDate
                    // PENTING: PaidStatus = 1 (Tidak Dibayar), BUKAN 0!
                    string insertDocSql = @"
                        INSERT INTO Document
                            (Number, Date, StockDate, DocumentTypeId, Total, UserId, CustomerId, WarehouseId, DateCreated, DateUpdated, DueDate, Discount, DiscountType, PaidStatus, DiscountApplyRule, IsClockedOut)
                        VALUES
                            (@number, @date, @stockDate, @typeId, @total, @userId, @customerId, @warehouseId, @created, @updated, @dueDate, 0, 0, 1, 0, 0)";

                    using var cmdDoc = new SqliteCommand(insertDocSql, connection, transaction);
                    cmdDoc.Parameters.AddWithValue("@number", docNumber);
                    cmdDoc.Parameters.AddWithValue("@date", today);
                    cmdDoc.Parameters.AddWithValue("@stockDate", now);
                    cmdDoc.Parameters.AddWithValue("@typeId", purchaseTypeId); // 1 = Purchase (BUKAN 100!)
                    cmdDoc.Parameters.AddWithValue("@total", total);
                    cmdDoc.Parameters.AddWithValue("@userId", userId);
                    cmdDoc.Parameters.AddWithValue("@customerId", customerId); // Walk-in customer
                    cmdDoc.Parameters.AddWithValue("@warehouseId", warehouseId);
                    cmdDoc.Parameters.AddWithValue("@created", now);
                    cmdDoc.Parameters.AddWithValue("@updated", now);
                    cmdDoc.Parameters.AddWithValue("@dueDate", today); // DueDate WAJIB diisi sama dengan Date

                    await cmdDoc.ExecuteNonQueryAsync();

                    // Get ID dokumen yang baru dibuat
                    using var cmdId = new SqliteCommand("SELECT last_insert_rowid()", connection, transaction);
                    int documentId = Convert.ToInt32(await cmdId.ExecuteScalarAsync());

                    // 3. Insert DocumentItem (Detail)
                    // Schema Aronium Verified: Id, DocumentId, ProductId, Quantity, ExpectedQuantity, PriceBeforeTax, Price, Discount, DiscountType, ProductCost, PriceBeforeTaxAfterDiscount, PriceAfterDiscount, Total, TotalAfterDocumentDiscount, DiscountApplyRule
                    string insertItemSql = @"
                        INSERT INTO DocumentItem
                            (DocumentId, ProductId, Quantity, ExpectedQuantity, PriceBeforeTax, Price, Discount, DiscountType, ProductCost, PriceBeforeTaxAfterDiscount, PriceAfterDiscount, Total, TotalAfterDocumentDiscount, DiscountApplyRule)
                        VALUES
                            (@docId, @prodId, @qty, @expectedQty, @priceBeforeTax, @price, 0, 0, @cost, @priceAfterDiscount, @priceAfterDiscount, @total, @total, 0)";

                    using var cmdItem = new SqliteCommand(insertItemSql, connection, transaction);
                    cmdItem.Parameters.AddWithValue("@docId", documentId);
                    cmdItem.Parameters.AddWithValue("@prodId", productId);
                    cmdItem.Parameters.AddWithValue("@qty", quantity);
                    cmdItem.Parameters.AddWithValue("@expectedQty", quantity); // ExpectedQuantity sama dengan Quantity untuk Purchase
                    cmdItem.Parameters.AddWithValue("@priceBeforeTax", price);
                    cmdItem.Parameters.AddWithValue("@price", price);
                    cmdItem.Parameters.AddWithValue("@cost", price); // ProductCost sama dengan Price untuk Purchase
                    cmdItem.Parameters.AddWithValue("@priceAfterDiscount", price);
                    cmdItem.Parameters.AddWithValue("@total", total);

                    await cmdItem.ExecuteNonQueryAsync();

                    // 4. UPDATE TABEL STOCK (PENTING!)
                    // Aronium tidak otomatis update tabel Stock untuk dokumen eksternal
                    // Kita perlu manual update stok untuk Purchase (menambah stok)
                    
                    // First, check if stock record exists
                    string checkStockSql = "SELECT Id, Quantity FROM Stock WHERE ProductId = @prodId AND WarehouseId = @warehouseId";
                    using var cmdCheckStock = new SqliteCommand(checkStockSql, connection, transaction);
                    cmdCheckStock.Parameters.AddWithValue("@prodId", productId);
                    cmdCheckStock.Parameters.AddWithValue("@warehouseId", warehouseId);
                    using var stockReader = await cmdCheckStock.ExecuteReaderAsync();
                    
                    if (await stockReader.ReadAsync())
                    {
                        // Stock record exists, UPDATE it
                        long stockId = stockReader.GetInt64(0);
                        decimal currentQty = stockReader.GetDecimal(1);
                        decimal newQty = currentQty + quantity;
                        
                        string updateStockSql = "UPDATE Stock SET Quantity = @newQty WHERE Id = @stockId";
                        using var cmdUpdateStock = new SqliteCommand(updateStockSql, connection, transaction);
                        cmdUpdateStock.Parameters.AddWithValue("@newQty", newQty);
                        cmdUpdateStock.Parameters.AddWithValue("@stockId", stockId);
                        await cmdUpdateStock.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        // Stock record doesn't exist, INSERT it
                        string insertStockSql = "INSERT INTO Stock (ProductId, WarehouseId, Quantity) VALUES (@prodId, @warehouseId, @qty)";
                        using var cmdInsertStock = new SqliteCommand(insertStockSql, connection, transaction);
                        cmdInsertStock.Parameters.AddWithValue("@prodId", productId);
                        cmdInsertStock.Parameters.AddWithValue("@warehouseId", warehouseId);
                        cmdInsertStock.Parameters.AddWithValue("@qty", quantity);
                        await cmdInsertStock.ExecuteNonQueryAsync();
                    }
                    stockReader.Close();

                    // 5. Commit Transaction
                    transaction.Commit();

                    result.Success = true;
                    result.DocumentNumber = docNumber;
                    result.DocumentId = documentId;
                    result.Total = total;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    
                    // Berikan error message yang lebih spesifik untuk foreign key errors
                    if (ex.Message.Contains("FOREIGN KEY constraint failed"))
                    {
                        result.Error = $"Gagal membuat dokumen restock: Foreign Key constraint gagal.\n\n" +
                            $"Kemungkinan penyebab:\n" +
                            $"1. ProductId {productId} tidak ada atau tidak aktif\n" +
                            $"2. UserId {userId} tidak ada\n" +
                            $"3. WarehouseId tidak ada\n" +
                            $"4. CustomerId (Walk-in customer) tidak ada\n" +
                            $"5. DocumentTypeId (Purchase) tidak ada\n\n" +
                            $"Detail error: {ex.Message}";
                    }
                    else
                    {
                        result.Error = $"Gagal membuat dokumen restock: {ex.Message}";
                    }
                    
                    throw new Exception($"Gagal membuat dokumen restock: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error creating purchase document: {ex.Message}", "Restock", ex.ToString());
                }
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Membuat dokumen Inventory Count (Quick Inventory) untuk mengkoreksi stok.
        /// Document Type: 3 (Inventory Count), TypeCode: 300
        /// Parameter targetStock adalah stok akhir yang diinginkan, bukan jumlah tambahan.
        /// </summary>
        public async Task<RestockResult> CreateInventoryCountDocumentAsync(
            int productId,
            decimal targetStock,
            int userId = 1,
            decimal? productCostOverride = null,
            decimal? documentPriceOverride = null,
            string? internalNoteOverride = null,
            bool updateMasterCost = true)
        {
            var result = new RestockResult { Success = false };

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                // Enable foreign key enforcement
                using (var fkCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    await fkCmd.ExecuteNonQueryAsync();
                }

                // Gunakan Transaction untuk memastikan konsistensi data
                using var transaction = connection.BeginTransaction();

                try
                {
                    // VALIDASI: Cek apakah ProductId ada
                    var checkProductCmd = new SqliteCommand("SELECT Id, IsEnabled FROM Product WHERE Id = @id", connection, transaction);
                    checkProductCmd.Parameters.AddWithValue("@id", productId);
                    using var productReader = await checkProductCmd.ExecuteReaderAsync();
                    if (!await productReader.ReadAsync())
                    {
                        result.Error = $"ProductId {productId} tidak ditemukan di database.";
                        return result;
                    }

                    if (targetStock < 0)
                    {
                        result.Error = "Target stok tidak boleh negatif.";
                        return result;
                    }

                    bool isEnabled = productReader.IsDBNull(1) ? true : Convert.ToBoolean(productReader[1]);
                    if (!isEnabled)
                    {
                        result.Error = $"Produk dengan ID {productId} tidak aktif (IsEnabled = 0). Pilih produk lain.";
                        return result;
                    }
                    productReader.Close();

                    // VALIDASI: Cek apakah UserId ada
                    var checkUserCmd = new SqliteCommand("SELECT Id FROM [User] WHERE Id = @id", connection, transaction);
                    checkUserCmd.Parameters.AddWithValue("@id", userId);
                    var userExists = await checkUserCmd.ExecuteScalarAsync();
                    if (userExists == null)
                    {
                        result.Error = $"UserId {userId} tidak ditemukan.";
                        return result;
                    }

                    int warehouseId = await ResolveWarehouseIdAsync(connection, transaction);

                    // 1. Generate Nomor Dokumen (Format: YY-300-NNNNNN)
                    int inventoryTypeId = 3; // Inventory Count = 3
                    string docNumber = await GenerateNextDocumentNumberAsync(connection, transaction, inventoryTypeId);

                    // Format tanggal
                    string today = DateTime.Now.ToString("yyyy-MM-dd") + " 00:00:00";
                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");

                    // 2. Ambil stok saat ini dari Stock.Quantity (sumber kebenaran Aronium)
                    // BUKAN dari SUM(DocumentItem) — itu hanya histori/log
                    // Aronium Quick Inventory selalu baca Stock.Quantity sebagai basis
                    var stockSnapshot = await GetStockSnapshotAsync(connection, transaction, productId, warehouseId);
                    decimal currentStock = stockSnapshot.Quantity;

                    // LOGIC INVENTORY COUNT (SET MODE):
                    // Parameter 'targetStock' adalah TARGET stok akhir (bukan perubahan)
                    // selisih = target - currentStock
                    // newStock = target
                    decimal newStock = targetStock;
                    decimal selisih = targetStock - currentStock;

                    if (selisih == 0)
                    {
                        result.Error = "Tidak ada perubahan stok. Target stok sudah sama dengan stok saat ini.";
                        return result;
                    }

                    // Untuk Inventory Count Aronium, Quantity perlu menyimpan stok akhir
                    // sedangkan ExpectedQuantity menyimpan stok sebelum perubahan.
                    // Selisih tetap dihitung dari target - currentStock untuk log/bot.
                    decimal actualQuantity = targetStock;
                    decimal expectedQuantity = currentStock;

                    // Ambil data produk untuk mengisi harga
                    var getProductCmd = new SqliteCommand("SELECT Id, Cost, Price FROM Product WHERE Id = @id", connection, transaction);
                    getProductCmd.Parameters.AddWithValue("@id", productId);
                    using var productReader2 = await getProductCmd.ExecuteReaderAsync();
                    
                    decimal productCost = 0;
                    decimal productPrice = 0;
                    if (await productReader2.ReadAsync())
                    {
                        productCost = productReader2.IsDBNull(1) ? 0 : productReader2.GetDecimal(1);
                        productPrice = productReader2.IsDBNull(2) ? 0 : productReader2.GetDecimal(2);
                    }
                    productReader2.Close();
                    if (productCostOverride.GetValueOrDefault() > 0)
                    {
                        productCost = productCostOverride!.Value;
                    }
                    if (documentPriceOverride.GetValueOrDefault() > 0)
                    {
                        productPrice = documentPriceOverride!.Value;
                    }

                    if (_loggingService != null)
                    {
                        await _loggingService.LogInfoAsync(
                            $"[Inventory] ProductId={productId} WarehouseId={warehouseId} StockQuantity={currentStock} Target={targetStock} StoredQuantity={actualQuantity} Delta={selisih}",
                            "Inventory");
                    }

                    // 3. Insert Document (Header)
                    // SESUAI DENGAN ARONIUM: InternalNote diisi dengan "Quick inventory [tanggal]"
                    string insertDocSql = @"
                        INSERT INTO Document
                            (Number, Date, StockDate, DocumentTypeId, Total, UserId, WarehouseId, DateCreated, DateUpdated, DueDate, Discount, DiscountType, PaidStatus, DiscountApplyRule, IsClockedOut, InternalNote)
                        VALUES
                            (@number, @date, @stockDate, @typeId, @total, @userId, @warehouseId, @created, @updated, @dueDate, 0, 0, 0, 0, 0, @internalNote)";

                    // Total tetap merepresentasikan nilai perubahan stok, bukan stok akhir.
                    decimal total = selisih * productPrice;
                    string internalNote = !string.IsNullOrWhiteSpace(internalNoteOverride)
                        ? internalNoteOverride!
                        : $"Quick inventory {DateTime.Now:MM/dd/yyyy h:mm:ss tt}";

                    using var cmdDoc = new SqliteCommand(insertDocSql, connection, transaction);
                    cmdDoc.Parameters.AddWithValue("@number", docNumber);
                    cmdDoc.Parameters.AddWithValue("@date", today);
                    cmdDoc.Parameters.AddWithValue("@stockDate", now);
                    cmdDoc.Parameters.AddWithValue("@typeId", inventoryTypeId); // 3 = Inventory Count
                    cmdDoc.Parameters.AddWithValue("@total", total);
                    cmdDoc.Parameters.AddWithValue("@userId", userId);
                    cmdDoc.Parameters.AddWithValue("@warehouseId", warehouseId);
                    cmdDoc.Parameters.AddWithValue("@created", now);
                    cmdDoc.Parameters.AddWithValue("@updated", now);
                    cmdDoc.Parameters.AddWithValue("@dueDate", today);
                    cmdDoc.Parameters.AddWithValue("@internalNote", internalNote);

                    await cmdDoc.ExecuteNonQueryAsync();

                    // Get ID dokumen yang baru dibuat
                    using var cmdId = new SqliteCommand("SELECT last_insert_rowid()", connection, transaction);
                    int documentId = Convert.ToInt32(await cmdId.ExecuteScalarAsync());

                    // 4. Insert DocumentItem (Detail)
                    // Format yang dibutuhkan UI Aronium:
                    // - Quantity = stok akhir / target stock
                    // - ExpectedQuantity = stok saat ini sebelum perubahan
                    // - Delta diturunkan dari (Quantity - ExpectedQuantity)
                    // - Total tetap berbasis selisih perubahan stok
                    string insertItemSql = @"
                        INSERT INTO DocumentItem
                            (DocumentId, ProductId, Quantity, ExpectedQuantity, PriceBeforeTax, Price, Discount, DiscountType, ProductCost, PriceBeforeTaxAfterDiscount, PriceAfterDiscount, Total, TotalAfterDocumentDiscount, DiscountApplyRule)
                        VALUES
                            (@docId, @prodId, @qty, @expectedQty, @priceBeforeTax, @price, 0, 0, @productCost, 0, 0, @total, 0, 0)";

                    using var cmdItem = new SqliteCommand(insertItemSql, connection, transaction);
                    cmdItem.Parameters.AddWithValue("@docId", documentId);
                    cmdItem.Parameters.AddWithValue("@prodId", productId);
                    cmdItem.Parameters.AddWithValue("@qty", actualQuantity); // Stok akhir / target stock
                    cmdItem.Parameters.AddWithValue("@expectedQty", expectedQuantity); // Stok saat ini
                    cmdItem.Parameters.AddWithValue("@priceBeforeTax", productPrice);
                    cmdItem.Parameters.AddWithValue("@price", productPrice);
                    cmdItem.Parameters.AddWithValue("@productCost", productCost);
                    cmdItem.Parameters.AddWithValue("@total", total);

                    await cmdItem.ExecuteNonQueryAsync();

                    // 5. UPDATE TABEL STOCK
                    string countStockSql = "SELECT COUNT(*) FROM Stock WHERE ProductId = @prodId AND WarehouseId = @warehouseId";
                    using var cmdCountStock = new SqliteCommand(countStockSql, connection, transaction);
                    cmdCountStock.Parameters.AddWithValue("@prodId", productId);
                    cmdCountStock.Parameters.AddWithValue("@warehouseId", warehouseId);
                    long stockCount = Convert.ToInt64(await cmdCountStock.ExecuteScalarAsync());

                    if (stockCount > 0)
                    {
                        string updateStockSql = "UPDATE Stock SET Quantity = @newQty WHERE ProductId = @prodId AND WarehouseId = @warehouseId";
                        using var cmdUpdateStock = new SqliteCommand(updateStockSql, connection, transaction);
                        cmdUpdateStock.Parameters.AddWithValue("@newQty", newStock);
                        cmdUpdateStock.Parameters.AddWithValue("@prodId", productId);
                        cmdUpdateStock.Parameters.AddWithValue("@warehouseId", warehouseId);
                        await cmdUpdateStock.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        string insertStockSql = "INSERT INTO Stock (ProductId, WarehouseId, Quantity) VALUES (@prodId, @warehouseId, @qty)";
                        using var cmdInsertStock = new SqliteCommand(insertStockSql, connection, transaction);
                        cmdInsertStock.Parameters.AddWithValue("@prodId", productId);
                        cmdInsertStock.Parameters.AddWithValue("@warehouseId", warehouseId);
                        cmdInsertStock.Parameters.AddWithValue("@qty", newStock);
                        await cmdInsertStock.ExecuteNonQueryAsync();
                    }

                    if (updateMasterCost && productCostOverride.GetValueOrDefault() > 0)
                    {
                        var productColumns = await GetTableColumnsAsync(connection, "Product", transaction);
                        var productColumnSet = new HashSet<string>(productColumns, StringComparer.OrdinalIgnoreCase);
                        var setClauses = new List<string> { "Cost = @cost" };
                        if (productColumnSet.Contains("LastPurchasePrice"))
                        {
                            setClauses.Add("LastPurchasePrice = @cost");
                        }

                        if (productColumnSet.Contains("DateUpdated"))
                        {
                            setClauses.Add("DateUpdated = @updated");
                        }

                        string updateProductCostSql = $"UPDATE Product SET {string.Join(", ", setClauses)} WHERE Id = @prodId";
                        using var cmdUpdateProductCost = new SqliteCommand(updateProductCostSql, connection, transaction);
                        cmdUpdateProductCost.Parameters.AddWithValue("@cost", productCostOverride!.Value);
                        cmdUpdateProductCost.Parameters.AddWithValue("@updated", now);
                        cmdUpdateProductCost.Parameters.AddWithValue("@prodId", productId);
                        await cmdUpdateProductCost.ExecuteNonQueryAsync();
                    }

                    // 6. Commit Transaction
                    transaction.Commit();

                    result.Success = true;
                    result.DocumentNumber = docNumber;
                    result.DocumentId = documentId;
                    result.OldStock = currentStock;
                    result.NewStock = newStock;
                    result.Total = total;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception($"Gagal membuat dokumen inventory: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error creating inventory document: {ex.Message}", "Inventory", ex.ToString());
                }
                result.Error = ex.Message;
            }

            return result;
        }

        public async Task<BulkDocumentResult> CreateBulkPurchaseDocumentAsync(
            IReadOnlyCollection<BulkDocumentItemInput> items,
            int userId = 1,
            string? note = null,
            string? supplierName = null,
            int? supplierCustomerId = null)
        {
            var result = new BulkDocumentResult { Success = false };
            if (items == null || items.Count == 0)
            {
                result.Error = "Tidak ada item restock yang diproses.";
                return result;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                using (var fkCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    await fkCmd.ExecuteNonQueryAsync();
                }

                using var transaction = connection.BeginTransaction();

                try
                {
                    await EnsureUserExistsAsync(connection, transaction, userId);
                    int warehouseId = await ResolveWarehouseIdAsync(connection, transaction);
                    int customerId = supplierCustomerId.HasValue && supplierCustomerId.Value > 0
                        ? supplierCustomerId.Value
                        : string.IsNullOrWhiteSpace(supplierName)
                            ? await ResolvePurchaseCustomerIdAsync(connection, transaction)
                            : await ResolveOrCreateSupplierCustomerIdAsync(connection, transaction, supplierName);

                    var validatedItems = new List<(BulkDocumentItemInput Input, ValidatedProductData Product, StockSnapshot Stock)>();
                    foreach (var item in items)
                    {
                        if (item.Quantity <= 0)
                        {
                            throw new InvalidOperationException($"Quantity restock untuk produk {item.ProductName} harus lebih dari 0.");
                        }

                        var product = await LoadValidatedProductDataAsync(connection, transaction, item.ProductId);
                        var stock = await GetStockSnapshotAsync(connection, transaction, item.ProductId, warehouseId);
                        validatedItems.Add((item, product, stock));
                    }

                    int purchaseTypeId = 1;
                    string docNumber = await GenerateNextDocumentNumberAsync(connection, transaction, purchaseTypeId);
                    string today = DateTime.Now.ToString("yyyy-MM-dd") + " 00:00:00";
                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
                    decimal total = validatedItems.Sum(x => x.Input.Quantity * x.Input.Price);

                    const string insertDocSql = @"
                        INSERT INTO Document
                            (Number, Date, StockDate, DocumentTypeId, Total, UserId, CustomerId, WarehouseId, DateCreated, DateUpdated, DueDate, Discount, DiscountType, PaidStatus, DiscountApplyRule, IsClockedOut)
                        VALUES
                            (@number, @date, @stockDate, @typeId, @total, @userId, @customerId, @warehouseId, @created, @updated, @dueDate, 0, 0, 1, 0, 0)";

                    using (var cmdDoc = new SqliteCommand(insertDocSql, connection, transaction))
                    {
                        cmdDoc.Parameters.AddWithValue("@number", docNumber);
                        cmdDoc.Parameters.AddWithValue("@date", today);
                        cmdDoc.Parameters.AddWithValue("@stockDate", now);
                        cmdDoc.Parameters.AddWithValue("@typeId", purchaseTypeId);
                        cmdDoc.Parameters.AddWithValue("@total", total);
                        cmdDoc.Parameters.AddWithValue("@userId", userId);
                        cmdDoc.Parameters.AddWithValue("@customerId", customerId);
                        cmdDoc.Parameters.AddWithValue("@warehouseId", warehouseId);
                        cmdDoc.Parameters.AddWithValue("@created", now);
                        cmdDoc.Parameters.AddWithValue("@updated", now);
                        cmdDoc.Parameters.AddWithValue("@dueDate", today);
                        await cmdDoc.ExecuteNonQueryAsync();
                    }

                    int documentId;
                    using (var cmdId = new SqliteCommand("SELECT last_insert_rowid()", connection, transaction))
                    {
                        documentId = Convert.ToInt32(await cmdId.ExecuteScalarAsync());
                    }

                    const string insertItemSql = @"
                        INSERT INTO DocumentItem
                            (DocumentId, ProductId, Quantity, ExpectedQuantity, PriceBeforeTax, Price, Discount, DiscountType, ProductCost, PriceBeforeTaxAfterDiscount, PriceAfterDiscount, Total, TotalAfterDocumentDiscount, DiscountApplyRule)
                        VALUES
                            (@docId, @prodId, @qty, @expectedQty, @priceBeforeTax, @price, 0, 0, @cost, @priceAfterDiscount, @priceAfterDiscount, @total, @total, 0)";

                    foreach (var item in validatedItems)
                    {
                        decimal lineTotal = item.Input.Quantity * item.Input.Price;
                        using (var cmdItem = new SqliteCommand(insertItemSql, connection, transaction))
                        {
                            cmdItem.Parameters.AddWithValue("@docId", documentId);
                            cmdItem.Parameters.AddWithValue("@prodId", item.Input.ProductId);
                            cmdItem.Parameters.AddWithValue("@qty", item.Input.Quantity);
                            cmdItem.Parameters.AddWithValue("@expectedQty", item.Input.Quantity);
                            cmdItem.Parameters.AddWithValue("@priceBeforeTax", item.Input.Price);
                            cmdItem.Parameters.AddWithValue("@price", item.Input.Price);
                            cmdItem.Parameters.AddWithValue("@cost", item.Input.Price);
                            cmdItem.Parameters.AddWithValue("@priceAfterDiscount", item.Input.Price);
                            cmdItem.Parameters.AddWithValue("@total", lineTotal);
                            await cmdItem.ExecuteNonQueryAsync();
                        }

                        decimal oldStock = item.Stock.Quantity;
                        decimal newStock = oldStock + item.Input.Quantity;
                        await UpdateOrInsertStockAsync(connection, transaction, item.Input.ProductId, warehouseId, newStock, item.Stock);
                        result.Items.Add(new BulkDocumentItemResult
                        {
                            ProductId = item.Input.ProductId,
                            ProductName = item.Input.ProductName,
                            OldStock = oldStock,
                            NewStock = newStock,
                            Quantity = item.Input.Quantity,
                            Price = item.Input.Price,
                            Adjustment = item.Input.Quantity,
                            Unit = item.Input.Unit,
                            Total = lineTotal
                        });
                    }

                    transaction.Commit();
                    result.Success = true;
                    result.DocumentId = documentId;
                    result.DocumentNumber = docNumber;
                    result.Total = total;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception($"Gagal membuat dokumen bulk restock: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error creating bulk purchase document: {ex.Message}",
                        "Restock",
                        ex.ToString());
                }

                result.Error = ex.Message;
            }

            return result;
        }

        public async Task<BulkDocumentResult> CreateBulkInventoryCountDocumentAsync(
            IReadOnlyCollection<BulkDocumentItemInput> items,
            int userId = 1)
        {
            var result = new BulkDocumentResult { Success = false };
            if (items == null || items.Count == 0)
            {
                result.Error = "Tidak ada item inventory yang diproses.";
                return result;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                using (var fkCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    await fkCmd.ExecuteNonQueryAsync();
                }

                using var transaction = connection.BeginTransaction();

                try
                {
                    await EnsureUserExistsAsync(connection, transaction, userId);
                    int warehouseId = await ResolveWarehouseIdAsync(connection, transaction);

                    var validatedItems = new List<(BulkDocumentItemInput Input, ValidatedProductData Product, StockSnapshot Stock, decimal CurrentStock, decimal TargetStock, decimal Adjustment)>();
                    foreach (var item in items)
                    {
                        if (item.Quantity < 0)
                        {
                            throw new InvalidOperationException($"Target stok untuk produk {item.ProductName} tidak boleh negatif.");
                        }

                        var product = await LoadValidatedProductDataAsync(connection, transaction, item.ProductId);
                        var stock = await GetStockSnapshotAsync(connection, transaction, item.ProductId, warehouseId);
                        decimal currentStock = stock.Quantity;
                        decimal targetStock = item.Quantity;
                        decimal adjustment = targetStock - currentStock;
                        if (adjustment == 0)
                        {
                            continue;
                        }

                        validatedItems.Add((item, product, stock, currentStock, targetStock, adjustment));
                    }

                    if (!validatedItems.Any())
                    {
                        result.Error = "Tidak ada perubahan stok. Semua target inventory sudah sama dengan stok saat ini.";
                        return result;
                    }

                    int inventoryTypeId = 3;
                    string docNumber = await GenerateNextDocumentNumberAsync(connection, transaction, inventoryTypeId);
                    string today = DateTime.Now.ToString("yyyy-MM-dd") + " 00:00:00";
                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
                    decimal total = validatedItems.Sum(x => x.Adjustment * x.Product.Price);
                    string internalNote = $"Quick inventory {DateTime.Now:MM/dd/yyyy h:mm:ss tt}";

                    const string insertDocSql = @"
                        INSERT INTO Document
                            (Number, Date, StockDate, DocumentTypeId, Total, UserId, WarehouseId, DateCreated, DateUpdated, DueDate, Discount, DiscountType, PaidStatus, DiscountApplyRule, IsClockedOut, InternalNote)
                        VALUES
                            (@number, @date, @stockDate, @typeId, @total, @userId, @warehouseId, @created, @updated, @dueDate, 0, 0, 0, 0, 0, @internalNote)";

                    using (var cmdDoc = new SqliteCommand(insertDocSql, connection, transaction))
                    {
                        cmdDoc.Parameters.AddWithValue("@number", docNumber);
                        cmdDoc.Parameters.AddWithValue("@date", today);
                        cmdDoc.Parameters.AddWithValue("@stockDate", now);
                        cmdDoc.Parameters.AddWithValue("@typeId", inventoryTypeId);
                        cmdDoc.Parameters.AddWithValue("@total", total);
                        cmdDoc.Parameters.AddWithValue("@userId", userId);
                        cmdDoc.Parameters.AddWithValue("@warehouseId", warehouseId);
                        cmdDoc.Parameters.AddWithValue("@created", now);
                        cmdDoc.Parameters.AddWithValue("@updated", now);
                        cmdDoc.Parameters.AddWithValue("@dueDate", today);
                        cmdDoc.Parameters.AddWithValue("@internalNote", internalNote);
                        await cmdDoc.ExecuteNonQueryAsync();
                    }

                    int documentId;
                    using (var cmdId = new SqliteCommand("SELECT last_insert_rowid()", connection, transaction))
                    {
                        documentId = Convert.ToInt32(await cmdId.ExecuteScalarAsync());
                    }

                    const string insertItemSql = @"
                        INSERT INTO DocumentItem
                            (DocumentId, ProductId, Quantity, ExpectedQuantity, PriceBeforeTax, Price, Discount, DiscountType, ProductCost, PriceBeforeTaxAfterDiscount, PriceAfterDiscount, Total, TotalAfterDocumentDiscount, DiscountApplyRule)
                        VALUES
                            (@docId, @prodId, @qty, @expectedQty, @priceBeforeTax, @price, 0, 0, @productCost, 0, 0, @total, 0, 0)";

                    foreach (var item in validatedItems)
                    {
                        decimal lineTotal = item.Adjustment * item.Product.Price;
                        using (var cmdItem = new SqliteCommand(insertItemSql, connection, transaction))
                        {
                            cmdItem.Parameters.AddWithValue("@docId", documentId);
                            cmdItem.Parameters.AddWithValue("@prodId", item.Input.ProductId);
                            cmdItem.Parameters.AddWithValue("@qty", item.TargetStock);
                            cmdItem.Parameters.AddWithValue("@expectedQty", item.CurrentStock);
                            cmdItem.Parameters.AddWithValue("@priceBeforeTax", item.Product.Price);
                            cmdItem.Parameters.AddWithValue("@price", item.Product.Price);
                            cmdItem.Parameters.AddWithValue("@productCost", item.Product.Cost);
                            cmdItem.Parameters.AddWithValue("@total", lineTotal);
                            await cmdItem.ExecuteNonQueryAsync();
                        }

                        await UpdateOrInsertStockAsync(connection, transaction, item.Input.ProductId, warehouseId, item.TargetStock, item.Stock);
                        result.Items.Add(new BulkDocumentItemResult
                        {
                            ProductId = item.Input.ProductId,
                            ProductName = item.Input.ProductName,
                            OldStock = item.CurrentStock,
                            NewStock = item.TargetStock,
                            Quantity = item.TargetStock,
                            Price = item.Product.Price,
                            Adjustment = item.Adjustment,
                            Unit = item.Input.Unit,
                            Total = lineTotal
                        });
                    }

                    transaction.Commit();
                    result.Success = true;
                    result.DocumentId = documentId;
                    result.DocumentNumber = docNumber;
                    result.Total = total;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception($"Gagal membuat dokumen bulk inventory: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error creating bulk inventory document: {ex.Message}",
                        "Inventory",
                        ex.ToString());
                }

                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Generate nomor dokumen berikutnya berdasarkan tipe.
        /// Format: YY-TYPECODE-NNNNNN
        /// Contoh: 26-100-000002 (Purchase), 26-200-000001 (Sales)
        /// 
        /// PENTING: Ada mapping antara DocumentTypeId (FK) dan TypeCode (format nomor):
        /// - DocumentTypeId 1 (Purchase) → TypeCode 100
        /// - DocumentTypeId 2 (Sales) → TypeCode 200  
        /// - DocumentTypeId 3 (Inventory Count) → TypeCode 300
        /// - DocumentTypeId 4 (Refund) → TypeCode 220
        /// - DocumentTypeId 5 (Stock Return) → TypeCode 120
        /// - DocumentTypeId 6 (Loss) → TypeCode 400
        /// 
        /// Method ini akan:
        /// 1. Cek nomor terakhir di database berdasarkan TypeCode
        /// 2. Increment sequence
        /// 3. Return nomor baru yang berurutan
        /// 4. Handle race condition dengan retry jika nomor sudah ada
        /// </summary>
        private async Task<string> GenerateNextDocumentNumberAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int documentTypeId)
        {
            // Mapping DocumentTypeId → TypeCode untuk format nomor
            int typeCode = documentTypeId switch
            {
                1 => 100,  // Purchase
                2 => 200,  // Sales
                3 => 300,  // Inventory Count
                4 => 220,  // Refund
                5 => 120,  // Stock Return
                6 => 400,  // Loss
                _ => documentTypeId // Fallback: gunakan typeId langsung jika tidak ada di mapping
            };

            string year = DateTime.Now.ToString("yy");
            
            // PENTING: Cari berdasarkan TypeCode (100), BUKAN typeId (1)
            // Karena di database, nomor dokumen menggunakan TypeCode
            string pattern = $"{year}-{typeCode}-%";

            // Retry mechanism untuk menghindari race condition dengan Aronium
            int maxRetries = 5;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                // Cari nomor terakhir dengan typeCode ini
                string sql = @"
                    SELECT Number
                    FROM Document
                    WHERE Number LIKE @pattern
                    ORDER BY Number DESC
                    LIMIT 1";

                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@pattern", pattern);
                var result = await cmd.ExecuteScalarAsync();

                int sequence = 1;
                if (result != null && result != DBNull.Value)
                {
                    string lastNumber = result.ToString()!;
                    // Extract sequence dari "26-100-000001" -> "000001"
                    string[] parts = lastNumber.Split('-');
                    if (parts.Length == 3 && int.TryParse(parts[2], out int lastSeq))
                    {
                        sequence = lastSeq + 1;
                    }
                }

                string newNumber = $"{year}-{typeCode}-{sequence:D6}";

                // Cek apakah nomor ini sudah ada (race condition check)
                string checkSql = "SELECT COUNT(*) FROM Document WHERE Number = @number";
                using var checkCmd = new SqliteCommand(checkSql, connection, transaction);
                checkCmd.Parameters.AddWithValue("@number", newNumber);
                long count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());

                if (count == 0)
                {
                    // Nomor belum ada, aman digunakan
                    return newNumber;
                }

                // Nomor sudah ada (race condition), retry dengan sequence + 1
                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(100 * (attempt + 1)); // Exponential backoff
                }
            }

            // Jika semua retry gagal, throw exception
            throw new Exception("Gagal generate nomor dokumen unik setelah beberapa percobaan. Kemungkinan ada race condition dengan Aronium.");
        }

        #endregion

        #region History & Reports

        /// <summary>
        /// Mendapatkan riwayat restock (Purchase) untuk produk tertentu
        /// </summary>
        public async Task<List<RestockHistoryItem>> GetRestockHistoryAsync(int productId, int limit = 10)
        {
            var history = new List<RestockHistoryItem>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                // Cari DocumentItem untuk produk ini dengan DocumentType Purchase (Id = 1)
                // PENTING: DocumentTypeId = 1 untuk Purchase, BUKAN 100!
                string sql = @"
                    SELECT d.Number, d.Date, di.Quantity, di.Price, di.Total
                    FROM DocumentItem di
                    INNER JOIN Document d ON di.DocumentId = d.Id
                    WHERE di.ProductId = @prodId
                    AND d.DocumentTypeId = 1
                    ORDER BY d.Date DESC
                    LIMIT @limit";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@prodId", productId);
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    history.Add(new RestockHistoryItem
                    {
                        DocumentNumber = reader.IsDBNull(0) ? null : reader.GetString(0),
                        Date = reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1)),
                        Quantity = SafeConvertToDecimal(reader, 2) ?? 0,
                        Price = SafeConvertToDecimal(reader, 3) ?? 0,
                        Total = SafeConvertToDecimal(reader, 4) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading restock history: {ex.Message}", "Database", ex.ToString());
                }
            }

            return history;
        }

        /// <summary>
        /// Mendapatkan riwayat inventory untuk produk tertentu
        /// </summary>
        public async Task<List<InventoryHistoryItem>> GetInventoryHistoryAsync(int productId, int limit = 10)
        {
            var history = new List<InventoryHistoryItem>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                // Cari DocumentItem untuk produk ini dengan DocumentType Inventory Count (Id = 3)
                // PENTING: DocumentTypeId = 3 untuk Inventory Count, BUKAN 300!
                string sql = @"
                    SELECT d.Number, d.Date, (di.Quantity - di.ExpectedQuantity)
                    FROM DocumentItem di
                    INNER JOIN Document d ON di.DocumentId = d.Id
                    WHERE di.ProductId = @prodId
                    AND d.DocumentTypeId = 3
                    ORDER BY d.Date DESC
                    LIMIT @limit";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@prodId", productId);
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    history.Add(new InventoryHistoryItem
                    {
                        DocumentNumber = reader.IsDBNull(0) ? null : reader.GetString(0),
                        Date = reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1)),
                        QuantityChange = SafeConvertToDecimal(reader, 2) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading inventory history: {ex.Message}", "Database", ex.ToString());
                }
            }

            return history;
        }

        /// <summary>
        /// Mendapatkan produk dengan stok rendah untuk rekomendasi restock otomatis
        /// </summary>
        public async Task<List<RestockRecommendation>> GetAutoRestockRecommendationsAsync(int threshold = 10)
        {
            var recommendations = new List<RestockRecommendation>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                string sql = $@"
                    SELECT
                        p.Id,
                        p.Name,
                        {GetProductUnitSql("p")},
                        COALESCE(s.Quantity, 0) as CurrentStock,
                        p.Price,
                        p.Cost,
                        COALESCE((
                            SELECT SUM(di7.Quantity)
                            FROM DocumentItem di7
                            INNER JOIN Document d7 ON di7.DocumentId = d7.Id
                            WHERE di7.ProductId = p.Id
                            AND d7.DocumentTypeId = 2
                            AND date(d7.Date) >= date('now', '-6 days', 'localtime')
                        ), 0) as SalesLast7Days,
                        COALESCE((
                            SELECT SUM(di30.Quantity)
                            FROM DocumentItem di30
                            INNER JOIN Document d30 ON di30.DocumentId = d30.Id
                            WHERE di30.ProductId = p.Id
                            AND d30.DocumentTypeId = 2
                            AND date(d30.Date) >= date('now', '-29 days', 'localtime')
                        ), 0) as SalesLast30Days
                    FROM Product p
                    LEFT JOIN Stock s
                        ON p.Id = s.ProductId
                       AND s.WarehouseId = {PreferredWarehouseIdSql}
                    WHERE p.IsEnabled = 1
                    AND (s.Quantity < @threshold OR s.Quantity IS NULL)
                    AND p.Id NOT IN (
                        SELECT DISTINCT di.ProductId
                        FROM DocumentItem di
                        INNER JOIN Document d ON di.DocumentId = d.Id
                        WHERE d.DocumentTypeId = 1
                        AND date(d.Date) >= date('now', '-7 days')
                    )
                    ORDER BY s.Quantity ASC
                    LIMIT 20";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@threshold", threshold);

                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    decimal currentStock = SafeConvertToDecimal(reader, 3) ?? 0;
                    decimal salesLast7Days = SafeConvertToDecimal(reader, 6) ?? 0;
                    decimal salesLast30Days = SafeConvertToDecimal(reader, 7) ?? 0;
                    decimal average7Days = salesLast7Days / 7m;
                    decimal average30Days = salesLast30Days / 30m;
                    decimal blendedAverage = Math.Max(average7Days, average30Days);
                    bool requiresManualReview = salesLast7Days <= 0 && salesLast30Days <= 0;

                    decimal recommendedQty = 0;
                    string priority = "LOW";
                    int daysSafe = blendedAverage > 0
                        ? (int)Math.Floor(currentStock / blendedAverage)
                        : 999;

                    if (!requiresManualReview)
                    {
                        decimal targetStock = Math.Ceiling(Math.Max(average7Days * 14m, average30Days * 21m));
                        recommendedQty = Math.Max(0, targetStock - currentStock);

                        if (currentStock <= 0 || daysSafe <= 3)
                        {
                            priority = "HIGH";
                        }
                        else if (daysSafe <= 7)
                        {
                            priority = "MEDIUM";
                        }
                    }

                    recommendations.Add(new RestockRecommendation
                    {
                        ProductId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        ProductName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Unit = reader.IsDBNull(2) ? "Pcs" : reader.GetString(2),
                        CurrentStock = currentStock,
                        SellingPrice = SafeConvertToDecimal(reader, 4) ?? 0,
                        CostPrice = SafeConvertToDecimal(reader, 5) ?? 0,
                        RecommendedQty = recommendedQty,
                        AverageSales = blendedAverage,
                        AverageDailySales7Days = average7Days,
                        AverageDailySales30Days = average30Days,
                        SalesLast7Days = salesLast7Days,
                        SalesLast30Days = salesLast30Days,
                        DaysSafe = daysSafe,
                        Priority = requiresManualReview ? "REVIEW" : priority,
                        RequiresManualReview = requiresManualReview
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading auto restock recommendations: {ex.Message}", "Database", ex.ToString());
                }
            }

            return recommendations;
        }

        /// <summary>
        /// Mendapatkan produk dengan stok habis atau minus untuk notifikasi
        /// </summary>
        public async Task<List<Product>> GetCriticalStockProductsAsync()
        {
            var products = new List<Product>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                string sql = $@"
                    SELECT p.Id, p.Name, {GetProductUnitSql("p")}, s.Quantity
                    FROM Product p
                    LEFT JOIN Stock s
                        ON p.Id = s.ProductId
                       AND s.WarehouseId = {PreferredWarehouseIdSql}
                    WHERE p.IsEnabled = 1
                    AND (s.Quantity <= 0 OR s.Quantity IS NULL)
                    ORDER BY s.Quantity ASC
                    LIMIT 10";

                using var cmd = new SqliteCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Unit = reader.IsDBNull(2) ? "Pcs" : reader.GetString(2),
                        Stock = SafeConvertToDecimal(reader, 3) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading critical stock products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        #endregion

        #region Sales Analytics Methods

        /// <summary>
        /// Mendapatkan semua transaksi sales (DocumentTypeId = 2, TypeCode 200) dalam rentang tanggal
        /// </summary>
        public async Task<List<Transaction>> GetSalesTransactionsAsync(DateTime startDate, DateTime endDate)
        {
            var transactions = new List<Transaction>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi", "transaction" });

                if (string.IsNullOrEmpty(documentTable))
                    return transactions;

                // DocumentTypeId = 2 untuk Sales (TypeCode 200)
                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT
                        d.Id, d.Date, d.UserId, d.Total
                    FROM {ValidateTableName(documentTable)} d
                    WHERE d.DocumentTypeId = 2
                    AND date(d.Date) >= @startDate
                    AND date(d.Date) <= @endDate
                    ORDER BY d.Date ASC";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    transactions.Add(new Transaction
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Date = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                        UserId = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString(),
                        Total = SafeConvertToDecimal(reader, 3)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading sales transactions: {ex.Message}", "Database", ex.ToString());
                }
            }

            return transactions;
        }

        /// <summary>
        /// Menghitung total revenue dari transaksi sales (DocumentTypeId = 2) dalam rentang tanggal
        /// </summary>
        public async Task<decimal> GetSalesRevenueAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(documentTable))
                    return 0;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT COALESCE(SUM(Total), 0)
                    FROM {ValidateTableName(documentTable)}
                    WHERE DocumentTypeId = 2
                    AND date(Date) >= @startDate
                    AND date(Date) <= @endDate";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error calculating sales revenue: {ex.Message}", "Database", ex.ToString());
                }
            }

            return 0;
        }

        /// <summary>
        /// Menghitung total profit dari transaksi sales: SUM((Price - ProductCost) * Quantity)
        /// </summary>
        public async Task<decimal> GetSalesProfitAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return 0;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT COALESCE(SUM(
                        (di.Price - di.ProductCost) * di.Quantity
                    ), 0)
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    WHERE d.DocumentTypeId = 2
                    AND date(d.Date) >= @startDate
                    AND date(d.Date) <= @endDate";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToDecimal(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error calculating sales profit: {ex.Message}", "Database", ex.ToString());
                }
            }

            return 0;
        }

        /// <summary>
        /// Menghitung jumlah transaksi sales (DocumentTypeId = 2) dalam rentang tanggal
        /// </summary>
        public async Task<int> GetSalesTransactionCountAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(documentTable))
                    return 0;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT COUNT(*)
                    FROM {ValidateTableName(documentTable)}
                    WHERE DocumentTypeId = 2
                    AND date(Date) >= @startDate
                    AND date(Date) <= @endDate";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error counting sales transactions: {ex.Message}", "Database", ex.ToString());
                }
            }

            return 0;
        }

        /// <summary>
        /// Mendapatkan detail baris penjualan (per product per invoice) dari transaksi sales (DocumentTypeId = 2)
        /// dalam rentang tanggal. Mengembalikan list SalesLineItem.
        /// </summary>
        public async Task<List<SalesLineItem>> GetSalesLineItemsAsync(DateTime startDate, DateTime endDate)
        {
            var lineItems = new List<SalesLineItem>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi", "transaction" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return lineItems;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                // Build query: join Document + DocumentItem, optionally join Product for name
                string sql;
                if (!string.IsNullOrEmpty(productTable))
                {
                    sql = $@"
                        SELECT
                            d.Date,
                            d.Id as DocumentId,
                            p.Name as ProductName,
                            di.Quantity,
                            di.Price,
                            di.Total,
                            (di.Price - di.ProductCost) * di.Quantity as Profit
                        FROM {ValidateTableName(documentTable)} d
                        INNER JOIN {ValidateTableName(documentItemTable)} di ON d.Id = di.DocumentId
                        LEFT JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id
                        WHERE d.DocumentTypeId = 2
                        AND date(d.Date) >= @startDate
                        AND date(d.Date) <= @endDate
                        ORDER BY d.Date DESC, d.Id";
                }
                else
                {
                    sql = $@"
                        SELECT
                            d.Date,
                            d.Id as DocumentId,
                            NULL as ProductName,
                            di.Quantity,
                            di.Price,
                            di.Total,
                            (di.Price - di.ProductCost) * di.Quantity as Profit
                        FROM {ValidateTableName(documentTable)} d
                        INNER JOIN {ValidateTableName(documentItemTable)} di ON d.Id = di.DocumentId
                        WHERE d.DocumentTypeId = 2
                        AND date(d.Date) >= @startDate
                        AND date(d.Date) <= @endDate
                        ORDER BY d.Date DESC, d.Id";
                }

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    lineItems.Add(new SalesLineItem
                    {
                        Date = reader.IsDBNull(0) ? DateTime.Now : reader.GetDateTime(0),
                        Invoice = reader.IsDBNull(1) ? "-" : reader.GetValue(1).ToString(),
                        ProductName = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
                        Quantity = SafeConvertToDecimal(reader, 3) ?? 0,
                        Price = SafeConvertToDecimal(reader, 4) ?? 0,
                        Total = SafeConvertToDecimal(reader, 5) ?? 0,
                        Profit = SafeConvertToDecimal(reader, 6) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading sales line items: {ex.Message}", "Database", ex.ToString());
                }
            }

            return lineItems;
        }

        /// <summary>
        /// Mendapatkan top 10 produk terlaris berdasarkan quantity sold dari transaksi sales
        /// </summary>
        public async Task<List<ProductSalesData>> GetTopSellingProductsAsync(DateTime startDate, DateTime endDate, int limit = 10)
        {
            var products = new List<ProductSalesData>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });
                string? productTable = FindTable(tables, new[] { "Product", "products" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable) || string.IsNullOrEmpty(productTable))
                    return products;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT
                        p.Id,
                        p.Name,
                        {GetProductUnitSql("p")},
                        SUM(di.Quantity) as TotalQty,
                        SUM(di.Total) as TotalRevenue,
                        SUM((di.Price - di.ProductCost) * di.Quantity) as TotalProfit
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    INNER JOIN {ValidateTableName(productTable)} p ON di.ProductId = p.Id
                    WHERE d.DocumentTypeId = 2
                    AND date(d.Date) >= @startDate
                    AND date(d.Date) <= @endDate
                    GROUP BY p.Id, p.Name
                    ORDER BY TotalQty DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new ProductSalesData
                    {
                        ProductId = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        ProductName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                        Unit = reader.IsDBNull(2) ? "Pcs" : reader.GetString(2),
                        QuantitySold = SafeConvertToDecimal(reader, 3) ?? 0,
                        Revenue = SafeConvertToDecimal(reader, 4) ?? 0,
                        Profit = SafeConvertToDecimal(reader, 5) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading top selling products: {ex.Message}", "Database", ex.ToString());
                }
            }

            return products;
        }

        /// <summary>
        /// Mendapatkan data penjualan harian (tanggal dan total revenue per hari) dalam rentang tanggal
        /// </summary>
        public async Task<List<DailySalesData>> GetDailySalesAsync(DateTime startDate, DateTime endDate)
        {
            var dailySales = new List<DailySalesData>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(documentTable))
                    return dailySales;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT
                        date(Date) as SaleDate,
                        COUNT(*) as TransactionCount,
                        COALESCE(SUM(Total), 0) as DailyRevenue
                    FROM {ValidateTableName(documentTable)}
                    WHERE DocumentTypeId = 2
                    AND date(Date) >= @startDate
                    AND date(Date) <= @endDate
                    GROUP BY date(Date)
                    ORDER BY SaleDate ASC";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string dateStr = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    dailySales.Add(new DailySalesData
                    {
                        Date = DateTime.TryParse(dateStr, out var parsedDate) ? parsedDate : DateTime.Today,
                        DateLabel = dateStr,
                        TransactionCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        Revenue = SafeConvertToDecimal(reader, 2) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading daily sales: {ex.Message}", "Database", ex.ToString());
                }
            }

            return dailySales;
        }

        /// <summary>
        /// Mendapatkan informasi pelanggan yang melakukan pembelian dalam rentang tanggal
        /// </summary>
        public async Task<List<CustomerPurchaseInfo>> GetCustomerPurchasesAsync(DateTime startDate, DateTime endDate)
        {
            var customers = new List<CustomerPurchaseInfo>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? customerTable = FindTable(tables, new[] { "Customer", "customers", "pelanggan" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });

                if (string.IsNullOrEmpty(customerTable) || string.IsNullOrEmpty(documentTable))
                    return customers;

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT
                        c.Id, c.Name,
                        COUNT(d.Id) as PurchaseCount,
                        COALESCE(SUM(d.Total), 0) as TotalSpent,
                        MAX(d.Date) as LastPurchaseDate
                    FROM {ValidateTableName(customerTable)} c
                    INNER JOIN {ValidateTableName(documentTable)} d ON c.Id = d.CustomerId
                    WHERE d.DocumentTypeId = 2
                    AND date(d.Date) >= @startDate
                    AND date(d.Date) <= @endDate
                    GROUP BY c.Id, c.Name
                    ORDER BY PurchaseCount DESC, TotalSpent DESC";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    customers.Add(new CustomerPurchaseInfo
                    {
                        Id = reader.IsDBNull(0) ? null : reader.GetValue(0).ToString(),
                        Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        PurchaseCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        TotalSpent = SafeConvertToDecimal(reader, 3) ?? 0,
                        LastPurchaseDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4)
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading customer purchases: {ex.Message}", "Database", ex.ToString());
                }
            }

            return customers;
        }

        public async Task<int> GetNewCustomerCountAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                if (string.IsNullOrEmpty(documentTable))
                {
                    return 0;
                }

                string startDateStr = startDate.ToString("yyyy-MM-dd");
                string endDateStr = endDate.ToString("yyyy-MM-dd");

                string sql = $@"
                    SELECT COUNT(*)
                    FROM (
                        SELECT d.CustomerId
                        FROM {ValidateTableName(documentTable)} d
                        WHERE d.DocumentTypeId = 2
                          AND d.CustomerId IS NOT NULL
                        GROUP BY d.CustomerId
                        HAVING date(MIN(d.Date)) >= @startDate
                           AND date(MIN(d.Date)) <= @endDate
                    ) x";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@startDate", startDateStr);
                command.Parameters.AddWithValue("@endDate", endDateStr);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result ?? 0);
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading new customer count: {ex.Message}", "Database", ex.ToString());
                }

                return 0;
            }
        }

        #endregion

        #region Path Detection

        public static string? AutoDetectPosDbPath()
        {
            try
            {
                string username = Environment.UserName;
                string basePath = $@"C:\Users\{username}\AppData\Local\Aronium\Data\pos.db";

                if (File.Exists(basePath))
                {
                    return basePath;
                }

                // Coba cari di folder lain jika tidak ditemukan
                string[] possiblePaths = new[]
                {
                    $@"C:\Users\{username}\AppData\Local\Aronium\pos.db",
                    $@"C:\ProgramData\Aronium\Data\pos.db",
                    $@"C:\Aronium\Data\pos.db"
                };

                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Ignore errors during auto-detection
            }

            return null;
        }

        public static bool IsValidPosDbPath(string path)
        {
            return File.Exists(path);
        }

        #endregion

        #region Additional Methods for CommandHandler

        /// <summary>
        /// Mendapatkan riwayat restock untuk produk tertentu
        /// </summary>
        public async Task<List<RestockHistoryItem>> GetRestockHistoryAsync(string productId, int limit = 10)
        {
            var history = new List<RestockHistoryItem>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return history;

                string sql = $@"
                    SELECT 
                        d.Number, d.Date, di.Quantity, di.ProductCost, di.Total
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    WHERE di.ProductId = @prodId
                    AND d.DocumentTypeId = 1
                    ORDER BY d.Date DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@prodId", productId);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    history.Add(new RestockHistoryItem
                    {
                        DocumentNumber = reader.IsDBNull(0) ? null : reader.GetString(0),
                        Date = reader.IsDBNull(1) ? DateTime.Now : reader.GetDateTime(1),
                        Quantity = SafeConvertToDecimal(reader, 2) ?? 0,
                        UnitCost = SafeConvertToDecimal(reader, 3) ?? 0,
                        TotalCost = SafeConvertToDecimal(reader, 4) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading restock history: {ex.Message}", "Database", ex.ToString());
                }
            }

            return history;
        }

        /// <summary>
        /// Mendapatkan riwayat inventory untuk produk tertentu
        /// </summary>
        public async Task<List<InventoryHistoryItem>> GetInventoryHistoryAsync(string productId, int limit = 10)
        {
            var history = new List<InventoryHistoryItem>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return history;

                string sql = $@"
                    SELECT 
                        d.Number, d.Date, (di.Quantity - di.ExpectedQuantity)
                    FROM {ValidateTableName(documentItemTable)} di
                    INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                    WHERE di.ProductId = @prodId
                    AND d.DocumentTypeId = 3
                    ORDER BY d.Date DESC
                    LIMIT @limit";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@prodId", productId);
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    history.Add(new InventoryHistoryItem
                    {
                        DocumentNumber = reader.IsDBNull(0) ? null : reader.GetString(0),
                        Date = reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1)),
                        QuantityChange = SafeConvertToDecimal(reader, 2) ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error reading inventory history: {ex.Message}", "Database", ex.ToString());
                }
            }

            return history;
        }

        /// <summary>
        /// Update stok produk (untuk Quick Inventory)
        /// </summary>
        public async Task<bool> UpdateProductStockAsync(string productId, decimal newStock)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? stockTable = FindTable(tables, new[] { "Stock", "stock" });

                if (string.IsNullOrEmpty(stockTable))
                    return false;

                string sql = $@"
                    UPDATE {ValidateTableName(stockTable)}
                    SET Quantity = @newStock
                    WHERE ProductId = @prodId";

                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@newStock", newStock);
                command.Parameters.AddWithValue("@prodId", productId);

                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error updating product stock: {ex.Message}", "Database", ex.ToString());
                }
                return false;
            }
        }

        public async Task<bool> UpdateProductPricingAsync(string productId, decimal? cost, decimal? sellingPrice)
        {
            if (string.IsNullOrWhiteSpace(productId) ||
                (cost.GetValueOrDefault() <= 0 && sellingPrice.GetValueOrDefault() <= 0))
            {
                return false;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var columns = await GetTableColumnsAsync(connection, "Product");
                var columnSet = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
                var setClauses = new List<string>();

                if (cost.GetValueOrDefault() > 0)
                {
                    setClauses.Add("Cost = @cost");
                    if (columnSet.Contains("LastPurchasePrice"))
                    {
                        setClauses.Add("LastPurchasePrice = @cost");
                    }
                }

                if (sellingPrice.GetValueOrDefault() > 0)
                {
                    setClauses.Add("Price = @price");
                }

                if (columnSet.Contains("DateUpdated"))
                {
                    setClauses.Add("DateUpdated = @updated");
                }

                if (!setClauses.Any())
                {
                    return false;
                }

                string sql = $"UPDATE Product SET {string.Join(", ", setClauses)} WHERE Id = @id";
                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@id", productId);
                command.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
                if (cost.GetValueOrDefault() > 0)
                {
                    command.Parameters.AddWithValue("@cost", cost!.Value);
                }

                if (sellingPrice.GetValueOrDefault() > 0)
                {
                    command.Parameters.AddWithValue("@price", sellingPrice!.Value);
                }

                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error updating product pricing: {ex.Message}", "Database", ex.ToString());
                }

                return false;
            }
        }

        public async Task<RestockResult> AdjustStockAsync(string productId, decimal adjustmentQty, int userId = 1)
        {
            return await AdjustStockInternalAsync(productId, adjustmentQty, null, userId);
        }

        public async Task<RestockResult> AdjustStockWithCostAsync(
            string productId,
            decimal adjustmentQty,
            decimal unitCost,
            int userId = 1,
            bool updateMasterCost = true,
            string? internalNote = null)
        {
            decimal? costOverride = unitCost > 0 ? unitCost : null;
            return await AdjustStockInternalAsync(productId, adjustmentQty, costOverride, userId, updateMasterCost, internalNote);
        }

        private async Task<RestockResult> AdjustStockInternalAsync(
            string productId,
            decimal adjustmentQty,
            decimal? unitCostOverride,
            int userId = 1,
            bool updateMasterCost = true,
            string? internalNote = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return new RestockResult { Success = false, Error = "ProductId kosong." };
            }

            if (adjustmentQty == 0)
            {
                return new RestockResult { Success = true };
            }

            if (!int.TryParse(productId, out int productIdInt))
            {
                return new RestockResult { Success = false, Error = $"ProductId '{productId}' tidak valid." };
            }

            Product? product = await GetProductByIdAsync(productId);
            if (product == null)
            {
                return new RestockResult { Success = false, Error = $"Produk dengan ID {productId} tidak ditemukan." };
            }

            decimal currentStock = product.Stock ?? 0;
            decimal targetStock = currentStock + adjustmentQty;
            if (targetStock < 0)
            {
                return new RestockResult
                {
                    Success = false,
                    Error = $"Penyesuaian stok membuat stok negatif untuk produk {product.Name ?? productId}."
                };
            }

            return await CreateInventoryCountDocumentAsync(
                productIdInt,
                targetStock,
                userId,
                unitCostOverride,
                unitCostOverride,
                internalNote,
                updateMasterCost);
        }

        /// <summary>
        /// Mendapatkan rekomendasi restock berdasarkan pola penjualan
        /// </summary>
        public async Task<RestockRecommendation> GetRestockRecommendationAsync(string productId)
        {
            var recommendation = new RestockRecommendation();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                var tables = await GetAvailableTablesAsync(connection);
                string? productTable = FindTable(tables, new[] { "Product", "products" });
                string? stockTable = FindTable(tables, new[] { "Stock", "stock" });
                string? documentTable = FindTable(tables, new[] { "Document", "documents", "transaksi" });
                string? documentItemTable = FindTable(tables, new[] { "DocumentItem", "document_items" });

                if (string.IsNullOrEmpty(productTable) || string.IsNullOrEmpty(documentTable) || string.IsNullOrEmpty(documentItemTable))
                    return recommendation;

                // Ambil data produk
                string productSql = $@"
                    SELECT p.Name, {GetProductUnitSql("p")}, COALESCE(s.Quantity, 0), p.Cost, p.Price
                    FROM {ValidateTableName(productTable)} p
                    LEFT JOIN {ValidateTableName(stockTable)} s
                        ON p.Id = s.ProductId
                       AND s.WarehouseId = {PreferredWarehouseIdSql}
                    WHERE p.Id = @prodId";

                using var productCmd = new SqliteCommand(productSql, connection);
                productCmd.Parameters.AddWithValue("@prodId", productId);

                using var productReader = await productCmd.ExecuteReaderAsync();
                if (await productReader.ReadAsync())
                {
                    recommendation.ProductId = int.Parse(productId);
                    recommendation.ProductName = productReader.IsDBNull(0) ? "" : productReader.GetString(0);
                    recommendation.Unit = productReader.IsDBNull(1) ? "Pcs" : productReader.GetString(1);
                    recommendation.CurrentStock = SafeConvertToDecimal(productReader, 2) ?? 0;
                    recommendation.CostPrice = SafeConvertToDecimal(productReader, 3) ?? 0;
                    recommendation.SellingPrice = SafeConvertToDecimal(productReader, 4) ?? 0;
                }
                productReader.Close();

                // Hitung rata-rata penjualan 30 hari terakhir
                string salesSql = $@"
                    SELECT 
                        AVG(daily_sales) as avg_sales,
                        COUNT(*) as days_count
                    FROM (
                        SELECT date(d.Date) as sale_date, SUM(di.Quantity) as daily_sales
                        FROM {ValidateTableName(documentItemTable)} di
                        INNER JOIN {ValidateTableName(documentTable)} d ON di.DocumentId = d.Id
                        WHERE di.ProductId = @prodId
                        AND d.DocumentTypeId = 2
                        AND date(d.Date) >= date('now', '-30 days')
                        GROUP BY date(d.Date)
                    )";

                using var salesCmd = new SqliteCommand(salesSql, connection);
                salesCmd.Parameters.AddWithValue("@prodId", productId);

                using var salesReader = await salesCmd.ExecuteReaderAsync();
                if (await salesReader.ReadAsync())
                {
                    recommendation.AverageSales = SafeConvertToDecimal(salesReader, 0) ?? 0;
                    int daysCount = salesReader.IsDBNull(1) ? 0 : salesReader.GetInt32(1);
                    
                    // Hitung hari aman berdasarkan stok saat ini
                    if (recommendation.AverageSales > 0)
                    {
                        recommendation.DaysSafe = (int)(recommendation.CurrentStock / recommendation.AverageSales);
                    }
                    else
                    {
                        recommendation.DaysSafe = 999; // Tidak ada data penjualan
                    }

                    // Rekomendasi: Jika stok < 7 hari, restock ke 30 hari
                    if (recommendation.DaysSafe < 7)
                    {
                        recommendation.RecommendedQty = (int)(recommendation.AverageSales * 30);
                        recommendation.Priority = "HIGH";
                    }
                    else if (recommendation.DaysSafe < 14)
                    {
                        recommendation.RecommendedQty = (int)(recommendation.AverageSales * 21);
                        recommendation.Priority = "MEDIUM";
                    }
                    else
                    {
                        recommendation.RecommendedQty = 0;
                        recommendation.Priority = "LOW";
                    }
                }
            }
            catch (Exception ex)
            {
                if (_loggingService != null)
                {
                    await _loggingService.LogErrorAsync(
                        $"Error generating restock recommendation: {ex.Message}", "Database", ex.ToString());
                }
            }

            return recommendation;
        }

        #endregion
    }
}
