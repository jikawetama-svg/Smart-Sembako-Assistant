using System.IO;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class LoggingService
    {
        private readonly DatabaseService _databaseService;

        public LoggingService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task LogInfoAsync(string message, string category = "System", string? details = null, string? userId = null)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Info",
                Category = category,
                Message = message,
                Details = details,
                UserId = userId
            };

            await _databaseService.AddLogAsync(logEntry);
        }

        public async Task LogWarningAsync(string message, string category = "System", string? details = null, string? userId = null)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Warning",
                Category = category,
                Message = message,
                Details = details,
                UserId = userId
            };

            await _databaseService.AddLogAsync(logEntry);
        }

        public async Task LogErrorAsync(string message, string category = "System", string? details = null, string? userId = null)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Error",
                Category = category,
                Message = message,
                Details = details,
                UserId = userId
            };

            await _databaseService.AddLogAsync(logEntry);
        }

        public async Task LogCriticalAsync(string message, string category = "System", string? details = null, string? userId = null)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Critical",
                Category = category,
                Message = message,
                Details = details,
                UserId = userId
            };

            await _databaseService.AddLogAsync(logEntry);
        }

        public async Task<List<LogEntry>> GetLogsAsync(
            string? category = null,
            string? level = null,
            int limit = 100,
            long? beforeId = null,
            CancellationToken cancellationToken = default)
        {
            return await _databaseService.GetLogsAsync(category, level, limit, beforeId, cancellationToken);
        }

        public async Task ExportLogsToCsvAsync(
            string filePath,
            string? category = null,
            string? level = null,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            const int batchSize = 1000;
            long? beforeId = null;
            int exported = 0;

            await using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            await writer.WriteLineAsync("ID,Timestamp,Level,Category,Message,Details,User ID");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var logs = await _databaseService.GetLogsAsync(
                    category,
                    level,
                    batchSize,
                    beforeId,
                    cancellationToken);

                if (logs.Count == 0)
                {
                    break;
                }

                foreach (var log in logs)
                {
                    string line = string.Join(",",
                        log.Id,
                        EscapeCsvField(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                        EscapeCsvField(log.Level),
                        EscapeCsvField(log.Category),
                        EscapeCsvField(log.Message),
                        EscapeCsvField(log.Details),
                        EscapeCsvField(log.UserId));
                    await writer.WriteLineAsync(line);
                }

                exported += logs.Count;
                beforeId = logs.Last().Id;
                progress?.Report(exported);

                if (logs.Count < batchSize)
                {
                    break;
                }
            }
        }

        private string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            // Escape double quotes
            string escaped = field.Replace("\"", "\"\"");

            // Wrap dalam quotes jika mengandung koma, newline, atau double quotes
            if (escaped.Contains(",") || escaped.Contains("\n") || escaped.Contains("\"") || escaped.Contains("\r"))
                return $"\"{escaped}\"";

            return escaped;
        }

        /// <summary>
        /// Clear logs older than specified days
        /// </summary>
        public async Task<int> ClearOldLogsAsync(int daysOld)
        {
            var result = await ClearOldLogsChunkedAsync(daysOld);
            return result.DeletedCount;
        }

        public async Task<LogDeleteResult> ClearOldLogsChunkedAsync(
            int daysOld,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var cutoffDate = DateTime.Now.AddDays(-daysOld);
            return await _databaseService.DeleteLogsBeforeChunkedAsync(cutoffDate, progress, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Clear ALL logs
        /// </summary>
        public async Task<int> ClearAllLogsAsync()
        {
            var result = await ClearAllLogsChunkedAsync();
            return result.DeletedCount;
        }

        public async Task<LogDeleteResult> ClearAllLogsChunkedAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await _databaseService.DeleteAllLogsChunkedAsync(progress, cancellationToken: cancellationToken);
        }
    }
}
