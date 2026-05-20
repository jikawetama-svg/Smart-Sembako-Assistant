namespace SmartSembakoAssistant.Models
{
    public sealed class IntegrationDiagnostic
    {
        public string Area { get; set; } = "";
        public string Status { get; set; } = "";
        public string UserMessage { get; set; } = "";
        public string? TechnicalDetail { get; set; }
        public bool CanAutoFix { get; set; }
        public string? FixAction { get; set; }
    }
}
