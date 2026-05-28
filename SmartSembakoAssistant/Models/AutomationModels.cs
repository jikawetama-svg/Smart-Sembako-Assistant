namespace SmartSembakoAssistant.Models
{
    public enum ChannelType
    {
        Telegram,
        WhatsApp,
        Baileys,
        System
    }

    public class ChannelIdentity
    {
        public ChannelType Channel { get; set; }
        public string SenderId { get; set; } = "";
        public string? SenderName { get; set; }
    }

    public class InboundMessage
    {
        public ChannelType Channel { get; set; }
        public string SenderId { get; set; } = "";
        public string? SenderName { get; set; }
        public string Text { get; set; } = "";
        public string? MediaUrl { get; set; }
        public string? MediaMimeType { get; set; }
        public string? FileName { get; set; }
        public string? RawSenderJid { get; set; }
        public string? ResolvedSenderJid { get; set; }
        public string? MessageId { get; set; }
        public string? CorrelationId { get; set; }
        public string? PayloadHash { get; set; }
        public string? AppInstanceId { get; set; }
        public string? SourceAppInstanceId { get; set; }
        public string? SourceMachineName { get; set; }
        public string? UpsertType { get; set; }
        public string? OriginalUpsertType { get; set; }
        public DateTime? SidecarStartedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public long? MessageTimestampMs { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class OutboundMessage
    {
        public ChannelType Channel { get; set; }
        public string RecipientId { get; set; } = "";
        public string Text { get; set; } = "";
        public string ParseMode { get; set; } = "";
        public string? MediaUrl { get; set; }
        public string? MenuKeyboardType { get; set; }
        public string MessageKind { get; set; } = "text";
        public string? TemplateName { get; set; }
        public string? TemplateLanguageCode { get; set; }
        public int TemplateBodyParameterCount { get; set; }
        public bool RequiresConfirmation { get; set; }
        public string? CorrelationId { get; set; }
        public string? AppInstanceId { get; set; }
        public string? SourceInboundMessageId { get; set; }
        public DateTime? SourceInboundReceivedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string OutboundSourceType { get; set; } = "manual_admin";
        public long QueueId { get; set; }
    }

    public class ExecutionContext
    {
        public ChannelIdentity Identity { get; set; } = new();
        public string UserRole { get; set; } = "Guest";
        public bool IsOwner { get; set; }
        public bool IsKasir { get; set; }
        public bool IsAuthorized { get; set; }
        public string TriggerType { get; set; } = "Message";
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class AutomationRuleCondition
    {
        public string? Channel { get; set; }
        public string? UserRole { get; set; }
        public bool? RequiresAuthorization { get; set; }
        public bool? RequiresCommand { get; set; }
        public string? Command { get; set; }
        public string? BusinessHoursStart { get; set; }
        public string? BusinessHoursEnd { get; set; }
        public decimal? MinimumStockLevel { get; set; }
        public decimal? MaximumStockLevel { get; set; }
    }

    public class AutomationRuleAction
    {
        public string Type { get; set; } = "";
        public string? Value { get; set; }
    }

    public class AutomationRule
    {
        public string TriggerType { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; }
        public string? Description { get; set; }
        public List<AutomationRuleCondition>? Conditions { get; set; }
        public List<AutomationRuleAction>? Actions { get; set; }
    }

    public class AutomationTemplate
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public List<AutomationRule>? DefaultRules { get; set; }
    }

    public class IntegrationStatus
    {
        public string? ActiveConfigPath { get; set; }
        public string? ConfigWarning { get; set; }
        public bool TelegramConfigured { get; set; }
        public bool TelegramValidated { get; set; }
        public bool TelegramRunning { get; set; }
        public string? TelegramLastError { get; set; }
        public DateTime? TelegramLastValidatedAt { get; set; }
        public string? TelegramActionHint { get; set; }
        public bool WhatsAppRunning { get; set; }
        public bool BaileysRunning { get; set; }
        public bool TunnelRunning { get; set; }
        public bool DatabaseConnected { get; set; }
        public bool AiConfigured { get; set; }
        public bool WhatsAppConfigured { get; set; }
        public bool WhatsAppCloudConfigured { get; set; }
        public bool WhatsAppCloudOutboundReady { get; set; }
        public bool BaileysConfigured { get; set; }
        public bool BaileysReachable { get; set; }
        public bool BaileysPaired { get; set; }
        public bool BaileysOutboundReady { get; set; }
        public bool BaileysPairingInProgress { get; set; }
        public string? BaileysConnectionState { get; set; }
        public int? BaileysLastDisconnectStatusCode { get; set; }
        public string? BaileysLastDisconnectReason { get; set; }
        public string? BaileysSidecarBuildTag { get; set; }
        public DateTime? BaileysLastValidatedAt { get; set; }
        public string? AppInstanceId { get; set; }
        public string? MachineName { get; set; }
        public DateTime? ActiveRuntimeSince { get; set; }
        public string? LastIgnoredInboundReason { get; set; }
        public string? WhatsAppActionHint { get; set; }
        public string? BaileysActionHint { get; set; }
        public bool SignatureValidationEnabled { get; set; }
        public bool ProductionReady { get; set; }
        public string? WhatsAppMode { get; set; }
        public int LocalWebhookPort { get; set; }
        public string? PosDbSchemaStatus { get; set; }
        public DateTime? PosDbLastValidatedAt { get; set; }
        public string? PosDbActionHint { get; set; }
        public int PendingOutboundCount { get; set; }
        public int PendingWhatsAppLikeOutboundCount { get; set; }
        public string? TunnelPublicUrl { get; set; }
        public string? WhatsAppWebhookUrl { get; set; }
        public string? TunnelProvider { get; set; }
        public string? LastWebhookStatus { get; set; }
        public string? LastFailureMessage { get; set; }
        public DateTime? LastWebhookReceivedAt { get; set; }
        public DateTime? LastOutboundSentAt { get; set; }
        public DateTime? LastOutboundFailureAt { get; set; }
    }

    public class PendingConfirmation
    {
        public string Key { get; set; } = "";
        public string Command { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class ProductAliasEntry
    {
        public string AliasName { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string? ProductName { get; set; }
        public string? Source { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class OcrReviewQueueItem
    {
        public long Id { get; set; }
        public string ReceiptCorrelationId { get; set; } = "";
        public string? SenderId { get; set; }
        public string? SupplierName { get; set; }
        public DateTime? ReceiptDate { get; set; }
        public string RawProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string? Unit { get; set; }
        public int? IsiPerBox { get; set; }
        public string Status { get; set; } = "pending";
        public string? CandidateSummary { get; set; }
        public string? Note { get; set; }
        public string? ResolvedProductId { get; set; }
        public string? ResolvedProductName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ResolvedAt { get; set; }
    }

    public enum ShadowConversionStatus
    {
        Applied,
        Ambiguous,
        NoMapping,
        NoChildFound,
        AutoMapped,
        Failed
    }

    public class ShadowConversionResult
    {
        public string ParentProductId { get; set; } = string.Empty;
        public string ParentProductName { get; set; } = string.Empty;
        public decimal ParentQuantity { get; set; }
        public string? ChildProductId { get; set; }
        public string? ChildProductName { get; set; }
        public decimal? ChildQuantity { get; set; }
        public decimal? RateUsed { get; set; }
        public decimal? ChildUnitCost { get; set; }
        public decimal? ChildTotalCost { get; set; }
        public string RateSource { get; set; } = "none";
        public ShadowConversionStatus Status { get; set; }
        public string? Message { get; set; }
    }

    public class UnitConversionMapping
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ParentProductId { get; set; } = "";
        public string? ParentProductName { get; set; }
        public string ChildProductId { get; set; } = "";
        public string? ChildProductName { get; set; }
        public decimal ConversionRate { get; set; }
        public string? FamilyName { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public enum StockUnitIntent
    {
        General,
        ParentOnly,
        ChildOnly,
        Total
    }

    public class ProductFamilyStock
    {
        public UnitConversionMapping Mapping { get; set; } = new();
        public Product ParentProduct { get; set; } = new();
        public Product ChildProduct { get; set; } = new();
        public decimal ParentStock { get; set; }
        public decimal ChildStock { get; set; }
        public decimal ConversionRate { get; set; }
        public decimal TotalChildStock => ParentStock * ConversionRate + ChildStock;
        public decimal TotalParentStock => ConversionRate > 0 ? TotalChildStock / ConversionRate : 0;
        public string FamilyName => !string.IsNullOrWhiteSpace(Mapping.FamilyName)
            ? Mapping.FamilyName!
            : ParentProduct.Name ?? Mapping.ParentProductName ?? "Produk Dual Stok";
    }

    public class StockMutationDocument
    {
        public long DocumentId { get; set; }
        public int DocumentTypeId { get; set; }
        public DateTime Date { get; set; }
        public string? InternalNote { get; set; }
        public string ProductId { get; set; } = "";
        public string? ProductName { get; set; }
        public string? Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal ExpectedQuantity { get; set; }
    }

    public class InboundEventRecord
    {
        public long Id { get; set; }
        public string Channel { get; set; } = "";
        public string SenderId { get; set; } = "";
        public string MessageKey { get; set; } = "";
        public string? MessageId { get; set; }
        public string CorrelationId { get; set; } = "";
        public string? PayloadHash { get; set; }
        public string? AppInstanceId { get; set; }
        public string? Text { get; set; }
        public string Status { get; set; } = "received";
        public string? LastError { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
        public DateTime? ProcessedAt { get; set; }
    }

    public class OutboundMessageRecord
    {
        public long Id { get; set; }
        public string CorrelationId { get; set; } = "";
        public ChannelType Channel { get; set; }
        public string RecipientId { get; set; } = "";
        public string Text { get; set; } = "";
        public string ParseMode { get; set; } = "";
        public string? MediaUrl { get; set; }
        public string? MenuKeyboardType { get; set; }
        public string MessageKind { get; set; } = "text";
        public string? TemplateName { get; set; }
        public string? TemplateLanguageCode { get; set; }
        public int TemplateBodyParameterCount { get; set; }
        public bool RequiresConfirmation { get; set; }
        public string? AppInstanceId { get; set; }
        public string Status { get; set; } = "queued";
        public int AttemptCount { get; set; }
        public DateTime NextAttemptAt { get; set; } = DateTime.Now;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string? ExternalMessageId { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastStatusEventAt { get; set; }
        public string? SourceInboundMessageId { get; set; }
        public DateTime? SourceInboundReceivedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string OutboundSourceType { get; set; } = "manual_admin";
    }

    public class OutboxCleanupResult
    {
        public int TotalCancelled { get; set; }
        public int WhatsAppCancelled { get; set; }
        public int BaileysCancelled { get; set; }
    }

    public class MessageStatusEventRecord
    {
        public long Id { get; set; }
        public string Channel { get; set; } = "";
        public string? CorrelationId { get; set; }
        public string? ExternalMessageId { get; set; }
        public string? RecipientId { get; set; }
        public string Status { get; set; } = "";
        public string? ErrorDetails { get; set; }
        public string? RawPayload { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.Now;
    }

    public class AutomationExecutionRecord
    {
        public long Id { get; set; }
        public string CorrelationId { get; set; } = "";
        public string TriggerType { get; set; } = "";
        public string Channel { get; set; } = "";
        public string SenderId { get; set; } = "";
        public string UserRole { get; set; } = "Guest";
        public string Status { get; set; } = "received";
        public string? MatchedRules { get; set; }
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class OcrSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SenderId { get; set; } = "";
        public string Channel { get; set; } = "";
        public string? SupplierName { get; set; }
        public string? ReceiptNumber { get; set; }
        public string? ReceiptDate { get; set; }
        public string ItemsJson { get; set; } = "[]";
        public int PageCount { get; set; }
        public bool IsComplete { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(30);
    }

    public class PendingInputState
    {
        public string Action { get; set; } = "";
        public string? Context { get; set; }
        public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(5);

        public static TimeSpan GetTimeout(string action) => action switch
        {
            "ocr_foto" or "input_struk" or "set_family" => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(5)
        };
    }
}
