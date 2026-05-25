namespace SmartSembakoAssistant.Models
{
    public enum ExportFormat
    {
        Csv,
        Excel,
        Pdf
    }

    public class ExportResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public ExportFormat Format { get; set; }

        public static ExportResult Ok(string filePath, int rowCount, ExportFormat format, string message)
        {
            return new ExportResult
            {
                Success = true,
                FilePath = filePath,
                RowCount = rowCount,
                Format = format,
                Message = message
            };
        }

        public static ExportResult Fail(ExportFormat format, string message)
        {
            return new ExportResult
            {
                Success = false,
                Format = format,
                Message = message
            };
        }
    }
}
