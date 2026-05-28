using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartSembakoAssistant.Helpers;
using SmartSembakoAssistant.Models;
using SkiaSharp;
using Tesseract;
using AutomationExecutionContext = SmartSembakoAssistant.Models.ExecutionContext;

namespace SmartSembakoAssistant.Services
{
    public class AutomationEngine
    {
        private sealed class BulkPendingItem
        {
            public string ProductId { get; set; } = "";
            public string ProductName { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal? Price { get; set; }
            public decimal? CurrentStock { get; set; }
            public string? Unit { get; set; }
            public int? IsiPerBox { get; set; }
            public List<string> RawProductNames { get; set; } = new();
        }

        private sealed class OcrBulkPendingPayload
        {
            public string? StoreName { get; set; }
            public string? SupplierName { get; set; }
            public string? BuyerName { get; set; }
            public DateTime? ReceiptDate { get; set; }
            public string? ReceiptNumber { get; set; }
            public decimal? ReceiptTotal { get; set; }
            public List<BulkPendingItem> Items { get; set; } = new();
            public List<OcrReviewQueueItem> ReviewItems { get; set; } = new();
        }

        private sealed class ReceiptMappingOutcome
        {
            public List<BulkPendingItem> ValidItems { get; set; } = new();
            public List<OcrReviewQueueItem> ReviewItems { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
        }

        private sealed class PriceOverridePendingPayload
        {
            public string OriginalCommand { get; set; } = "";
            public string OriginalProductId { get; set; } = "";
            public string OriginalProductName { get; set; } = "";
            public decimal OriginalQuantity { get; set; }
            public decimal? OriginalPrice { get; set; }
            public string? OriginalCorrelationId { get; set; }
            public bool ManualPriceDiscardWarningShown { get; set; }
            public List<PriceChangeItem> Changes { get; set; } = new();
        }

        private sealed class PriceChangeItem
        {
            public string ProductId { get; set; } = "";
            public string ProductName { get; set; } = "";
            public string Source { get; set; } = "purchase";
            public bool IsShadowChild { get; set; }
            public string? ParentProductId { get; set; }
            public string? ParentProductName { get; set; }
            public decimal? ConversionRate { get; set; }
            public decimal OldCost { get; set; }
            public decimal NewCost { get; set; }
            public decimal OldSellingPrice { get; set; }
            public decimal SuggestedSellingPrice { get; set; }
            public decimal? ManualSellingPrice { get; set; }
            public decimal DeltaAmount => NewCost - OldCost;
            public decimal DeltaPercent => OldCost > 0 ? (DeltaAmount / OldCost) * 100 : 100;
            public decimal EffectiveSellingPrice => ManualSellingPrice.GetValueOrDefault() > 0 ? ManualSellingPrice!.Value : SuggestedSellingPrice;
        }

        private sealed class PriceOverrideDecision
        {
            public bool UpdateCost { get; set; }
            public bool UpdateSellingPrice { get; set; }
        }

        private sealed class ConfirmProcessingResult
        {
            public DateTime CompletedAt { get; set; } = DateTime.Now;
            public string? DocumentNumber { get; set; }
        }

        private sealed class OcrExtractionResult
        {
            public string Text { get; set; } = "";
            public float Confidence { get; set; }
            public bool UsedPreprocessedImage { get; set; }
        }

        private sealed class OcrTextCandidate
        {
            public string Text { get; set; } = "";
            public float Confidence { get; set; }
            public bool IsPreprocessed { get; set; }
            public double Score { get; set; }
        }

        private sealed class ChildProductDiscovery
        {
            public bool IsAmbiguous { get; set; }
            public Product? Product { get; set; }
            public List<Product> Candidates { get; set; } = new();
        }

        private sealed class DeterministicIntent
        {
            public string Kind { get; set; } = "";
            public string? Argument { get; set; }
            public bool OwnerOnly { get; set; }
        }

        private sealed class PendingExportRequest
        {
            public string Kind { get; set; } = "";
            public string? Argument { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
        }

        private sealed class ListPageState
        {
            public int NextOffset { get; set; }
            public int PageSize { get; set; }
        }

        private sealed class ProductListPageState
        {
            public string Mode { get; set; } = "all";
            public string? Query { get; set; }
            public int NextOffset { get; set; }
            public int PageSize { get; set; } = 10;
        }

        private sealed class DocumentPageState
        {
            public string DocumentId { get; set; } = "";
            public string DocumentNumber { get; set; } = "";
            public string? CustomerId { get; set; }
            public string? CustomerName { get; set; }
            public int NextOffset { get; set; }
            public int PageSize { get; set; } = 10;
        }

        private sealed class CustomerDocumentPageState
        {
            public string CustomerId { get; set; } = "";
            public string CustomerName { get; set; } = "";
            public int NextOffset { get; set; }
            public int PageSize { get; set; } = 5;
        }

        private sealed class CustomerTransactionPageState
        {
            public string CustomerId { get; set; } = "";
            public string CustomerName { get; set; } = "";
            public int NextOffset { get; set; }
            public int PageSize { get; set; } = 5;
        }

        private enum TopicType
        {
            None,
            LoyalCustomers,
            AtRiskCustomers,
            CustomerDetail,
            ReceivableList,
            ReceivableDetail,
            SalesDocumentDetail,
            DocumentPickPending,
            ProductDetail,
            SetFamilyPending,
            ExpiredContext
        }

        private sealed class TopicState
        {
            public string Topic { get; set; } = "";
            public TopicType TopicType { get; set; } = TopicType.None;
            public string? EntityId { get; set; }
            public string? EntityName { get; set; }
            public int? CurrentPage { get; set; }
            public int PageSize { get; set; } = 5;
            public string? ExportType { get; set; }
            public string? LastDocumentNumber { get; set; }
            public string? CustomerId { get; set; }
            public string? CustomerName { get; set; }
            public List<string> RelatedDocumentNumbers { get; set; } = new();
            public List<string> CandidateDocuments { get; set; } = new();
            public object? LastData { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int? DaysLeft { get; set; }
            public decimal? Stock { get; set; }
            public string? Unit { get; set; }
            public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(10);
        }

        private sealed class ShadowMappingIntent
        {
            public string ParentQuery { get; set; } = "";
            public string ChildQuery { get; set; } = "";
            public string ProductKeyword { get; set; } = "";
            public string? ParentUnit { get; set; }
            public string? ChildUnit { get; set; }
            public decimal Rate { get; set; }
            public string OriginalMessage { get; set; } = "";
            public bool FromNaturalLanguage { get; set; }
        }

        private sealed class ShadowMappingCandidate
        {
            public Product Product { get; set; } = new();
            public int Confidence { get; set; }
        }

        private sealed class ShadowMappingPendingState
        {
            public List<ShadowMappingCandidate> ParentCandidates { get; set; } = new();
            public List<ShadowMappingCandidate> ChildCandidates { get; set; } = new();
            public decimal Rate { get; set; }
            public string? ParentUnit { get; set; }
            public string? ChildUnit { get; set; }
            public string OriginalMessage { get; set; } = "";
            public int? SelectedParentIndex { get; set; }
            public int? SelectedChildIndex { get; set; }
            public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(10);
        }

        private static readonly HashSet<string> SearchStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "cek", "stok", "berapa", "ada", "dong", "tolong", "mohon", "produk", "barang",
            "restock", "inventory", "quick", "quick_inventory", "minta", "lihat", "cari",
            "bantu", "gimana", "bagaimana", "yang", "untuk", "dan", "atau", "di", "ke",
            "nih", "lagi", "kak", "pak", "bu", "sih"
        };

        private static readonly Dictionary<string, int> IndonesianMonths = new(StringComparer.OrdinalIgnoreCase)
        {
            ["januari"] = 1,
            ["jan"] = 1,
            ["februari"] = 2,
            ["feb"] = 2,
            ["maret"] = 3,
            ["mar"] = 3,
            ["april"] = 4,
            ["apr"] = 4,
            ["mei"] = 5,
            ["juni"] = 6,
            ["jun"] = 6,
            ["juli"] = 7,
            ["jul"] = 7,
            ["agustus"] = 8,
            ["agu"] = 8,
            ["ags"] = 8,
            ["september"] = 9,
            ["sep"] = 9,
            ["oktober"] = 10,
            ["okt"] = 10,
            ["oct"] = 10,
            ["november"] = 11,
            ["nov"] = 11,
            ["desember"] = 12,
            ["des"] = 12
        };
        private static readonly CultureInfo IndonesianCulture = new("id-ID");

        private const string StateLastDailySummaryDate = "automation.last_daily_summary_date";
        private const string StateLastLowStockAlertDate = "automation.last_low_stock_alert_date";
        private const string StateLegacyLastLowStockAlertAt = "automation.last_low_stock_alert_at";
        private const string StateLastWebhookReceivedAt = "integration.last_webhook_received_at";
        private const string StateLastWebhookStatus = "integration.last_webhook_status";
        private const string StateLastOutboundSentAt = "integration.last_outbound_sent_at";
        private const string StateLastOutboundFailureAt = "integration.last_outbound_failure_at";
        private const string StateLastOutboundFailureMessage = "integration.last_outbound_failure_message";
        private const string StateLastIgnoredInboundReason = "integration.last_ignored_inbound_reason";
        private const string StateLastReceivableAlertDate = "automation.last_receivable_alert_date";
        private const string StateLastExpiryAlertDate = "automation.last_expiry_alert_date";
        private const string StateLastAnomalyAlertDate = "automation.last_anomaly_alert_date";
        private const string StateLastDualStockWatcherDocumentId = "last_processed_pos_document_id";
        private const string StateLegacyLastDualStockWatcherDocumentId = "automation.dual_stock_watcher.last_document_id";
        private const string StateLastDualStockDailySyncDate = "automation.dual_stock.last_daily_sync_date";
        private const string DualStockInternalNotePrefix = "SSA DualStock";
        private const decimal InventoryLargeAdjustmentThreshold = 50m;
        private const decimal InventorySpikeMultiplier = 3m;
        private const float OcrVisionFallbackConfidenceThreshold = 0.62f;
        private const string DefaultStockUnit = "Pcs";
        private const string IconStore = "\U0001F3EA";
        private const string IconPackage = "\U0001F4E6";
        private const string IconInventory = "\U0001F504";
        private const string IconChart = "\U0001F4CA";
        private const string IconMoney = "\U0001F4B0";
        private const string IconProfit = "\U0001F4C8";
        private const string IconReceipt = "\U0001F9FE";
        private const string IconClipboard = "\U0001F4CB";
        private const string IconRobot = "\U0001F916";
        private const string IconSearch = "\U0001F50D";
        private const string IconWarning = "\u26A0\uFE0F";
        private const string IconSiren = "\U0001F6A8";
        private const string IconDocument = "\U0001F4C4";
        private const string IconCustomer = "\U0001F465";
        private const string IconUser = "\U0001F464";
        private const string IconPhone = "\U0001F4F1";
        private const string IconEmail = "\U0001F4E7";
        private const string IconCalendar = "\U0001F5D3\uFE0F";
        private const string IconTag = "\U0001F3F7\uFE0F";
        private const string IconBoxArchive = "\U0001F5C3\uFE0F";
        private const string IconEye = "\U0001F440";
        private const string IconCheck = "\u2705";
        private const string IconCross = "\u274C";
        private const string IconGreen = "\U0001F7E2";
        private const string IconYellow = "\U0001F7E1";
        private const string IconRed = "\U0001F534";
        private const string IconUp = "\u2B06\uFE0F";
        private const string IconDown = "\u2B07\uFE0F";
        private const string IconRight = "\u27A1\uFE0F";
        private static readonly Regex DocumentNumberRegex = new(@"\b\d{2}-\d{3}-\d{6}\b", RegexOptions.Compiled);
        private static readonly string[] OcrFooterKeywords =
        {
            "kasbon",
            "subtotal",
            "sub total",
            "total",
            "bayar",
            "kembalian",
            "diskon",
            "ppn",
            "dpp",
            "terbilang",
            "keterangan",
            "catatan",
            "jumlah",
            "tunai",
            "saldo",
            "end of document",
            "harga sudah termasuk ppn",
            "total stlm pot",
            "total yang dibayar",
            "lanjutan dari halaman"
        };

        private readonly ConfigService _configService;
        private readonly GroqService _groqService;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly PosDbService? _posDbService;
        private string CurrentAppInstanceId => _configService.Config?.App?.InstanceId ?? "unknown";
        private readonly ConcurrentDictionary<string, PendingExportRequest> _pendingExportBySender = new();
        private readonly ConcurrentDictionary<string, ListPageState> _customerPaginationBySender = new();
        private readonly ConcurrentDictionary<string, ListPageState> _supplierPaginationBySender = new();
        private readonly ConcurrentDictionary<string, ProductListPageState> _productPaginationBySender = new();
        private readonly ConcurrentDictionary<string, DocumentPageState> _documentPaginationBySender = new();
        private readonly ConcurrentDictionary<string, CustomerDocumentPageState> _customerDocumentPaginationBySender = new();
        private readonly ConcurrentDictionary<string, CustomerTransactionPageState> _customerTxPaginationBySender = new();
        private readonly ConcurrentDictionary<string, string> _lastDocumentBySender = new();
        private readonly ConcurrentDictionary<string, TopicState> _lastTopicBySender = new();
        private readonly ConcurrentDictionary<string, PendingInputState> _pendingInputBySender = new();
        private readonly ConcurrentDictionary<string, ShadowMappingPendingState> _shadowMappingPendingBySender = new();
        private readonly ConcurrentDictionary<string, byte> _priceOverrideProcessingByKey = new();
        private readonly ConcurrentDictionary<string, ConfirmProcessingResult> _priceOverrideCompletedByKey = new();
        private Func<ChannelType, string, string, string, Task<string?>>? _documentSender;

        private DateTime? _lastDailySummaryDate;
        private DateTime? _lastLowStockAlertDate;
        private DateTime? _lastReceivableAlertDate;
        private DateTime? _lastExpiryAlertDate;
        private DateTime? _lastAnomalyAlertDate;
        private DateTime? _lastWebhookReceivedAt;
        private DateTime? _lastOutboundSentAt;
        private DateTime? _lastOutboundFailureAt;
        private string? _lastWebhookStatus;
        private string? _lastFailureMessage;

        public AutomationEngine(
            ConfigService configService,
            GroqService groqService,
            DatabaseService databaseService,
            LoggingService loggingService,
            PosDbService? posDbService = null)
        {
            _configService = configService;
            _groqService = groqService;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _posDbService = posDbService;

            LoadRuntimeState();
            SeedAutomationDefaults();
        }

        public void SetPendingInput(ChannelType channel, string senderId, string action, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            _pendingInputBySender[BuildSenderStateKey(channel, senderId)] = new PendingInputState
            {
                Action = action.Trim(),
                Context = context,
                ExpiresAt = DateTime.Now.Add(PendingInputState.GetTimeout(action.Trim()))
            };
        }

        public bool CancelPendingInput(ChannelType channel, string senderId)
        {
            if (string.IsNullOrWhiteSpace(senderId))
            {
                return false;
            }

            return _pendingInputBySender.TryRemove(BuildSenderStateKey(channel, senderId), out _);
        }

        public async Task<OutboundMessage?> ProcessInboundMessageAsync(InboundMessage message)
        {
            message.Timestamp = message.Timestamp == default ? DateTime.Now : message.Timestamp;
            message.AppInstanceId ??= CurrentAppInstanceId;
            message.CorrelationId = string.IsNullOrWhiteSpace(message.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : message.CorrelationId;
            message.PayloadHash ??= ComputePayloadHash(message);

            var context = BuildExecutionContext(message);
            var matchedRules = GetMatchingRules(context.TriggerType, context, message, null).ToList();
            string matchedRuleSummary = string.Join(", ", matchedRules.Select(GetRuleName));

            await _databaseService.AddAutomationExecutionAsync(new AutomationExecutionRecord
            {
                CorrelationId = context.CorrelationId,
                TriggerType = context.TriggerType,
                Channel = context.Identity.Channel.ToString(),
                SenderId = context.Identity.SenderId,
                UserRole = context.UserRole,
                Status = "received",
                MatchedRules = string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                Details = "Inbound message received."
            });

            bool isNewEvent = await _databaseService.TryRegisterInboundEventAsync(message, context.CorrelationId);
            if (!isNewEvent)
            {
                await _databaseService.UpdateAutomationExecutionAsync(
                    context.CorrelationId,
                    "duplicate",
                    string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                    "Inbound duplicate ignored.");
                await _loggingService.LogInfoAsync(
                    $"Inbound duplicate diabaikan untuk {message.Channel} dari {message.SenderId}.",
                    "Automation",
                    userId: message.SenderId);
                await RecordIgnoredInboundReasonAsync("duplicate", message);
                return null;
            }

            if (IsNonLiveBaileysUpsert(message))
            {
                await _databaseService.MarkInboundEventProcessedAsync(message, "non_live_upsert_ignored");
                await _databaseService.UpdateAutomationExecutionAsync(
                    context.CorrelationId,
                    "ignored",
                    string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                    $"Non-live Baileys upsert ignored: {message.UpsertType}.");
                await _loggingService.LogInfoAsync(
                    $"Inbound Baileys non-live diabaikan: upsert={message.UpsertType}; sender={message.SenderId}.",
                    "Automation",
                    userId: message.SenderId);
                await RecordIgnoredInboundReasonAsync("non_live_upsert_ignored", message);
                return null;
            }

            if (IsStaleBaileysHistory(message))
            {
                await _databaseService.MarkInboundEventProcessedAsync(message, "history_ignored");
                await _databaseService.UpdateAutomationExecutionAsync(
                    context.CorrelationId,
                    "ignored",
                    string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                    "Stale Baileys history message ignored by desktop guard.");
                await _loggingService.LogInfoAsync(
                    $"Inbound Baileys history lama diabaikan: timestamp={message.Timestamp:O}; sidecar_started={message.SidecarStartedAt:O}; sender={message.SenderId}.",
                    "Automation",
                    userId: message.SenderId);
                await RecordIgnoredInboundReasonAsync("history_ignored", message);
                return null;
            }

            await RecordInboundRuntimeAsync(message, "message_received");

            if (IsWhatsAppLikeChannel(message.Channel) && _configService.Config?.App?.IsActiveBotRuntime == false)
            {
                await _databaseService.MarkInboundEventProcessedAsync(message, "inactive_runtime_ignored");
                await _databaseService.UpdateAutomationExecutionAsync(
                    context.CorrelationId,
                    "ignored",
                    string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                    $"Inactive bot runtime ignored inbound for instance {CurrentAppInstanceId}.");
                await _loggingService.LogWarningAsync(
                    $"Inbound {message.Channel} diabaikan karena runtime bot instance ini tidak aktif: {CurrentAppInstanceId}.",
                    "Automation",
                    userId: message.SenderId);
                await RecordIgnoredInboundReasonAsync("inactive_runtime_ignored", message);
                return null;
            }

            try
            {
                if (!context.IsAuthorized)
                {
                    if (ShouldSuppressUnauthorizedReply(message))
                    {
                        await _databaseService.MarkInboundEventProcessedAsync(message, "unauthorized_ignored");
                        await _databaseService.UpdateAutomationExecutionAsync(
                            context.CorrelationId,
                            "ignored",
                            string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                            "Unauthorized WhatsApp/Baileys message ignored without outbound reply.");
                        await _loggingService.LogWarningAsync(
                            $"Pesan {message.Channel} dari nomor tidak terdaftar diabaikan tanpa balasan: {NormalizeWhatsAppNumber(message.SenderId)}.",
                            "Automation",
                            userId: message.SenderId);
                        await RecordIgnoredInboundReasonAsync("unauthorized_ignored", message);
                        return null;
                    }

                    var denied = CreateOutboundMessage(
                        message,
                        "Akses ditolak. Nomor/chat Anda belum diizinkan di konfigurasi aplikasi.",
                        context.CorrelationId);
                    await EnqueueOutboundMessageAsync(denied);
                    await _databaseService.MarkInboundEventProcessedAsync(message, "unauthorized");
                    await _databaseService.UpdateAutomationExecutionAsync(
                        context.CorrelationId,
                        "queued",
                        string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                        "Unauthorized request queued with denial response.");
                    return denied;
                }

                if (string.IsNullOrWhiteSpace(message.Text) && string.IsNullOrWhiteSpace(message.MediaUrl))
                {
                    await _databaseService.MarkInboundEventProcessedAsync(message, "empty_ignored");
                    await _databaseService.UpdateAutomationExecutionAsync(
                        context.CorrelationId,
                        "ignored",
                        string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                        "Empty inbound message ignored.");
                    await RecordIgnoredInboundReasonAsync("empty_ignored", message);
                    return null;
                }

                await SaveConversationAsync(message, "user", message.Text);

                string response = await ExecuteInboundFlowAsync(message, context, matchedRules);

                await SaveConversationAsync(message, "assistant", response);

                var outbound = CreateOutboundMessage(message, response, context.CorrelationId);
                await EnqueueOutboundMessageAsync(outbound);
                await _databaseService.MarkInboundEventProcessedAsync(message, "queued");
                await _databaseService.UpdateAutomationExecutionAsync(
                    context.CorrelationId,
                    "queued",
                    string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                    $"Outbound queued for {message.Channel}.");
                return outbound;
            }
            catch (Exception ex)
            {
                await _databaseService.MarkInboundEventProcessedAsync(message, "failed", ex.Message);
                await _databaseService.UpdateAutomationExecutionAsync(
                    context.CorrelationId,
                    "failed",
                    string.IsNullOrWhiteSpace(matchedRuleSummary) ? null : matchedRuleSummary,
                    ex.Message);
                await _loggingService.LogErrorAsync(
                    $"Automation inbound gagal: {ex.Message}",
                    "Automation",
                    ex.ToString(),
                    message.SenderId);

                var fallback = CreateOutboundMessage(
                    message,
                    "Terjadi gangguan saat memproses pesan. Coba lagi sebentar lagi atau gunakan command seperti /stok.",
                    context.CorrelationId);
                await EnqueueOutboundMessageAsync(fallback);
                return fallback;
            }
        }

        private static bool ShouldSuppressUnauthorizedReply(InboundMessage message)
        {
            return IsWhatsAppLikeChannel(message.Channel);
        }

        private static bool IsWhatsAppLikeChannel(ChannelType channel)
        {
            return channel == ChannelType.WhatsApp || channel == ChannelType.Baileys;
        }

        private static bool IsNonLiveBaileysUpsert(InboundMessage message)
        {
            return message.Channel == ChannelType.Baileys &&
                   !string.IsNullOrWhiteSpace(message.UpsertType) &&
                   !string.Equals(message.UpsertType, "notify", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsStaleBaileysHistory(InboundMessage message)
        {
            if (message.Channel != ChannelType.Baileys)
            {
                return false;
            }

            DateTime messageTimestampUtc = ToUtcComparable(message.Timestamp);

            if (message.SidecarStartedAt.HasValue)
            {
                DateTime sidecarStartedAtUtc = ToUtcComparable(message.SidecarStartedAt.Value);
                if (messageTimestampUtc < sidecarStartedAtUtc.AddSeconds(-120))
                {
                    return true;
                }

                if (DateTime.UtcNow < sidecarStartedAtUtc.AddSeconds(30) &&
                    messageTimestampUtc <= sidecarStartedAtUtc)
                {
                    return true;
                }
            }

            DateTime? activeRuntimeSince = _configService.Config?.App?.ActiveRuntimeSince;
            if (activeRuntimeSince.HasValue &&
                messageTimestampUtc < ToUtcComparable(activeRuntimeSince.Value).AddSeconds(-120))
            {
                return true;
            }

            return false;
        }

        private static DateTime ToUtcComparable(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };
        }

        private async Task RecordIgnoredInboundReasonAsync(string reason, InboundMessage message)
        {
            string detail = $"{reason}; channel={message.Channel}; sender={message.SenderId}; message_id={message.MessageId ?? "-"}; at={DateTime.Now:O}";
            await _databaseService.SetRuntimeStateAsync(StateLastIgnoredInboundReason, detail);
        }

        public async Task<List<OutboundMessage>> RunBackgroundAutomationAsync()
        {
            var outputs = new List<OutboundMessage>();
            var automation = _configService.Config?.Automation;
            if (_posDbService == null || automation == null)
            {
                return outputs;
            }

            if (automation.EnableLowStockAlerts &&
                TimeSpan.TryParse(automation.LowStockAlertTime ?? "07:00", out var lowStockAlertTime) &&
                DateTime.Now.TimeOfDay >= lowStockAlertTime &&
                _lastLowStockAlertDate != DateTime.Today)
            {
                var critical = await _posDbService.GetCriticalStockProductsAsync();
                var dualStockDeficits = await GetDualStockDeficitFamiliesAsync();
                bool shouldSendCritical = critical.Any() &&
                    ShouldExecuteBackgroundTrigger("StockAlert", critical.Min(x => x.Stock ?? 0), out _);
                if (shouldSendCritical || dualStockDeficits.Any())
                {
                    outputs.AddRange(BuildLowStockAlertBroadcasts(
                        BuildCriticalStockResponse(critical, dualStockDeficits),
                        "StockAlert"));
                }

                _lastLowStockAlertDate = DateTime.Today;
                await _databaseService.SetRuntimeStateAsync(StateLastLowStockAlertDate, DateTime.Today.ToString("yyyy-MM-dd"));
            }

            if (automation.EnableDailySummary &&
                TimeSpan.TryParse(automation.DailySummaryTime, out var summaryTime) &&
                DateTime.Now.TimeOfDay >= summaryTime &&
                _lastDailySummaryDate != DateTime.Today &&
                ShouldExecuteBackgroundTrigger("Schedule", null, out _))
            {
                outputs.AddRange(BuildOwnerBroadcasts(await BuildDailySummaryAsync(), "Schedule"));
                if (_configService.Config?.GoogleSheets?.Enabled == true)
                {
                    try
                    {
                        var sheetsService = new GoogleSheetsService(_configService, _loggingService);
                        var syncService = new GoogleSheetsSyncService(_configService, _loggingService, sheetsService, _posDbService);
                        var syncResult = await syncService.SyncDailySnapshotAsync(DateTime.Today);
                        if (!syncResult.Success)
                        {
                            await _loggingService.LogWarningAsync($"Sheets sync gagal: {syncResult.Message}", "GoogleSheets");
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogErrorAsync($"Sheets sync gagal: {ex.Message}", "GoogleSheets", ex.ToString());
                    }
                }

                _lastDailySummaryDate = DateTime.Today;
                await _databaseService.SetRuntimeStateAsync(StateLastDailySummaryDate, DateTime.Today.ToString("yyyy-MM-dd"));
            }

            if (automation.EnableReceivableAlerts &&
                TimeSpan.TryParse(automation.ReceivableAlertTime ?? "08:00", out var receivableAlertTime) &&
                DateTime.Now.TimeOfDay >= receivableAlertTime &&
                _lastReceivableAlertDate != DateTime.Today)
            {
                string? receivableAlert = await BuildReceivableAlertAsync();
                if (!string.IsNullOrWhiteSpace(receivableAlert))
                {
                    outputs.AddRange(BuildOwnerBroadcasts(receivableAlert, "ReceivableAlert"));
                }

                _lastReceivableAlertDate = DateTime.Today;
                await _databaseService.SetRuntimeStateAsync(StateLastReceivableAlertDate, DateTime.Today.ToString("yyyy-MM-dd"));
            }

            if (automation.EnableExpiryAlerts &&
                TimeSpan.TryParse(automation.ExpiryAlertTime ?? "08:30", out var expiryAlertTime) &&
                DateTime.Now.TimeOfDay >= expiryAlertTime &&
                _lastExpiryAlertDate != DateTime.Today)
            {
                string? expiryAlert = await BuildExpiryAlertAsync();
                if (!string.IsNullOrWhiteSpace(expiryAlert))
                {
                    outputs.AddRange(BuildOwnerBroadcasts(expiryAlert, "ExpiryAlert"));
                }

                _lastExpiryAlertDate = DateTime.Today;
                await _databaseService.SetRuntimeStateAsync(StateLastExpiryAlertDate, DateTime.Today.ToString("yyyy-MM-dd"));
            }

            if (automation.EnableAnomalyAlerts &&
                TimeSpan.TryParse(automation.AnomalyAlertTime ?? "21:00", out var anomalyAlertTime) &&
                DateTime.Now.TimeOfDay >= anomalyAlertTime &&
                _lastAnomalyAlertDate != DateTime.Today)
            {
                string anomalyAlert = await BuildAnomalyInsightAsync();
                if (!string.IsNullOrWhiteSpace(anomalyAlert))
                {
                    outputs.AddRange(BuildOwnerBroadcasts(anomalyAlert, "AnomalyAlert"));
                }

                _lastAnomalyAlertDate = DateTime.Today;
                await _databaseService.SetRuntimeStateAsync(StateLastAnomalyAlertDate, DateTime.Today.ToString("yyyy-MM-dd"));
            }

            return outputs;
        }

        public async Task StartDatabaseSyncWatcherAsync(CancellationToken token)
        {
            if (!IsDualStockRealtimeWatcherEnabled())
            {
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RunDatabaseSyncWatcherAsync();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync(
                        $"Dual stock watcher error: {ex.Message}",
                        "DualStockWatcher",
                        ex.ToString());
                }

                await Task.Delay(TimeSpan.FromSeconds(GetDualStockSyncIntervalSeconds()), token);
            }
        }

        public async Task<List<OutboundMessage>> RunDatabaseSyncWatcherAsync()
        {
            var outputs = new List<OutboundMessage>();
            if (_posDbService == null || _configService.Config?.Automation?.EnableDualStockSync == false)
            {
                return outputs;
            }

            string? cursorValue = _databaseService.GetRuntimeState(StateLastDualStockWatcherDocumentId);
            cursorValue ??= _databaseService.GetRuntimeState(StateLegacyLastDualStockWatcherDocumentId);
            if (!long.TryParse(cursorValue, out long lastDocumentId))
            {
                long latest = await _posDbService.GetLatestStockMutationDocumentIdAsync();
                await _databaseService.SetRuntimeStateAsync(StateLastDualStockWatcherDocumentId, latest.ToString(CultureInfo.InvariantCulture));
                return outputs;
            }

            var mutations = await _posDbService.GetStockMutationDocumentsAfterAsync(lastDocumentId, 100);
            if (!mutations.Any())
            {
                return outputs;
            }

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            foreach (var documentGroup in mutations.GroupBy(item => item.DocumentId).OrderBy(group => group.Key))
            {
                var first = documentGroup.First();
                try
                {
                    DateTime documentTimestamp = first.Date.AddSeconds(1);
                    if (!IsDualStockInternalMutation(first.InternalNote))
                    {
                        var affectedMappings = documentGroup
                            .Select(item => FindMappingForProduct(mappings, item.ProductId))
                            .Where(mapping => mapping != null)
                            .Cast<UnitConversionMapping>()
                            .GroupBy(mapping => mapping.Id, StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.First())
                            .ToList();

                        foreach (var mapping in affectedMappings)
                        {
                            if (first.DocumentTypeId == 3)
                            {
                                foreach (var mutation in documentGroup)
                                {
                                    if (string.Equals(mutation.ProductId, mapping.ParentProductId, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(mutation.ProductId, mapping.ChildProductId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        await ReconcileCompanionMinusAsync(
                                            mapping,
                                            mutation.ProductId,
                                            mutation.Date.AddSeconds(1),
                                            first.DocumentId,
                                            mapping.Id);
                                    }
                                }
                            }

                            string? alert = await EvaluateFamilyEquilibriumAsync(
                                mapping,
                                documentTimestamp,
                                "watcher equilibrium",
                                first.DocumentId,
                                mapping.Id);
                            if (!string.IsNullOrWhiteSpace(alert))
                            {
                                outputs.AddRange(BuildDualStockAlertBroadcasts(alert, "DualStockWatcher"));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync(
                        $"Dual stock watcher gagal memproses dokumen {documentGroup.Key}: {ex.Message}",
                        "DualStockWatcher",
                        ex.ToString());
                }
                finally
                {
                    lastDocumentId = Math.Max(lastDocumentId, documentGroup.Key);
                    await _databaseService.SetRuntimeStateAsync(StateLastDualStockWatcherDocumentId, lastDocumentId.ToString(CultureInfo.InvariantCulture));
                }
            }

            return outputs;
        }

        public async Task<List<OutboundMessage>> RunDualStockScheduledSyncAsync()
        {
            var outputs = new List<OutboundMessage>();
            var automation = _configService.Config?.Automation;
            if (_posDbService == null || automation?.EnableDualStockSync == false)
            {
                return outputs;
            }

            if (!TimeSpan.TryParse(automation.DualStockDailySyncTime ?? "21:00", out var syncTime))
            {
                syncTime = TimeSpan.FromHours(21);
            }

            DateTime todaySyncAt = DateTime.Today.Add(syncTime);
            if (DateTime.Now < todaySyncAt)
            {
                return outputs;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastDualStockDailySyncDate), out var lastSyncDate) &&
                lastSyncDate.Date >= DateTime.Today)
            {
                return outputs;
            }

            var scan = await RunDualStockEquilibriumScanAsync(todaySyncAt, "scheduled close-time sync");
            if (scan.Alerts.Any())
            {
                outputs.AddRange(BuildDualStockAlertBroadcasts(
                    BuildDualStockAlertMessage(scan.Alerts),
                    "DualStockScheduledSync"));
            }

            await _databaseService.SetRuntimeStateAsync(StateLastDualStockDailySyncDate, DateTime.Today.ToString("yyyy-MM-dd"));
            return outputs;
        }

        public async Task RunDualStockStartupCatchUpAsync()
        {
            var automation = _configService.Config?.Automation;
            if (_posDbService == null || automation?.EnableDualStockSync == false)
            {
                return;
            }

            if (!TimeSpan.TryParse(automation.DualStockDailySyncTime ?? "21:00", out var syncTime))
            {
                syncTime = TimeSpan.FromHours(21);
            }

            DateTime yesterday = DateTime.Today.AddDays(-1);
            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastDualStockDailySyncDate), out var lastSyncDate) &&
                lastSyncDate.Date >= yesterday)
            {
                return;
            }

            var watcherMessages = await RunDatabaseSyncWatcherAsync();
            await EnqueueOutboundMessagesAsync(watcherMessages);

            var scan = await RunDualStockEquilibriumScanAsync(yesterday.Add(syncTime), "startup catch-up daily sync");
            if (scan.Alerts.Any())
            {
                await EnqueueOutboundMessagesAsync(BuildDualStockAlertBroadcasts(
                    BuildDualStockAlertMessage(scan.Alerts),
                    "DualStockStartupCatchUp"));
            }

            await _databaseService.SetRuntimeStateAsync(StateLastDualStockDailySyncDate, yesterday.ToString("yyyy-MM-dd"));
        }

        public async Task<string> ForceDualStockDailySyncAsync(DateTime? documentTimestamp = null, string reason = "manual daily sync")
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (_configService.Config?.Automation?.EnableDualStockSync == false)
            {
                return "Dual stock sync nonaktif di Settings.";
            }

            var scan = await RunDualStockEquilibriumScanAsync(documentTimestamp ?? DateTime.Now, reason);

            await _databaseService.SetRuntimeStateAsync(
                StateLastDualStockDailySyncDate,
                (documentTimestamp ?? DateTime.Now).Date.ToString("yyyy-MM-dd"));

            string alertInfo = scan.Alerts.Any()
                ? $" Alert defisit: {scan.Alerts.Count}."
                : string.Empty;
            return $"Konsolidasi dual stok selesai. {scan.Processed} mapping keluarga dievaluasi.{alertInfo}";
        }

        public async Task RunDualStockShutdownSyncAsync()
        {
            var automation = _configService.Config?.Automation;
            if (_posDbService == null || automation?.EnableDualStockSync == false)
            {
                return;
            }

            var watcherMessages = await RunDatabaseSyncWatcherAsync();
            await EnqueueOutboundMessagesAsync(watcherMessages);

            var scan = await RunDualStockEquilibriumScanAsync(DateTime.Now, "app shutdown sync");
            if (scan.Alerts.Any())
            {
                await EnqueueOutboundMessagesAsync(BuildDualStockAlertBroadcasts(
                    BuildDualStockAlertMessage(scan.Alerts),
                    "DualStockShutdownSync"));
            }
        }

        public int GetDualStockSyncIntervalSeconds()
        {
            int value = _configService.Config?.Automation?.DualStockSyncIntervalSeconds ?? 15;
            return Math.Clamp(value, 5, 3600);
        }

        public bool IsDualStockRealtimeWatcherEnabled()
        {
            var automation = _configService.Config?.Automation;
            return automation?.EnableDualStockSync != false &&
                   automation?.EnableDualStockRealtimeWatcher == true;
        }

        private static UnitConversionMapping? FindMappingForProduct(IEnumerable<UnitConversionMapping> mappings, string? productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return null;
            }

            return mappings.FirstOrDefault(mapping =>
                string.Equals(mapping.ParentProductId, productId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapping.ChildProductId, productId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsDualStockInternalMutation(string? internalNote)
        {
            if (string.IsNullOrWhiteSpace(internalNote))
            {
                return false;
            }

            return internalNote.Contains(DualStockInternalNotePrefix, StringComparison.OrdinalIgnoreCase) ||
                   internalNote.Contains("SSA shadow conversion", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ReconcileCompanionMinusAsync(
            UnitConversionMapping mapping,
            string changedProductId,
            DateTime? documentTimestamp = null,
            long? triggerDocumentId = null,
            string? triggerMappingId = null)
        {
            if (_posDbService == null)
            {
                return;
            }

            bool changedParent = string.Equals(changedProductId, mapping.ParentProductId, StringComparison.OrdinalIgnoreCase);
            string companionId = changedParent ? mapping.ChildProductId : mapping.ParentProductId;
            var changed = await _posDbService.GetProductByIdAsync(changedProductId);
            var companion = await _posDbService.GetProductByIdAsync(companionId);
            if (changed == null || companion == null)
            {
                return;
            }

            decimal changedStock = changed.Stock ?? 0;
            decimal companionStock = companion.Stock ?? 0;
            if (changedStock < 0 || companionStock >= 0)
            {
                return;
            }

            const string action = "zero-baseline";
            string mappingToken = triggerMappingId ?? mapping.Id;
            if (await IsDuplicateDualStockTriggerAsync(triggerDocumentId, mappingToken, action))
            {
                await _loggingService.LogInfoAsync(
                    $"Dual stock zero-baseline dilewati karena trigger {triggerDocumentId} untuk mapping {mappingToken} sudah diproses.",
                    "DualStockWatcher");
                return;
            }

            await CreateDualStockInventoryDocumentAsync(
                new[]
                {
                    new DualStockTarget(companion, 0)
                },
                AppendDualStockTriggerMetadata(
                    $"{DualStockInternalNotePrefix}: zero-baseline companion minus after direct Aronium opname",
                    triggerDocumentId,
                    mappingToken,
                    action),
                documentTimestamp: documentTimestamp);

            await _loggingService.LogInfoAsync(
                $"Dual stock zero-baseline: {companion.Name} diset 0 dari {companionStock} setelah opname {changed.Name}.",
                "DualStockWatcher");
        }

        private async Task<string?> EvaluateFamilyEquilibriumAsync(
            UnitConversionMapping mapping,
            DateTime? documentTimestamp = null,
            string reason = "watcher equilibrium",
            long? triggerDocumentId = null,
            string? triggerMappingId = null)
        {
            if (_posDbService == null || mapping.ConversionRate <= 0)
            {
                return null;
            }

            var parent = await _posDbService.GetProductByIdAsync(mapping.ParentProductId);
            var child = await _posDbService.GetProductByIdAsync(mapping.ChildProductId);
            if (parent == null || child == null)
            {
                return null;
            }

            decimal rate = mapping.ConversionRate;
            decimal parentStock = parent.Stock ?? 0;
            decimal childStock = child.Stock ?? 0;

            if (childStock <= -rate)
            {
                const string action = "auto-break";
                string mappingToken = triggerMappingId ?? mapping.Id;
                if (await IsDuplicateDualStockTriggerAsync(triggerDocumentId, mappingToken, action))
                {
                    await _loggingService.LogInfoAsync(
                        $"Dual stock auto-break dilewati karena trigger {triggerDocumentId} untuk mapping {mappingToken} sudah diproses.",
                        "DualStockWatcher");
                    return null;
                }

                decimal fullBreaks = decimal.Floor(Math.Abs(childStock) / rate);
                if (fullBreaks <= 0)
                {
                    return null;
                }

                decimal availableFullParents = parentStock >= 1 ? decimal.Floor(parentStock) : 0;
                decimal breakQty = availableFullParents > 0
                    ? Math.Min(fullBreaks, availableFullParents)
                    : fullBreaks;
                if (breakQty <= 0)
                {
                    breakQty = 1;
                }

                decimal targetParent = parentStock - breakQty;
                decimal targetChild = childStock + breakQty * rate;
                var result = await CreateDualStockInventoryDocumentAsync(
                    new[]
                    {
                        new DualStockTarget(parent, targetParent),
                        new DualStockTarget(child, targetChild)
                    },
                    AppendDualStockTriggerMetadata(
                        $"{DualStockInternalNotePrefix}: auto-break {FormatStockValue(breakQty)} {GetUnitLabel(parent.Unit)} -> {FormatStockValue(breakQty * rate)} {GetUnitLabel(child.Unit)} | {reason}",
                        triggerDocumentId,
                        mappingToken,
                        action),
                    allowNegativeTargets: true,
                    documentTimestamp: documentTimestamp);

                if (!result.Success)
                {
                    await _loggingService.LogWarningAsync(
                        $"Dual stock auto-break gagal untuk {mapping.ParentProductName}: {result.Error}",
                        "DualStockWatcher");
                    return null;
                }

                await _loggingService.LogInfoAsync(
                    $"Dual stock auto-break: {parent.Name} {parentStock}->{targetParent}, {child.Name} {childStock}->{targetChild}.",
                    "DualStockWatcher");

                decimal totalChild = targetParent * rate + targetChild;
                if (targetParent < 0 || totalChild <= 0)
                {
                    return $"{IconWarning} Dual stock auto-break: {FormatOptional(mapping.FamilyName ?? parent.Name)} sekarang defisit. " +
                           $"{FormatOptional(parent.Name)} {FormatStockValue(targetParent)} {GetUnitLabel(parent.Unit)}, " +
                           $"{FormatOptional(child.Name)} {FormatStockValue(targetChild)} {GetUnitLabel(child.Unit)}. " +
                           "Input restock atau lakukan opname fisik.";
                }

                return null;
            }

            if (childStock >= rate)
            {
                const string action = "auto-pack";
                string mappingToken = triggerMappingId ?? mapping.Id;
                if (await IsDuplicateDualStockTriggerAsync(triggerDocumentId, mappingToken, action))
                {
                    await _loggingService.LogInfoAsync(
                        $"Dual stock auto-pack dilewati karena trigger {triggerDocumentId} untuk mapping {mappingToken} sudah diproses.",
                        "DualStockWatcher");
                    return null;
                }

                decimal packQty = decimal.Floor(childStock / rate);
                if (packQty <= 0)
                {
                    return null;
                }

                decimal targetParent = parentStock + packQty;
                decimal targetChild = childStock - packQty * rate;
                var result = await CreateDualStockInventoryDocumentAsync(
                    new[]
                    {
                        new DualStockTarget(parent, targetParent),
                        new DualStockTarget(child, targetChild)
                    },
                    AppendDualStockTriggerMetadata(
                        $"{DualStockInternalNotePrefix}: auto-pack {FormatStockValue(packQty * rate)} {GetUnitLabel(child.Unit)} -> {FormatStockValue(packQty)} {GetUnitLabel(parent.Unit)} | {reason}",
                        triggerDocumentId,
                        mappingToken,
                        action),
                    allowNegativeTargets: true,
                    documentTimestamp: documentTimestamp);

                if (!result.Success)
                {
                    await _loggingService.LogWarningAsync(
                        $"Dual stock auto-pack gagal untuk {mapping.ParentProductName}: {result.Error}",
                        "DualStockWatcher");
                    return null;
                }

                await _loggingService.LogInfoAsync(
                    $"Dual stock auto-pack: {parent.Name} {parentStock}->{targetParent}, {child.Name} {childStock}->{targetChild}.",
                    "DualStockWatcher");
            }

            return null;
        }

        private async Task<DualStockScanResult> RunDualStockEquilibriumScanAsync(DateTime documentTimestamp, string reason)
        {
            var alerts = new List<string>();
            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            int processed = 0;
            foreach (var mapping in mappings.Where(mapping => mapping.ConversionRate > 0))
            {
                string? alert = await EvaluateFamilyEquilibriumAsync(mapping, documentTimestamp, reason);
                if (!string.IsNullOrWhiteSpace(alert))
                {
                    alerts.Add(alert);
                }

                processed++;
            }

            return new DualStockScanResult(processed, alerts);
        }

        private async Task<bool> IsDuplicateDualStockTriggerAsync(long? triggerDocumentId, string? mappingId, string action)
        {
            if (_posDbService == null ||
                !triggerDocumentId.HasValue ||
                triggerDocumentId.Value <= 0 ||
                string.IsNullOrWhiteSpace(mappingId) ||
                string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            return await _posDbService.HasDualStockDocumentForTriggerAsync(triggerDocumentId.Value, mappingId, action);
        }

        private static string AppendDualStockTriggerMetadata(string internalNote, long? triggerDocumentId, string? mappingId, string action)
        {
            if (!triggerDocumentId.HasValue ||
                triggerDocumentId.Value <= 0 ||
                string.IsNullOrWhiteSpace(mappingId) ||
                string.IsNullOrWhiteSpace(action))
            {
                return internalNote;
            }

            return $"{internalNote} | trigger={triggerDocumentId.Value} | map={mappingId} | action={action}";
        }

        private async Task<BulkDocumentResult> CreateDualStockInventoryDocumentAsync(
            IEnumerable<DualStockTarget> targets,
            string internalNote,
            bool allowNegativeTargets = false,
            DateTime? documentTimestamp = null)
        {
            if (_posDbService == null)
            {
                return new BulkDocumentResult { Success = false, Error = "Database pos.db belum dikonfigurasi." };
            }

            var inputs = new List<BulkDocumentItemInput>();
            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.Product.Id) ||
                    !int.TryParse(target.Product.Id, out int productId))
                {
                    continue;
                }

                inputs.Add(new BulkDocumentItemInput
                {
                    ProductId = productId,
                    ProductName = target.Product.Name ?? target.Product.Id,
                    Quantity = target.TargetStock,
                    Price = target.Product.SellingPrice ?? 0,
                    CurrentStock = target.Product.Stock,
                    Unit = target.Product.Unit
                });
            }

            if (!inputs.Any())
            {
                return new BulkDocumentResult { Success = false, Error = "Tidak ada target dual stock valid." };
            }

            return await _posDbService.CreateBulkInventoryCountDocumentAsync(
                inputs,
                1,
                internalNote,
                allowNegativeTargets,
                documentTimestamp);
        }

        private sealed record DualStockScanResult(int Processed, List<string> Alerts);
        private sealed record DualStockTarget(Product Product, decimal TargetStock);

        public async Task<long> EnqueueOutboundMessageAsync(OutboundMessage outbound)
        {
            outbound.AppInstanceId ??= CurrentAppInstanceId;
            if (string.IsNullOrWhiteSpace(outbound.OutboundSourceType))
            {
                outbound.OutboundSourceType = string.IsNullOrWhiteSpace(outbound.SourceInboundMessageId)
                    ? "manual_admin"
                    : "inbound_reply";
            }

            outbound.ExpiresAt ??= BuildOutboundExpiry(outbound);
            long queueId = await _databaseService.QueueOutboundMessageAsync(outbound);
            outbound.QueueId = queueId;
            return queueId;
        }

        private static DateTime? BuildOutboundExpiry(OutboundMessage outbound)
        {
            if (outbound.Channel != ChannelType.WhatsApp && outbound.Channel != ChannelType.Baileys)
            {
                return null;
            }

            if (string.Equals(outbound.OutboundSourceType, "scheduled_alert", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.Now.AddMinutes(15);
            }

            bool longLived = !string.IsNullOrWhiteSpace(outbound.MediaUrl) ||
                             !string.Equals(outbound.MessageKind, "text", StringComparison.OrdinalIgnoreCase);
            return DateTime.Now.Add(longLived ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(2));
        }

        public async Task EnqueueOutboundMessagesAsync(IEnumerable<OutboundMessage> messages)
        {
            foreach (var message in messages)
            {
                await EnqueueOutboundMessageAsync(message);
            }
        }

        public void RegisterDocumentSender(Func<ChannelType, string, string, string, Task<string?>> sender)
        {
            _documentSender = sender;
        }

        public void RegisterTelegramDocumentSender(Func<long, string, string, Task> sender)
        {
            _documentSender = async (channel, recipientId, filePath, caption) =>
            {
                if (channel != ChannelType.Telegram || !long.TryParse(recipientId, out var chatId))
                {
                    return null;
                }

                await sender(chatId, filePath, caption);
                return null;
            };
        }

        public async Task<IReadOnlyList<OutboundMessageRecord>> GetDueOutboundMessagesAsync(int limit = 20)
        {
            return await _databaseService.GetDueOutboundMessagesAsync(limit: limit);
        }

        public async Task HandleOutboundDispatchSuccessAsync(OutboundMessageRecord record, string? externalMessageId)
        {
            await _databaseService.MarkOutboundSentAsync(record.Id, externalMessageId);
            _lastOutboundSentAt = DateTime.Now;
            _lastFailureMessage = null;
            await SaveRuntimeDateAsync(StateLastOutboundSentAt, _lastOutboundSentAt.Value);
            await _databaseService.SetRuntimeStateAsync(StateLastOutboundFailureMessage, string.Empty);
            await _databaseService.UpdateAutomationExecutionAsync(
                record.CorrelationId,
                "sent",
                details: $"Outbound delivered to {record.Channel}:{record.RecipientId}");
        }

        public async Task HandleOutboundDispatchFailureAsync(OutboundMessageRecord record, string error)
        {
            int nextAttempt = record.AttemptCount + 1;
            int maxRetries = GetMaxRetries(record.Channel);

            _lastOutboundFailureAt = DateTime.Now;
            _lastFailureMessage = error;
            await SaveRuntimeDateAsync(StateLastOutboundFailureAt, _lastOutboundFailureAt.Value);
            await _databaseService.SetRuntimeStateAsync(StateLastOutboundFailureMessage, error);

            if (nextAttempt >= maxRetries)
            {
                await _databaseService.MarkOutboundDeadLetterAsync(record.Id, error);
                await _databaseService.UpdateAutomationExecutionAsync(
                    record.CorrelationId,
                    "dead_letter",
                    details: error);
                return;
            }

            TimeSpan delay = GetRetryDelay(record.Channel, nextAttempt);
            await _databaseService.MarkOutboundRetryAsync(record.Id, error, DateTime.Now.Add(delay));
            await _databaseService.UpdateAutomationExecutionAsync(
                record.CorrelationId,
                "retry",
                details: $"Retry {nextAttempt}/{maxRetries - 1}: {error}");
        }

        public async Task HandleOutboundDispatchDeferredAsync(OutboundMessageRecord record, string reason, TimeSpan delay)
        {
            _lastOutboundFailureAt = DateTime.Now;
            _lastFailureMessage = reason;
            await SaveRuntimeDateAsync(StateLastOutboundFailureAt, _lastOutboundFailureAt.Value);
            await _databaseService.SetRuntimeStateAsync(StateLastOutboundFailureMessage, reason);
            await _databaseService.DeferOutboundMessageAsync(record.Id, reason, DateTime.Now.Add(delay));
            await _databaseService.UpdateAutomationExecutionAsync(
                record.CorrelationId,
                "deferred",
                details: reason);
        }

        public async Task HandleOutboundDispatchRejectedAsync(OutboundMessageRecord record, string reason)
        {
            _lastOutboundFailureAt = DateTime.Now;
            _lastFailureMessage = reason;
            await SaveRuntimeDateAsync(StateLastOutboundFailureAt, _lastOutboundFailureAt.Value);
            await _databaseService.SetRuntimeStateAsync(StateLastOutboundFailureMessage, reason);
            await _databaseService.MarkOutboundDeadLetterAsync(record.Id, reason);
            await _databaseService.UpdateAutomationExecutionAsync(
                record.CorrelationId,
                "dead_letter",
                details: reason);
        }

        public async Task RecordExternalStatusEventAsync(
            ChannelType channel,
            string? externalMessageId,
            string status,
            string rawPayload,
            string? correlationId = null,
            string? errorDetails = null)
        {
            await _databaseService.RecordMessageStatusEventAsync(new MessageStatusEventRecord
            {
                Channel = channel.ToString(),
                CorrelationId = correlationId,
                ExternalMessageId = externalMessageId,
                Status = status,
                ErrorDetails = errorDetails,
                RawPayload = rawPayload,
                RecordedAt = DateTime.Now
            });

            _lastWebhookStatus = status;
            await _databaseService.SetRuntimeStateAsync(StateLastWebhookStatus, status);
        }

        public IntegrationStatus GetIntegrationStatus(
            bool telegramRunning,
            bool whatsAppRunning,
            bool tunnelRunning,
            string? tunnelPublicUrl,
            bool baileysRunning = false,
            bool baileysReachable = false,
            bool baileysPaired = false,
            ConfigService? configService = null,
            TelegramBotService? telegramService = null,
            BaileysSidecarService? baileysService = null,
            PosDbService? posDbService = null)
        {
            configService ??= _configService;
            var whatsApp = configService.Config?.WhatsApp;
            var baileys = configService.Config?.Baileys;
            string mode = WhatsAppModes.Normalize(whatsApp?.Mode);
            bool cloudEnabled = whatsApp?.Enabled == true && WhatsAppModes.UsesCloudApi(mode);
            bool baileysEnabled = baileys?.Enabled == true && WhatsAppModes.UsesBaileys(mode);
            bool telegramConfigured = !string.IsNullOrWhiteSpace(configService.Config?.Telegram?.BotToken) &&
                                      configService.Config?.Telegram?.BotToken != "YOUR_TELEGRAM_BOT_TOKEN";
            bool telegramValidated = TelegramBotService.IsBotTokenFormatValid(configService.Config?.Telegram?.BotToken);
            bool cloudConfigured =
                cloudEnabled &&
                !string.IsNullOrWhiteSpace(whatsApp.AccessToken) &&
                !string.IsNullOrWhiteSpace(whatsApp.PhoneNumberId);
            bool cloudOutboundReady = cloudConfigured;
            bool baileysConfigured =
                baileysEnabled &&
                !string.IsNullOrWhiteSpace(baileys?.BotPhoneNumber) &&
                !string.IsNullOrWhiteSpace(baileys?.NodeBinaryPath) &&
                !string.IsNullOrWhiteSpace(baileys?.SidecarEntryPath) &&
                !string.IsNullOrWhiteSpace(baileys?.SessionPath);
            bool baileysOutboundReady = baileysConfigured &&
                                        (baileysService?.CanSendOutbound() ?? (baileysReachable && baileysPaired));
            bool signatureEnabled = !string.IsNullOrWhiteSpace(whatsApp?.AppSecret);
            bool overallWhatsAppConfigured = (!cloudEnabled || cloudConfigured) && (!baileysEnabled || baileysConfigured);

            return new IntegrationStatus
            {
                ActiveConfigPath = configService.ConfigPath,
                ConfigWarning = configService.DuplicateConfigWarning,
                TelegramConfigured = telegramConfigured,
                TelegramValidated = telegramValidated,
                TelegramRunning = telegramRunning,
                TelegramLastError = telegramService?.LastError,
                TelegramLastValidatedAt = telegramService?.LastValidatedAt,
                TelegramActionHint = !telegramConfigured
                    ? "Isi Telegram bot token jika ingin memakai Telegram."
                    : !telegramValidated
                        ? "Perbaiki format token Telegram agar mengikuti pola angka:secret."
                        : telegramRunning
                            ? "Telegram aktif."
                            : string.IsNullOrWhiteSpace(telegramService?.LastError)
                                ? "Klik Start Bot untuk validasi langsung ke Bot API."
                                : "Periksa token Telegram atau koneksi internet.",
                WhatsAppRunning = whatsAppRunning,
                BaileysRunning = baileysRunning,
                TunnelRunning = tunnelRunning,
                DatabaseConnected = posDbService != null,
                AiConfigured = !string.IsNullOrWhiteSpace(configService.Config?.Groq?.ApiKey) &&
                               configService.Config?.Groq?.ApiKey != "YOUR_GROQ_API_KEY",
                WhatsAppConfigured = overallWhatsAppConfigured,
                WhatsAppCloudConfigured = cloudConfigured,
                WhatsAppCloudOutboundReady = cloudOutboundReady,
                BaileysConfigured = baileysConfigured,
                BaileysReachable = baileysReachable,
                BaileysPaired = baileysPaired,
                BaileysOutboundReady = baileysOutboundReady,
                BaileysPairingInProgress = baileysService?.PairingInProgress == true,
                BaileysConnectionState = baileysService?.ConnectionState,
                BaileysLastDisconnectStatusCode = baileysService?.LastDisconnectStatusCode,
                BaileysLastDisconnectReason = baileysService?.LastDisconnectReason,
                BaileysSidecarBuildTag = baileysService?.SidecarBuildTag,
                BaileysLastValidatedAt = baileysService?.LastValidatedAt,
                AppInstanceId = configService.Config?.App?.InstanceId,
                MachineName = configService.Config?.App?.MachineName,
                ActiveRuntimeSince = configService.Config?.App?.ActiveRuntimeSince,
                LastIgnoredInboundReason = _databaseService.GetRuntimeState(StateLastIgnoredInboundReason),
                WhatsAppActionHint = BuildWhatsAppActionHint(cloudEnabled, cloudOutboundReady, baileysEnabled, baileysConfigured),
                BaileysActionHint = baileysService?.BuildActionHint(),
                SignatureValidationEnabled = signatureEnabled,
                ProductionReady =
                    (!cloudEnabled || (cloudConfigured && signatureEnabled && !string.IsNullOrWhiteSpace(BuildPublicWebhookUrl(tunnelPublicUrl)))) &&
                    (!baileysEnabled || baileysConfigured),
                WhatsAppMode = mode,
                LocalWebhookPort = whatsApp?.LocalWebhookPort ?? 8090,
                PendingOutboundCount = _databaseService.GetPendingOutboundCount(),
                PendingWhatsAppLikeOutboundCount = _databaseService.GetPendingWhatsAppLikeOutboundCount(),
                TunnelPublicUrl = tunnelPublicUrl,
                WhatsAppWebhookUrl = BuildWebhookUrl(tunnelPublicUrl),
                TunnelProvider = configService.Config?.Tunnel?.Provider,
                PosDbSchemaStatus = posDbService?.SchemaStatus,
                PosDbLastValidatedAt = posDbService?.LastSchemaValidatedAt,
                PosDbActionHint = posDbService?.LastSchemaActionHint,
                LastWebhookStatus = _lastWebhookStatus,
                LastFailureMessage = _lastFailureMessage,
                LastWebhookReceivedAt = _lastWebhookReceivedAt,
                LastOutboundSentAt = _lastOutboundSentAt,
                LastOutboundFailureAt = _lastOutboundFailureAt
            };
        }

        private async Task<string> ExecuteInboundFlowAsync(
            InboundMessage message,
            AutomationExecutionContext context,
            IReadOnlyList<AutomationRule> matchedRules)
        {
            bool prefersAi = HasRuleAction(matchedRules, "route-ai");
            bool isCommand = message.Text.StartsWith("/", StringComparison.Ordinal);

            if (!string.IsNullOrWhiteSpace(message.MediaUrl))
            {
                ApplyPendingOcrPhotoIfNeeded(message, context);
                return await HandleMediaMessageAsync(message, context);
            }

            if (!isCommand)
            {
                string? pendingInputResponse = await TryHandlePendingInputAsync(message, context);
                if (!string.IsNullOrWhiteSpace(pendingInputResponse))
                {
                    return pendingInputResponse;
                }
            }

            string? staticReply = matchedRules
                .SelectMany(r => r.Actions ?? Enumerable.Empty<AutomationRuleAction>())
                .FirstOrDefault(a => string.Equals(a.Type, "reply", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(staticReply))
            {
                return staticReply!;
            }

            if (isCommand)
            {
                return await HandleCommandAsync(message, context);
            }

            var ocrSettings = _configService.Config?.OcrReceipt;
            if (CanUseOperationalFeatures(context) &&
                TryExtractTextReceiptPayload(message.Text, ocrSettings, out string textReceiptPayload))
            {
                return await HandleTextReceiptAsync(message, context, ocrSettings, textReceiptPayload);
            }

            if (CanUseOperationalFeatures(context) &&
                ocrSettings?.Enabled == true &&
                ocrSettings.AutoDetectTextReceipt &&
                LooksLikeReceiptText(message.Text))
            {
                return await HandleTextReceiptAsync(message, context, ocrSettings, message.Text);
            }

            string? shortcutResponse = await TryHandleShortcutAsync(message.Text, context);
            if (!string.IsNullOrWhiteSpace(shortcutResponse))
            {
                return shortcutResponse;
            }

            string? pendingShadowResponse = await TryHandlePendingShadowMappingReplyAsync(message.Text, context);
            if (!string.IsNullOrWhiteSpace(pendingShadowResponse))
            {
                return pendingShadowResponse;
            }

            string? preIntentResponse = await TryHandlePreIntentPatternAsync(message.Text, context);
            if (!string.IsNullOrWhiteSpace(preIntentResponse))
            {
                return preIntentResponse;
            }

            if (LooksLikeOperationalMutationRequest(message.Text))
            {
                return BuildSlashGuidance(message.Text);
            }

            if (prefersAi || HasRuleAction(matchedRules, "route-command") || !isCommand)
            {
                return await HandleNaturalLanguageAsync(message, context);
            }

            return await HandleNaturalLanguageAsync(message, context);
        }

        private void ApplyPendingOcrPhotoIfNeeded(InboundMessage message, AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (!_pendingInputBySender.TryGetValue(senderKey, out var pending))
            {
                return;
            }

            if (pending.ExpiresAt <= DateTime.Now)
            {
                _pendingInputBySender.TryRemove(senderKey, out _);
                return;
            }

            if (!string.Equals(pending.Action, "ocr_foto", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pendingInputBySender.TryRemove(senderKey, out _);
            string trigger = _configService.Config?.OcrReceipt?.TriggerCaption?.Trim() ?? "/struk";
            message.Text = trigger;
        }

        private async Task<string?> TryHandlePendingInputAsync(InboundMessage message, AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (!_pendingInputBySender.TryGetValue(senderKey, out var pending))
            {
                return null;
            }

            if (pending.ExpiresAt <= DateTime.Now)
            {
                _pendingInputBySender.TryRemove(senderKey, out _);
                return null;
            }

            string input = (message.Text ?? string.Empty).Trim();
            if (string.Equals(pending.Action, "ocr_foto", StringComparison.OrdinalIgnoreCase))
            {
                return "Silakan kirim foto faktur. Untuk input teks gunakan menu Input Teks Faktur.";
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                return BuildPendingInputPrompt(pending.Action);
            }

            _pendingInputBySender.TryRemove(senderKey, out _);
            string normalizedInput = NormalizeText(input);
            if (IsZeroCostExportAllKeyword(normalizedInput))
            {
                return await HandleExportZeroCostAsync(context, includeAllZeroCostProducts: true);
            }

            if (IsZeroCostExportKeyword(normalizedInput))
            {
                return await HandleExportZeroCostAsync(context);
            }

            string? internalCommand = pending.Action switch
            {
                "cek_dokumen" => $"/dokumen {input}",
                "detail_nota" => $"/dokumen {input}",
                "cek_stok" => $"/stok {input}",
                "inventory" => $"/inventory {input}",
                "restock" => $"/restock {input}",
                "input_struk" => $"/inputstruk {input}",
                "riwayat_restock" => $"/riwayat_restock {input}",
                "riwayat_inventory" => $"/riwayat_inventory {input}",
                "penjualan" => $"/penjualan {input}",
                "stok_kategori" => $"/stok_kategori {input}",
                "stok_efektif" => $"/stok_efektif {input}",
                "set_family" => $"/set_family {input}",
                "pelanggan" => $"/pelanggan {input}",
                "piutang" => IsAllKeyword(normalizedInput) ? "/piutang" : $"/piutang {input}",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(internalCommand))
            {
                return null;
            }

            var routedMessage = new InboundMessage
            {
                Channel = message.Channel,
                SenderId = message.SenderId,
                SenderName = message.SenderName,
                Text = internalCommand,
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                Timestamp = message.Timestamp
            };

            return await HandleCommandAsync(routedMessage, context);
        }

        private static bool CanUseOperationalFeatures(AutomationExecutionContext context)
        {
            return context.IsOwner || context.IsKasir;
        }

        private async Task<string> HandleCommandAsync(InboundMessage message, AutomationExecutionContext context)
        {
            string[] parts = message.Text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            string args = parts.Length > 1 ? parts[1] : string.Empty;

            return cmd switch
            {
                "/start" => BuildStartText(),
                "/menu" => BuildMenuHeaderText("main"),
                "/help" => BuildHelpCommandText(args, context.IsOwner),
                "/confirm" => await ConfirmPendingActionAsync(message, context),
                "/simpan" or "/confirm_modal" => context.IsOwner ? await ConfirmPriceOverrideAsync(message, args, updateCost: true, updateSellingPrice: false) : BuildOwnerOnlyDeniedMessage(),
                "/simpan_jual" or "/confirm_modal_jual" => context.IsOwner ? await ConfirmPriceOverrideAsync(message, args, updateCost: true, updateSellingPrice: true) : BuildOwnerOnlyDeniedMessage(),
                "/lewati_harga" or "/skip_modal" => context.IsOwner ? await ConfirmPriceOverrideAsync(message, args, updateCost: false, updateSellingPrice: false) : BuildOwnerOnlyDeniedMessage(),
                "/jual" or "/set_harga_jual" => context.IsOwner ? await SetPendingManualSellingPriceAsync(message, args) : BuildOwnerOnlyDeniedMessage(),
                "/detail_harga" => context.IsOwner ? await ShowPendingPriceOverrideDetailAsync(message) : BuildOwnerOnlyDeniedMessage(),
                "/batal" or "/cancel" => CancelPendingInput(message.Channel, message.SenderId) ? "Input dibatalkan." : await CancelPendingActionAsync(message, context),
                "/export" => context.IsOwner ? await HandleLegacyExportCommandAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/stok" => await HandleStockAsync(args, context),
                "/laporan" => await HandleReportAsync(context.IsOwner, args),
                "/laporan_periode" => context.IsOwner ? await HandlePeriodReportCommandAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/statistik" => context.IsOwner ? await HandleStatisticsAsync() : BuildOwnerOnlyDeniedMessage(),
                "/produk" => context.IsOwner ? await HandleProductsCommandAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/pelanggan_loyal" => CanUseOperationalFeatures(context) ? await HandleLoyalCustomersAsync(context) : BuildOwnerOnlyDeniedMessage(),
                "/pelanggan" => CanUseOperationalFeatures(context) ? await HandleCustomersAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/supplier" => context.IsOwner ? await HandleSuppliersAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/user" => context.IsOwner ? await HandleUsersAsync(args) : BuildOwnerOnlyDeniedMessage(),
                "/piutang" => CanUseOperationalFeatures(context) ? await HandleReceivablesCommandAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/penjualan" => CanUseOperationalFeatures(context) ? await HandleProductSalesAsync(args) : BuildOwnerOnlyDeniedMessage(),
                "/dokumen" => CanUseOperationalFeatures(context) ? await HandleDocumentLookupAsync(args, context) : BuildOwnerOnlyDeniedMessage(),
                "/restock" => CanUseOperationalFeatures(context) ? await QueueRestockAsync(message, args) : "Akses ditolak. Fitur restock hanya untuk owner/kasir.",
                "/struk" => CanUseOperationalFeatures(context) ? "Kirim foto struk sebagai foto dengan caption /struk untuk memulai OCR pembelian." : BuildOwnerOnlyDeniedMessage(),
                "/inputstruk" => CanUseOperationalFeatures(context) ? await HandleTextReceiptAsync(message, context, _configService.Config?.OcrReceipt, args) : BuildOwnerOnlyDeniedMessage(),
                "/selesai_struk" => CanUseOperationalFeatures(context) ? await HandleFinishOcrSessionAsync(message, context) : BuildOwnerOnlyDeniedMessage(),
                "/inventory" or "/quick_inventory" => CanUseOperationalFeatures(context) ? await QueueInventoryAsync(message, args) : "Akses ditolak. Fitur inventory hanya untuk owner/kasir.",
                "/inventory_family" => CanUseOperationalFeatures(context) ? await QueueInventoryFamilyAsync(message, args) : "Akses ditolak. Fitur inventory family hanya untuk owner/kasir.",
                "/analisa" => context.IsOwner ? await HandleAnalysisAsync() : "Akses ditolak. Fitur analisa hanya untuk owner.",
                "/analisa_stok" => context.IsOwner ? await HandleStockMovementAnalysisAsync() : "Akses ditolak. Fitur analisa stok hanya untuk owner.",
                "/cek_modal" => context.IsOwner ? await HandleZeroCostAsync() : "Akses ditolak. Fitur cek modal hanya untuk owner.",
                "/laporan_kasir" => context.IsOwner ? await HandleCashierReportAsync(args) : "Akses ditolak. Fitur laporan kasir hanya untuk owner.",
                "/dead_stock" => context.IsOwner ? await HandleDeadStockAsync() : "Akses ditolak. Fitur dead stock hanya untuk owner.",
                "/slow_moving" => context.IsOwner ? await HandleSlowMovingProductsAsync() : "Akses ditolak. Fitur slow moving hanya untuk owner.",
                "/sleeping_stock" => context.IsOwner ? await HandleSleepingStockAsync() : "Akses ditolak. Fitur sleeping stock hanya untuk owner.",
                "/cek_expired" => CanUseOperationalFeatures(context) ? await HandleExpiryInfoAsync(args, context) : "Akses ditolak. Fitur cek expired hanya untuk owner/kasir.",
                "/stok_kategori" => CanUseOperationalFeatures(context) ? await HandleCategorySearchAsync(args, context) : "Akses ditolak. Fitur stok kategori hanya untuk owner/kasir.",
                "/shadow_stok" => context.IsOwner ? await HandleShadowStockAsync() : "Akses ditolak. Fitur shadow stok hanya untuk owner.",
                "/stok_efektif" => context.IsOwner ? await HandleEffectiveStockAsync(args) : "Akses ditolak. Fitur stok efektif hanya untuk owner.",
                "/list_family" => context.IsOwner ? await HandleListFamilyAsync(args) : "Akses ditolak. Fitur list family hanya untuk owner.",
                "/dual_stock" or "/dualstok" => context.IsOwner ? await HandleDualStockCommandAsync(args) : "Akses ditolak. Fitur dual stock hanya untuk owner.",
                "/dual_stock_alert" => context.IsOwner ? await HandleDualStockAlertCommandAsync() : "Akses ditolak. Fitur dual stock alert hanya untuk owner.",
                "/dual_stock_sync" => context.IsOwner ? await HandleDualStockSyncCommandAsync() : "Akses ditolak. Fitur dual stock sync hanya untuk owner.",
                "/dual_stock_watcher" => context.IsOwner ? HandleDualStockWatcherCommand(args) : "Akses ditolak. Fitur dual stock watcher hanya untuk owner.",
                "/dual_stock_channel" => context.IsOwner ? HandleDualStockChannelCommand(args) : "Akses ditolak. Fitur dual stock channel hanya untuk owner.",
                "/set_family" => context.IsOwner ? await HandleSetFamilyFlexibleAsync(args, context) : "Akses ditolak. Fitur set family hanya untuk owner.",
                "/hapus_family" => context.IsOwner ? await HandleDeleteFamilyAsync(args) : "Akses ditolak. Fitur hapus family hanya untuk owner.",
                "/riwayat_restock" => CanUseOperationalFeatures(context) ? await HandleRestockHistoryAsync(args) : "Akses ditolak. Fitur riwayat restock hanya untuk owner/kasir.",
                "/riwayat_inventory" => CanUseOperationalFeatures(context) ? await HandleInventoryHistoryAsync(args) : "Akses ditolak. Fitur riwayat inventory hanya untuk owner/kasir.",
                "/rekomendasi_restock" => context.IsOwner ? await HandleAutoRestockRecommendationsAsync(args) : "Akses ditolak. Fitur rekomendasi restock hanya untuk owner.",
                "/notifikasi_stok" => CanUseOperationalFeatures(context) ? await HandleStockNotificationAsync() : "Akses ditolak. Fitur notifikasi stok hanya untuk owner/kasir.",
                "/ekspor_lengkap" => context.IsOwner ? await HandleExportBundleAsync(context) : BuildOwnerOnlyDeniedMessage(),
                _ => "Command tidak dikenal. Ketik /help untuk melihat daftar command yang tersedia."
            };
        }

        private async Task<string> HandleStockAsync(string args, AutomationExecutionContext? context = null)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string searchQuery = args.Trim();
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                var lowStock = await _posDbService.GetLowStockProductsAsync(10);
                if (!lowStock.Any())
                {
                    return "Semua stok aman.";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"{IconPackage} STOK RENDAH (top 10)");
                sb.AppendLine();
                foreach (var product in lowStock.Take(10))
                {
                    sb.AppendLine(BuildStockSearchLine(product));
                }

                sb.AppendLine();
                sb.AppendLine($"{IconWarning} Banyak stok minus besar - lakukan /inventory untuk koreksi.");
                sb.Append("Atau: /stok [nama] untuk cek produk spesifik.");

                return sb.ToString().TrimEnd();
            }

            var products = await FindProductsAsync(searchQuery, 10);
            if (!products.Any())
            {
                return $"Produk \"{searchQuery}\" tidak ditemukan.";
            }

            if (context != null)
            {
                var first = products[0];
                SetTopicState(context, "produk", entityId: first.Id, entityName: first.Name ?? searchQuery);
            }

            string? familyResponse = await TryBuildFamilyStockResponseAsync(searchQuery, products);
            if (!string.IsNullOrWhiteSpace(familyResponse))
            {
                return familyResponse;
            }

            var result = new StringBuilder();
            result.AppendLine($"{IconSearch} Stok \"{searchQuery}\":");
            result.AppendLine();
            foreach (var product in products)
            {
                result.AppendLine(BuildStockSearchLine(product));
            }

            return result.ToString().TrimEnd();
        }

        private async Task<string?> TryBuildFamilyStockResponseAsync(string query, List<Product> matchedProducts)
        {
            if (_posDbService == null)
            {
                return null;
            }

            var matchedIds = matchedProducts
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .Select(product => product.Id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!matchedIds.Any())
            {
                return null;
            }

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            var mapping = mappings.FirstOrDefault(item =>
                matchedIds.Contains(item.ParentProductId) ||
                matchedIds.Contains(item.ChildProductId));
            if (mapping == null || mapping.ConversionRate <= 0)
            {
                return null;
            }

            var allProducts = await _posDbService.GetAllProductsAsync();
            var productById = allProducts
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .GroupBy(product => product.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            if (!productById.TryGetValue(mapping.ParentProductId, out var parent) ||
                !productById.TryGetValue(mapping.ChildProductId, out var child))
            {
                return null;
            }

            var family = new ProductFamilyStock
            {
                Mapping = mapping,
                ParentProduct = parent,
                ChildProduct = child,
                ParentStock = parent.Stock ?? 0,
                ChildStock = child.Stock ?? 0,
                ConversionRate = mapping.ConversionRate
            };

            var sb = new StringBuilder();
            sb.AppendLine($"{IconSearch} Hasil Pencarian \"{query}\":");
            sb.AppendLine();
            sb.AppendLine(GroqService.FormatDualStockResponse(family, query));

            var otherMatches = matchedProducts
                .Where(product => !string.Equals(product.Id, mapping.ParentProductId, StringComparison.OrdinalIgnoreCase) &&
                                  !string.Equals(product.Id, mapping.ChildProductId, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();
            if (otherMatches.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Produk lainnya:");
                foreach (var product in otherMatches)
                {
                    sb.AppendLine(BuildStockSearchLine(product));
                }
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleReportAsync(bool isOwner, string args = "")
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string period = string.IsNullOrWhiteSpace(args)
                ? "today"
                : TryExtractSalesPeriodArgument(args) ?? TryExtractSalesPeriodArgument($"/laporan {args}") ?? args;
            var (startDate, endDate, _, titleLabel, _) = ResolveSalesPeriod(period);
            var reportDate = startDate.Date;
            bool singleDay = startDate.Date == endDate.Date;

            var revenue = await _posDbService.GetSalesRevenueAsync(startDate, endDate);
            var profit = await _posDbService.GetSalesProfitAsync(startDate, endDate);
            int transactionCount = await _posDbService.GetSalesTransactionCountAsync(startDate, endDate);
            var recentSales = (await _posDbService.GetSalesTransactionsAsync(startDate, endDate))
                .OrderByDescending(item => item.Date)
                .Take(3)
                .ToList();
            var topProducts = await _posDbService.GetTopSellingProductsAsync(startDate, endDate, 3);
            var payments = singleDay
                ? await _posDbService.GetPaymentBreakdownAsync(reportDate)
                : new List<PaymentBreakdownItem>();
            var zReports = singleDay
                ? await _posDbService.GetZReportsAsync(reportDate)
                : new List<ZReportSummary>();
            decimal noCostRevenuePercent = await _posDbService.GetNoCostRevenuePercentAsync(startDate, endDate);
            decimal marginPercent = revenue > 0 ? profit / revenue * 100 : 0;

            var sb = new StringBuilder();
            sb.AppendLine(singleDay
                ? $"{IconChart} LAPORAN {reportDate:dd/MM/yyyy}"
                : $"{IconChart} LAPORAN {titleLabel.ToUpperInvariant()}");
            sb.AppendLine();
            sb.AppendLine($"  {IconMoney} Omzet    : {FormatCurrency(revenue)}");
            if (isOwner)
            {
                sb.AppendLine($"  {IconProfit} Profit   : {FormatCurrency(profit)} ({marginPercent:0.#}%)");
            }
            sb.AppendLine(transactionCount == 0
                ? $"  {IconReceipt} Transaksi: 0"
                : $"  {IconReceipt} Transaksi: {transactionCount}");

            if (isOwner && noCostRevenuePercent > 0)
            {
                sb.AppendLine($"{IconWarning} Catatan: {noCostRevenuePercent:0.#}% omzet tidak ada data modal.");
            }

            if (singleDay && transactionCount == 0)
            {
                var lastSale = await _posDbService.GetLastSalesSummaryBeforeAsync(reportDate);
                var purchaseActivity = await _posDbService.GetDocumentActivitySummaryAsync(reportDate, 1);
                var inventoryActivity = await _posDbService.GetDocumentActivitySummaryAsync(reportDate, 3);

                sb.AppendLine();
                sb.AppendLine("Info:");
                sb.AppendLine("  Tidak ada transaksi penjualan pada tanggal ini.");
                if (lastSale.LastSaleDate.HasValue)
                {
                    int daysAgo = Math.Max(1, (reportDate - lastSale.LastSaleDate.Value.Date).Days);
                    sb.AppendLine($"  Terakhir ada penjualan: {lastSale.LastSaleDate.Value:dd/MM/yyyy} ({daysAgo} hari sebelumnya) - {FormatCurrency(lastSale.Omzet)}");
                }

                if (purchaseActivity.Count > 0 || inventoryActivity.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"{IconPackage} Aktivitas non-penjualan:");
                    if (purchaseActivity.Count > 0)
                    {
                        sb.AppendLine($"  {IconCheck} {purchaseActivity.Count} dokumen pembelian masuk ({FormatCurrency(purchaseActivity.Total)})");
                    }
                    if (inventoryActivity.Count > 0)
                    {
                        sb.AppendLine($"  {IconCheck} {inventoryActivity.Count} dokumen inventory/koreksi");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Ketik /laporan_periode untuk lihat tanggal lain.");
            }

            if (payments.Any())
            {
                sb.AppendLine();
                sb.AppendLine("\U0001F4B3 Pembayaran:");
                foreach (var payment in payments)
                {
                    sb.AppendLine($"  {FormatOptional(payment.PaymentTypeName).PadRight(13)}: {FormatCurrency(payment.Amount)} ({payment.TransactionCount} trx)");
                }
            }

            if (topProducts.Any())
            {
                sb.AppendLine();
                sb.AppendLine("\U0001F3C6 Produk Terlaris:");
                foreach (var product in topProducts)
                {
                    sb.AppendLine($"  {FormatOptional(product.ProductName)} | {FormatDisplayQuantity(product.QuantitySold)} {GetUnitLabel(product.Unit)} | {FormatCurrency(product.Revenue)}");
                }
            }

            if (zReports.Any())
            {
                sb.AppendLine();
                foreach (var zReport in zReports)
                {
                    sb.AppendLine($"{IconChart} ZReport #{FormatOptional(zReport.Number)} tutup hari ini | {FormatCurrency(zReport.Amount)}");
                }
            }

            if (recentSales.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconClipboard} Transaksi Terakhir:");
                foreach (var item in recentSales.Take(3))
                {
                    sb.AppendLine($"  {FormatShortDate(item.Date)} | {FormatCompactDocumentNumber(item.Id)} | {FormatCurrency(item.Total ?? 0)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandlePeriodReportCommandAsync(string args, AutomationExecutionContext context)
        {
            string period = string.IsNullOrWhiteSpace(args)
                ? "month"
                : TryExtractSalesPeriodArgument(args) ?? TryExtractSalesPeriodArgument($"/laporan_periode {args}") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(period))
            {
                return "Format: /laporan_periode <periode>\nContoh: /laporan_periode bulan lalu\nContoh: /laporan_periode 1 Jan 2026 - 30 Apr 2026";
            }

            return await HandleSalesSummaryAsync(period, context);
        }

        private async Task<string> HandleLegacyExportCommandAsync(string args, AutomationExecutionContext context)
        {
            string normalized = NormalizeText(args);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return BuildExportMenuResponse();
            }

            if (ContainsAny(normalized, "csv transaksi", "transaksi", "penjualan"))
            {
                return await HandleExportSalesAsync(ResolveExportSalesPeriod(TryExtractSalesPeriodArgument(args), context), context);
            }

            if (ContainsAny(normalized, "csv piutang", "piutang", "hutang"))
            {
                return await HandleExportReceivablesAsync(context);
            }

            if (ContainsAny(normalized, "csv pelanggan", "pelanggan"))
            {
                return await HandleExportCustomersAsync(context);
            }

            if (ContainsAny(normalized, "csv supplier", "supplier"))
            {
                return await HandleExportSuppliersAsync(context);
            }

            if (ContainsAny(normalized, "csv produk", "csv stok", "produk", "stok"))
            {
                return await HandleExportStockAsync(context);
            }

            if (ContainsAny(normalized, "lengkap", "semua"))
            {
                return await HandleExportBundleAsync(context);
            }

            return "Format export belum dikenali. Gunakan /export csv transaksi, /export csv piutang, /export csv produk, atau /ekspor_lengkap.";
        }

        private async Task<string> HandleReceivablesCommandAsync(string args, AutomationExecutionContext context)
        {
            string normalized = NormalizeText(args);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return await HandleReceivablesListAsync(context);
            }

            if (ContainsAny(normalized, "ekspor", "export"))
            {
                return await HandleExportReceivablesAsync(context);
            }

            if (IsAllKeyword(normalized))
            {
                return await HandleReceivablesListAsync(context);
            }

            if (ContainsAny(normalized, "total", "jumlah", "summary", "ringkasan"))
            {
                return await HandleTotalReceivableAsync();
            }

            return await HandleReceivableDetailAsync(args, context);
        }

        private async Task<string> HandleProductsCommandAsync(string args, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = args.Trim();
            string normalized = NormalizeText(query);
            string senderKey = BuildSenderStateKey(context);

            if (ContainsAny(normalized, "ekspor", "export"))
            {
                return await HandleExportStockAsync(context);
            }

            if (ContainsAny(normalized, "terlaris", "terlaku", "terbanyak"))
            {
                var topSelling = await _posDbService.GetTopSellingProductsAsync(GetMonthStart(DateTime.Today), DateTime.Today, 10);
                _productPaginationBySender.TryRemove(senderKey, out _);
                SetTopicState(context, "produk", entityName: "produk terlaris");
                return BuildProductRankingResponse("PRODUK TERLARIS BULAN INI", topSelling, rankByProfit: false);
            }

            if (ContainsAny(normalized, "profit", "margin", "untung"))
            {
                var topProfit = (await _posDbService.GetTopSellingProductsAsync(GetMonthStart(DateTime.Today), DateTime.Today, 25))
                    .OrderByDescending(item => item.Profit)
                    .ThenByDescending(item => item.Revenue)
                    .Take(10)
                    .ToList();
                _productPaginationBySender.TryRemove(senderKey, out _);
                SetTopicState(context, "produk", entityName: "produk profit");
                return BuildProductRankingResponse("PRODUK PROFIT TERTINGGI BULAN INI", topProfit, rankByProfit: true);
            }

            var allProducts = (await _posDbService.GetAllProductsAsync())
                .Where(product => product.IsActive)
                .OrderBy(product => product.Name)
                .ToList();

            if (!allProducts.Any())
            {
                return "Tidak ada produk aktif di database.";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return BuildProductPageResponse(
                    context,
                    allProducts,
                    mode: "all",
                    query: null,
                    title: $"{IconPackage} PRODUK ({allProducts.Count} total)",
                    intro: "Menampilkan 10 produk pertama. Ketik LANJUT PRODUK untuk halaman berikutnya.");
            }

            string? categoryQuery = ExtractKeywordAfterAny(query, "kategori");
            if (!string.IsNullOrWhiteSpace(categoryQuery))
            {
                var categoryProducts = allProducts
                    .Where(product => !string.IsNullOrWhiteSpace(product.Category) &&
                                      product.Category.Contains(categoryQuery, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!categoryProducts.Any())
                {
                    return $"Kategori produk \"{categoryQuery}\" tidak ditemukan.";
                }

                return BuildProductPageResponse(
                    context,
                    categoryProducts,
                    mode: "category",
                    query: categoryQuery,
                    title: $"{IconPackage} PRODUK KATEGORI - {categoryQuery}",
                    intro: $"Ditemukan {categoryProducts.Count} produk.");
            }

            var matches = await FindProductsAsync(query, 10);
            _productPaginationBySender.TryRemove(senderKey, out _);
            if (!matches.Any())
            {
                return $"Produk dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} HASIL PRODUK - \"{query}\"");
            sb.AppendLine();
            foreach (var product in matches)
            {
                sb.AppendLine(BuildProductListLine(product, includeCategory: true, includeCost: true));
            }

            sb.AppendLine();
            sb.Append("Ketik EKSPOR PRODUK untuk file CSV lengkap.");
            SetTopicState(context, "produk", entityName: query);
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleProductPaginationAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_productPaginationBySender.TryGetValue(senderKey, out var state))
            {
                return "Tidak ada halaman produk lanjutan. Ketik /produk untuk mulai lagi.";
            }

            var allProducts = (await _posDbService.GetAllProductsAsync())
                .Where(product => product.IsActive)
                .OrderBy(product => product.Name)
                .ToList();

            List<Product> source = state.Mode switch
            {
                "category" => allProducts
                    .Where(product => !string.IsNullOrWhiteSpace(product.Category) &&
                                      product.Category.Contains(state.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                "category_keyword" => await _posDbService.GetProductCategoryGroupAsync(state.Query ?? string.Empty, 100, categoryOnly: true),
                _ => allProducts
            };

            var pageItems = source.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _productPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada halaman produk berikutnya.";
            }

            int startNumber = state.NextOffset + 1;
            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= source.Count)
            {
                _productPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _productPaginationBySender[senderKey] = state;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} PRODUK - LANJUTAN");
            sb.AppendLine();
            for (int i = 0; i < pageItems.Count; i++)
            {
                sb.AppendLine($"{startNumber + i}. {BuildProductListLine(pageItems[i], includeCategory: true, includeCost: true)}");
            }

            if (state.NextOffset < source.Count)
            {
                sb.AppendLine();
                sb.Append("Ketik LANJUT PRODUK untuk halaman berikutnya.");
            }

            SetTopicState(context, "produk", entityName: state.Query ?? state.Mode, currentPage: (startNumber - 1) / state.PageSize + 1);
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleStatisticsAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            DateTime today = DateTime.Today;
            DateTime monthStart = GetMonthStart(today);
            DateTime previousMonthStart = monthStart.AddMonths(-1);
            DateTime previousMonthEnd = monthStart.AddDays(-1);
            int elapsedDays = Math.Max(1, (today - monthStart).Days + 1);

            decimal revenueMtd = await _posDbService.GetSalesRevenueAsync(monthStart, today);
            decimal profitMtd = await _posDbService.GetSalesProfitAsync(monthStart, today);
            int transactionMtd = await _posDbService.GetSalesTransactionCountAsync(monthStart, today);
            decimal revenueLastMonth = await _posDbService.GetSalesRevenueAsync(previousMonthStart, previousMonthEnd);
            decimal growthPct = revenueLastMonth == 0
                ? (revenueMtd > 0 ? 100 : 0)
                : ((revenueMtd - revenueLastMonth) / revenueLastMonth) * 100;

            decimal avgTransactionsPerDay = transactionMtd / (decimal)elapsedDays;
            int newCustomers = await _posDbService.GetNewCustomerCountAsync(monthStart, today);
            var topSelling = await _posDbService.GetTopSellingProductsAsync(monthStart, today, 5);
            var topProfit = topSelling
                .OrderByDescending(item => item.Profit)
                .ThenByDescending(item => item.Revenue)
                .Take(5)
                .ToList();
            string anomalyNote = await BuildAnomalyInsightAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"{IconChart} STATISTIK BISNIS");
            sb.AppendLine();
            sb.AppendLine($"Periode: {FormatDateRangeLabel(monthStart, today)}");
            sb.AppendLine($"Omzet MTD: {FormatCurrency(revenueMtd)}");
            sb.AppendLine($"Profit MTD: {FormatCurrency(profitMtd)}");
            sb.AppendLine($"Omzet bulan lalu: {FormatCurrency(revenueLastMonth)}");
            sb.AppendLine($"Growth omzet: {growthPct:+0.##;-0.##;0}%");
            sb.AppendLine($"Rata-rata transaksi/hari: {avgTransactionsPerDay:0.##}");
            sb.AppendLine($"Pelanggan baru bulan ini: {newCustomers}");

            if (topSelling.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Top 5 produk terlaris:");
                for (int i = 0; i < topSelling.Count; i++)
                {
                    var item = topSelling[i];
                    sb.AppendLine($"{i + 1}. {FormatOptional(item.ProductName)} | {FormatDisplayQuantity(item.QuantitySold)} {GetUnitLabel(item.Unit)} | {FormatCurrency(item.Revenue)}");
                }
            }

            if (topProfit.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Top 5 produk profit tertinggi:");
                for (int i = 0; i < topProfit.Count; i++)
                {
                    var item = topProfit[i];
                    sb.AppendLine($"{i + 1}. {FormatOptional(item.ProductName)} | profit {FormatCurrency(item.Profit)}");
                }
            }

            if (!string.IsNullOrWhiteSpace(anomalyNote))
            {
                sb.AppendLine();
                sb.AppendLine(anomalyNote);
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleCustomersAsync(string args, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = args.Trim();
            string senderKey = BuildSenderStateKey(context);
            string normalizedQuery = NormalizeText(query);

            if (normalizedQuery is "at risk" or "at_risk" or "atrisk" or "perlu perhatian")
            {
                return await HandleAtRiskCustomersAsync(context);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                int total = await _posDbService.GetTotalCustomersAsync();
                if (total <= 0)
                {
                    return "Tidak ada pelanggan aktif.";
                }

                var customers = (await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true))
                    .OrderByDescending(customer => customer.PurchaseCount)
                    .ThenByDescending(customer => customer.TotalSpent)
                    .ThenBy(customer => customer.Name)
                    .ToList();

                int pageSize = total <= 20 ? total : 15;
                var pageItems = customers.Take(pageSize).ToList();
                if (total > pageItems.Count)
                {
                    _customerPaginationBySender[senderKey] = new ListPageState
                    {
                        NextOffset = pageItems.Count,
                        PageSize = 15
                    };
                }
                else
                {
                    _customerPaginationBySender.TryRemove(senderKey, out _);
                }

                SetPendingExport(context, "customers");
                SetTopicState(context, "pelanggan", entityName: "daftar pelanggan", currentPage: 1);

                var summary = new StringBuilder();
                summary.AppendLine($"{IconCustomer} PELANGGAN ({total} total)");
                summary.AppendLine(total <= 20
                    ? "Menampilkan semua pelanggan aktif."
                    : "Menampilkan 15 teratas berdasarkan jumlah transaksi.");
                summary.AppendLine();

                for (int i = 0; i < pageItems.Count; i++)
                {
                    summary.AppendLine(BuildCustomerListLine(pageItems[i], i + 1));
                }

                summary.AppendLine();
                summary.AppendLine($"{IconDocument} Ketik EKSPOR PELANGGAN untuk file CSV lengkap.");
                if (total > 20)
                {
                    summary.Append("Ketik LANJUT PELANGGAN untuk halaman berikutnya.");
                }

                return summary.ToString().TrimEnd();
            }

            _customerPaginationBySender.TryRemove(senderKey, out _);
            var matches = await _posDbService.GetCustomersAsync(query, 5, onlyCustomers: true);
            if (!matches.Any())
            {
                return $"Pelanggan dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var exactMatch = matches.FirstOrDefault(customer =>
                string.Equals(NormalizeText(customer.Name ?? string.Empty), NormalizeText(query), StringComparison.Ordinal));
            if (exactMatch != null || matches.Count == 1)
            {
                return await BuildCustomerDetailResponseAsync(exactMatch ?? matches[0], context);
            }

            SetTopicState(context, "pelanggan", entityName: query);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconCustomer} PELANGGAN - \"{query}\"");
            sb.AppendLine();
            for (int i = 0; i < matches.Count; i++)
            {
                sb.AppendLine(BuildCustomerListLine(matches[i], i + 1));
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<List<CustomerInfo>> GetPersonalCustomersAsync()
        {
            if (_posDbService == null)
            {
                return new List<CustomerInfo>();
            }

            return (await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true))
                .Where(customer => !IsGenericCustomerName(customer.Name))
                .ToList();
        }

        private async Task<List<CustomerInfo>> GetLoyalCustomerSourceAsync()
        {
            return (await GetPersonalCustomersAsync())
                .Where(customer => customer.PurchaseCount >= 8 || customer.TotalSpent >= 5_000_000m)
                .OrderByDescending(customer => customer.PurchaseCount)
                .ThenByDescending(customer => customer.TotalSpent)
                .ThenBy(customer => customer.Name)
                .ToList();
        }

        private async Task<List<CustomerInfo>> GetAtRiskCustomerSourceAsync()
        {
            return (await GetLoyalCustomerSourceAsync())
                .Where(customer => GetDaysSince(customer.LastPurchaseDate) > 30)
                .OrderByDescending(customer => GetDaysSince(customer.LastPurchaseDate))
                .ThenByDescending(customer => customer.TotalSpent)
                .ThenBy(customer => customer.Name)
                .ToList();
        }

        private async Task<string> HandleLoyalCustomersAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var loyalCustomers = await GetLoyalCustomerSourceAsync();
            if (!loyalCustomers.Any())
            {
                return "Belum ada data pelanggan loyal.";
            }

            var routine = loyalCustomers.Take(5).ToList();
            var biggest = loyalCustomers
                .OrderByDescending(customer => customer.TotalSpent)
                .ThenByDescending(customer => customer.PurchaseCount)
                .Take(5)
                .ToList();
            var atRisk = loyalCustomers
                .Where(customer => GetDaysSince(customer.LastPurchaseDate) > 30)
                .OrderByDescending(customer => GetDaysSince(customer.LastPurchaseDate))
                .Take(5)
                .ToList();

            string senderKey = BuildSenderStateKey(context);
            if (loyalCustomers.Count > 5)
            {
                _customerPaginationBySender[senderKey] = new ListPageState
                {
                    NextOffset = 5,
                    PageSize = 5
                };
            }
            else
            {
                _customerPaginationBySender.TryRemove(senderKey, out _);
            }

            SetPendingExport(context, "customers_loyal");
            SetTopicState(
                context,
                "pelanggan_loyal",
                entityName: "pelanggan loyal",
                currentPage: 1,
                pageSize: 5,
                exportType: "pelanggan_loyal.csv",
                lastData: loyalCustomers);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconCustomer} PELANGGAN LOYAL");
            sb.AppendLine();
            sb.AppendLine($"{IconPackage} PALING RUTIN BELANJA");
            AppendCustomerRankRows(sb, routine, (customer) => $"{customer.PurchaseCount} trx | {FormatCurrency(customer.TotalSpent)}");

            sb.AppendLine();
            sb.AppendLine($"{IconMoney} TOTAL BELANJA TERBESAR");
            AppendCustomerRankRows(sb, biggest, (customer) => $"{FormatCurrency(customer.TotalSpent)} | {customer.PurchaseCount} trx");

            if (atRisk.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconWarning} LOYAL TAPI MULAI JARANG BELANJA");
                foreach (var customer in atRisk)
                {
                    sb.AppendLine($"- {FormatOptional(customer.Name)} | terakhir {GetDaysSince(customer.LastPurchaseDate)} hari lalu");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"{IconChart} Insight:");
            sb.AppendLine($"- {FormatOptional(routine.FirstOrDefault()?.Name)} paling rutin. {FormatOptional(biggest.FirstOrDefault()?.Name)} total terbesar.");
            if (atRisk.Any())
            {
                var mostRisk = atRisk.First();
                sb.AppendLine($"- {FormatOptional(mostRisk.Name)} sudah {GetDaysSince(mostRisk.LastPurchaseDate)} hari tidak belanja.");
            }

            sb.AppendLine();
            sb.AppendLine("\u2139\uFE0F \"Umum\" dan \"Walk-in customer\" tidak dihitung.");
            sb.AppendLine();
            sb.AppendLine("\U0001F4A1 Ketik:");
            sb.AppendLine($"- /pelanggan {FormatOptional(routine.FirstOrDefault()?.Name)}");
            sb.AppendLine("- /pelanggan at_risk");
            if (loyalCustomers.Count > 5)
            {
                sb.AppendLine("- LANJUT PELANGGAN");
            }
            sb.Append("- EKSPOR PELANGGAN");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleAtRiskCustomersAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var atRisk = await GetAtRiskCustomerSourceAsync();
            var receivables = (await _posDbService.GetCustomerReceivablesAsync()).Take(5).ToList();

            if (!atRisk.Any() && !receivables.Any())
            {
                return "Belum ada pelanggan loyal yang mulai jarang belanja atau piutang aktif.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (atRisk.Count > 5)
            {
                _customerPaginationBySender[senderKey] = new ListPageState
                {
                    NextOffset = 5,
                    PageSize = 5
                };
            }
            else
            {
                _customerPaginationBySender.TryRemove(senderKey, out _);
            }

            SetPendingExport(context, "customers_at_risk");
            SetTopicState(
                context,
                "pelanggan_at_risk",
                entityName: "pelanggan perlu perhatian",
                currentPage: 1,
                pageSize: 5,
                exportType: "pelanggan_at_risk.csv",
                lastData: atRisk);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} PELANGGAN PERLU PERHATIAN");

            if (atRisk.Any())
            {
                sb.AppendLine();
                sb.AppendLine("\U0001F552 LOYAL MULAI JARANG BELANJA");
                foreach (var customer in atRisk.Take(5))
                {
                    int days = GetDaysSince(customer.LastPurchaseDate);
                    sb.AppendLine($"- {FormatOptional(customer.Name)} | {customer.PurchaseCount} trx | terakhir {days} hari lalu {GetAtRiskIcon(days)}");
                }
            }

            if (receivables.Any())
            {
                sb.AppendLine();
                sb.AppendLine("\U0001F4B3 PIUTANG PERLU FOLLOW-UP");
                foreach (var receivable in receivables)
                {
                    int overdue = receivable.OldestDueDate.HasValue && receivable.OldestDueDate.Value.Date < DateTime.Today ? 1 : 0;
                    string overdueLabel = overdue > 0 ? " lewat JT" : string.Empty;
                    sb.AppendLine($"- {receivable.CustomerName} | {FormatCurrency(receivable.TotalOwed)} | {receivable.InvoiceCount} faktur{overdueLabel}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"{IconChart} Insight:");
            if (atRisk.Any())
            {
                sb.AppendLine($"- {FormatOptional(atRisk.First().Name)} paling lama tidak belanja.");
            }
            if (receivables.Any())
            {
                sb.AppendLine($"- {receivables.First().CustomerName} piutang terbesar.");
            }

            sb.AppendLine();
            sb.AppendLine("\U0001F4A1 Ketik:");
            if (atRisk.Any())
            {
                sb.AppendLine($"- /pelanggan {FormatOptional(atRisk.First().Name)}");
            }
            if (receivables.Any())
            {
                sb.AppendLine($"- /piutang {receivables.First().CustomerName}");
            }
            sb.AppendLine("- EKSPOR PELANGGAN");
            sb.Append("- EKSPOR AT_RISK");
            return sb.ToString().TrimEnd();
        }

        private static void AppendCustomerRankRows(StringBuilder sb, IReadOnlyList<CustomerInfo> customers, Func<CustomerInfo, string> detailBuilder)
        {
            for (int i = 0; i < customers.Count; i++)
            {
                string medal = i switch
                {
                    0 => "\U0001F947",
                    1 => "\U0001F948",
                    2 => "\U0001F949",
                    _ => $"{i + 1}."
                };

                sb.AppendLine($"{medal} {FormatOptional(customers[i].Name)} | {detailBuilder(customers[i])}");
            }
        }

        private async Task<string> HandleSuppliersAsync(string args, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = args.Trim();
            string senderKey = BuildSenderStateKey(context);

            if (string.IsNullOrWhiteSpace(query))
            {
                int total = await _posDbService.GetTotalSuppliersAsync();
                if (total <= 0)
                {
                    return "Tidak ada supplier aktif.";
                }

                var suppliers = await _posDbService.GetSuppliersAsync(null, null);
                int pageSize = total <= 15 ? total : 15;
                var pageItems = suppliers.Take(pageSize).ToList();
                if (total > pageItems.Count)
                {
                    _supplierPaginationBySender[senderKey] = new ListPageState
                    {
                        NextOffset = pageItems.Count,
                        PageSize = 15
                    };
                }
                else
                {
                    _supplierPaginationBySender.TryRemove(senderKey, out _);
                }

                SetPendingExport(context, "suppliers");
                SetTopicState(context, "supplier", entityName: "daftar supplier", currentPage: 1);

                var summary = new StringBuilder();
                summary.AppendLine($"\U0001F3ED SUPPLIER ({total} total)");
                summary.AppendLine(total <= 15
                    ? "Menampilkan semua supplier aktif."
                    : "Menampilkan 15 supplier pertama.");
                summary.AppendLine();

                for (int i = 0; i < pageItems.Count; i++)
                {
                    summary.AppendLine(BuildSupplierListLine(pageItems[i], i + 1));
                }

                summary.AppendLine();
                summary.AppendLine($"{IconDocument} Ketik EKSPOR SUPPLIER untuk file CSV lengkap.");
                if (total > 15)
                {
                    summary.Append("Ketik LANJUT SUPPLIER untuk halaman berikutnya.");
                }

                return summary.ToString().TrimEnd();
            }

            _supplierPaginationBySender.TryRemove(senderKey, out _);
            var matches = await _posDbService.GetSuppliersAsync(query, 5);
            if (!matches.Any())
            {
                return $"Supplier dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var exactMatch = matches.FirstOrDefault(supplier =>
                string.Equals(NormalizeText(supplier.Name ?? string.Empty), NormalizeText(query), StringComparison.Ordinal));
            if (exactMatch != null || matches.Count == 1)
            {
                SetTopicState(context, "supplier", entityId: (exactMatch ?? matches[0]).Id, entityName: (exactMatch ?? matches[0]).Name);
                return BuildSupplierDetailResponse(exactMatch ?? matches[0], query);
            }

            SetTopicState(context, "supplier", entityName: query);

            var sb = new StringBuilder();
            sb.AppendLine($"\U0001F3ED SUPPLIER - \"{query}\"");
            sb.AppendLine();
            for (int i = 0; i < matches.Count; i++)
            {
                sb.AppendLine(BuildSupplierListLine(matches[i], i + 1));
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleUsersAsync(string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = args.Trim();
            var users = await _posDbService.GetUsersAsync(string.IsNullOrWhiteSpace(query) ? null : query, 10);
            if (!users.Any())
            {
                return string.IsNullOrWhiteSpace(query)
                    ? "Tidak ada user aktif."
                    : $"User dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(query)
                ? $"{IconUser} USER"
                : $"{IconUser} USER - \"{query}\"");

            bool first = true;
            foreach (var user in users.Take(10))
            {
                string fullName = string.IsNullOrWhiteSpace(user.FullName) ? "-" : user.FullName!;
                string username = string.IsNullOrWhiteSpace(user.Username) ? "-" : user.Username!;
                string activeLabel = user.IsActive ? $"{IconCheck} Aktif" : $"{IconCross} Nonaktif";

                if (first)
                {
                    sb.AppendLine();
                    first = false;
                }
                else
                {
                    sb.AppendLine();
                }

                sb.AppendLine($"  {fullName} (Lv.{user.RoleId}) {activeLabel}");
                sb.AppendLine($"  Username: {username}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleProductSalesAsync(string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = args.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return "Format: /penjualan <produk>";
            }

            var (product, error) = await TryResolveProductAsync(query, isMutation: false, actionLabel: "lihat data penjualan");
            if (product == null || !string.IsNullOrWhiteSpace(error))
            {
                return error ?? $"Produk \"{query}\" tidak ditemukan.";
            }

            var summary = await _posDbService.GetProductSalesSummaryAsync(product.Id);
            if (summary == null || summary.QuantitySold <= 0)
            {
                return $"Belum ada data penjualan untuk {product.Name}.";
            }

            var transactions = await _posDbService.GetProductSalesTransactionsAsync(product.Id, 5);
            var sb = new StringBuilder();
            sb.AppendLine($"{IconProfit} PENJUALAN - {product.Name}");
            sb.AppendLine();
            sb.AppendLine($"  {IconPackage} Qty terjual : {FormatStockValue(summary.QuantitySold)} {GetUnitLabel(product.Unit)}");
            sb.AppendLine($"  {IconMoney} Revenue     : {FormatCurrency(summary.Revenue)}");
            sb.AppendLine($"  {IconChart} Profit      : {FormatCurrency(summary.Profit)}");
            sb.AppendLine($"  {IconCalendar} Terakhir    : {FormatDateTime(summary.LastSaleDate)}");

            if (transactions.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Transaksi terakhir:");
                foreach (var item in transactions)
                {
                    string counterparty = !string.IsNullOrWhiteSpace(item.CustomerName)
                        ? ShortenCounterparty(item.CustomerName!)
                        : FormatOptional(item.UserName);
                    sb.AppendLine($"  {FormatShortDate(item.Date)} | {FormatCompactDocumentNumber(item.DocumentNumber)} | {FormatStockValue(item.Quantity)} {GetUnitLabel(product.Unit)} | {FormatCurrency(item.Total)} | {counterparty}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleDocumentLookupAsync(string args, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string documentNumber = ExtractDocumentNumber(args) ?? args.Trim();
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                return "Format: /dokumen <nomor>";
            }

            var document = await _posDbService.GetDocumentByNumberAsync(documentNumber);
            if (document == null || string.IsNullOrWhiteSpace(document.Id))
            {
                return $"Dokumen \"{documentNumber}\" tidak ditemukan.";
            }

            var items = await _posDbService.GetDocumentItemsAsync(document.Id);
            string senderKey = BuildSenderStateKey(context);
            _lastDocumentBySender[senderKey] = document.Number ?? documentNumber;
            SetTopicState(context, "dokumen", entityId: document.Id, entityName: document.Number ?? documentNumber, currentPage: 1);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconDocument} DOKUMEN {document.Number}");
            sb.AppendLine();
            sb.AppendLine($"  {IconTag} Tipe     : {FormatOptional(document.DocumentTypeLabel)}");
            sb.AppendLine($"  {IconCalendar} Tanggal  : {FormatDateTime(document.Date)}");
            sb.AppendLine($"  {IconUser} Kasir    : {FormatOptional(document.UserName)}");
            sb.AppendLine($"  {IconCustomer} Customer : {ShortenCounterparty(FormatOptional(document.CustomerName))}");
            sb.AppendLine($"  {IconMoney} Total    : {FormatCurrency(document.Total)}");

            if (items.Any())
            {
                const int pageSize = 10;
                var firstPage = items.Take(pageSize).ToList();
                sb.AppendLine();
                sb.AppendLine("Item:");
                AppendAlignedRows(
                    sb,
                    firstPage.Select(item => (
                        Name: FormatOptional(item.ProductName),
                        Col2: $"{FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)}",
                        Col3: $"@ {FormatCurrency(item.Price)}",
                        Col4: $"= {FormatCurrency(item.Total)}")));

                if (items.Count > firstPage.Count)
                {
                    _documentPaginationBySender[senderKey] = new DocumentPageState
                    {
                        DocumentId = document.Id,
                        DocumentNumber = document.Number ?? documentNumber,
                        NextOffset = firstPage.Count,
                        PageSize = pageSize
                    };

                    sb.AppendLine();
                    sb.Append($"Masih ada {items.Count - firstPage.Count} item lagi. Ketik LANJUT DOKUMEN untuk halaman berikutnya.");
                }
                else
                {
                    _documentPaginationBySender.TryRemove(senderKey, out _);
                }
            }
            else
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> QueueRestockAsync(InboundMessage message, string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (args.Contains(','))
            {
                return await QueueBulkRestockAsync(message, args);
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                return "Format: /restock <produk> <qty> [harga_modal]";
            }

            var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                return "Format: /restock <produk> <qty> [harga_modal]";
            }

            decimal qty;
            decimal? price = null;
            int qtyIndex;

            if (tokens.Length >= 3 &&
                TryParseDecimal(tokens[^1], out var parsedPrice) &&
                TryParseDecimal(tokens[^2], out var parsedQty))
            {
                price = parsedPrice;
                qty = parsedQty;
                qtyIndex = tokens.Length - 2;
            }
            else if (TryParseDecimal(tokens[^1], out parsedQty))
            {
                qty = parsedQty;
                qtyIndex = tokens.Length - 1;
            }
            else
            {
                return "Quantity restock harus berupa angka.";
            }

            if (qty <= 0)
            {
                return "Quantity restock harus lebih dari 0.";
            }

            string productQuery = string.Join(" ", tokens.Take(qtyIndex));
            if (string.IsNullOrWhiteSpace(productQuery))
            {
                return "Nama produk tidak boleh kosong.";
            }

            var (product, error) = await TryResolveProductAsync(productQuery, isMutation: true, actionLabel: "restock");
            if (product == null || !string.IsNullOrWhiteSpace(error))
            {
                return error ?? $"Produk \"{productQuery}\" tidak ditemukan.";
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "restock",
                ProductId = product.Id,
                ProductName = product.Name ?? productQuery,
                Quantity = qty,
                Price = price ?? product.PurchasePrice ?? 0,
                CorrelationId = message.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            return BuildRestockConfirmationMessage(
                product.Name ?? productQuery,
                product.Unit,
                qty,
                price ?? product.PurchasePrice ?? 0);
        }

        private async Task<string> QueueInventoryAsync(InboundMessage message, string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (args.Contains(','))
            {
                return await QueueBulkInventoryAsync(message, args);
            }

            var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                return "Format: /inventory <produk> <stok_target>";
            }

            if (!TryParseDecimal(tokens[^1], out var targetStock))
            {
                return "Stok target harus berupa angka.";
            }

            if (targetStock < 0)
            {
                return "❌ Target stok tidak boleh negatif. Gunakan 0 jika ingin mengosongkan stok.";
            }

            string productQuery = string.Join(" ", tokens.Take(tokens.Length - 1));
            var (product, error) = await TryResolveProductAsync(productQuery, isMutation: true, actionLabel: "inventory");
            if (product == null || !string.IsNullOrWhiteSpace(error))
            {
                return error ?? $"Produk \"{productQuery}\" tidak ditemukan.";
            }

            // Stock.Quantity adalah sumber kebenaran — sama dengan yang Aronium tampilkan
            // JANGAN pakai SUM(DocumentItem) karena itu histori, bukan snapshot real-time
            decimal currentStock = product.Stock ?? 0;
            int.TryParse(product.Id, out var productId);
            if (targetStock == currentStock)
            {
                return $"ℹ️ Stok {product.Name} sudah {FormatStockValue(currentStock)} {GetUnitLabel(product.Unit)}. Tidak ada perubahan yang perlu diproses.";
            }

            decimal adjustment = targetStock - currentStock;
            string key = GetConfirmationKey(message.Channel, message.SenderId);

            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "inventory",
                ProductId = product.Id,
                ProductName = product.Name ?? productQuery,
                Quantity = targetStock,
                CorrelationId = message.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            return BuildInventoryConfirmationMessage(product.Name ?? productQuery, product.Unit, currentStock, targetStock, adjustment);
        }

        private async Task<string> QueueInventoryFamilyAsync(InboundMessage message, string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var match = Regex.Match(args.Trim(), @"^(?<name>.+?)\s+(?<qty>-?\d+(?:[.,]\d+)?)\s*(?<unit>[A-Za-z]+)?$", RegexOptions.IgnoreCase);
            if (!match.Success || !TryParseDecimal(match.Groups["qty"].Value, out decimal targetQuantity) || targetQuantity < 0)
            {
                return "Format: /inventory_family <nama produk keluarga> <stok_target> <unit>\nContoh: /inventory_family kapal api mix 22 rcg";
            }

            string productQuery = match.Groups["name"].Value.Trim();
            string requestedUnit = match.Groups["unit"].Success ? match.Groups["unit"].Value.Trim() : string.Empty;
            var (product, error) = await TryResolveProductAsync(productQuery, isMutation: true, actionLabel: "inventory family");
            if (product == null || !string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(product.Id))
            {
                return error ?? $"Produk \"{productQuery}\" tidak ditemukan.";
            }

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            var mapping = FindMappingForProduct(mappings, product.Id);
            if (mapping == null || mapping.ConversionRate <= 0)
            {
                return $"Produk \"{product.Name}\" belum punya mapping dual stok. Buat dulu dengan /set_family.";
            }

            var parent = await _posDbService.GetProductByIdAsync(mapping.ParentProductId);
            var child = await _posDbService.GetProductByIdAsync(mapping.ChildProductId);
            if (parent == null || child == null)
            {
                return "Produk parent/child pada mapping tidak ditemukan di pos.db.";
            }

            bool unitAsParent = IsRequestedFamilyParentUnit(requestedUnit, parent.Unit) ||
                                (string.IsNullOrWhiteSpace(requestedUnit) &&
                                 string.Equals(product.Id, mapping.ParentProductId, StringComparison.OrdinalIgnoreCase));
            decimal targetTotalChild = unitAsParent
                ? targetQuantity * mapping.ConversionRate
                : targetQuantity;
            decimal targetParent = decimal.Floor(targetTotalChild / mapping.ConversionRate);
            decimal targetChild = targetTotalChild - targetParent * mapping.ConversionRate;

            var items = new List<BulkPendingItem>
            {
                new()
                {
                    ProductId = mapping.ParentProductId,
                    ProductName = parent.Name ?? mapping.ParentProductName ?? mapping.ParentProductId,
                    Quantity = targetParent,
                    CurrentStock = parent.Stock,
                    Unit = parent.Unit
                },
                new()
                {
                    ProductId = mapping.ChildProductId,
                    ProductName = child.Name ?? mapping.ChildProductName ?? mapping.ChildProductId,
                    Quantity = targetChild,
                    CurrentStock = child.Stock,
                    Unit = child.Unit
                }
            };

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "bulk_inventory",
                ProductId = SerializeBulkItems(items),
                ProductName = $"Inventory family {mapping.FamilyName ?? parent.Name}",
                Quantity = targetTotalChild,
                CorrelationId = message.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            string confirmation = BuildBulkInventoryConfirmationMessage(items, new List<string>(), new List<string>());
            string targetSummary = $"Target keluarga: {FormatStockValue(targetTotalChild)} {GetUnitLabel(child.Unit)} efektif " +
                                   $"= {FormatStockValue(targetParent)} {GetUnitLabel(parent.Unit)} + {FormatStockValue(targetChild)} {GetUnitLabel(child.Unit)}";
            return confirmation.Replace(BuildConfirmationActions(), targetSummary + Environment.NewLine + Environment.NewLine + BuildConfirmationActions());
        }

        private static bool IsRequestedFamilyParentUnit(string requestedUnit, string? parentUnit)
        {
            if (string.IsNullOrWhiteSpace(requestedUnit))
            {
                return false;
            }

            string normalized = NormalizeReceiptUnit(requestedUnit);
            return string.Equals(normalized, NormalizeReceiptUnit(parentUnit), StringComparison.OrdinalIgnoreCase) ||
                   IsBulkReceiptUnit(normalized);
        }

        private async Task<string> ConfirmPendingActionAsync(InboundMessage message, AutomationExecutionContext? context = null)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (context != null)
            {
                string? shadowConfirm = await TryConfirmPendingShadowMappingAsync(context);
                if (!string.IsNullOrWhiteSpace(shadowConfirm))
                {
                    return shadowConfirm;
                }
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            var pending = await _databaseService.GetPendingConfirmationAsync(key);
            if (pending == null)
            {
                return "Tidak ada aksi yang menunggu konfirmasi.";
            }

            await _databaseService.DeletePendingConfirmationAsync(key);

            if (pending.Command == "price_override_confirmation")
            {
                await _databaseService.SavePendingConfirmationAsync(pending);
                return "Harga beli/jual menunggu keputusan. Gunakan /simpan, /simpan_jual, /jual <nomor_item> <nominal>, /lewati_harga, atau /batal.";
            }

            if (pending.Command == "bulk_restock")
            {
                return await ExecuteBulkRestockAsync(message, pending);
            }

            if (pending.Command == "ocr_bulk_restock")
            {
                return await ExecuteOcrBulkRestockAsync(message, pending);
            }

            if (pending.Command == "bulk_inventory")
            {
                return await ExecuteBulkInventoryAsync(message, pending);
            }

            if (!int.TryParse(pending.ProductId, out var productId))
            {
                return "Produk tidak valid untuk dieksekusi.";
            }

            if (pending.Command == "restock")
            {
                return await ExecuteSingleRestockAsync(message, pending);
            }

            var product = await _posDbService.GetProductByIdAsync(pending.ProductId);
            decimal targetStock = pending.Quantity;
            var inventoryResult = await _posDbService.CreateInventoryCountDocumentAsync(productId, targetStock, 1);

            if (!inventoryResult.Success)
            {
                return $"Inventory gagal: {inventoryResult.Error}";
            }

            decimal currentStock = inventoryResult.OldStock;
            decimal adjustment = inventoryResult.NewStock - currentStock;

            await _databaseService.AddInventoryLogAsync(new InventoryLog
            {
                ProductId = pending.ProductId,
                ProductName = pending.ProductName,
                OldStock = currentStock,
                NewStock = inventoryResult.NewStock,
                Adjustment = adjustment,
                Reason = "Confirmed via automation engine",
                UserId = message.SenderId,
                Channel = message.Channel.ToString(),
                Timestamp = DateTime.Now
            });

            return BuildInventorySuccessMessage(
                inventoryResult.DocumentNumber,
                pending.ProductName,
                product?.Unit,
                currentStock,
                inventoryResult.NewStock,
                adjustment);
        }

        private async Task<string> ExecuteSingleRestockAsync(
            InboundMessage message,
            PendingConfirmation pending,
            PriceOverridePendingPayload? pricePayload = null,
            PriceOverrideDecision? decision = null)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (!int.TryParse(pending.ProductId, out var productId))
            {
                return "Produk tidak valid untuk dieksekusi.";
            }

            pricePayload ??= await BuildPriceOverridePayloadAsync(pending, new[]
            {
                new BulkPendingItem
                {
                    ProductId = pending.ProductId,
                    ProductName = pending.ProductName,
                    Quantity = pending.Quantity,
                    Price = pending.Price ?? 0
                }
            }, includeShadow: false);

            if (decision == null && ShouldPromptPriceOverride(pricePayload))
            {
                return await QueuePriceOverrideConfirmationAsync(message, pending, pricePayload);
            }

            var restockProduct = await _posDbService.GetProductByIdAsync(pending.ProductId);
            var result = await _posDbService.CreatePurchaseDocumentAsync(
                productId,
                pending.Quantity,
                pending.Price ?? 0,
                1,
                $"Confirmed via {message.Channel} by {message.SenderId}");

            if (!result.Success)
            {
                return $"Restock gagal: {result.Error}";
            }

            await ApplyMasterPriceOverridesAsync(pricePayload, decision);

            var messageText = new StringBuilder();
            messageText.Append(BuildRestockSuccessMessage(
                result.DocumentNumber,
                pending.ProductName,
                restockProduct?.Unit,
                pending.Quantity,
                result.Total));

            AppendPriceOverrideSummary(messageText, pricePayload, decision);
            await AppendUnpromptedPurchaseCostNotesAsync(messageText, new[]
            {
                new BulkPendingItem
                {
                    ProductId = pending.ProductId,
                    ProductName = pending.ProductName,
                    Quantity = pending.Quantity,
                    Price = pending.Price ?? 0,
                    Unit = restockProduct?.Unit
                }
            }, pricePayload, decision);
            return messageText.ToString();
        }

        private async Task<string> ConfirmPriceOverrideAsync(InboundMessage message, string args, bool updateCost, bool updateSellingPrice)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            CleanupCompletedPriceOverrideConfirmations();
            if (_priceOverrideProcessingByKey.ContainsKey(key))
            {
                return "Konfirmasi harga sedang diproses. Tunggu hasil dokumen sebelumnya.";
            }

            var pending = await _databaseService.GetPendingConfirmationAsync(key);
            if (pending == null || pending.Command != "price_override_confirmation")
            {
                return "Tidak ada perubahan harga yang menunggu konfirmasi.";
            }

            PriceOverridePendingPayload payload = DeserializePriceOverridePayload(pending.ProductId);
            if (string.IsNullOrWhiteSpace(payload.OriginalCommand))
            {
                return "Data konfirmasi harga tidak valid.";
            }

            if (updateSellingPrice && !string.IsNullOrWhiteSpace(args))
            {
                string? parseError = ApplyManualSellingPricesFromConfirmArgs(payload, args);
                if (!string.IsNullOrWhiteSpace(parseError))
                {
                    return parseError;
                }
            }
            else if (!updateSellingPrice && updateCost && !string.IsNullOrWhiteSpace(args))
            {
                return "Format: /simpan tanpa argumen. Command ini menyimpan pembelian dan update harga beli saja.";
            }
            else if (!updateSellingPrice && updateCost && HasManualSellingPrice(payload) && !payload.ManualPriceDiscardWarningShown)
            {
                payload.ManualPriceDiscardWarningShown = true;
                pending.ProductId = SerializePriceOverridePayload(payload);
                pending.UpdatedAt = DateTime.Now;
                await _databaseService.SavePendingConfirmationAsync(pending);
                return BuildManualSellingPriceDiscardWarning(payload);
            }
            else if (!updateSellingPrice && !updateCost && !string.IsNullOrWhiteSpace(args))
            {
                return "Format: /lewati_harga tanpa argumen. Command ini menyimpan pembelian tanpa ubah data produk.";
            }

            if (!_priceOverrideProcessingByKey.TryAdd(key, 0))
            {
                return "Konfirmasi harga sedang diproses. Tunggu hasil dokumen sebelumnya.";
            }

            await _databaseService.DeletePendingConfirmationAsync(key);

            var originalPending = new PendingConfirmation
            {
                Key = key,
                Command = payload.OriginalCommand,
                ProductId = payload.OriginalProductId,
                ProductName = payload.OriginalProductName,
                Quantity = payload.OriginalQuantity,
                Price = payload.OriginalPrice,
                CorrelationId = payload.OriginalCorrelationId,
                CreatedAt = pending.CreatedAt,
                UpdatedAt = DateTime.Now
            };

            var decision = new PriceOverrideDecision
            {
                UpdateCost = updateCost,
                UpdateSellingPrice = updateSellingPrice
            };

            try
            {
                string response = payload.OriginalCommand switch
                {
                    "restock" => await ExecuteSingleRestockAsync(message, originalPending, payload, decision),
                    "bulk_restock" => await ExecuteBulkRestockAsync(message, originalPending, payload, decision),
                    "ocr_bulk_restock" => await ExecuteOcrBulkRestockAsync(message, originalPending, payload, decision),
                    _ => "Jenis konfirmasi harga tidak didukung."
                };

                _priceOverrideCompletedByKey.TryRemove(key, out _);

                return response;
            }
            finally
            {
                _priceOverrideProcessingByKey.TryRemove(key, out _);
            }
        }

        private static bool HasManualSellingPrice(PriceOverridePendingPayload payload)
        {
            return payload.Changes.Any(change => change.ManualSellingPrice.GetValueOrDefault() > 0);
        }

        private static string? ApplyManualSellingPricesFromConfirmArgs(PriceOverridePendingPayload payload, string args)
        {
            var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return null;
            }

            if (payload.Changes.Count == 1 && tokens.Length == 1)
            {
                if (!TryParseDecimal(tokens[0], out decimal singlePrice) || singlePrice <= 0)
                {
                    return "Format: /simpan_jual <nominal>. Contoh: /simpan_jual 289703";
                }

                payload.Changes[0].ManualSellingPrice = singlePrice;
                return null;
            }

            if (tokens.Length % 2 != 0)
            {
                return payload.Changes.Count == 1
                    ? "Format: /simpan_jual <nominal> atau /simpan_jual 1 <nominal>."
                    : "Format: /simpan_jual <nomor_item> <nominal>. Contoh: /simpan_jual 1 289703 2 30500";
            }

            for (int i = 0; i < tokens.Length; i += 2)
            {
                if (!int.TryParse(tokens[i], out int oneBasedIndex))
                {
                    return $"Nomor item \"{tokens[i]}\" tidak valid. Pilih 1 sampai {payload.Changes.Count}.";
                }

                int targetIndex = oneBasedIndex - 1;
                if (targetIndex < 0 || targetIndex >= payload.Changes.Count)
                {
                    return $"Nomor item {oneBasedIndex} tidak valid. Pilih 1 sampai {payload.Changes.Count}.";
                }

                if (!TryParseDecimal(tokens[i + 1], out decimal manualPrice) || manualPrice <= 0)
                {
                    return $"Harga jual \"{tokens[i + 1]}\" tidak valid. Contoh: /simpan_jual {oneBasedIndex} 289703";
                }

                payload.Changes[targetIndex].ManualSellingPrice = manualPrice;
            }

            return null;
        }

        private static string BuildManualSellingPriceDiscardWarning(PriceOverridePendingPayload payload)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} Ada harga jual manual yang belum dipakai:");
            foreach (var change in payload.Changes.Where(change => change.ManualSellingPrice.GetValueOrDefault() > 0).Take(10))
            {
                sb.AppendLine($"- {change.ProductName} -> {FormatCurrency(change.ManualSellingPrice!.Value)}");
            }

            sb.AppendLine();
            sb.AppendLine("/simpan = lanjut simpan, harga jual manual diabaikan");
            sb.AppendLine("/simpan_jual = simpan dan pakai harga jual manual");
            sb.AppendLine("/batal = batal");
            return sb.ToString().TrimEnd();
        }

        private void CleanupCompletedPriceOverrideConfirmations()
        {
            DateTime cutoff = DateTime.Now.AddMinutes(-30);
            foreach (var item in _priceOverrideCompletedByKey.ToArray())
            {
                if (item.Value.CompletedAt < cutoff)
                {
                    _priceOverrideCompletedByKey.TryRemove(item.Key, out _);
                }
            }
        }

        private static string? TryExtractDocumentNumber(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }

            var match = DocumentNumberRegex.Match(response);
            return match.Success ? match.Value : null;
        }

        private async Task<string> SetPendingManualSellingPriceAsync(InboundMessage message, string args)
        {
            string key = GetConfirmationKey(message.Channel, message.SenderId);
            var pending = await _databaseService.GetPendingConfirmationAsync(key);
            if (pending == null || pending.Command != "price_override_confirmation")
            {
                return "Tidak ada perubahan harga yang menunggu pengaturan harga jual.";
            }

            PriceOverridePendingPayload payload = DeserializePriceOverridePayload(pending.ProductId);
            if (!payload.Changes.Any())
            {
                return "Data perubahan harga tidak valid.";
            }

            var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int targetIndex = 0;
            string? priceToken = tokens.LastOrDefault();

            if (payload.Changes.Count > 1 && tokens.Length < 2)
            {
                return "Format: /jual <nomor_item> <nominal>. Contoh: /jual 2 32000";
            }

            if (tokens.Length >= 2 && int.TryParse(tokens[0], out int oneBasedIndex))
            {
                targetIndex = oneBasedIndex - 1;
            }

            if (string.IsNullOrWhiteSpace(priceToken) || !TryParseDecimal(priceToken, out decimal manualPrice) || manualPrice <= 0)
            {
                return payload.Changes.Count == 1
                    ? "Format: /jual <nominal>. Contoh: /jual 32000"
                    : "Format: /jual <nomor_item> <nominal>. Contoh: /jual 2 32000";
            }

            if (targetIndex < 0 || targetIndex >= payload.Changes.Count)
            {
                return $"Nomor item tidak valid. Pilih 1 sampai {payload.Changes.Count}.";
            }

            payload.Changes[targetIndex].ManualSellingPrice = manualPrice;
            payload.ManualPriceDiscardWarningShown = false;
            pending.ProductId = SerializePriceOverridePayload(payload);
            pending.UpdatedAt = DateTime.Now;
            await _databaseService.SavePendingConfirmationAsync(pending);

            return BuildPriceOverridePrompt(payload, $"Harga jual item {targetIndex + 1} diset ke {FormatCurrency(manualPrice)}.");
        }

        private async Task<string> ShowPendingPriceOverrideDetailAsync(InboundMessage message)
        {
            string key = GetConfirmationKey(message.Channel, message.SenderId);
            var pending = await _databaseService.GetPendingConfirmationAsync(key);
            if (pending == null || pending.Command != "price_override_confirmation")
            {
                return "Tidak ada perubahan harga yang menunggu detail.";
            }

            PriceOverridePendingPayload payload = DeserializePriceOverridePayload(pending.ProductId);
            if (!payload.Changes.Any())
            {
                return "Data perubahan harga tidak valid.";
            }

            return BuildPriceOverridePrompt(payload, showAll: true);
        }

        private async Task<string> CancelPendingActionAsync(InboundMessage message, AutomationExecutionContext? context = null)
        {
            if (context != null &&
                _shadowMappingPendingBySender.TryRemove(BuildSenderStateKey(context), out _))
            {
                return "Mapping stok dibatalkan.";
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            var pending = await _databaseService.GetPendingConfirmationAsync(key);
            await _databaseService.DeletePendingConfirmationAsync(key);
            if (pending?.Command == "price_override_confirmation")
            {
                return $"{IconCross} Aksi dibatalkan. Dokumen pembelian tidak dibuat.";
            }

            return $"{IconCross} Aksi yang menunggu konfirmasi dibatalkan.";
        }

        private async Task<string> QueueBulkRestockAsync(InboundMessage message, string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var items = new List<BulkPendingItem>();
            var segments = args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                return "Format bulk restock: /restock produk1 qty1 [harga1], produk2 qty2 [harga2]";
            }

            if (segments.Length > 10)
            {
                return "Maksimal 10 produk per bulk restock.";
            }

            var warnings = new List<string>();
            foreach (var segment in segments)
            {
                var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                {
                    warnings.Add($"⚠️ Format tidak valid, dilewati: \"{segment}\"");
                    continue;
                }

                decimal qty;
                decimal? price = null;
                int qtyIndex;

                if (tokens.Length >= 3 &&
                    TryParseDecimal(tokens[^1], out var parsedPrice) &&
                    TryParseDecimal(tokens[^2], out var parsedQty))
                {
                    price = parsedPrice;
                    qty = parsedQty;
                    qtyIndex = tokens.Length - 2;
                }
                else if (TryParseDecimal(tokens[^1], out parsedQty))
                {
                    qty = parsedQty;
                    qtyIndex = tokens.Length - 1;
                }
                else
                {
                    warnings.Add($"⚠️ Qty tidak valid, dilewati: \"{segment}\"");
                    continue;
                }

                if (qty <= 0)
                {
                    warnings.Add($"⚠️ Qty harus > 0, dilewati: \"{segment}\"");
                    continue;
                }

                string productQuery = string.Join(" ", tokens.Take(qtyIndex));
                if (string.IsNullOrWhiteSpace(productQuery))
                {
                    warnings.Add($"⚠️ Nama produk kosong, dilewati: \"{segment}\"");
                    continue;
                }

                var (product, error) = await TryResolveProductAsync(productQuery, isMutation: true, actionLabel: "restock");
                if (product == null || !string.IsNullOrWhiteSpace(error))
                {
                    // Ambigu atau tidak ditemukan → skip dengan peringatan, jangan blokir seluruh bulk
                    warnings.Add($"⚠️ {error ?? $"Produk \"{productQuery}\" tidak ditemukan."} — dilewati.");
                    continue;
                }

                items.Add(new BulkPendingItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name ?? productQuery,
                    Quantity = qty,
                    Price = price ?? product.PurchasePrice ?? 0,
                    Unit = product.Unit
                });
            }

            if (!items.Any())
            {
                var noItemMsg = new StringBuilder("Tidak ada produk valid untuk bulk restock.");
                if (warnings.Any()) { noItemMsg.AppendLine(); noItemMsg.AppendLine(string.Join("\n", warnings)); }
                return noItemMsg.ToString();
            }

            var duplicateRestock = items
                .GroupBy(item => item.ProductId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateRestock != null)
            {
                return $"Produk \"{duplicateRestock.First().ProductName}\" muncul lebih dari sekali pada bulk restock. Gabungkan qty-nya lalu kirim ulang.";
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "bulk_restock",
                ProductId = SerializeBulkItems(items),
                ProductName = $"Bulk restock {items.Count} produk",
                Quantity = items.Count,
                CorrelationId = message.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            return BuildBulkRestockConfirmationMessage(items, warnings);

            var confirmLines = new StringBuilder();
            confirmLines.AppendLine("Konfirmasi bulk restock (1 dokumen):");
            foreach (var item in items)
            {
                confirmLines.AppendLine($"- {item.ProductName}: {FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)} @ Rp {(item.Price ?? 0):N0}");
            }
            if (warnings.Any())
            {
                confirmLines.AppendLine();
                confirmLines.AppendLine("⚠️ Item berikut tidak diproses:");
                foreach (var w in warnings) { confirmLines.AppendLine(w); }
            }
            confirmLines.AppendLine();
            confirmLines.Append("Kirim /confirm untuk lanjut atau /cancel untuk batal.");
            return confirmLines.ToString();
        }

        private async Task<string> QueueBulkInventoryAsync(InboundMessage message, string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var items = new List<BulkPendingItem>();
            var segments = args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                return "Format bulk inventory: /inventory produk1 target1, produk2 target2";
            }

            if (segments.Length > 10)
            {
                return "Maksimal 10 produk per bulk inventory.";
            }

            var warnings = new List<string>();
            var skippedSameStock = new List<string>();
            foreach (var segment in segments)
            {
                var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                {
                    warnings.Add($"⚠️ Format tidak valid, dilewati: \"{segment}\"");
                    continue;
                }

                if (!TryParseDecimal(tokens[^1], out var targetStock))
                {
                    warnings.Add($"⚠️ Stok target tidak valid, dilewati: \"{segment}\"");
                    continue;
                }

                if (targetStock < 0)
                {
                    warnings.Add($"⚠️ Target tidak boleh negatif, dilewati: \"{segment}\"");
                    continue;
                }

                string productQuery = string.Join(" ", tokens.Take(tokens.Length - 1));
                if (string.IsNullOrWhiteSpace(productQuery))
                {
                    warnings.Add($"⚠️ Nama produk kosong, dilewati: \"{segment}\"");
                    continue;
                }

                var (product, error) = await TryResolveProductAsync(productQuery, isMutation: true, actionLabel: "inventory");
                if (product == null || !string.IsNullOrWhiteSpace(error))
                {
                    // Ambigu atau tidak ditemukan → skip dengan peringatan
                    warnings.Add($"⚠️ {error ?? $"Produk \"{productQuery}\" tidak ditemukan."} — dilewati.");
                    continue;
                }

                // Pakai Stock.Quantity (sumber kebenaran Aronium), bukan SUM(DocumentItem)
                decimal currentStock = product.Stock ?? 0;
                if (targetStock == currentStock)
                {
                    skippedSameStock.Add($"↔️ {product.Name}: stok sudah {FormatStockValue(currentStock)} {GetUnitLabel(product.Unit)}, tidak ada perubahan.");
                    continue;
                }

                items.Add(new BulkPendingItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name ?? productQuery,
                    Quantity = targetStock,
                    CurrentStock = currentStock,
                    Unit = product.Unit
                });
            }

            if (!items.Any())
            {
                var noItemMsg = new StringBuilder("Tidak ada produk yang perlu diubah stoknya.");
                if (skippedSameStock.Any()) { noItemMsg.AppendLine(); noItemMsg.AppendLine(string.Join("\n", skippedSameStock)); }
                if (warnings.Any()) { noItemMsg.AppendLine(); noItemMsg.AppendLine(string.Join("\n", warnings)); }
                return noItemMsg.ToString();
            }

            var duplicateInventory = items
                .GroupBy(item => item.ProductId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateInventory != null)
            {
                return $"Produk \"{duplicateInventory.First().ProductName}\" muncul lebih dari sekali pada bulk inventory. Pakai satu target stok saja per produk.";
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "bulk_inventory",
                ProductId = SerializeBulkItems(items),
                ProductName = $"Bulk inventory {items.Count} produk",
                Quantity = items.Count,
                CorrelationId = message.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            return BuildBulkInventoryConfirmationMessage(items, skippedSameStock, warnings);
        }

        private async Task<string> ExecuteBulkRestockAsync(
            InboundMessage message,
            PendingConfirmation pending,
            PriceOverridePendingPayload? pricePayload = null,
            PriceOverrideDecision? decision = null)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var items = DeserializeBulkItems(pending.ProductId);
            if (!items.Any())
            {
                return "Data bulk restock tidak valid.";
            }

            var bulkInputs = new List<BulkDocumentItemInput>();
            foreach (var item in items)
            {
                if (!int.TryParse(item.ProductId, out var productId))
                {
                    return $"Data bulk restock tidak valid. ID produk \"{item.ProductName}\" tidak valid.";
                }

                bulkInputs.Add(new BulkDocumentItemInput
                {
                    ProductId = productId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    Price = item.Price ?? 0,
                    CurrentStock = item.CurrentStock,
                    Unit = item.Unit
                });
            }

            pricePayload ??= await BuildPriceOverridePayloadAsync(pending, items, includeShadow: true);
            if (decision == null && ShouldPromptPriceOverride(pricePayload))
            {
                return await QueuePriceOverrideConfirmationAsync(message, pending, pricePayload);
            }

            var debtErasureResult = await EraseNegativeStockDebtAsync(
                items,
                $"{DualStockInternalNotePrefix}: negative debt erasure before manual restock via {message.Channel}");

            var result = await _posDbService.CreateBulkPurchaseDocumentAsync(
                bulkInputs,
                1,
                $"Bulk confirmed via {message.Channel} by {message.SenderId}");

            if (!result.Success)
            {
                return $"Bulk restock gagal: {result.Error}";
            }

            var shadowConversionResults = await ApplyShadowConversionAsync(items, pricePayload, decision);
            await ApplyMasterPriceOverridesAsync(pricePayload, decision);

            var successMessage = new StringBuilder();
            successMessage.Append(BuildBulkRestockSuccessMessage(result.DocumentNumber, result.Items));
            AppendDebtErasureSummary(successMessage, debtErasureResult);
            AppendShadowConversionSummary(successMessage, shadowConversionResults);
            AppendPriceOverrideSummary(successMessage, pricePayload, decision);
            await AppendUnpromptedPurchaseCostNotesAsync(successMessage, items, pricePayload, decision);
            return successMessage.ToString();

            var lines = result.Items
                .Take(10)
                .Select(item => $"- {item.ProductName}: {FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)} @ Rp {item.Price:N0}");
            return $"Bulk restock selesai: {result.Items.Count}/{result.Items.Count} produk berhasil.\n" +
                   $"Dokumen: {result.DocumentNumber}\n" +
                   string.Join("\n", lines);
        }

        private async Task<string> ExecuteOcrBulkRestockAsync(
            InboundMessage message,
            PendingConfirmation pending,
            PriceOverridePendingPayload? pricePayload = null,
            PriceOverrideDecision? decision = null)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var payload = DeserializeOcrBulkPayload(pending.ProductId);
            if (!payload.Items.Any())
            {
                return "Data OCR bulk restock tidak valid.";
            }

            string? supplierName = payload.SupplierName ?? payload.StoreName;
            int? supplierCustomerId = null;
            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                string? supplierId = await _posDbService.GetOrCreateSupplierAsync(supplierName);
                if (int.TryParse(supplierId, out int parsedSupplierCustomerId))
                {
                    supplierCustomerId = parsedSupplierCustomerId;
                }
            }

            var bulkInputs = new List<BulkDocumentItemInput>();
            foreach (var item in payload.Items)
            {
                if (!int.TryParse(item.ProductId, out var productId))
                {
                    return $"Data OCR bulk restock tidak valid. ID produk \"{item.ProductName}\" tidak valid.";
                }

                bulkInputs.Add(new BulkDocumentItemInput
                {
                    ProductId = productId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    Price = item.Price ?? 0,
                    CurrentStock = item.CurrentStock,
                    Unit = item.Unit
                });
            }

            pricePayload ??= await BuildPriceOverridePayloadAsync(pending, payload.Items, includeShadow: true);
            if (decision == null && ShouldPromptPriceOverride(pricePayload))
            {
                return await QueuePriceOverrideConfirmationAsync(message, pending, pricePayload);
            }

            var debtErasureResult = await EraseNegativeStockDebtAsync(
                payload.Items,
                $"{DualStockInternalNotePrefix}: negative debt erasure before OCR restock via {message.Channel}");

            var result = await _posDbService.CreateBulkPurchaseDocumentAsync(
                bulkInputs,
                1,
                BuildOcrPurchaseNote(message, payload),
                supplierName,
                supplierCustomerId);

            if (!result.Success)
            {
                return $"OCR bulk restock gagal: {result.Error}";
            }

            PersistConfirmedOcrMappings(payload.Items);
            var shadowConversionResults = await ApplyShadowConversionAsync(payload.Items, pricePayload, decision);
            await ApplyMasterPriceOverridesAsync(pricePayload, decision);

            var sb = new StringBuilder();
            sb.AppendLine(BuildBulkRestockSuccessMessage(result.DocumentNumber, result.Items));
            AppendDebtErasureSummary(sb, debtErasureResult);
            AppendPriceOverrideSummary(sb, pricePayload, decision);
            await AppendUnpromptedPurchaseCostNotesAsync(sb, payload.Items, pricePayload, decision);

            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                sb.AppendLine();
                sb.AppendLine($"Supplier: {supplierName}");
            }

            if (payload.ReceiptDate.HasValue)
            {
                sb.AppendLine($"Tanggal struk: {payload.ReceiptDate:dd/MM/yyyy}");
            }

            if (payload.ReviewItems.Any())
            {
                await _databaseService.AddOcrReviewQueueItemsAsync(payload.ReviewItems);
                sb.AppendLine();
                sb.AppendLine($"{IconWarning} {payload.ReviewItems.Count} item masuk OCR Review Queue untuk diperbaiki di aplikasi desktop.");
            }

            if (shadowConversionResults.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Shadow conversion:");
                foreach (ShadowConversionResult shadowResult in shadowConversionResults)
                {
                    sb.AppendLine($"- {FormatShadowConversionResult(shadowResult)}");
                }
            }

            if (_configService.Config?.GoogleSheets?.Enabled == true)
            {
                var sheetsService = new GoogleSheetsService(_configService, _loggingService);
                var exportResult = await sheetsService.AppendPurchaseRowsAsync(
                    result.Items,
                    result.DocumentNumber,
                    supplierName,
                    payload.ReceiptDate);

                sb.AppendLine();
                sb.AppendLine(exportResult.Success
                    ? $"{IconCheck} {exportResult.Message}"
                    : $"{IconWarning} Google Sheets: {exportResult.Message}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<PriceOverridePendingPayload> BuildPriceOverridePayloadAsync(
            PendingConfirmation originalPending,
            IEnumerable<BulkPendingItem> items,
            bool includeShadow)
        {
            var payload = new PriceOverridePendingPayload
            {
                OriginalCommand = originalPending.Command,
                OriginalProductId = originalPending.ProductId,
                OriginalProductName = originalPending.ProductName,
                OriginalQuantity = originalPending.Quantity,
                OriginalPrice = originalPending.Price,
                OriginalCorrelationId = originalPending.CorrelationId
            };

            foreach (var item in items ?? Enumerable.Empty<BulkPendingItem>())
            {
                await AddPriceChangeIfNeededAsync(
                    payload,
                    item.ProductId,
                    item.ProductName,
                    item.Price ?? 0,
                    "purchase",
                    isShadowChild: false,
                    parentProductId: null,
                    parentProductName: null,
                    conversionRate: null);

                if (!includeShadow || item.Price.GetValueOrDefault() <= 0)
                {
                    continue;
                }

                UnitConversionMapping? conversion = await _databaseService.GetConversionByParentIdAsync(item.ProductId);
                if (conversion == null ||
                    string.Equals(conversion.ParentProductId, conversion.ChildProductId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                decimal effectiveRate = item.IsiPerBox.GetValueOrDefault() > 0
                    ? item.IsiPerBox!.Value
                    : conversion.ConversionRate;
                decimal? childUnitCost = CalculateShadowChildUnitCost(item.Price, effectiveRate);
                if (childUnitCost.GetValueOrDefault() <= 0)
                {
                    continue;
                }

                await AddPriceChangeIfNeededAsync(
                    payload,
                    conversion.ChildProductId,
                    conversion.ChildProductName ?? conversion.ChildProductId,
                    childUnitCost.GetValueOrDefault(),
                    "shadow",
                    isShadowChild: true,
                    parentProductId: item.ProductId,
                    parentProductName: item.ProductName,
                    conversionRate: effectiveRate);
            }

            return payload;
        }

        private async Task AddPriceChangeIfNeededAsync(
            PriceOverridePendingPayload payload,
            string productId,
            string productName,
            decimal newCost,
            string source,
            bool isShadowChild,
            string? parentProductId,
            string? parentProductName,
            decimal? conversionRate)
        {
            if (_posDbService == null || string.IsNullOrWhiteSpace(productId) || newCost <= 0)
            {
                return;
            }

            if (payload.Changes.Any(change =>
                    string.Equals(change.ProductId, productId, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(change.NewCost - newCost) < 0.01m))
            {
                return;
            }

            Product? product = await _posDbService.GetProductByIdAsync(productId);
            decimal oldCost = product?.PurchasePrice ?? 0;
            if (!ShouldIncludePriceChange(oldCost, newCost))
            {
                return;
            }

            decimal oldSellingPrice = product?.SellingPrice ?? 0;
            payload.Changes.Add(new PriceChangeItem
            {
                ProductId = productId,
                ProductName = product?.Name ?? productName,
                Source = source,
                IsShadowChild = isShadowChild,
                ParentProductId = parentProductId,
                ParentProductName = parentProductName,
                ConversionRate = conversionRate,
                OldCost = oldCost,
                NewCost = newCost,
                OldSellingPrice = oldSellingPrice,
                SuggestedSellingPrice = SuggestSellingPrice(oldCost, oldSellingPrice, newCost)
            });
        }

        private static bool ShouldIncludePriceChange(decimal oldCost, decimal newCost)
        {
            if (newCost <= 0)
            {
                return false;
            }

            if (oldCost <= 0)
            {
                return true;
            }

            decimal delta = Math.Abs(newCost - oldCost);
            decimal deltaPercent = (delta / oldCost) * 100;
            return delta >= 1 && deltaPercent >= 1;
        }

        private static bool ShouldPromptPriceOverride(PriceOverridePendingPayload payload)
        {
            return payload.Changes.Any();
        }

        private async Task<string> QueuePriceOverrideConfirmationAsync(
            InboundMessage message,
            PendingConfirmation originalPending,
            PriceOverridePendingPayload payload)
        {
            string key = GetConfirmationKey(message.Channel, message.SenderId);
            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "price_override_confirmation",
                ProductId = SerializePriceOverridePayload(payload),
                ProductName = $"Konfirmasi harga {payload.Changes.Count} produk",
                Quantity = payload.Changes.Count,
                CorrelationId = originalPending.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            return BuildPriceOverridePrompt(payload);
        }

        private async Task ApplyMasterPriceOverridesAsync(
            PriceOverridePendingPayload? payload,
            PriceOverrideDecision? decision,
            bool includeShadowItems = true)
        {
            if (_posDbService == null ||
                payload == null ||
                decision == null ||
                (!decision.UpdateCost && !decision.UpdateSellingPrice))
            {
                return;
            }

            foreach (var change in payload.Changes)
            {
                if (!includeShadowItems && change.IsShadowChild)
                {
                    continue;
                }

                await _posDbService.UpdateProductPricingAsync(
                    change.ProductId,
                    decision.UpdateCost ? change.NewCost : null,
                    decision.UpdateSellingPrice ? change.EffectiveSellingPrice : null);
            }
        }

        private static PriceChangeItem? FindPriceChange(
            PriceOverridePendingPayload? payload,
            string productId,
            decimal? newCost = null)
        {
            if (payload == null || string.IsNullOrWhiteSpace(productId))
            {
                return null;
            }

            return payload.Changes.FirstOrDefault(change =>
                string.Equals(change.ProductId, productId, StringComparison.OrdinalIgnoreCase) &&
                (!newCost.HasValue || Math.Abs(change.NewCost - newCost.Value) < 0.01m));
        }

        private static decimal SuggestSellingPrice(decimal oldCost, decimal oldSellingPrice, decimal newCost)
        {
            if (newCost <= 0)
            {
                return oldSellingPrice > 0 ? RoundSellingPrice(oldSellingPrice) : 0;
            }

            const decimal minimumGrossMarginPercent = 3m;
            decimal minimumSafePrice = newCost / (1 - (minimumGrossMarginPercent / 100m));

            if (oldCost > 0 &&
                newCost < oldCost &&
                oldSellingPrice >= minimumSafePrice)
            {
                return RoundSellingPrice(oldSellingPrice);
            }

            decimal rawSuggestion = minimumSafePrice;
            if (oldCost > 0 && oldSellingPrice > oldCost)
            {
                rawSuggestion = Math.Max(rawSuggestion, newCost * (oldSellingPrice / oldCost));
            }
            else if (oldSellingPrice > 0 && oldSellingPrice >= newCost)
            {
                rawSuggestion = Math.Max(rawSuggestion, oldSellingPrice);
            }

            rawSuggestion = Math.Max(rawSuggestion, newCost);
            return RoundSellingPrice(rawSuggestion);
        }

        private static decimal RoundSellingPrice(decimal rawSuggestion)
        {
            decimal step = rawSuggestion < 10000 ? 500 : 1000;
            return Math.Ceiling(rawSuggestion / step) * step;
        }

        private static decimal CalculateGrossMarginPercent(decimal sellingPrice, decimal cost)
        {
            if (sellingPrice <= 0)
            {
                return 0;
            }

            return ((sellingPrice - cost) / sellingPrice) * 100;
        }

        private static string FormatPercent(decimal value)
        {
            return value.ToString("+0.##;-0.##;0", IndonesianCulture) + "%";
        }

        private string BuildPriceOverridePrompt(PriceOverridePendingPayload payload, string? prefix = null, bool showAll = false)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                sb.AppendLine(prefix);
                sb.AppendLine();
            }

            sb.AppendLine($"{IconWarning} Harga beli berubah.");
            sb.AppendLine("Dokumen pembelian belum disimpan.");
            sb.AppendLine();

            if (!showAll && payload.Changes.Count > 3)
            {
                int increased = payload.Changes.Count(change => change.NewCost > change.OldCost);
                int decreased = payload.Changes.Count(change => change.NewCost < change.OldCost);
                int riskySell = payload.Changes.Count(change => change.OldSellingPrice > 0 && change.OldSellingPrice < change.NewCost);
                sb.AppendLine($"Ada {payload.Changes.Count} produk dengan harga beli berubah.");
                sb.AppendLine($"Ringkasan: {increased} naik, {decreased} turun, {riskySell} harga jual di bawah harga beli baru.");
                sb.AppendLine();
                sb.AppendLine("Yang perlu perhatian:");
            }

            int index = 1;
            int maxItems = showAll ? 20 : payload.Changes.Count > 3 ? 3 : 10;
            foreach (var change in payload.Changes.Take(maxItems))
            {
                decimal sellingCandidate = change.EffectiveSellingPrice;
                sb.AppendLine($"{index}. {BuildPriceChangeDisplayName(change)}");
                if (change.IsShadowChild && !string.IsNullOrWhiteSpace(change.ParentProductName))
                {
                    sb.AppendLine($"Parent: {change.ParentProductName} x {FormatStockValue(change.ConversionRate ?? 0)}");
                }

                sb.AppendLine($"Harga beli lama : {FormatCurrency(change.OldCost)}");
                sb.AppendLine($"Harga beli baru : {FormatCurrency(change.NewCost)}");
                sb.AppendLine($"{BuildDeltaLabel(change)}           : {FormatCurrency(Math.Abs(change.DeltaAmount))} ({FormatPercent(change.DeltaPercent)})");
                sb.AppendLine($"Harga jual sekarang: {FormatCurrency(change.OldSellingPrice)}");
                sb.AppendLine($"Margin jika tetap  : {FormatPercent(CalculateGrossMarginPercent(change.OldSellingPrice, change.NewCost))}");
                sb.AppendLine(BuildSellingSuggestionLine(change));
                if (change.ManualSellingPrice.GetValueOrDefault() > 0)
                {
                    sb.AppendLine($"Harga jual manual: {FormatCurrency(change.ManualSellingPrice!.Value)}");
                    sb.AppendLine($"Margin manual   : {FormatPercent(CalculateGrossMarginPercent(sellingCandidate, change.NewCost))}");
                }

                sb.AppendLine();
                index++;
            }

            if (payload.Changes.Count > maxItems)
            {
                sb.AppendLine($"...dan {payload.Changes.Count - maxItems} produk lain.");
                sb.AppendLine();
            }

            sb.AppendLine("Pilih:");
            sb.AppendLine("/simpan = simpan pembelian + update harga beli saja");
            sb.AppendLine(HasManualSellingPrice(payload)
                ? "/simpan_jual = simpan pembelian + pakai harga jual manual/saran"
                : "/simpan_jual = simpan pembelian + pakai saran harga jual");
            sb.AppendLine(payload.Changes.Count == 1
                ? "/jual 284500 = ubah harga jual, lalu tampilkan ulang"
                : "/jual 1 284500 = ubah harga jual item 1, lalu tampilkan ulang");
            if (payload.Changes.Count > maxItems && !showAll)
            {
                sb.AppendLine("/detail_harga = lihat semua item");
            }

            sb.AppendLine("/lewati_harga = simpan pembelian tanpa ubah data produk");
            sb.AppendLine("/batal = batal");
            return sb.ToString().TrimEnd();
        }

        private static string BuildPriceChangeDisplayName(PriceChangeItem change)
        {
            if (!change.IsShadowChild)
            {
                return change.ProductName;
            }

            return change.ProductName.Contains("ecer", StringComparison.OrdinalIgnoreCase)
                ? change.ProductName
                : $"{change.ProductName} ecer";
        }

        private static string BuildDeltaLabel(PriceChangeItem change)
        {
            if (change.DeltaAmount > 0)
            {
                return "Naik ";
            }

            if (change.DeltaAmount < 0)
            {
                return "Turun";
            }

            return "Tetap";
        }

        private static string BuildSellingSuggestionLine(PriceChangeItem change)
        {
            string suggestion = FormatCurrency(change.SuggestedSellingPrice);
            if (change.SuggestedSellingPrice == change.OldSellingPrice && change.OldSellingPrice > 0)
            {
                return $"Saran saya         : tetap {suggestion} karena margin masih aman";
            }

            if (change.OldSellingPrice > 0 && change.OldSellingPrice < change.NewCost)
            {
                return $"Saran saya         : {suggestion} agar tidak rugi";
            }

            if (change.NewCost < change.OldCost && change.OldSellingPrice > 0)
            {
                return $"Saran saya         : {suggestion} agar margin tetap aman";
            }

            decimal margin = CalculateGrossMarginPercent(change.SuggestedSellingPrice, change.NewCost);
            return $"Saran saya         : {suggestion} agar margin sekitar {FormatPercent(margin)}";
        }

        private static void AppendPriceOverrideSummary(
            StringBuilder sb,
            PriceOverridePendingPayload? payload,
            PriceOverrideDecision? decision,
            bool includeShadowItems = true)
        {
            if (payload == null || decision == null || !payload.Changes.Any())
            {
                return;
            }

            var changes = payload.Changes
                .Where(change => includeShadowItems || !change.IsShadowChild)
                .ToList();
            if (!changes.Any())
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("Update harga:");
            if (!decision.UpdateCost && !decision.UpdateSellingPrice)
            {
                sb.AppendLine("- Data produk tidak diubah. Dokumen tetap memakai harga beli transaksi.");
                return;
            }

            foreach (var change in changes.Take(10))
            {
                string sellingText = decision.UpdateSellingPrice
                    ? $" | harga jual -> {FormatCurrency(change.EffectiveSellingPrice)}"
                    : string.Empty;
                sb.AppendLine($"- {BuildPriceChangeDisplayName(change)}: harga beli -> {FormatCurrency(change.NewCost)}{sellingText}");
            }

            if (decision.UpdateCost && !decision.UpdateSellingPrice)
            {
                sb.AppendLine("- Harga jual tidak diubah.");
            }
        }

        private async Task<BulkDocumentResult?> EraseNegativeStockDebtAsync(
            IEnumerable<BulkPendingItem> items,
            string internalNote)
        {
            if (_posDbService == null)
            {
                return null;
            }

            var targets = new List<DualStockTarget>();
            foreach (var item in items
                         .Where(item => !string.IsNullOrWhiteSpace(item.ProductId))
                         .GroupBy(item => item.ProductId, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.Last()))
            {
                var product = await _posDbService.GetProductByIdAsync(item.ProductId);
                if (product?.Stock.GetValueOrDefault() < 0)
                {
                    targets.Add(new DualStockTarget(product, 0));
                }
            }

            if (!targets.Any())
            {
                return null;
            }

            return await CreateDualStockInventoryDocumentAsync(targets, internalNote);
        }

        private static void AppendDebtErasureSummary(StringBuilder sb, BulkDocumentResult? result)
        {
            if (result == null)
            {
                return;
            }

            sb.AppendLine();
            if (!result.Success)
            {
                sb.AppendLine($"Debt erasure dilewati: {result.Error}");
                return;
            }

            sb.AppendLine("Debt erasure:");
            foreach (var item in result.Items.Take(10))
            {
                sb.AppendLine($"- {item.ProductName}: {FormatStockValue(item.OldStock)} -> 0 {GetUnitLabel(item.Unit)} sebelum restock");
            }
        }

        private static void AppendShadowConversionSummary(StringBuilder sb, IEnumerable<ShadowConversionResult> results)
        {
            var list = results?.ToList() ?? new List<ShadowConversionResult>();
            if (!list.Any())
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("Shadow conversion:");
            foreach (ShadowConversionResult shadowResult in list)
            {
                sb.AppendLine($"- {FormatShadowConversionResult(shadowResult)}");
            }
        }

        private async Task AppendUnpromptedPurchaseCostNotesAsync(
            StringBuilder sb,
            IEnumerable<BulkPendingItem> items,
            PriceOverridePendingPayload? payload,
            PriceOverrideDecision? decision)
        {
            if (_posDbService == null || decision != null)
            {
                return;
            }

            var notes = new List<string>();
            foreach (var item in items
                         .Where(item => !string.IsNullOrWhiteSpace(item.ProductId) && item.Price.GetValueOrDefault() > 0)
                         .GroupBy(item => item.ProductId, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.Last()))
            {
                bool alreadyPrompted = payload?.Changes.Any(change =>
                    string.Equals(change.ProductId, item.ProductId, StringComparison.OrdinalIgnoreCase)) == true;
                if (alreadyPrompted)
                {
                    continue;
                }

                Product? product = await _posDbService.GetProductByIdAsync(item.ProductId);
                decimal masterCost = product?.PurchasePrice ?? 0;
                decimal transactionCost = item.Price!.Value;
                if (masterCost <= 0 || Math.Abs(masterCost - transactionCost) < 0.01m)
                {
                    continue;
                }

                if (ShouldIncludePriceChange(masterCost, transactionCost))
                {
                    continue;
                }

                decimal deltaPercent = ((transactionCost - masterCost) / masterCost) * 100;
                notes.Add($"{product?.Name ?? item.ProductName}: harga beli transaksi {FormatCurrency(transactionCost)}, data produk {FormatCurrency(masterCost)}, selisih {FormatPercent(deltaPercent)} di bawah batas 1%. Data produk tidak diubah.");
            }

            if (!notes.Any())
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("Catatan harga beli:");
            foreach (string note in notes.Take(10))
            {
                sb.AppendLine($"- {note}");
            }
        }

        private void PersistConfirmedOcrMappings(IEnumerable<BulkPendingItem> items)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ProductId) || string.IsNullOrWhiteSpace(item.ProductName))
                {
                    continue;
                }

                foreach (string rawName in item.RawProductNames ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        continue;
                    }

                    _configService.AddOcrMapping(rawName, item.ProductId, item.ProductName);
                }
            }
        }

        private async Task<string> ExecuteBulkInventoryAsync(InboundMessage message, PendingConfirmation pending)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var items = DeserializeBulkItems(pending.ProductId);
            if (!items.Any())
            {
                return "Data bulk inventory tidak valid.";
            }

            var bulkInputs = new List<BulkDocumentItemInput>();
            foreach (var item in items)
            {
                if (!int.TryParse(item.ProductId, out var productId))
                {
                    return $"Data bulk inventory tidak valid. ID produk \"{item.ProductName}\" tidak valid.";
                }

                bulkInputs.Add(new BulkDocumentItemInput
                {
                    ProductId = productId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    CurrentStock = item.CurrentStock,
                    Unit = item.Unit
                });
            }

            var result = await _posDbService.CreateBulkInventoryCountDocumentAsync(bulkInputs, 1);
            if (!result.Success)
            {
                return $"Bulk inventory gagal: {result.Error}";
            }

            foreach (var item in result.Items)
            {
                await _databaseService.AddInventoryLogAsync(new InventoryLog
                {
                    ProductId = item.ProductId.ToString(CultureInfo.InvariantCulture),
                    ProductName = item.ProductName,
                    OldStock = item.OldStock,
                    NewStock = item.NewStock,
                    Adjustment = item.Adjustment,
                    Reason = "Bulk confirmed via automation engine",
                    UserId = message.SenderId,
                    Channel = message.Channel.ToString(),
                    Timestamp = DateTime.Now
                });
            }

            return BuildBulkInventorySuccessMessage(result.DocumentNumber, result.Items);

            var lines = result.Items
                .Take(10)
                .Select(item => $"- {item.ProductName}: {FormatStockValue(item.OldStock)} -> {FormatStockValue(item.NewStock)} {GetUnitLabel(item.Unit)} ({FormatSignedStockValue(item.Adjustment)})");
            return $"Bulk inventory selesai: {result.Items.Count}/{result.Items.Count} produk berhasil.\n" +
                   $"Dokumen: {result.DocumentNumber}\n" +
                   string.Join("\n", lines);
        }

        private async Task<string> HandleAnalysisAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            DateTime startOfWeek = DateTime.Today.AddDays(-6);
            var todayRevenue = await _posDbService.GetTodayRevenueAsync();
            var todayProfit = await _posDbService.GetTodayProfitAsync();
            var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();
            var weeklyRevenue = await _posDbService.GetSalesRevenueAsync(startOfWeek, DateTime.Now);
            var weeklyProfit = await _posDbService.GetSalesProfitAsync(startOfWeek, DateTime.Now);
            var topSelling = await _posDbService.GetTopSellingProductsAsync(startOfWeek, DateTime.Now, 3);
            var lowStock = await _posDbService.GetLowStockProductsAsync(5);
            var deadStock = await _posDbService.GetDeadStockProductsAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"{IconChart} ANALISA BISNIS");
            sb.AppendLine();
            sb.AppendLine($"  {IconCalendar} Hari ini  : {FormatCurrency(todayRevenue)} omzet | {FormatCurrency(todayProfit)} profit");
            sb.AppendLine($"  {IconCalendar} Kemarin   : {FormatCurrency(yesterdayRevenue)} omzet");
            sb.AppendLine($"  {IconCalendar} 7 hari    : {FormatCurrency(weeklyRevenue)} omzet | {FormatCurrency(weeklyProfit)} profit");

            sb.AppendLine();
            sb.AppendLine("\U0001F3C6 Produk Terlaris (7 hari):");
            if (topSelling.Any())
            {
                int rank = 1;
                foreach (var item in topSelling)
                {
                    sb.AppendLine($"  {rank}. {FormatOptional(item.ProductName).PadRight(18)} - {FormatDisplayQuantity(item.QuantitySold)} {GetUnitLabel(item.Unit)}  {FormatCurrency(item.Revenue)}");
                    rank++;
                }
            }
            else
            {
                sb.AppendLine("  Belum ada data penjualan 7 hari terakhir.");
            }

            if (lowStock.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconWarning} Stok Perlu Perhatian:");
                AppendStockAttentionLines(sb, lowStock, maxPerGroup: 3);
            }

            sb.AppendLine();
            sb.AppendLine($"{IconBoxArchive} Dead stock: {deadStock.Count} produk");
            sb.AppendLine("   Ketik /dead_stock untuk detail");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleZeroCostAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var tierAProducts = await _posDbService.GetZeroCostProductsAsync();
            var allZeroCostProducts = await _posDbService.GetNoCostProductsForExportAsync(includeAllZeroCostProducts: true);
            if (!tierAProducts.Any())
            {
                return "Semua produk sudah memiliki harga modal.";
            }

            var sb = new StringBuilder();
            decimal totalRevenue30Days = tierAProducts.Sum(item => item.Revenue30Days);
            sb.AppendLine($"{IconWarning} PRODUK TANPA HARGA MODAL - TOP 20 PALING URGENT");
            sb.AppendLine($"   ({tierAProducts.Count} aktif 30hr | total revenue {FormatCurrency(totalRevenue30Days)} tak terhitung)");
            sb.AppendLine();

            int rank = 1;
            foreach (var product in tierAProducts.Take(20))
            {
                sb.AppendLine($"  {rank}. {FormatOptional(product.ProductName).PadRight(22)} jual {FormatCurrency(product.SellingPrice)} | {FormatDisplayQuantity(product.QuantitySold30Days)} terjual | {FormatCurrency(product.Revenue30Days)}");
                rank++;
            }

            if (tierAProducts.Count > 20)
            {
                sb.AppendLine($"...dan {tierAProducts.Count - 20} produk Tier A lainnya.");
            }

            sb.AppendLine();
            sb.AppendLine($"{IconWarning} Produk tanpa modal aktif total: {allZeroCostProducts.Count}.");
            sb.AppendLine("Ketik EKSPOR TANPA MODAL untuk CSV Tier A.");
            sb.Append("Ketik EKSPOR TANPA MODAL SEMUA untuk CSV audit seluruh produk tanpa modal.");
            return sb.ToString();
        }

        private async Task<string> HandleCashierReportAsync(string args = "")
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string normalizedArgs = NormalizeText(args);
            string period = ContainsAny(normalizedArgs, "hari", "today", "sekarang") ? "today" :
                ContainsAny(normalizedArgs, "minggu", "7 hari", "pekan") ? "last_N_days:7" :
                "month";
            var (startDate, endDate, _, titleLabel, _) = ResolveSalesPeriod(period);
            string? cashierFilter = null;
            if (!string.IsNullOrWhiteSpace(args) && period == "month")
            {
                cashierFilter = args.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(args))
            {
                cashierFilter = Regex.Replace(args, @"\b(hari|today|sekarang|minggu|pekan|7\s*hari)\b", "", RegexOptions.IgnoreCase).Trim();
            }

            var reports = await _posDbService.GetSalesPerUserAsync(startDate, endDate);
            if (!string.IsNullOrWhiteSpace(cashierFilter))
            {
                reports = reports
                    .Where(report => !string.IsNullOrWhiteSpace(report.Name) &&
                                     report.Name.Contains(cashierFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            if (!reports.Any())
            {
                return $"Belum ada transaksi kasir untuk periode {titleLabel}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconUser} PERFORMA KASIR - {titleLabel}");
            sb.AppendLine();
            bool hasNegative = false;
            int rank = 1;
            foreach (var report in reports)
            {
                bool isNegative = report.TotalSales < 0;
                hasNegative |= isNegative;
                decimal average = report.TransactionCount > 0 ? report.TotalSales / report.TransactionCount : 0;
                sb.AppendLine($"  {rank}. {FormatOptional(report.Name).PadRight(16)} {report.TransactionCount} trx | {FormatCurrency(report.TotalSales)} | avg {FormatCurrency(average)}{(isNegative ? $" {IconWarning}" : string.Empty)}");
                rank++;
            }

            if (hasNegative)
            {
                sb.AppendLine();
                sb.Append("Nilai negatif = ada retur/void. Cek dokumen di Aronium.");
            }
            else
            {
                sb.AppendLine();
                sb.Append("Ketik /laporan_kasir hari atau /laporan_kasir minggu untuk periode lain.");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleDeadStockAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var deadStock = await GetDeadStockWithShadowFilterAsync();
            if (!deadStock.Any())
            {
                return "Tidak ada dead stock saat ini.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconBoxArchive} DEAD STOCK (Layer B)");
            sb.AppendLine("Kriteria: stok > 0, tidak terjual >21 hari, bukan baru restock, bukan kategori mandatory.");
            sb.AppendLine();
            foreach (var product in deadStock.Take(15))
            {
                sb.AppendLine($"  {FormatOptional(product.Name).PadRight(24)} {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}");
            }

            sb.AppendLine();
            sb.Append($"Total: {deadStock.Count} produk | Pertimbangkan promosi atau retur ke supplier.");
            return sb.ToString();
        }

        private async Task<List<Product>> GetDeadStockWithShadowFilterAsync()
        {
            if (_posDbService == null)
            {
                return new List<Product>();
            }

            var deadStock = await _posDbService.GetDeadStockProductsAsync();
            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            var mappedParentIds = new HashSet<string>(
                mappings.Select(mapping => mapping.ParentProductId),
                StringComparer.OrdinalIgnoreCase);

            var filtered = new List<Product>();
            foreach (var product in deadStock)
            {
                if (!string.IsNullOrWhiteSpace(product.Id) &&
                    mappedParentIds.Contains(product.Id))
                {
                    var mapping = mappings.FirstOrDefault(item =>
                        string.Equals(item.ParentProductId, product.Id, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(mapping?.ChildProductId) &&
                        await _posDbService.GetProductSoldQuantityAsync(mapping.ChildProductId, 30) > 0)
                    {
                        continue;
                    }
                }

                filtered.Add(product);
            }

            return filtered;
        }

        private async Task<List<Product>> GetUnmappedLargeUnitProductsAsync(int limit = 50)
        {
            if (_posDbService == null)
            {
                return new List<Product>();
            }

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            var mappedParentIds = new HashSet<string>(
                mappings.Select(mapping => mapping.ParentProductId),
                StringComparer.OrdinalIgnoreCase);

            return (await _posDbService.GetLargeUnitProductsAsync(limit))
                .Where(product => string.IsNullOrWhiteSpace(product.Id) || !mappedParentIds.Contains(product.Id))
                .ToList();
        }

        private async Task<string> HandleShadowStockAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var products = await GetUnmappedLargeUnitProductsAsync(50);
            if (!products.Any())
            {
                return "Semua produk unit besar aktif yang terdeteksi sudah punya mapping keluarga.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} PRODUK UNIT BESAR BELUM DIMAPPING");
            sb.AppendLine();
            foreach (var product in products.Take(20))
            {
                decimal sold30d = string.IsNullOrWhiteSpace(product.Id)
                    ? 0
                    : await _posDbService.GetProductSoldQuantityAsync(product.Id, 30);
                sb.AppendLine($"  {FormatOptional(product.Name)} | {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)} | sold30d={FormatDisplayQuantity(sold30d)}");
            }

            sb.AppendLine();
            sb.Append("Ketik /set_family [nama unit besar] -> [nama unit kecil] @ [isi] untuk mapping.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleListFamilyAsync(string args = "")
        {
            var families = await GetDualStockFamiliesAsync(args);
            if (!families.Any())
            {
                return string.IsNullOrWhiteSpace(args)
                    ? "Belum ada mapping family/dual stock."
                    : $"Mapping family untuk \"{args}\" tidak ditemukan.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} LIST FAMILY / DUAL STOCK");
            if (!string.IsNullOrWhiteSpace(args))
            {
                sb.AppendLine($"Filter: {args.Trim()}");
            }

            sb.AppendLine();
            int index = 1;
            foreach (var family in families.Take(20))
            {
                AppendDualStockFamilyDetail(sb, family, index);
                index++;
            }

            if (families.Count > 20)
            {
                sb.AppendLine($"... {families.Count - 20} mapping lain tidak ditampilkan.");
            }

            sb.AppendLine();
            sb.Append("Shadow parent = stok unit besar dikonversi ke unit kecil. Tidak mengubah stok Aronium.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleDualStockCommandAsync(string args)
        {
            return await HandleListFamilyAsync(args);
        }

        private async Task<string> HandleDualStockAlertCommandAsync()
        {
            var deficits = await GetDualStockDeficitFamiliesAsync();
            if (!deficits.Any())
            {
                return $"{IconCheck} Tidak ada defisit dual stock saat ini.";
            }

            return BuildDualStockAlertMessage(deficits.Select(BuildDualStockDeficitLine).ToList());
        }

        private async Task<string> HandleDualStockSyncCommandAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (_configService.Config?.Automation?.EnableDualStockSync == false)
            {
                return "Dual stock sync nonaktif di Settings.";
            }

            var scan = await RunDualStockEquilibriumScanAsync(DateTime.Now, "manual bot command");
            var sb = new StringBuilder();
            sb.AppendLine($"{IconCheck} Konsolidasi dual stock selesai.");
            sb.AppendLine($"Mapping dievaluasi: {scan.Processed}");
            if (scan.Alerts.Any())
            {
                sb.AppendLine();
                sb.AppendLine(BuildDualStockAlertMessage(scan.Alerts));
            }

            return sb.ToString().TrimEnd();
        }

        private string HandleDualStockWatcherCommand(string args)
        {
            var automation = _configService.Config?.Automation;
            if (automation == null)
            {
                return "Config automation belum tersedia.";
            }

            string mode = (args ?? string.Empty).Trim().ToLowerInvariant();
            if (mode is "on" or "aktif" or "enable")
            {
                automation.EnableDualStockRealtimeWatcher = true;
                _configService.SaveConfig();
                return $"{IconCheck} Realtime watcher DualStock diaktifkan. Jika loop watcher belum berjalan, restart runtime/aplikasi agar timer dimulai.";
            }

            if (mode is "off" or "mati" or "disable")
            {
                automation.EnableDualStockRealtimeWatcher = false;
                _configService.SaveConfig();
                return $"{IconCheck} Realtime watcher DualStock dimatikan. Tick berikutnya tidak akan memproses watcher realtime.";
            }

            return "DualStock watcher:\n" +
                   $"- Sync utama: {FormatEnabled(automation.EnableDualStockSync)}\n" +
                   $"- Realtime watcher: {FormatEnabled(automation.EnableDualStockRealtimeWatcher)}\n" +
                   $"- Interval: {GetDualStockSyncIntervalSeconds()} detik\n\n" +
                   "Command: /dual_stock_watcher on atau /dual_stock_watcher off";
        }

        private string HandleDualStockChannelCommand(string args)
        {
            var automation = _configService.Config?.Automation;
            if (automation == null)
            {
                return "Config automation belum tersedia.";
            }

            var tokens = (args ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length >= 2 && TryParseOnOff(tokens[1], out bool enabled))
            {
                string channel = tokens[0].ToLowerInvariant();
                if (channel is "telegram" or "tg")
                {
                    automation.EnableTelegramDualStockAlerts = enabled;
                }
                else if (channel is "cloud" or "wa_cloud" or "whatsapp_cloud" or "meta")
                {
                    automation.EnableWhatsAppCloudDualStockAlerts = enabled;
                }
                else if (channel is "baileys" or "local" or "wa_local")
                {
                    automation.EnableBaileysDualStockAlerts = enabled;
                }
                else
                {
                    return "Channel tidak dikenal. Gunakan: telegram, cloud, atau baileys.";
                }

                _configService.SaveConfig();
                return $"{IconCheck} Channel alert DualStock {channel} diset {FormatEnabled(enabled)}.";
            }

            return "Channel alert DualStock:\n" +
                   $"- Telegram: {FormatEnabled(automation.EnableTelegramDualStockAlerts)}\n" +
                   $"- WhatsApp Cloud API: {FormatEnabled(automation.EnableWhatsAppCloudDualStockAlerts)}\n" +
                   $"- WhatsApp Baileys: {FormatEnabled(automation.EnableBaileysDualStockAlerts)}\n\n" +
                   "Command: /dual_stock_channel <telegram|cloud|baileys> <on|off>";
        }

        private async Task<List<ProductFamilyStock>> GetDualStockFamiliesAsync(string? query = null)
        {
            var result = new List<ProductFamilyStock>();
            if (_posDbService == null)
            {
                return result;
            }

            string filter = (query ?? string.Empty).Trim();
            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            foreach (var mapping in mappings.Where(mapping => mapping.ConversionRate > 0))
            {
                var parent = await _posDbService.GetProductByIdAsync(mapping.ParentProductId);
                var child = await _posDbService.GetProductByIdAsync(mapping.ChildProductId);
                if (parent == null || child == null)
                {
                    continue;
                }

                var family = new ProductFamilyStock
                {
                    Mapping = mapping,
                    ParentProduct = parent,
                    ChildProduct = child,
                    ParentStock = parent.Stock ?? 0,
                    ChildStock = child.Stock ?? 0,
                    ConversionRate = mapping.ConversionRate
                };

                if (string.IsNullOrWhiteSpace(filter) || MatchesDualStockFamily(family, filter))
                {
                    result.Add(family);
                }
            }

            return result
                .OrderByDescending(IsDualStockFamilyDeficit)
                .ThenBy(family => family.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<ProductFamilyStock>> GetDualStockDeficitFamiliesAsync()
        {
            return (await GetDualStockFamiliesAsync())
                .Where(IsDualStockFamilyDeficit)
                .ToList();
        }

        private static bool MatchesDualStockFamily(ProductFamilyStock family, string query)
        {
            return (family.FamilyName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                   (family.ParentProduct.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                   (family.ChildProduct.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                   (family.Mapping.ParentProductName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                   (family.Mapping.ChildProductName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
        }

        private static bool IsDualStockFamilyDeficit(ProductFamilyStock family)
        {
            return family.ParentStock < 0 || family.TotalChildStock <= 0;
        }

        private string BuildDualStockDeficitLine(ProductFamilyStock family)
        {
            return $"{FormatOptional(family.FamilyName)}: " +
                   $"{FormatOptional(family.ParentProduct.Name)} {FormatStockValue(family.ParentStock)} {GetUnitLabel(family.ParentProduct.Unit)}, " +
                   $"{FormatOptional(family.ChildProduct.Name)} {FormatStockValue(family.ChildStock)} {GetUnitLabel(family.ChildProduct.Unit)}, " +
                   $"total {FormatStockValue(family.TotalChildStock)} {GetUnitLabel(family.ChildProduct.Unit)}.";
        }

        private string BuildDualStockAlertMessage(IReadOnlyCollection<string> alerts)
        {
            if (!alerts.Any())
            {
                return $"{IconCheck} Tidak ada defisit dual stock.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} DEFISIT DUAL STOCK");
            sb.AppendLine();
            foreach (var alert in alerts.Take(10))
            {
                sb.AppendLine($"- {alert}");
            }

            if (alerts.Count > 10)
            {
                sb.AppendLine($"... {alerts.Count - 10} alert lain tidak ditampilkan.");
            }

            sb.AppendLine();
            sb.Append("Input restock atau lakukan /inventory_family untuk koreksi stok fisik keluarga.");
            return sb.ToString().TrimEnd();
        }

        private string BuildDualStockFamilyCompactLine(ProductFamilyStock family)
        {
            return $"{FormatOptional(family.FamilyName)} | " +
                   $"{FormatStockValue(family.ParentStock)} {GetUnitLabel(family.ParentProduct.Unit)} + " +
                   $"{FormatStockValue(family.ChildStock)} {GetUnitLabel(family.ChildProduct.Unit)} | " +
                   $"total {FormatStockValue(family.TotalChildStock)} {GetUnitLabel(family.ChildProduct.Unit)}";
        }

        private void AppendDualStockFamilyDetail(StringBuilder sb, ProductFamilyStock family, int index)
        {
            decimal parentShadow = family.ParentStock * family.ConversionRate;
            sb.AppendLine($"{index}. {FormatOptional(family.FamilyName)}");
            sb.AppendLine($"   Parent: {FormatOptional(family.ParentProduct.Name)} = {FormatStockValue(family.ParentStock)} {GetUnitLabel(family.ParentProduct.Unit)} (shadow {FormatStockValue(parentShadow)} {GetUnitLabel(family.ChildProduct.Unit)})");
            sb.AppendLine($"   Child : {FormatOptional(family.ChildProduct.Name)} = {FormatStockValue(family.ChildStock)} {GetUnitLabel(family.ChildProduct.Unit)}");
            sb.AppendLine($"   Rasio : 1 {GetUnitLabel(family.ParentProduct.Unit)} = {FormatStockValue(family.ConversionRate)} {GetUnitLabel(family.ChildProduct.Unit)}");
            sb.AppendLine($"   Total : {FormatStockValue(family.TotalChildStock)} {GetUnitLabel(family.ChildProduct.Unit)} ({FormatStockValue(family.TotalParentStock)} {GetUnitLabel(family.ParentProduct.Unit)})");
            sb.AppendLine($"   Status: {BuildDualStockFamilyStatus(family)}");
        }

        private string BuildDualStockFamilyStatus(ProductFamilyStock family)
        {
            if (IsDualStockFamilyDeficit(family))
            {
                return $"{IconWarning} defisit";
            }

            if (family.ChildStock >= family.ConversionRate)
            {
                return "perlu auto-pack";
            }

            if (family.ChildStock <= -family.ConversionRate && family.ParentStock > 0)
            {
                return "perlu auto-break";
            }

            return $"{IconCheck} seimbang";
        }

        private static bool TryParseOnOff(string value, out bool enabled)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized is "on" or "aktif" or "enable" or "enabled" or "true" or "1")
            {
                enabled = true;
                return true;
            }

            if (normalized is "off" or "mati" or "disable" or "disabled" or "false" or "0")
            {
                enabled = false;
                return true;
            }

            enabled = false;
            return false;
        }

        private static string FormatEnabled(bool value)
        {
            return value ? "aktif" : "nonaktif";
        }

        private async Task<string> HandleEffectiveStockAsync(string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                return "Format: /stok_efektif <nama produk unit besar>";
            }

            var (parent, parentError) = await TryResolveProductAsync(args, isMutation: false, actionLabel: "cek stok efektif");
            if (parent == null || !string.IsNullOrWhiteSpace(parentError))
            {
                return parentError ?? $"Produk \"{args}\" tidak ditemukan.";
            }

            var mapping = string.IsNullOrWhiteSpace(parent.Id)
                ? null
                : await _databaseService.GetConversionByParentIdAsync(parent.Id);
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.ChildProductId))
            {
                return await BuildEffectiveStockMappingSuggestionAsync(parent, args);
            }

            var child = await _posDbService.GetProductByIdAsync(mapping.ChildProductId);
            if (child == null)
            {
                return $"{IconWarning} Produk turunan mapping tidak ditemukan di pos.db: {mapping.ChildProductName ?? mapping.ChildProductId}.";
            }

            decimal parentStock = parent.Stock ?? 0;
            decimal childStock = child.Stock ?? 0;
            decimal shadowQty = parentStock * mapping.ConversionRate;
            decimal effectiveStock = shadowQty + childStock;

            var sb = new StringBuilder();
            sb.AppendLine($"{IconSearch} STOK EFEKTIF - {FormatOptional(mapping.FamilyName ?? parent.Name)}");
            sb.AppendLine();
            sb.AppendLine("Unit fisik di Aronium:");
            sb.AppendLine($"  {FormatOptional(parent.Name)} -> {FormatStockValue(parentStock)} {GetUnitLabel(parent.Unit)} (fisik)");
            sb.AppendLine($"  {FormatOptional(child.Name)} -> {FormatStockValue(childStock)} {GetUnitLabel(child.Unit)} (fisik)");
            sb.AppendLine();
            sb.AppendLine("Konversi shadow:");
            sb.AppendLine($"  {FormatStockValue(parentStock)} {GetUnitLabel(parent.Unit)} x {FormatStockValue(mapping.ConversionRate)} = {FormatStockValue(shadowQty)} {GetUnitLabel(child.Unit)} (shadow)");
            sb.AppendLine($"  + {FormatStockValue(childStock)} {GetUnitLabel(child.Unit)} = {FormatStockValue(effectiveStock)} {GetUnitLabel(child.Unit)} efektif");
            sb.AppendLine();
            sb.AppendLine("Status:");
            if (childStock < 0 && parentStock > 0)
            {
                sb.Append($"{IconWarning} Stok unit kecil minus tapi unit besar masih ada. Pertimbangkan konversi/opname, bukan restock otomatis.");
            }
            else
            {
                sb.Append("Shadow stock hanya analisa. Tidak mengubah stok fisik Aronium.");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleSetFamilyFlexibleAsync(string args, AutomationExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return "Format: /set_family [nama unit besar] -> [nama unit kecil] @ [isi]\nContoh: /set_family Kapal Api Mix@1Dus -> Kapal Api mix @ 12";
            }

            if (!TryParseSetFamilyIntent(args, out var intent))
            {
                return "Format: /set_family [unit besar] -> [unit kecil] @ [isi]\nContoh lain: /set_family SCORPION 1 Pak SCORP @10";
            }

            intent.OriginalMessage = args;
            return await HandleShadowMappingIntentAsync(intent, context, requireConfirmForSingleMatch: false);
        }

        private async Task<string> BuildEffectiveStockMappingSuggestionAsync(Product product, string originalQuery)
        {
            string keyword = BuildShadowKeyword(product.Name ?? originalQuery);
            var parentCandidates = await FindShadowMappingCandidatesAsync(keyword, product.Unit, preferBulk: true);
            var childCandidates = await FindShadowMappingCandidatesAsync(keyword, null, preferBulk: false);
            if (IsBulkReceiptUnit(product.Unit) && !string.IsNullOrWhiteSpace(product.Id))
            {
                parentCandidates = parentCandidates
                    .OrderByDescending(candidate => string.Equals(candidate.Product.Id, product.Id, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(candidate => candidate.Confidence)
                    .ToList();
            }
            else if (IsChildReceiptUnit(product.Unit) && !string.IsNullOrWhiteSpace(product.Id))
            {
                childCandidates = childCandidates
                    .OrderByDescending(candidate => string.Equals(candidate.Product.Id, product.Id, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(candidate => candidate.Confidence)
                    .ToList();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} STOK EFEKTIF - {FormatOptional(product.Name)}");
            sb.AppendLine();
            sb.AppendLine("Mapping keluarga belum dikonfigurasi.");
            if (parentCandidates.Any() || childCandidates.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Kemungkinan unit terkait:");
                foreach (var candidate in parentCandidates.Take(2))
                {
                    sb.AppendLine($"  Unit besar: {FormatOptional(candidate.Product.Name)} | {FormatStockValue(candidate.Product.Stock ?? 0)} {GetUnitLabel(candidate.Product.Unit)} | confidence {candidate.Confidence}%");
                }
                foreach (var candidate in childCandidates.Take(2))
                {
                    sb.AppendLine($"  Unit kecil: {FormatOptional(candidate.Product.Name)} | {FormatStockValue(candidate.Product.Stock ?? 0)} {GetUnitLabel(candidate.Product.Unit)} | confidence {candidate.Confidence}%");
                }
            }

            var parent = parentCandidates.FirstOrDefault()?.Product;
            var child = childCandidates.FirstOrDefault()?.Product;
            sb.AppendLine();
            if (parent != null && child != null)
            {
                sb.AppendLine("Ketik perintah ini untuk mapping:");
                sb.AppendLine($"  /set_family {parent.Name} -> {child.Name} @ 12");
                sb.AppendLine();
                sb.Append("Ganti angka 12 dengan isi sebenarnya per unit besar.");
            }
            else
            {
                sb.Append("Gunakan /set_family [unit besar] -> [unit kecil] @ [isi] setelah nama produk jelas.");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildShadowKeyword(string value)
        {
            var tokens = GetShadowNameTokens(value);
            return tokens.Any() ? string.Join(' ', tokens) : value;
        }

        private bool TryParseSetFamilyIntent(string args, out ShadowMappingIntent intent)
        {
            intent = new ShadowMappingIntent();
            string source = args.Trim();
            var arrowMatch = Regex.Match(
                source,
                @"^(?<parent>.+?)\s*(?:->|=>|>|â†’|ke)\s*(?<child>.+?)\s*(?:@|isi)\s*(?<rate>\d+(?:[,.]\d+)?)\s*$",
                RegexOptions.IgnoreCase);
            if (arrowMatch.Success &&
                TryParseDecimal(arrowMatch.Groups["rate"].Value.Replace(',', '.'), out var arrowRate) &&
                arrowRate > 0)
            {
                intent.ParentQuery = arrowMatch.Groups["parent"].Value.Trim();
                intent.ChildQuery = arrowMatch.Groups["child"].Value.Trim();
                intent.Rate = arrowRate;
                return true;
            }

            var compactMatch = Regex.Match(
                source,
                @"^(?<body>.+?)\s*@?\s*(?<rate>\d+(?:[,.]\d+)?)\s*$",
                RegexOptions.IgnoreCase);
            if (!compactMatch.Success ||
                !TryParseDecimal(compactMatch.Groups["rate"].Value.Replace(',', '.'), out var compactRate) ||
                compactRate <= 0)
            {
                return false;
            }

            string body = compactMatch.Groups["body"].Value.Trim().TrimEnd('@').Trim();
            var unitSplitMatch = Regex.Match(
                body,
                @"^(?<keyword>.+?)\s+(?:\d+\s*)?(?<parentUnit>dus|pak|box|krat|bal|bks)\s+(?<child>.+)$",
                RegexOptions.IgnoreCase);
            if (unitSplitMatch.Success)
            {
                string keyword = unitSplitMatch.Groups["keyword"].Value.Trim();
                string parentUnit = unitSplitMatch.Groups["parentUnit"].Value.Trim();
                string child = unitSplitMatch.Groups["child"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(keyword) && !string.IsNullOrWhiteSpace(child))
                {
                    intent.ProductKeyword = keyword;
                    intent.ParentQuery = $"{keyword} {parentUnit}";
                    intent.ChildQuery = child;
                    intent.ParentUnit = parentUnit;
                    intent.Rate = compactRate;
                    return true;
                }
            }

            var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
            {
                return false;
            }

            intent.ParentQuery = string.Join(' ', tokens.Take(tokens.Length - 1));
            intent.ChildQuery = tokens[^1];
            intent.Rate = compactRate;
            return true;
        }

        private async Task<string> SaveUnitConversionMappingAsync(Product parent, Product child, decimal rate, string notes)
        {
            if (string.IsNullOrWhiteSpace(parent.Id) || string.IsNullOrWhiteSpace(child.Id))
            {
                return "Produk parent/child tidak punya ID valid.";
            }

            await _databaseService.UpsertUnitConversionAsync(new UnitConversionMapping
            {
                ParentProductId = parent.Id,
                ParentProductName = parent.Name,
                ChildProductId = child.Id,
                ChildProductName = child.Name,
                ConversionRate = rate,
                FamilyName = BuildFamilyName(parent.Name, child.Name),
                Notes = notes,
                UpdatedAt = DateTime.Now
            });

            return $"{IconCheck} Mapping disimpan: 1 {FormatOptional(parent.Name)} = {FormatStockValue(rate)} {FormatOptional(child.Name)}. Shadow stock hanya untuk analisa dan tidak mengubah Aronium.";
        }

        private async Task<string> HandleShadowMappingIntentAsync(
            ShadowMappingIntent intent,
            AutomationExecutionContext? context,
            bool requireConfirmForSingleMatch)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var parentCandidates = await FindShadowMappingCandidatesAsync(
                string.IsNullOrWhiteSpace(intent.ParentQuery) ? intent.ProductKeyword : intent.ParentQuery,
                intent.ParentUnit,
                preferBulk: true);
            var childCandidates = await FindShadowMappingCandidatesAsync(
                string.IsNullOrWhiteSpace(intent.ChildQuery) ? intent.ProductKeyword : intent.ChildQuery,
                intent.ChildUnit,
                preferBulk: false);

            if (!parentCandidates.Any() || !childCandidates.Any())
            {
                return BuildNoShadowMappingCandidateMessage(intent, parentCandidates, childCandidates);
            }

            var pending = new ShadowMappingPendingState
            {
                ParentCandidates = parentCandidates.Take(5).ToList(),
                ChildCandidates = childCandidates.Take(5).ToList(),
                Rate = intent.Rate,
                ParentUnit = intent.ParentUnit,
                ChildUnit = intent.ChildUnit,
                OriginalMessage = intent.OriginalMessage,
                ExpiresAt = DateTime.Now.AddMinutes(10)
            };

            bool singleConfidentMatch = pending.ParentCandidates.Count == 1 &&
                                        pending.ChildCandidates.Count == 1 &&
                                        pending.ParentCandidates[0].Confidence >= 85 &&
                                        pending.ChildCandidates[0].Confidence >= 85;
            if (singleConfidentMatch && !requireConfirmForSingleMatch)
            {
                return await SaveUnitConversionMappingAsync(
                    pending.ParentCandidates[0].Product,
                    pending.ChildCandidates[0].Product,
                    pending.Rate,
                    "Manual mapping via /set_family");
            }

            if (singleConfidentMatch)
            {
                pending.SelectedParentIndex = 0;
                pending.SelectedChildIndex = 0;
            }

            if (context != null)
            {
                _shadowMappingPendingBySender[BuildSenderStateKey(context)] = pending;
            }

            return BuildShadowMappingCandidateMessage(pending, singleConfidentMatch);
        }

        private async Task<List<ShadowMappingCandidate>> FindShadowMappingCandidatesAsync(string query, string? unit, bool preferBulk)
        {
            var matches = await FindProductMatchesAsync(query, 20);
            return matches
                .Select(match =>
                {
                    int score = match.Score;
                    bool unitMatches = !string.IsNullOrWhiteSpace(unit) &&
                                       string.Equals(NormalizeReceiptUnit(match.Product.Unit), NormalizeReceiptUnit(unit), StringComparison.OrdinalIgnoreCase);
                    if (unitMatches)
                    {
                        score += 25;
                    }

                    if (preferBulk && IsBulkReceiptUnit(match.Product.Unit))
                    {
                        score += 15;
                    }
                    else if (!preferBulk && IsChildReceiptUnit(match.Product.Unit))
                    {
                        score += 15;
                    }
                    else
                    {
                        score -= 10;
                    }

                    if (preferBulk && HasPackageMarker(match.Product.Name))
                    {
                        score += 10;
                    }

                    return new ShadowMappingCandidate
                    {
                        Product = match.Product,
                        Confidence = Math.Clamp(score, 0, 100)
                    };
                })
                .Where(candidate => candidate.Confidence >= 35)
                .GroupBy(candidate => candidate.Product.Id ?? candidate.Product.Name ?? Guid.NewGuid().ToString("N"))
                .Select(group => group.OrderByDescending(candidate => candidate.Confidence).First())
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.Product.Name)
                .Take(5)
                .ToList();
        }

        private string BuildNoShadowMappingCandidateMessage(
            ShadowMappingIntent intent,
            IReadOnlyCollection<ShadowMappingCandidate> parentCandidates,
            IReadOnlyCollection<ShadowMappingCandidate> childCandidates)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} Saya belum menemukan pasangan produk untuk mapping stok.");
            sb.AppendLine();
            sb.AppendLine($"Input: {intent.OriginalMessage}");
            sb.AppendLine($"Konversi terbaca: 1 {FormatOptional(intent.ParentUnit ?? "unit besar")} = {FormatStockValue(intent.Rate)} {FormatOptional(intent.ChildUnit ?? "unit kecil")}");
            if (!parentCandidates.Any())
            {
                sb.AppendLine("- Kandidat unit besar belum ditemukan.");
            }
            if (!childCandidates.Any())
            {
                sb.AppendLine("- Kandidat unit kecil belum ditemukan.");
            }
            sb.AppendLine();
            sb.Append("Coba pakai nama produk lebih lengkap. Contoh: /set_family Kapal Api Mix@1Dus -> Kapal Api mix @ 12");
            return sb.ToString();
        }

        private string BuildShadowMappingCandidateMessage(ShadowMappingPendingState pending, bool singleConfidentMatch)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconSearch} Saya menemukan kemungkinan mapping:");
            sb.AppendLine();
            sb.AppendLine("Parent:");
            for (int i = 0; i < pending.ParentCandidates.Count; i++)
            {
                var candidate = pending.ParentCandidates[i];
                sb.AppendLine($"{i + 1}. {FormatOptional(candidate.Product.Name)} | stok {FormatStockValue(candidate.Product.Stock ?? 0)} {GetUnitLabel(candidate.Product.Unit)} | confidence {candidate.Confidence}%");
            }
            sb.AppendLine();
            sb.AppendLine("Child:");
            for (int i = 0; i < pending.ChildCandidates.Count; i++)
            {
                var candidate = pending.ChildCandidates[i];
                char label = (char)('A' + i);
                sb.AppendLine($"{label}. {FormatOptional(candidate.Product.Name)} | stok {FormatStockValue(candidate.Product.Stock ?? 0)} {GetUnitLabel(candidate.Product.Unit)} | confidence {candidate.Confidence}%");
            }
            sb.AppendLine();
            sb.AppendLine($"Konversi: 1 {FormatOptional(pending.ParentUnit ?? "unit besar")} = {FormatStockValue(pending.Rate)} {FormatOptional(pending.ChildUnit ?? "unit kecil")}");
            sb.AppendLine();
            if (singleConfidentMatch)
            {
                sb.AppendLine("Balas /confirm untuk simpan mapping ini.");
            }
            else
            {
                sb.AppendLine("Balas kombinasi seperti 1A untuk simpan mapping.");
            }
            sb.Append("/cancel untuk batal.");
            return sb.ToString();
        }

        private async Task<string> HandleSetFamilyAsync(string args, AutomationExecutionContext? context = null)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return "Format: /set_family [nama unit besar] -> [nama unit kecil] @ [isi]\nContoh: /set_family Kapal Api Mix@1Dus -> Kapal Api mix @ 12";
            }

            string[] parts = Regex.Split(args, @"\s*(?:->|=>|→)\s*", RegexOptions.IgnoreCase);
            if (parts.Length != 2)
            {
                return "Format: /set_family [nama unit besar] -> [nama unit kecil] @ [isi]";
            }

            var rightMatch = Regex.Match(parts[1].Trim(), @"^(?<child>.+?)\s*(?:@|isi)\s*(?<rate>\d+(?:[,.]\d+)?)\s*$", RegexOptions.IgnoreCase);
            if (!rightMatch.Success || !TryParseDecimal(rightMatch.Groups["rate"].Value.Replace(',', '.'), out var rate) || rate <= 0)
            {
                return "Format unit kecil harus seperti: Kapal Api mix @ 12";
            }

            var (parent, parentError) = await TryResolveProductAsync(parts[0].Trim(), isMutation: false, actionLabel: "set family");
            if (parent == null || !string.IsNullOrWhiteSpace(parentError))
            {
                return parentError ?? $"Produk \"{parts[0].Trim()}\" tidak ditemukan.";
            }

            string childQuery = rightMatch.Groups["child"].Value.Trim();
            var (child, childError) = await TryResolveProductAsync(childQuery, isMutation: false, actionLabel: "set family");
            if (child == null || !string.IsNullOrWhiteSpace(childError))
            {
                return childError ?? $"Produk \"{childQuery}\" tidak ditemukan.";
            }

            if (string.IsNullOrWhiteSpace(parent.Id) || string.IsNullOrWhiteSpace(child.Id))
            {
                return "Produk parent/child tidak punya ID valid.";
            }

            await _databaseService.UpsertUnitConversionAsync(new UnitConversionMapping
            {
                ParentProductId = parent.Id,
                ParentProductName = parent.Name,
                ChildProductId = child.Id,
                ChildProductName = child.Name,
                ConversionRate = rate,
                FamilyName = BuildFamilyName(parent.Name, child.Name),
                Notes = "Manual mapping via /set_family",
                UpdatedAt = DateTime.Now
            });

            return $"{IconCheck} Mapping disimpan: 1 {FormatOptional(parent.Name)} = {FormatStockValue(rate)} {FormatOptional(child.Name)}. Shadow stock hanya untuk analisa dan tidak mengubah Aronium.";
        }

        private async Task<string> HandleDeleteFamilyAsync(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return "Format: /hapus_family <nama produk unit besar>";
            }

            var (parent, parentError) = await TryResolveProductAsync(args, isMutation: false, actionLabel: "hapus family");
            if (parent == null || !string.IsNullOrWhiteSpace(parentError))
            {
                return parentError ?? $"Produk \"{args}\" tidak ditemukan.";
            }

            if (string.IsNullOrWhiteSpace(parent.Id))
            {
                return "Produk parent tidak punya ID valid.";
            }

            await _databaseService.DeleteUnitConversionByParentIdAsync(parent.Id);
            return $"{IconCheck} Mapping family untuk {FormatOptional(parent.Name)} sudah dihapus.";
        }

        private static string BuildFamilyName(string? parentName, string? childName)
        {
            string parent = parentName ?? string.Empty;
            string child = childName ?? string.Empty;
            string shortest = parent.Length <= child.Length ? parent : child;
            return string.IsNullOrWhiteSpace(shortest) ? parentName ?? childName ?? "Family" : shortest;
        }

        private async Task<string> HandleRestockHistoryAsync(string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                return $"{IconCross} Format salah.\nGunakan: /riwayat_restock <nama produk>\nContoh: /riwayat_restock kapal api mix";
            }

            var (product, error) = await TryResolveProductAsync(args, isMutation: false, actionLabel: "lihat riwayat restock");
            if (product == null || !string.IsNullOrWhiteSpace(error))
            {
                return error ?? $"Produk \"{args}\" tidak ditemukan.";
            }

            var history = await _posDbService.GetRestockHistoryAsync(product.Id);
            if (!history.Any())
            {
                return $"Belum ada riwayat restock untuk {product.Name}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} RIWAYAT RESTOCK - {product.Name}");
            sb.AppendLine();
            sb.AppendLine("  Tgl    Qty               Modal/unit     Total");
            AppendAlignedRows(
                sb,
                history.Take(10).Select(h => (
                    Name: FormatShortDate(h.Date),
                    Col2: $"{FormatDisplayQuantity(h.Quantity)} {GetUnitLabel(product.Unit)}",
                    Col3: $"@ {FormatCurrency(h.Price)}",
                    Col4: $"= {FormatCurrency(h.Total)}{(h.Price <= 0 ? $" {IconWarning}" : string.Empty)}")));
            sb.AppendLine();
            sb.Append($"Total restock ditampilkan: {Math.Min(10, history.Count)} entri");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandlePurchaseHistoryIntentAsync(string? productQuery, AutomationExecutionContext context)
        {
            string query = productQuery?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query) &&
                GetActiveTopicState(context) is { Topic: "produk", EntityName: { Length: > 0 } } productTopic)
            {
                query = productTopic.EntityName!;
            }

            return await HandleRestockHistoryAsync(query);
        }

        private async Task<string> HandleRecentDocumentsAsync(string args, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            int? typeId = args.Trim().ToLowerInvariant() switch
            {
                "purchase" or "pembelian" or "restock" => 1,
                "sales" or "sale" or "penjualan" => 2,
                _ => null
            };

            var documents = await _posDbService.GetRecentDocumentsAsync(typeId, 5);
            if (!documents.Any())
            {
                return "Belum ada dokumen terakhir yang bisa ditampilkan.";
            }

            var first = documents[0];
            if (!string.IsNullOrWhiteSpace(first.Number))
            {
                _lastDocumentBySender[BuildSenderStateKey(context)] = first.Number;
                SetTopicState(context, "dokumen", entityId: first.Id, entityName: first.Number);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconDocument} DOKUMEN TERAKHIR");
            sb.AppendLine();
            foreach (var document in documents)
            {
                sb.AppendLine($"{FormatShortDate(document.Date)} | {FormatCompactDocumentNumber(document.Number)} | {FormatOptional(document.DocumentTypeLabel)} | {FormatOptional(document.CustomerName)} | {FormatCurrency(document.Total)}");
            }

            sb.AppendLine();
            sb.Append("Ketik /dokumen <nomor> untuk detail item.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleExpiryInfoAsync(string productQuery, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = productQuery.Trim();
            List<Product> expiring;
            try
            {
                expiring = await _posDbService.GetExpiringProductsAsync(3650);
            }
            catch
            {
                expiring = new List<Product>();
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                expiring = expiring
                    .Where(product => !string.IsNullOrWhiteSpace(product.Name) &&
                                      product.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (expiring.Any())
            {
                var orderedExpiring = expiring
                    .OrderBy(product => product.ExpiryDate ?? DateTime.MaxValue)
                    .ThenBy(product => product.Name)
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"{IconCalendar} PRODUK DENGAN DATA EXPIRED");
                sb.AppendLine();
                foreach (var product in orderedExpiring.Take(10))
                {
                    int daysLeft = product.ExpiryDate.HasValue
                        ? (int)Math.Floor((product.ExpiryDate.Value.Date - DateTime.Today).TotalDays)
                        : 0;
                    sb.AppendLine($"  {FormatOptional(product.Name)} | exp {FormatDateTime(product.ExpiryDate)} | {daysLeft} hari | stok {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}");
                }

                if (!string.IsNullOrWhiteSpace(query) && orderedExpiring.Count == 1)
                {
                    var product = orderedExpiring[0];
                    int daysLeft = product.ExpiryDate.HasValue
                        ? (int)Math.Floor((product.ExpiryDate.Value.Date - DateTime.Today).TotalDays)
                        : 0;
                    SetTopicState(
                        context,
                        "expired",
                        entityId: product.Id,
                        entityName: product.Name,
                        expiryDate: product.ExpiryDate,
                        daysLeft: daysLeft,
                        stock: product.Stock,
                        unit: product.Unit);
                }

                sb.AppendLine();
                sb.Append($"{IconEye} {orderedExpiring.Count} produk memiliki data expired. Isi tanggal expired saat input restock di Aronium.");

                return sb.ToString().TrimEnd();
            }

            return string.IsNullOrWhiteSpace(query)
                ? "Tidak ada produk dengan data expired di sistem."
                : $"Tidak ada data expired untuk produk \"{query}\" di sistem.";
        }

        private async Task<string> HandleSlowMovingProductsAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var products = await _posDbService.GetSlowMovingProductsAsync(days: 30, thresholdQty: 5, limit: 15);
            if (!products.Any())
            {
                return "Belum ada produk slow moving sesuai kriteria V6: masih terjual tetapi di bawah 40% rata-rata kategorinya dalam 30 hari.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("\U0001F422 SLOW MOVING (Layer A)");
            sb.AppendLine("Kriteria: masih terjual, tetapi <40% rata-rata kategori per 30 hari.");
            sb.AppendLine();
            for (int i = 0; i < products.Count; i++)
            {
                var product = products[i];
                bool stockNeedsCorrection = product.CurrentStock < 0;
                string prefix = stockNeedsCorrection ? $"{IconWarning} [STOK PERLU KOREKSI] " : $"{i + 1}. ";
                sb.AppendLine($"{prefix}{FormatOptional(product.ProductName)} | {GetUnitLabel(product.Unit)} | stok={FormatStockValue(product.CurrentStock)}");
                sb.AppendLine($"   Terjual 30hr: {FormatDisplayQuantity(product.QuantitySold)} (vs avg {FormatOptional(product.Category)}: {FormatDisplayQuantity(product.AverageCategoryQuantity)}) -> {product.PercentVsCategory:0.#}% dari rata-rata");
                if (stockNeedsCorrection)
                {
                    sb.AppendLine("   Stok minus tapi produk masih bergerak -> cek via /stok atau opname.");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleSleepingStockAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var products = await _posDbService.GetSleepingMandatoryProductsAsync(days: 30, limit: 30);
            if (!products.Any())
            {
                return "Tidak ada sleeping mandatory stock saat ini.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("\U0001F3E5 SLEEPING MANDATORY (Layer C)");
            sb.AppendLine("Kategori wajib ada. Jangan retur/hapus; pertahankan dan cek expired.");
            sb.AppendLine();
            foreach (var product in products.Take(20))
            {
                sb.AppendLine($"- {FormatOptional(product.ProductName)} | {FormatOptional(product.Category)} | stok {FormatStockValue(product.CurrentStock)} {GetUnitLabel(product.Unit)} | sold30d {FormatDisplayQuantity(product.QuantitySold)}");
            }
            sb.AppendLine();
            sb.Append($"Total: {products.Count} produk. Saran: pertahankan, cek expired, dan pastikan stok fisik benar.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleStockMovementAnalysisAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var slowMoving = await _posDbService.GetSlowMovingProductsAsync(days: 30, thresholdQty: 5, limit: 500);
            var deadStock = await GetDeadStockWithShadowFilterAsync();
            var sleeping = await _posDbService.GetSleepingMandatoryProductsAsync(days: 30, limit: 500);
            var unmappedLargeUnits = await GetUnmappedLargeUnitProductsAsync(500);

            int obatCount = sleeping.Count(item => ContainsAny(NormalizeText(item.Category ?? ""), "obat"));
            int sembakoCount = sleeping.Count(item => ContainsAny(NormalizeText(item.Category ?? ""), "sembako"));
            int bayiCount = sleeping.Count(item => ContainsAny(NormalizeText(item.Category ?? ""), "bayi"));

            var sb = new StringBuilder();
            sb.AppendLine($"{IconChart} ANALISA PERGERAKAN STOK - {DateTime.Today:dd/MM/yyyy}");
            sb.AppendLine();
            sb.AppendLine($"\U0001F422 SLOW MOVING (<40% rata-rata kategori): {slowMoving.Count} produk");
            sb.AppendLine($"   Termasuk {slowMoving.Count(item => item.CurrentStock < 0)} produk stok minus yang perlu koreksi data.");
            sb.AppendLine("   Ketik /slow_moving untuk detail.");
            sb.AppendLine();
            sb.AppendLine($"{IconBoxArchive} DEAD STOCK (tidak bergerak, non-mandatory): {deadStock.Count} produk");
            sb.AppendLine("   Tidak termasuk produk baru restock atau mandatory.");
            sb.AppendLine("   Ketik /dead_stock untuk detail.");
            sb.AppendLine();
            sb.AppendLine($"\U0001F3E5 SLEEPING MANDATORY (jarang laku, wajib ada): {sleeping.Count} produk");
            sb.AppendLine($"   Obat: {obatCount} | Sembako: {sembakoCount} | Bayi: {bayiCount}");
            sb.AppendLine("   Ketik /sleeping_stock untuk detail.");
            sb.AppendLine();
            sb.AppendLine($"{IconWarning} SHADOW STOCK ALERT: {unmappedLargeUnits.Count} produk unit besar belum dimapping.");
            sb.AppendLine("   Ketik /shadow_stok untuk cek.");
            sb.AppendLine();
            sb.AppendLine("Rekomendasi:");
            sb.AppendLine($"- {deadStock.Count} produk dead stock -> promosi atau retur supplier");
            sb.AppendLine($"- {slowMoving.Count} produk slow moving -> review harga / bundling");
            sb.Append("- Sleeping stock -> pertahankan, cek expired");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleProfitExplanationAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var summary = await _posDbService.GetProfitCalculationExplanationAsync(DateTime.Today, DateTime.Today);
            var sb = new StringBuilder();
            sb.AppendLine($"{IconProfit} CARA HITUNG PROFIT TOKO");
            sb.AppendLine();
            sb.AppendLine($"Periode: {FormatDateRangeLabel(summary.StartDate, summary.EndDate)}");
            sb.AppendLine($"Omzet: {FormatCurrency(summary.Revenue)}");
            sb.AppendLine($"HPP/modal barang: {FormatCurrency(summary.CostOfGoodsSold)}");
            sb.AppendLine($"Profit kotor: {FormatCurrency(summary.GrossProfit)}");
            sb.AppendLine($"Margin: {summary.MarginPercent:0.##}%");
            sb.AppendLine();
            sb.Append("Rumus: profit kotor = SUM((harga jual - modal produk) x qty terjual). Produk tanpa modal membuat profit terlihat lebih besar dari kondisi sebenarnya.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleCategorySearchAsync(string categoryKeyword, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string keyword = string.IsNullOrWhiteSpace(categoryKeyword) ? "sembako" : categoryKeyword.Trim();
            var products = await _posDbService.GetProductCategoryGroupAsync(keyword, 20, categoryOnly: true);
            if (!products.Any())
            {
                return $"Belum ada produk dengan kategori \"{keyword}\".";
            }

            return BuildProductPageResponse(
                context,
                products,
                mode: "category_keyword",
                query: keyword,
                title: $"{IconPackage} PRODUK GRUP - {keyword}",
                intro: $"Ditemukan {products.Count} produk dari kategori.");
        }

        private async Task<string> HandleTopSupplierAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var suppliers = await _posDbService.GetSupplierPurchaseSummaryAsync(10);
            if (!suppliers.Any())
            {
                return "Belum ada ringkasan pembelian supplier.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("\U0001F3ED TOP SUPPLIER PEMBELIAN");
            sb.AppendLine();
            for (int i = 0; i < suppliers.Count; i++)
            {
                var supplier = suppliers[i];
                sb.AppendLine($"{i + 1}. {FormatOptional(supplier.SupplierName)} | {supplier.PurchaseCount} dokumen | {FormatCurrency(supplier.TotalPurchase)} | terakhir {FormatShortDate(supplier.LastPurchaseDate)}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleDailyTrendAsync(string? argument)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            int days = int.TryParse(argument, out var parsed) ? parsed : 7;
            var trend = await _posDbService.GetDailyTrendAsync(days);
            if (!trend.Any())
            {
                return "Belum ada data tren harian.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconChart} TREN PENJUALAN {trend.Count} HARI");
            sb.AppendLine();
            foreach (var item in trend)
            {
                sb.AppendLine($"{FormatShortDate(item.Date)} | {item.TransactionCount} trx | {FormatCurrency(item.Revenue)}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleInventoryHistoryAsync(string args)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                return $"{IconCross} Format salah.\nGunakan: /riwayat_inventory <nama produk>\nContoh: /riwayat_inventory kapal api mix";
            }

            var (product, error) = await TryResolveProductAsync(args, isMutation: false, actionLabel: "lihat riwayat inventory");
            if (product == null || !string.IsNullOrWhiteSpace(error))
            {
                return error ?? $"Produk \"{args}\" tidak ditemukan.";
            }

            var history = await _posDbService.GetInventoryHistoryAsync(product.Id);
            if (!history.Any())
            {
                return $"Belum ada riwayat inventory untuk {product.Name}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconInventory} RIWAYAT INVENTORY - {product.Name}");
            sb.AppendLine();
            foreach (var item in history.Take(10))
            {
                decimal change = item.QuantityChange;
                if (change == 0)
                {
                    sb.AppendLine($"  {FormatShortDate(item.Date)}  {GetInventoryDirectionIcon(change)} {FormatSignedStockValue(change).PadLeft(5)}      (tidak berubah)");
                }
                else
                {
                    sb.AppendLine($"  {FormatShortDate(item.Date)}  {GetInventoryDirectionIcon(change)} {FormatSignedStockValue(change).PadLeft(5)} {GetUnitLabel(product.Unit)}");
                }
            }

            sb.AppendLine();
            sb.Append($"Total ditampilkan: {Math.Min(10, history.Count)} entri");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleAutoRestockRecommendationsAsync(string? args = null)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string query = args?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var (product, error) = await TryResolveProductAsync(query, isMutation: false, actionLabel: "lihat rekomendasi restock");
                if (product == null || !string.IsNullOrWhiteSpace(error))
                {
                    return error ?? $"Produk \"{query}\" tidak ditemukan.";
                }

                var recommendation = await _posDbService.GetRestockRecommendationAsync(product.Id ?? string.Empty);
                if (recommendation.ProductId <= 0 || string.IsNullOrWhiteSpace(recommendation.ProductName))
                {
                    return $"Belum ada data rekomendasi restock khusus untuk {product.Name}.";
                }

                var detail = new StringBuilder();
                detail.AppendLine($"{IconRobot} REKOMENDASI RESTOCK - {recommendation.ProductName}");
                detail.AppendLine();
                detail.AppendLine($"Stok saat ini: {FormatDisplayQuantity(recommendation.CurrentStock)} {GetUnitLabel(recommendation.Unit)}");
                detail.AppendLine($"Rata-rata jual: {FormatDisplayQuantity(recommendation.AverageSales)} {GetUnitLabel(recommendation.Unit)}/hari aktif");
                detail.AppendLine($"Hari aman: {recommendation.DaysSafe}");
                detail.AppendLine($"Prioritas: {recommendation.Priority}");
                detail.AppendLine($"Saran qty: {FormatDisplayQuantity(recommendation.RecommendedQty)} {GetUnitLabel(recommendation.Unit)}");
                detail.AppendLine();
                detail.Append("Gunakan /restock ");
                detail.Append(recommendation.ProductName);
                detail.Append(' ');
                detail.Append(FormatStockValue(Math.Max(1, recommendation.RecommendedQty)));
                return detail.ToString().TrimEnd();
            }

            var recommendations = await _posDbService.GetAutoRestockRecommendationsAsync(10);
            if (!recommendations.Any())
            {
                return "Tidak ada rekomendasi restock saat ini.";
            }

            var actionable = recommendations
                .Where(r => !r.RequiresManualReview && r.RecommendedQty > 0)
                .OrderByDescending(r => string.Equals(r.Priority, "HIGH", StringComparison.OrdinalIgnoreCase))
                .ThenBy(r => r.DaysSafe)
                .ThenBy(r => r.ProductName)
                .Take(10)
                .ToList();
            var manual = recommendations
                .Where(r => r.RequiresManualReview)
                .OrderBy(r => r.ProductName)
                .Take(10)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"{IconRobot} REKOMENDASI RESTOCK");
            sb.AppendLine();

            if (actionable.Any())
            {
                sb.AppendLine($"{IconWarning} Perlu Restock:");
                foreach (var item in actionable)
                {
                    sb.AppendLine($"  {GetStockIndicator(item.CurrentStock)} {FormatOptional(item.ProductName).PadRight(22)} saran {FormatStockValue(item.RecommendedQty)} {GetUnitLabel(item.Unit)}");
                }
            }
            else
            {
                sb.AppendLine($"{IconCheck} Tidak ada item urgent berdasarkan histori penjualan.");
            }

            if (manual.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconEye} Perlu Review Manual (stok 0, belum ada penjualan 30 hari):");
                foreach (var item in manual)
                {
                    sb.AppendLine($"  • {FormatOptional(item.ProductName).PadRight(22)} {FormatDisplayQuantity(item.CurrentStock)} {GetUnitLabel(item.Unit)}");
                }

                sb.AppendLine();
                sb.Append("Gunakan /restock [nama] [qty] untuk restock item di atas.");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleStockNotificationAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var critical = await _posDbService.GetCriticalStockProductsAsync();
            if (!critical.Any())
            {
                return "Semua stok aman.";
            }

            return BuildCriticalStockResponse(critical);
        }

        private async Task<string> HandleNaturalLanguageAsync(InboundMessage message, AutomationExecutionContext context)
        {
            try
            {
                if (IsIdentityQuestion(message.Text))
                {
                    return BuildBotIdentityResponse();
                }

                int historyCount = _configService.Config?.Memory?.ShortTermHistoryCount ?? 5;
                var history = await _databaseService.GetRecentConversationsAsync(GetConversationChatId(message), historyCount);
                var historyTexts = history.Select(h => $"{h.Role}: {h.Message}").ToList();
                string? directResponse = await TryHandleDeterministicIntentAsync(message.Text, context);
                if (!string.IsNullOrWhiteSpace(directResponse))
                {
                    return directResponse;
                }

                string realDataInfo = await BuildRealStoreDataAsync(message.Text, context.IsOwner);

                return await _groqService.GenerateNaturalResponseAsync(
                    message.Text,
                    historyTexts,
                    context.UserRole,
                    realDataInfo);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Natural language handling failed: {ex.Message}", "Automation", ex.ToString());
                return "AI sedang bermasalah. Gunakan command seperti /stok, /laporan, atau /analisa.";
            }
        }

        private async Task<string?> TryHandlePreIntentPatternAsync(string text, AutomationExecutionContext context)
        {
            if (!context.IsOwner)
            {
                return null;
            }

            if (TryParseNaturalShadowMappingIntent(text, out var shadowIntent))
            {
                shadowIntent.OriginalMessage = text.Trim();
                shadowIntent.FromNaturalLanguage = true;
                return await HandleShadowMappingIntentAsync(shadowIntent, context, requireConfirmForSingleMatch: true);
            }

            if (LooksLikeShadowMappingAttempt(text))
            {
                return BuildSafeShadowMappingFallback();
            }

            return null;
        }

        private bool TryParseNaturalShadowMappingIntent(string text, out ShadowMappingIntent intent)
        {
            intent = new ShadowMappingIntent();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string source = text.Trim();
            var naturalMatch = Regex.Match(
                source,
                @"^(?<qty>\d+)\s*(?<parentUnit>dus|pak|box|krat|bal|bks)\s+(?<keyword>.+?)\s+(?:isi|setara|=)\s+(?<rate>\d+(?:[,.]\d+)?)\s*(?<childUnit>pcs|pc|rcg|btl|sachet|pack|pak|ecer|biji|buah)?$",
                RegexOptions.IgnoreCase);
            if (naturalMatch.Success &&
                TryParseDecimal(naturalMatch.Groups["rate"].Value.Replace(',', '.'), out var naturalRate) &&
                naturalRate > 0)
            {
                string keyword = naturalMatch.Groups["keyword"].Value.Trim();
                string parentUnit = naturalMatch.Groups["parentUnit"].Value.Trim();
                string? childUnit = naturalMatch.Groups["childUnit"].Success ? naturalMatch.Groups["childUnit"].Value.Trim() : null;
                intent.ProductKeyword = keyword;
                intent.ParentQuery = $"{keyword} {parentUnit}";
                intent.ChildQuery = keyword;
                intent.ParentUnit = parentUnit;
                intent.ChildUnit = childUnit;
                intent.Rate = naturalRate;
                return true;
            }

            var arrowMatch = Regex.Match(
                source,
                @"^(?<parent>.+?)\s*(?:->|=>|>|â†’|ke)\s*(?<child>.+?)\s*@\s*(?<rate>\d+(?:[,.]\d+)?)\s*$",
                RegexOptions.IgnoreCase);
            if (arrowMatch.Success &&
                TryParseDecimal(arrowMatch.Groups["rate"].Value.Replace(',', '.'), out var arrowRate) &&
                arrowRate > 0)
            {
                intent.ParentQuery = arrowMatch.Groups["parent"].Value.Trim();
                intent.ChildQuery = arrowMatch.Groups["child"].Value.Trim();
                intent.Rate = arrowRate;
                return true;
            }

            return false;
        }

        private static bool LooksLikeShadowMappingAttempt(string text)
        {
            string normalized = NormalizeText(text);
            return Regex.IsMatch(normalized, @"\b\d+\s*(dus|pak|box|krat|bal|bks)\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\bisi\s+\d+\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(text, @"(?:->|=>|>)\s*.+@\s*\d+", RegexOptions.IgnoreCase);
        }

        private static string BuildSafeShadowMappingFallback()
        {
            return "Saya belum memahami mapping stoknya.\n\n" +
                   "Contoh format:\n" +
                   "/set_family Kapal Api Mix@1Dus -> Kapal Api mix @ 12\n" +
                   "atau:\n" +
                   "1 dus kapal api isi 12 rcg";
        }

        private async Task<string?> TryHandlePendingShadowMappingReplyAsync(string text, AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (!_shadowMappingPendingBySender.TryGetValue(senderKey, out var pending))
            {
                return null;
            }

            if (pending.ExpiresAt <= DateTime.Now)
            {
                _shadowMappingPendingBySender.TryRemove(senderKey, out _);
                return null;
            }

            string normalized = NormalizeText(text);
            if (normalized is "batal" or "cancel")
            {
                _shadowMappingPendingBySender.TryRemove(senderKey, out _);
                return "Mapping stok dibatalkan.";
            }

            if (!TryParseShadowMappingSelection(text, pending, out int parentIndex, out int childIndex))
            {
                return BuildShadowMappingCandidateMessage(pending, singleConfidentMatch: false);
            }

            var parent = pending.ParentCandidates[parentIndex].Product;
            var child = pending.ChildCandidates[childIndex].Product;
            _shadowMappingPendingBySender.TryRemove(senderKey, out _);
            return await SaveUnitConversionMappingAsync(parent, child, pending.Rate, "Manual mapping via natural/set_family selection");
        }

        private async Task<string?> TryConfirmPendingShadowMappingAsync(AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (!_shadowMappingPendingBySender.TryGetValue(senderKey, out var pending))
            {
                return null;
            }

            if (pending.ExpiresAt <= DateTime.Now)
            {
                _shadowMappingPendingBySender.TryRemove(senderKey, out _);
                return null;
            }

            int parentIndex = pending.SelectedParentIndex ?? 0;
            int childIndex = pending.SelectedChildIndex ?? 0;
            if (parentIndex < 0 || parentIndex >= pending.ParentCandidates.Count ||
                childIndex < 0 || childIndex >= pending.ChildCandidates.Count)
            {
                return BuildShadowMappingCandidateMessage(pending, singleConfidentMatch: false);
            }

            _shadowMappingPendingBySender.TryRemove(senderKey, out _);
            return await SaveUnitConversionMappingAsync(
                pending.ParentCandidates[parentIndex].Product,
                pending.ChildCandidates[childIndex].Product,
                pending.Rate,
                "Confirmed mapping via natural shadow intent");
        }

        private static bool TryParseShadowMappingSelection(
            string text,
            ShadowMappingPendingState pending,
            out int parentIndex,
            out int childIndex)
        {
            parentIndex = -1;
            childIndex = -1;
            string compact = Regex.Replace(text.Trim(), @"\s+", "");
            var match = Regex.Match(compact, @"^(?<parent>\d+)(?<child>[A-Za-z])$");
            if (match.Success)
            {
                parentIndex = int.Parse(match.Groups["parent"].Value, CultureInfo.InvariantCulture) - 1;
                childIndex = char.ToUpperInvariant(match.Groups["child"].Value[0]) - 'A';
                return parentIndex >= 0 && parentIndex < pending.ParentCandidates.Count &&
                       childIndex >= 0 && childIndex < pending.ChildCandidates.Count;
            }

            if (int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBasedParent) &&
                pending.ChildCandidates.Count == 1)
            {
                parentIndex = oneBasedParent - 1;
                childIndex = 0;
                return parentIndex >= 0 && parentIndex < pending.ParentCandidates.Count;
            }

            string normalized = NormalizeText(text);
            parentIndex = pending.ParentCandidates.FindIndex(candidate =>
                NormalizeText(candidate.Product.Name ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase));
            childIndex = pending.ChildCandidates.FindIndex(candidate =>
                NormalizeText(candidate.Product.Name ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase));
            if (parentIndex >= 0 && pending.ChildCandidates.Count == 1)
            {
                childIndex = 0;
            }
            if (childIndex >= 0 && pending.ParentCandidates.Count == 1)
            {
                parentIndex = 0;
            }

            return parentIndex >= 0 && childIndex >= 0;
        }

        private AutomationExecutionContext BuildExecutionContext(InboundMessage message)
        {
            var identity = new ChannelIdentity
            {
                Channel = message.Channel,
                SenderId = message.SenderId,
                SenderName = message.SenderName
            };

            bool isOwner = false;
            bool isKasir = false;
            bool isAuthorized = false;

            if (message.Channel == ChannelType.Telegram)
            {
                if (long.TryParse(message.SenderId, out var chatId))
                {
                    var telegram = _configService.Config?.Telegram;
                    isOwner = telegram?.OwnerChatIds?.Contains(chatId) == true ||
                              (telegram?.OwnerChatIds == null || !telegram.OwnerChatIds.Any()) &&
                              (telegram?.AllowedChatIds?.Contains(chatId) == true);
                    isKasir = telegram?.KasirChatIds?.Contains(chatId) == true;
                    isAuthorized = telegram?.AllowedChatIds == null || !telegram.AllowedChatIds.Any() ||
                                   telegram.AllowedChatIds.Contains(chatId) || isOwner || isKasir;
                }
            }
            else if (message.Channel == ChannelType.WhatsApp || message.Channel == ChannelType.Baileys)
            {
                string normalized = NormalizeWhatsAppNumber(message.SenderId);
                bool isBaileys = message.Channel == ChannelType.Baileys;
                var ownerNumbers = isBaileys
                    ? _configService.Config?.Baileys?.OwnerNumbers
                    : _configService.Config?.WhatsApp?.OwnerNumbers;
                var kasirNumbers = isBaileys
                    ? _configService.Config?.Baileys?.KasirNumbers
                    : _configService.Config?.WhatsApp?.KasirNumbers;
                bool transportEnabled = isBaileys
                    ? _configService.Config?.Baileys?.Enabled == true &&
                      WhatsAppModes.UsesBaileys(_configService.Config?.WhatsApp?.Mode)
                    : _configService.Config?.WhatsApp?.Enabled == true &&
                      WhatsAppModes.UsesCloudApi(_configService.Config?.WhatsApp?.Mode);

                bool hasConfiguredPrincipals =
                    ownerNumbers?.Any(n => !string.IsNullOrWhiteSpace(NormalizeWhatsAppNumber(n))) == true ||
                    kasirNumbers?.Any(n => !string.IsNullOrWhiteSpace(NormalizeWhatsAppNumber(n))) == true;
                bool setupCompleted = _configService.Config?.Setup?.SetupCompleted == true;

                isOwner = ownerNumbers?.Any(n => NormalizeWhatsAppNumber(n) == normalized) == true;
                isKasir = kasirNumbers?.Any(n => NormalizeWhatsAppNumber(n) == normalized) == true;
                bool allowCloudNoPrincipals = !isBaileys && !hasConfiguredPrincipals;
                bool allowBaileysSetupOnly = isBaileys && !hasConfiguredPrincipals && !setupCompleted;
                isAuthorized = transportEnabled &&
                               (isOwner ||
                                isKasir ||
                                allowCloudNoPrincipals ||
                                allowBaileysSetupOnly);
            }

            return new AutomationExecutionContext
            {
                Identity = identity,
                UserRole = isOwner ? "Owner" : isKasir ? "Kasir" : "Guest",
                IsOwner = isOwner,
                IsKasir = isKasir,
                IsAuthorized = isAuthorized,
                TriggerType = message.Channel == ChannelType.Telegram ? "TelegramMessage" :
                              (message.Channel == ChannelType.WhatsApp || message.Channel == ChannelType.Baileys) ? "WhatsAppMessage" : "System",
                CorrelationId = message.CorrelationId ?? Guid.NewGuid().ToString("N"),
                Timestamp = message.Timestamp
            };
        }

        private async Task SaveConversationAsync(InboundMessage message, string role, string text)
        {
            await _databaseService.AddConversationAsync(new Conversation
            {
                ChatId = GetConversationChatId(message),
                UserName = message.SenderName ?? message.SenderId,
                Role = role,
                Message = text,
                Timestamp = message.Timestamp == default ? DateTime.Now : message.Timestamp,
                MessageType = string.IsNullOrWhiteSpace(message.MediaUrl) ? "text" : "media"
            });
        }

        private long GetConversationChatId(InboundMessage message)
        {
            if (message.Channel == ChannelType.Telegram && long.TryParse(message.SenderId, out var chatId))
            {
                return chatId;
            }

            string source = $"{message.Channel}:{NormalizeWhatsAppNumber(message.SenderId)}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            long value = BitConverter.ToInt64(hash, 0);
            return Math.Abs(value == long.MinValue ? long.MaxValue : value);
        }

        private static string BuildStartText()
        {
            return "\U0001F3EA Smart Sembako Assistant\n\n" +
                   "Asisten toko untuk membantu:\n" +
                   "- cek stok & inventory\n" +
                   "- laporan penjualan\n" +
                   "- OCR faktur/struk\n" +
                   "- pelanggan & piutang\n" +
                   "- analisa stok dan restock\n\n" +
                   "Pilih menu di bawah, atau ketik command langsung.\n\n" +
                   "Contoh cepat:\n" +
                   "- /stok kapal api\n" +
                   "- /laporan\n" +
                   "- /piutang";
        }

        public static string BuildMenuHeaderText(string menuType)
        {
            return menuType switch
            {
                "operasional" => "\U0001F680 OPERASIONAL CEPAT",
                "laporan" => "\U0001F4CA LAPORAN & ANALISA",
                "stok" => "\U0001F4E6 STOK & INVENTORY",
                "ocr" => "\U0001F6D2 PEMBELIAN & OCR",
                "pelanggan" => "\U0001F465 PELANGGAN & PIUTANG",
                "dokumen" => "\U0001F9FE DOKUMEN & RIWAYAT",
                "shadow" => "\U0001F9E9 SHADOW STOCK",
                "export" => "\u2B07\uFE0F EXPORT DATA",
                "aksi" => "\u2699\uFE0F AKSI PENDING",
                "help" => "\u2753 BANTUAN SMART SEMBAKO",
                _ => "\U0001F4CB MENU SMART SEMBAKO"
            };
        }

        private string BuildHelpCommandText(string args, bool isOwner)
        {
            string category = (args ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(category))
            {
                return "\u2753 Bantuan Smart Sembako\n\n" +
                       "Pilih kategori bantuan di bawah, atau ketik:\n" +
                       "- /help lengkap\n" +
                       "- /help stok\n" +
                       "- /help dual_stock\n" +
                       "- /help ocr\n" +
                       "- /help pelanggan\n" +
                       "- /help dokumen";
            }

            if (category == "lengkap" || category == "full")
            {
                return BuildHelpText(isOwner);
            }

            return category switch
            {
                "mulai_cepat" or "mulai" or "cepat" =>
                    "Mulai cepat:\n/stok <nama produk>\n/laporan\n/piutang\n/menu",
                "laporan" =>
                    isOwner
                        ? "Laporan & analisa:\n/laporan\n/laporan_periode <periode>\n/statistik\n/analisa\n/rekomendasi_restock"
                        : "Laporan kasir:\n/laporan untuk ringkasan operasional yang diizinkan.",
                "stok" =>
                    "Stok & inventory:\n/stok <nama produk>\n/inventory <produk> <stok_target>\n/stok_kategori <nama kategori>\n/notifikasi_stok",
                "ocr" =>
                    "Pembelian & OCR:\n/struk lalu kirim foto faktur\n/inputstruk <teks faktur>\n/restock <produk> <qty> [harga]\n/selesai_struk",
                "pelanggan" =>
                    "Pelanggan & piutang:\n/pelanggan <nama>\n/piutang <nama>\n/pelanggan_loyal\n/pelanggan at_risk",
                "dokumen" =>
                    "Dokumen & riwayat:\n/dokumen <nomor>\n/riwayat_restock <produk>\n/riwayat_inventory <produk>\n/cek_expired",
                "shadow" =>
                    isOwner
                        ? "Shadow stock:\n/shadow_stok\n/stok_efektif <produk>\n/set_family <besar> -> <kecil> @ <isi>"
                        : "Shadow stock hanya tersedia untuk owner.",
                "dual_stock" or "dualstok" =>
                    isOwner
                        ? "Dual stock:\n/list_family - lihat mapping parent/child dan shadow stock\n/dual_stock [produk] - ringkasan keluarga\n/dual_stock_alert - cek defisit tanpa kirim WA\n/dual_stock_sync - konsolidasi manual\n/dual_stock_watcher status|on|off\n/dual_stock_channel [telegram|cloud|baileys] [on|off]"
                        : "Dual stock hanya tersedia untuk owner.",
                "export" =>
                    isOwner
                        ? "Export data:\n/ekspor_lengkap\nEKSPOR penjualan <periode>"
                        : "Export data hanya tersedia untuk owner.",
                "aksi" =>
                    "Aksi pending:\n/confirm\n/simpan\n/simpan_jual\n/detail_harga\n/lewati_harga\n/batal",
                _ => "Kategori help tidak dikenal. Ketik /help untuk melihat kategori."
            };
        }

        public static string BuildPendingInputPrompt(string action)
        {
            return action switch
            {
                "cek_dokumen" => "\U0001F4C4 CEK DOKUMEN\n\nMasukkan nomor dokumen yang ingin dicek.\n\nContoh:\n- 26-200-004217\n- 26-100-000009\n- 004217",
                "detail_nota" => "\U0001F9FE DETAIL NOTA\n\nMasukkan nomor nota penjualan.\n\nContoh:\n- 26-200-004217\n- 004217",
                "cek_stok" => "\U0001F50D CEK STOK\n\nMasukkan nama produk.\n\nContoh:\n- kapal api\n- beras ramos",
                "inventory" => "\U0001F9EE KOREKSI STOK\n\nMasukkan format: produk stok_target\n\nContoh:\n- kapal api 10",
                "restock" => "\U0001F4E6 RESTOCK\n\nMasukkan format: produk qty harga_opsional\n\nContoh:\n- gula pasir 12 14500",
                "input_struk" => "\U0001F9FE INPUT TEKS FAKTUR\n\nTempel teks faktur/struk lengkap di pesan berikutnya.",
                "ocr_foto" => "\U0001F4F7 OCR FOTO\n\nKirim foto faktur/struk berikutnya. Foto tanpa caption tetap akan diproses sebagai /struk.",
                "riwayat_restock" => "\U0001F4CB RIWAYAT RESTOCK\n\nMasukkan nama produk.",
                "riwayat_inventory" => "\U0001F4CB RIWAYAT INVENTORY\n\nMasukkan nama produk.",
                "penjualan" => "\U0001F4CA PENJUALAN PRODUK\n\nMasukkan nama produk.",
                "stok_kategori" => "\U0001F3F7\uFE0F STOK KATEGORI\n\nMasukkan nama kategori.",
                "stok_efektif" => "\U0001F9EE STOK EFEKTIF\n\nMasukkan nama produk unit besar.",
                "set_family" => "\U0001F517 SET FAMILY\n\nMasukkan format: produk besar -> produk kecil @ isi\n\nContoh:\n- mie dus -> mie pcs @ 40",
                "pelanggan" => "\U0001F465 PELANGGAN\n\nMasukkan nama pelanggan atau kata kunci.",
                "piutang" => "\U0001F4B3 PIUTANG\n\nMasukkan nama pelanggan, atau ketik semua untuk ringkasan.",
                _ => "Masukkan data yang diminta."
            };
        }

        private string BuildHelpText(bool isOwner)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconStore} Smart Sembako Assistant");
            sb.AppendLine();
            sb.AppendLine($"{IconPackage} STOK & LAPORAN");
            sb.AppendLine("/stok [nama] - cek stok produk");
            sb.AppendLine("/laporan - omzet & profit hari ini");

            if (isOwner)
            {
                sb.AppendLine("/laporan_periode <periode> - laporan penjualan fleksibel");
                sb.AppendLine("/statistik - insight bisnis bulanan");
                sb.AppendLine("/analisa - analisa bisnis");
                sb.AppendLine("/notifikasi_stok - stok kritis");
                sb.AppendLine();
                sb.AppendLine("\U0001F6D2 PEMBELIAN & KOREKSI");
                sb.AppendLine("/restock <produk> <qty> [harga] - tambah stok masuk");
                sb.AppendLine("/struk - kirim foto struk dengan caption /struk");
                sb.AppendLine("/inputstruk <teks> - tempel teks faktur/struk untuk OCR tanpa foto");
                sb.AppendLine("/selesai_struk - akhiri OCR multi-halaman dan tampilkan preview");
                sb.AppendLine("/inventory <produk> <stok_target> - set stok akhir (koreksi)");
                sb.AppendLine("↳ Bulk: pisahkan item dengan koma");
                sb.AppendLine();
                sb.AppendLine($"{IconClipboard} DATA");
                sb.AppendLine("/penjualan <produk> - ringkasan penjualan produk");
                sb.AppendLine("/dokumen <nomor> - cek detail dokumen");
                sb.AppendLine("/riwayat_restock <produk> - riwayat restock");
                sb.AppendLine("/riwayat_inventory <produk> - riwayat koreksi");
                sb.AppendLine("/rekomendasi_restock - saran restock");
                sb.AppendLine("/analisa_stok - ringkasan slow/dead/sleeping stock");
                sb.AppendLine("/slow_moving - barang lambat vs rata-rata kategori");
                sb.AppendLine("/dead_stock - barang tidak bergerak non-mandatory");
                sb.AppendLine("/sleeping_stock - kategori wajib yang jarang laku");
                sb.AppendLine("/shadow_stok - unit besar belum dimapping");
                sb.AppendLine("/stok_efektif <produk> - hitung stok shadow keluarga");
                sb.AppendLine("/list_family - list mapping family parent/child");
                sb.AppendLine("/dual_stock [produk] - ringkasan dual stock");
                sb.AppendLine("/dual_stock_alert - cek defisit dual stock");
                sb.AppendLine("/dual_stock_sync - konsolidasi dual stock manual");
                sb.AppendLine("/dual_stock_watcher status|on|off - atur watcher realtime");
                sb.AppendLine("/dual_stock_channel [telegram|cloud|baileys] [on|off] - atur channel alert");
                sb.AppendLine("/set_family <besar> -> <kecil> @ <isi> - mapping unit");
                sb.AppendLine("/cek_expired - produk dengan data expired");
                sb.AppendLine("/stok_kategori <nama> - stok per kategori");
                sb.AppendLine("/cek_modal - cek produk tanpa modal");
                sb.AppendLine("/produk [kata kunci] - daftar/cari/ranking produk");
                sb.AppendLine();
                sb.AppendLine($"{IconUser} MASTER DATA");
                sb.AppendLine("/pelanggan_loyal - overview pelanggan loyal");
                sb.AppendLine("/pelanggan [nama] - cari pelanggan");
                sb.AppendLine("/pelanggan at_risk - pelanggan perlu perhatian");
                sb.AppendLine("/supplier [nama] - cari supplier");
                sb.AppendLine("/piutang [nama] - ringkasan atau detail piutang");
                sb.AppendLine("/user [nama] - cari user kasir");
                sb.AppendLine("/laporan_kasir - performa kasir");
                sb.AppendLine("/ekspor_lengkap - ZIP seluruh data utama");
            }

            sb.AppendLine();
            sb.AppendLine("\u2699\uFE0F AKSI");
            sb.AppendLine("/confirm - konfirmasi aksi menunggu");
            sb.AppendLine("/simpan - simpan pembelian + update harga beli saja");
            sb.AppendLine("/simpan_jual - simpan pembelian + pakai saran/manual harga jual");
            sb.AppendLine("/jual <nomor_item> <nominal> - ubah harga jual manual saat konfirmasi harga");
            sb.AppendLine("/detail_harga - lihat semua perubahan harga");
            sb.AppendLine("/lewati_harga - simpan pembelian tanpa ubah data produk");
            sb.AppendLine("/batal - batalkan aksi menunggu");
            sb.AppendLine();
            sb.AppendLine("Chat natural juga didukung. Contoh: \"stok beras berapa?\"");
            return sb.ToString().TrimEnd();
        }

        private async Task<string?> TryHandleShortcutAsync(string userMessage, AutomationExecutionContext context)
        {
            string text = userMessage.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var activeTopic = GetActiveTopicState(context);
            if (activeTopic?.TopicType == TopicType.DocumentPickPending)
            {
                if (!context.IsOwner)
                {
                    return BuildOwnerOnlyDeniedMessage();
                }

                if (Regex.IsMatch(text, @"^\d{1,2}\s*[,;/ ]\s*\d{1,2}", RegexOptions.CultureInvariant))
                {
                    return "Pilih satu nomor saja, misalnya 1 atau 2.";
                }

                return context.IsOwner ? await HandleDocumentPickReplyAsync(text, context, activeTopic) : BuildOwnerOnlyDeniedMessage();
            }

            bool isShortcut =
                Regex.IsMatch(text, @"^(DETAIL\s+NOTA|NOTA|CEK\s+NOTA|LIHAT\s+NOTA|DETAIL\s+TRANSAKSI)\s+(\d{2}-\d{3}-\d{6}|\d{3,6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(text, @"^DETAIL\s+PRODUK\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                text.Equals("LANJUT NOTA", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("LANJUT ITEM", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("NOTA SEBELUMNYA", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("PRODUK FAVORIT", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("PIUTANG PELANGGAN", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("EKSPOR", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("EXPORT", StringComparison.OrdinalIgnoreCase);

            if (!isShortcut)
            {
                return null;
            }

            if (!context.IsOwner)
            {
                return BuildOwnerOnlyDeniedMessage();
            }

            var noteMatch = Regex.Match(
                text,
                @"^(DETAIL\s+NOTA|NOTA|CEK\s+NOTA|LIHAT\s+NOTA|DETAIL\s+TRANSAKSI)\s+(\d{2}-\d{3}-\d{6}|\d{3,6})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (noteMatch.Success)
            {
                return await HandleSalesNoteDetailShortcutAsync(noteMatch.Groups[2].Value, context);
            }

            if (text.Equals("LANJUT NOTA", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleNextCustomerDocumentsAsync(context);
            }

            if (text.Equals("LANJUT ITEM", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleSalesDocumentItemPaginationAsync(context);
            }

            if (text.Equals("NOTA SEBELUMNYA", StringComparison.OrdinalIgnoreCase))
            {
                return await HandlePreviousCustomerDocumentsAsync(context);
            }

            if (text.Equals("PRODUK FAVORIT", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleCustomerFavoriteProductsShortcutAsync(context);
            }

            if (text.Equals("PIUTANG PELANGGAN", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleCustomerReceivablesShortcutAsync(context);
            }

            var productMatch = Regex.Match(text, @"^DETAIL\s+PRODUK\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (productMatch.Success)
            {
                return await HandleProductSalesAsync(productMatch.Groups[1].Value.Trim());
            }

            if (text.StartsWith("EKSPOR", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("EXPORT", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleExportByContextAsync(text, context);
            }

            return null;
        }

        private async Task<string> HandleDocumentPickReplyAsync(string text, AutomationExecutionContext context, TopicState activeTopic)
        {
            if (!int.TryParse(text, out int selectedIndex) ||
                selectedIndex < 1 ||
                selectedIndex > activeTopic.CandidateDocuments.Count)
            {
                return "Pilihan tidak valid. Balas nomor kandidat yang tersedia.";
            }

            string documentNumber = activeTopic.CandidateDocuments[selectedIndex - 1];
            return await HandleSalesDocumentDetailByNumberAsync(documentNumber, context);
        }

        private async Task<string> HandleSalesNoteDetailShortcutAsync(string numberOrSuffix, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string trimmed = numberOrSuffix.Trim();
            if (DocumentNumberRegex.IsMatch(trimmed))
            {
                var explicitDocument = await _posDbService.GetDocumentByNumberAsync(trimmed);
                if (explicitDocument == null)
                {
                    return BuildSalesNoteNotFoundResponse(GetDocumentNumberSuffix(trimmed), null);
                }

                if (explicitDocument.DocumentTypeId != 2)
                {
                    return BuildPurchaseFoundForSalesNoteResponse(explicitDocument.Number ?? trimmed, GetDocumentNumberSuffix(trimmed));
                }

                return await HandleSalesDocumentDetailByNumberAsync(explicitDocument.Number ?? trimmed, context);
            }

            string suffix = NormalizeShortDocumentNumber(trimmed);
            var activeTopic = GetActiveTopicState(context);
            string? relatedDocument = ResolveRelatedSalesDocument(suffix, activeTopic);
            if (!string.IsNullOrWhiteSpace(relatedDocument))
            {
                return await HandleSalesDocumentDetailByNumberAsync(relatedDocument, context);
            }

            var salesCandidates = await _posDbService.GetDocumentsByNumberSuffixAsync(suffix, documentTypeId: 2, limit: 5);
            if (salesCandidates.Count == 1)
            {
                return await HandleSalesDocumentDetailByNumberAsync(salesCandidates[0].Number ?? suffix, context);
            }

            if (salesCandidates.Count > 1)
            {
                SetTopicState(
                    context,
                    "document_pick_pending",
                    currentPage: 1,
                    candidateDocuments: salesCandidates
                        .Select(document => document.Number)
                        .Where(number => !string.IsNullOrWhiteSpace(number))
                        .Select(number => number!)
                        .ToList(),
                    lastData: salesCandidates);

                var sb = new StringBuilder();
                sb.AppendLine($"Ditemukan beberapa nota penjualan dengan nomor {suffix}:");
                sb.AppendLine();
                for (int i = 0; i < salesCandidates.Count; i++)
                {
                    var candidate = salesCandidates[i];
                    sb.AppendLine($"{i + 1}. {candidate.Number} | {FormatDateTime(candidate.Date)} | {FormatOptional(candidate.CustomerName)} | {FormatCurrency(candidate.Total)}");
                }
                sb.AppendLine();
                sb.Append("Balas: 1, 2, 3, dst.");
                return sb.ToString().TrimEnd();
            }

            var nonSalesMatches = await _posDbService.GetDocumentsByNumberSuffixAsync(suffix, documentTypeId: null, limit: 5);
            var purchase = nonSalesMatches.FirstOrDefault(document => document.DocumentTypeId != 2);
            return BuildSalesNoteNotFoundResponse(suffix, purchase);
        }

        private async Task<string> HandleSalesDocumentDetailByNumberAsync(string documentNumber, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var document = await _posDbService.GetDocumentByNumberAsync(documentNumber);
            if (document == null || string.IsNullOrWhiteSpace(document.Id))
            {
                return BuildSalesNoteNotFoundResponse(GetDocumentNumberSuffix(documentNumber), null);
            }

            if (document.DocumentTypeId != 2)
            {
                return BuildPurchaseFoundForSalesNoteResponse(document.Number ?? documentNumber, GetDocumentNumberSuffix(documentNumber));
            }

            var items = await _posDbService.GetDocumentItemsAsync(document.Id);
            string senderKey = BuildSenderStateKey(context);
            _lastDocumentBySender[senderKey] = document.Number ?? documentNumber;

            const int pageSize = 10;
            var firstPage = items.Take(pageSize).ToList();
            if (items.Count > firstPage.Count)
            {
                _documentPaginationBySender[senderKey] = new DocumentPageState
                {
                    DocumentId = document.Id,
                    DocumentNumber = document.Number ?? documentNumber,
                    CustomerId = document.CustomerId,
                    CustomerName = document.CustomerName,
                    NextOffset = firstPage.Count,
                    PageSize = pageSize
                };
            }
            else
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
            }

            SetTopicState(
                context,
                "sales_document_detail",
                entityId: document.Id,
                entityName: document.Number ?? documentNumber,
                currentPage: 1,
                pageSize: pageSize,
                exportType: $"nota_{GetDocumentNumberSuffix(document.Number ?? documentNumber)}.csv",
                lastDocumentNumber: document.Number ?? documentNumber,
                customerId: document.CustomerId,
                customerName: document.CustomerName,
                relatedDocumentNumbers: new List<string> { document.Number ?? documentNumber },
                lastData: items);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconReceipt} DETAIL NOTA - {FormatOptional(document.CustomerName)}");
            sb.AppendLine();
            sb.AppendLine($"{IconDocument} Nota      : {FormatCompactDocumentNumber(document.Number)}");
            sb.AppendLine($"{IconCalendar} Tanggal   : {FormatDateTime(document.Date)}");
            sb.AppendLine($"{IconMoney} Total     : {FormatCurrency(document.Total)}");
            sb.AppendLine($"{IconPackage} Total item: {items.Count} produk");

            if (firstPage.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconClipboard} Daftar Belanja");
                AppendSalesItemRows(sb, firstPage, 1);
            }

            if (items.Count > firstPage.Count)
            {
                sb.AppendLine();
                sb.AppendLine($"{IconRight} Masih ada {items.Count - firstPage.Count} item lagi");
            }

            sb.AppendLine();
            sb.AppendLine("\U0001F4A1 Ketik:");
            if (items.Count > firstPage.Count)
            {
                sb.AppendLine("- LANJUT ITEM");
            }
            string? firstProduct = firstPage.FirstOrDefault()?.ProductName;
            if (!string.IsNullOrWhiteSpace(firstProduct))
            {
                sb.AppendLine($"- DETAIL PRODUK {firstProduct}");
            }
            sb.AppendLine("- NOTA SEBELUMNYA");
            sb.Append("- EKSPOR NOTA");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleSalesDocumentItemPaginationAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_documentPaginationBySender.TryGetValue(senderKey, out var state))
            {
                return "Tidak ada item nota lanjutan. Ketik DETAIL NOTA <nomor> dulu.";
            }

            var document = await _posDbService.GetDocumentByNumberAsync(state.DocumentNumber);
            if (document == null || string.IsNullOrWhiteSpace(document.Id) || document.DocumentTypeId != 2)
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
                return $"Nota penjualan \"{state.DocumentNumber}\" tidak ditemukan.";
            }

            var items = await _posDbService.GetDocumentItemsAsync(document.Id);
            var pageItems = items.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada item nota berikutnya.";
            }

            int startNumber = state.NextOffset + 1;
            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= items.Count)
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _documentPaginationBySender[senderKey] = state;
            }

            SetTopicState(
                context,
                "sales_document_detail",
                entityId: document.Id,
                entityName: document.Number,
                currentPage: (startNumber - 1) / state.PageSize + 1,
                pageSize: state.PageSize,
                exportType: $"nota_{GetDocumentNumberSuffix(document.Number ?? state.DocumentNumber)}.csv",
                lastDocumentNumber: document.Number ?? state.DocumentNumber,
                customerId: document.CustomerId,
                customerName: document.CustomerName,
                relatedDocumentNumbers: new List<string> { document.Number ?? state.DocumentNumber },
                lastData: items);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconReceipt} DETAIL NOTA - LANJUTAN");
            sb.AppendLine($"{IconDocument} {FormatOptional(document.Number)}");
            sb.AppendLine();
            AppendSalesItemRows(sb, pageItems, startNumber);

            if (state.NextOffset < items.Count)
            {
                sb.AppendLine();
                sb.Append($"{IconRight} Masih ada {items.Count - state.NextOffset} item lagi. Ketik LANJUT ITEM.");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleNextCustomerDocumentsAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_customerDocumentPaginationBySender.TryGetValue(senderKey, out var state))
            {
                var topic = GetActiveTopicState(context);
                string? customerId = topic?.CustomerId ?? (topic?.TopicType == TopicType.CustomerDetail ? topic.EntityId : null);
                string? customerName = topic?.CustomerName ?? topic?.EntityName;
                if (string.IsNullOrWhiteSpace(customerId))
                {
                    return "Tidak ada daftar nota pelanggan lanjutan. Ketik /pelanggan <nama> dulu.";
                }

                state = new CustomerDocumentPageState
                {
                    CustomerId = customerId,
                    CustomerName = customerName ?? string.Empty,
                    NextOffset = 0,
                    PageSize = 5
                };
            }

            var documents = await _posDbService.GetCustomerRecentDocumentsAsync(state.CustomerId, 50);
            var pageItems = documents.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _customerDocumentPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada nota pelanggan berikutnya.";
            }

            int startNumber = state.NextOffset + 1;
            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= documents.Count)
            {
                _customerDocumentPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _customerDocumentPaginationBySender[senderKey] = state;
            }

            SetTopicState(
                context,
                "customer_detail",
                entityId: state.CustomerId,
                entityName: state.CustomerName,
                currentPage: (startNumber - 1) / state.PageSize + 1,
                pageSize: state.PageSize,
                exportType: $"transaksi_{MakeSafeFileToken(state.CustomerName)}.csv",
                customerId: state.CustomerId,
                customerName: state.CustomerName,
                relatedDocumentNumbers: pageItems
                    .Select(document => document.DocumentNumber)
                    .Where(number => !string.IsNullOrWhiteSpace(number))
                    .Select(number => number!)
                    .ToList(),
                lastData: documents);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconClipboard} NOTA PELANGGAN - {FormatOptional(state.CustomerName)}");
            sb.AppendLine();
            for (int i = 0; i < pageItems.Count; i++)
            {
                var document = pageItems[i];
                sb.AppendLine($"{startNumber + i}. {FormatShortDate(document.Date)} | {FormatCompactDocumentNumber(document.DocumentNumber)} | {FormatCurrency(document.Total)} | {document.ItemCount} item");
            }

            if (state.NextOffset < documents.Count)
            {
                sb.AppendLine();
                sb.Append("Ketik LANJUT NOTA untuk halaman berikutnya.");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandlePreviousCustomerDocumentsAsync(AutomationExecutionContext context)
        {
            var topic = GetActiveTopicState(context);
            if (topic?.TopicType == TopicType.SalesDocumentDetail &&
                !string.IsNullOrWhiteSpace(topic.CustomerId))
            {
                string senderKey = BuildSenderStateKey(context);
                _customerDocumentPaginationBySender.TryAdd(senderKey, new CustomerDocumentPageState
                {
                    CustomerId = topic.CustomerId!,
                    CustomerName = topic.CustomerName ?? string.Empty,
                    NextOffset = 0,
                    PageSize = 5
                });
            }

            return await HandleNextCustomerDocumentsAsync(context);
        }

        private async Task<string> HandleCustomerFavoriteProductsShortcutAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var topic = GetActiveTopicState(context);
            string? customerId = topic?.CustomerId ?? (topic?.TopicType == TopicType.CustomerDetail ? topic.EntityId : null);
            string? customerName = topic?.CustomerName ?? topic?.EntityName;
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return "Tidak ada pelanggan aktif. Ketik /pelanggan <nama> dulu.";
            }

            var favorites = await _posDbService.GetCustomerFavoriteProductsAsync(customerId, 10);
            if (!favorites.Any())
            {
                return $"Belum ada produk favorit untuk {FormatOptional(customerName)}.";
            }

            SetTopicState(
                context,
                "customer_detail",
                entityId: customerId,
                entityName: customerName,
                customerId: customerId,
                customerName: customerName,
                lastData: favorites);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} PRODUK FAVORIT - {FormatOptional(customerName)}");
            sb.AppendLine();
            for (int i = 0; i < favorites.Count; i++)
            {
                var favorite = favorites[i];
                sb.AppendLine($"{i + 1}. {FormatOptional(favorite.ProductName)} | {FormatStockValue(favorite.Quantity)} {GetUnitLabel(favorite.Unit)} | {favorite.TransactionCount} nota");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleCustomerReceivablesShortcutAsync(AutomationExecutionContext context)
        {
            var topic = GetActiveTopicState(context);
            string? customerName = topic?.CustomerName ?? topic?.EntityName;
            if (string.IsNullOrWhiteSpace(customerName))
            {
                return "Tidak ada pelanggan aktif. Ketik /pelanggan <nama> dulu.";
            }

            return await HandleReceivableDetailAsync(customerName, context);
        }

        private async Task<string?> TryHandleDeterministicIntentAsync(string userMessage, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return null;
            }

            string normalized = NormalizeText(userMessage);
            if (TryHandleExpiredFollowUp(normalized, context, out var expiredFollowUp))
            {
                return expiredFollowUp;
            }

            if (normalized is "ekspor" or "export")
            {
                return HasPendingExport(context)
                    ? await HandlePendingExportConfirmationAsync(context)
                    : BuildExportMenuResponse();
            }

            if (ContainsAny(normalized, "lanjut", "lanjutkan", "next", "berikutnya"))
            {
                if (normalized.Contains("dokumen", StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleDocumentPaginationAsync(context);
                }

                if (normalized.Contains("transaksi", StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleCustomerTransactionPaginationAsync(context);
                }

                if (normalized.Contains("pelanggan", StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleCustomerPaginationAsync(context);
                }

                if (normalized.Contains("supplier", StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleSupplierPaginationAsync(context);
                }

                if (normalized.Contains("produk", StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleProductPaginationAsync(context);
                }

                var topic = GetActiveTopicState(context);
                if (topic != null)
                {
                    return topic.Topic switch
                    {
                        "dokumen" => await HandleDocumentPaginationAsync(context),
                        "pelanggan" => await HandleCustomerPaginationAsync(context),
                        "supplier" => await HandleSupplierPaginationAsync(context),
                        "produk" => await HandleProductPaginationAsync(context),
                        "transaksi" => await HandleCustomerTransactionPaginationAsync(context),
                        _ => await HandleBestAvailablePaginationAsync(context)
                    };
                }

                return await HandleBestAvailablePaginationAsync(context);
            }

            if (ContainsAny(normalized, "yang tadi", "itu tadi", "detailnya", "lengkap") &&
                GetActiveTopicState(context) is { } activeTopic)
            {
                if (string.Equals(activeTopic.Topic, "dokumen", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(activeTopic.EntityName))
                {
                    return await HandleDocumentLookupAsync(activeTopic.EntityName, context);
                }

                if (string.Equals(activeTopic.Topic, "pelanggan", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(activeTopic.EntityName))
                {
                    return await HandleCustomersAsync(activeTopic.EntityName, context);
                }
            }

            if (ContainsAny(normalized, "transaksinya", "riwayat transaksinya", "belanjanya") &&
                GetActiveTopicState(context) is { Topic: "pelanggan", EntityName: { Length: > 0 } } customerTopic)
            {
                return await HandleCustomerTransactionsAsync(customerTopic.EntityName!, context);
            }

            if (ContainsAny(normalized, "tampilkan lengkap", "lihat lengkap", "detail lengkap", "semua item", "semua produk dokumen", "dokumen ini", "struk ini") &&
                _lastDocumentBySender.TryGetValue(BuildSenderStateKey(context), out var lastDocumentNumber) &&
                !string.IsNullOrWhiteSpace(lastDocumentNumber))
            {
                return await HandleDocumentLookupAsync(lastDocumentNumber, context);
            }

            var intent = DetectDeterministicIntent(userMessage);
            if (intent == null)
            {
                return null;
            }

            if (intent.OwnerOnly && !context.IsOwner)
            {
                return "Data ini hanya untuk owner.";
            }

            return intent.Kind switch
            {
                "loyal_customers" => await HandleLoyalCustomersAsync(context),
                "user_identity" => BuildUserIdentityResponse(context),
                "customers" => await HandleCustomersAsync(intent.Argument ?? string.Empty, context),
                "suppliers" => await HandleSuppliersAsync(intent.Argument ?? string.Empty, context),
                "users" => await HandleUsersAsync(intent.Argument ?? string.Empty),
                "stock_lookup" => await HandleStockAsync(intent.Argument ?? string.Empty, context),
                "products" => await HandleProductsCommandAsync(intent.Argument ?? string.Empty, context),
                "product_rank" => await HandleProductsCommandAsync(intent.Argument ?? string.Empty, context),
                "product_sales" => await HandleProductSalesAsync(intent.Argument ?? string.Empty),
                "purchase_history" => await HandlePurchaseHistoryIntentAsync(intent.Argument, context),
                "last_document" => await HandleRecentDocumentsAsync(intent.Argument ?? string.Empty, context),
                "expiry_info" => await HandleExpiryInfoAsync(intent.Argument ?? string.Empty, context),
                "slow_moving" => await HandleSlowMovingProductsAsync(),
                "dead_stock" => await HandleDeadStockAsync(),
                "sleeping_stock" => await HandleSleepingStockAsync(),
                "stock_analysis" => await HandleStockMovementAnalysisAsync(),
                "shadow_stock" => await HandleShadowStockAsync(),
                "effective_stock" => await HandleEffectiveStockAsync(intent.Argument ?? string.Empty),
                "profit_explain" => await HandleProfitExplanationAsync(),
                "document_lookup" => await HandleDocumentLookupAsync(intent.Argument ?? string.Empty, context),
                "customer_documents" => await HandleCustomerDocumentsAsync(intent.Argument ?? string.Empty),
                "document_type_guide" => BuildDocumentTypeGuideResponse(),
                "customer_transactions" => await HandleCustomerTransactionsAsync(intent.Argument ?? string.Empty, context),
                "store_count" => await HandleStoreCountIntentAsync(intent.Argument ?? string.Empty),
                "transaction_count" => await HandleSalesTransactionCountAsync(intent.Argument ?? "today"),
                "bot_capabilities" => BuildBotIdentityResponse(),
                "sales_summary" => await HandleSalesSummaryAsync(intent.Argument ?? "today", context),
                "statistics" => await HandleStatisticsAsync(),
                "export_customers" => await HandleExportCustomersAsync(context),
                "export_suppliers" => await HandleExportSuppliersAsync(context),
                "export_sales" => await HandleExportSalesIntentAsync(intent.Argument, context),
                "export_stock" => await HandleExportStockAsync(context),
                "receivables_list" => await HandleReceivablesListAsync(context),
                "receivables_detail" => await HandleReceivableDetailAsync(intent.Argument ?? string.Empty, context),
                "receivables_total" => await HandleTotalReceivableAsync(),
                "category_search" => await HandleCategorySearchAsync(intent.Argument ?? string.Empty, context),
                "top_supplier" => await HandleTopSupplierAsync(),
                "cashier_performance" => await HandleCashierReportAsync(),
                "daily_trend" => await HandleDailyTrendAsync(intent.Argument),
                "export_receivables" => await HandleExportReceivablesAsync(context),
                "export_bundle" => await HandleExportBundleAsync(context),
                "confirm_export" => await HandlePendingExportConfirmationAsync(context),
                "next_customers" => await HandleCustomerPaginationAsync(context),
                "next_suppliers" => await HandleSupplierPaginationAsync(context),
                "next_products" => await HandleProductPaginationAsync(context),
                "next_document" => await HandleDocumentPaginationAsync(context),
                "next_customer_tx" => await HandleCustomerTransactionPaginationAsync(context),
                "export_zero_cost" => await HandleExportZeroCostAsync(context, string.Equals(intent.Argument, "all", StringComparison.OrdinalIgnoreCase)),
                _ => null
            };
        }

        private bool TryHandleExpiredFollowUp(string normalizedMessage, AutomationExecutionContext context, out string response)
        {
            response = string.Empty;
            if (!ContainsAny(normalizedMessage, "berapa hari", "hari lagi", "masih lama", "kapan expired", "kapan kadaluarsa", "kapan kedaluwarsa", "sudah dekat"))
            {
                return false;
            }

            var state = GetActiveTopicState(context);
            if (state == null ||
                !string.Equals(state.Topic, "expired", StringComparison.OrdinalIgnoreCase) ||
                !state.ExpiryDate.HasValue)
            {
                return false;
            }

            int daysLeft = (int)Math.Floor((state.ExpiryDate.Value.Date - DateTime.Today).TotalDays);
            response = $"{FormatOptional(state.EntityName)} expired {FormatDateTime(state.ExpiryDate)}.\n" +
                       $"Berarti {daysLeft} hari lagi dari hari ini ({DateTime.Today:dd/MM/yyyy}).";
            return true;
        }

        private DeterministicIntent? DetectDeterministicIntent(string userMessage)
        {
            string normalized = NormalizeText(userMessage);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (IsCapabilityQuestion(normalized))
            {
                return new DeterministicIntent { Kind = "bot_capabilities" };
            }

            if (ContainsAny(normalized, "namaku siapa", "siapa nama saya", "nama saya siapa"))
            {
                return new DeterministicIntent { Kind = "user_identity" };
            }

            if (normalized is "ya" or "yes" or "y")
            {
                return new DeterministicIntent { Kind = "confirm_export", OwnerOnly = true };
            }

            if (normalized.Contains("lanjut pelanggan", StringComparison.OrdinalIgnoreCase))
            {
                return new DeterministicIntent { Kind = "next_customers", OwnerOnly = true };
            }

            if (normalized.Contains("lanjut supplier", StringComparison.OrdinalIgnoreCase))
            {
                return new DeterministicIntent { Kind = "next_suppliers", OwnerOnly = true };
            }

            if (normalized.Contains("lanjut produk", StringComparison.OrdinalIgnoreCase))
            {
                return new DeterministicIntent { Kind = "next_products", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "lanjut dokumen", "lanjutkan dokumen"))
            {
                return new DeterministicIntent { Kind = "next_document", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "lanjut transaksi", "lanjutkan transaksi"))
            {
                return new DeterministicIntent { Kind = "next_customer_tx", OwnerOnly = true };
            }

            if (ContainsCountKeyword(normalized, "pelanggan"))
            {
                return new DeterministicIntent { Kind = "store_count", Argument = "customers", OwnerOnly = true };
            }

            if (ContainsCountKeyword(normalized, "supplier"))
            {
                return new DeterministicIntent { Kind = "store_count", Argument = "suppliers", OwnerOnly = true };
            }

            if (ContainsCountKeyword(normalized, "produk") || ContainsCountKeyword(normalized, "barang"))
            {
                return new DeterministicIntent { Kind = "store_count", Argument = "products", OwnerOnly = true };
            }

            if ((normalized.Contains("transaksi", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("faktur", StringComparison.OrdinalIgnoreCase)) &&
                (normalized.Contains("jumlah", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("berapa", StringComparison.OrdinalIgnoreCase)) &&
                !normalized.Contains("pelanggan", StringComparison.OrdinalIgnoreCase))
            {
                return new DeterministicIntent
                {
                    Kind = "transaction_count",
                    Argument = TryExtractSalesPeriodArgument(userMessage) ?? "today",
                    OwnerOnly = true
                };
            }

            if ((normalized.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("jumlah", StringComparison.OrdinalIgnoreCase)) &&
                (normalized.Contains("piutang", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("hutang", StringComparison.OrdinalIgnoreCase)))
            {
                return new DeterministicIntent { Kind = "receivables_total", OwnerOnly = true };
            }

            string? documentNumber = ExtractDocumentNumber(userMessage);
            if (!string.IsNullOrWhiteSpace(documentNumber) &&
                (normalized.Contains("cek dokumen", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("dokumen", StringComparison.OrdinalIgnoreCase)))
            {
                return new DeterministicIntent { Kind = "document_lookup", Argument = documentNumber, OwnerOnly = true };
            }

            if (ContainsAny(normalized, "tampilkan lengkap", "lihat lengkap", "detail lengkap", "semua item", "semua produk dokumen", "dokumen ini", "struk ini"))
            {
                string? explicitDocumentNumber = ExtractDocumentNumber(userMessage);
                if (!string.IsNullOrWhiteSpace(explicitDocumentNumber))
                {
                    return new DeterministicIntent { Kind = "document_lookup", Argument = explicitDocumentNumber, OwnerOnly = true };
                }
            }

            if (ContainsAny(normalized,
                "cek dokumen pelanggan",
                "dokumen pelanggan",
                "cek struk pelanggan",
                "struk pelanggan",
                "riwayat struk pelanggan"))
            {
                string? customerName = ExtractKeywordAfterAny(userMessage,
                    "cek dokumen pelanggan",
                    "dokumen pelanggan",
                    "cek struk pelanggan",
                    "struk pelanggan",
                    "riwayat struk pelanggan");

                if (LooksLikeCustomerNameCandidate(customerName))
                {
                    return new DeterministicIntent
                    {
                        Kind = "customer_documents",
                        Argument = customerName,
                        OwnerOnly = true
                    };
                }
            }

            if (normalized.Contains("dokumen pembelian", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("dokumen penjualan", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("pembelian atau penjualan", StringComparison.OrdinalIgnoreCase))
            {
                return new DeterministicIntent { Kind = "document_type_guide", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "dokumen terakhir", "struk terakhir", "nota terakhir", "pembelian terakhir", "penjualan terakhir"))
            {
                string type = ContainsAny(normalized, "pembelian", "purchase", "restock") ? "purchase" :
                    ContainsAny(normalized, "penjualan", "sales") ? "sales" : string.Empty;
                return new DeterministicIntent { Kind = "last_document", Argument = type, OwnerOnly = true };
            }

            if (ContainsAny(normalized, "riwayat beli", "riwayat pembelian", "kapan terakhir beli", "history purchase", "riwayat restock", "history restock", "pas input /purchase", "input purchase"))
            {
                return new DeterministicIntent
                {
                    Kind = "purchase_history",
                    Argument = ExtractPurchaseHistoryProductKeyword(userMessage),
                    OwnerOnly = true
                };
            }

            if (ContainsAny(normalized, "expired", "kadaluarsa", "kedaluwarsa") ||
                Regex.IsMatch(normalized, @"\bexp\b", RegexOptions.IgnoreCase))
            {
                return new DeterministicIntent
                {
                    Kind = "expiry_info",
                    Argument = ExtractExpiryProductKeyword(userMessage),
                    OwnerOnly = true
                };
            }

            if (ContainsAny(normalized, "slow moving", "barang lambat", "produk lambat", "kurang laku tapi stok ada"))
            {
                return new DeterministicIntent { Kind = "slow_moving", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "dead stock", "barang mati", "produk mati", "tidak laku"))
            {
                return new DeterministicIntent { Kind = "dead_stock", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "sleeping stock", "sleeping mandatory", "mandatory jarang laku", "barang wajib jarang laku"))
            {
                return new DeterministicIntent { Kind = "sleeping_stock", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "analisa stok", "analisis stok", "pergerakan stok", "analisa pergerakan stok"))
            {
                return new DeterministicIntent { Kind = "stock_analysis", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "shadow stok", "shadow stock", "unit besar belum mapping", "unit besar belum dimapping"))
            {
                return new DeterministicIntent { Kind = "shadow_stock", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "stok efektif"))
            {
                string? keyword = ExtractKeywordAfterAny(userMessage, "stok efektif");
                return new DeterministicIntent { Kind = "effective_stock", Argument = keyword, OwnerOnly = true };
            }

            if (ContainsAny(normalized, "cara hitung profit", "hitung profit", "profit toko", "margin toko", "laba toko"))
            {
                return new DeterministicIntent { Kind = "profit_explain", OwnerOnly = true };
            }

            if (normalized.Contains("pelanggan terloyal", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("pelanggan loyal", StringComparison.OrdinalIgnoreCase) ||
                (normalized.Contains("pelanggan", StringComparison.OrdinalIgnoreCase) &&
                 normalized.Contains("loyal", StringComparison.OrdinalIgnoreCase)))
            {
                return new DeterministicIntent { Kind = "loyal_customers", OwnerOnly = true };
            }

            if (normalized.Contains("pelanggan", StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(normalized, "transaksi terbanyak", "paling banyak transaksi", "paling sering belanja"))
            {
                return new DeterministicIntent { Kind = "customers", Argument = string.Empty, OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor pelanggan", "export pelanggan", "kirim file pelanggan"))
            {
                return new DeterministicIntent { Kind = "export_customers", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor supplier", "export supplier", "kirim file supplier"))
            {
                return new DeterministicIntent { Kind = "export_suppliers", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor stok", "export stok", "kirim file stok"))
            {
                return new DeterministicIntent { Kind = "export_stock", OwnerOnly = true };
            }

            if (IsZeroCostExportAllKeyword(normalized))
            {
                return new DeterministicIntent { Kind = "export_zero_cost", Argument = "all", OwnerOnly = true };
            }

            if (IsZeroCostExportKeyword(normalized))
            {
                return new DeterministicIntent { Kind = "export_zero_cost", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor produk", "export produk", "kirim file produk", "kirim daftar produk"))
            {
                return new DeterministicIntent { Kind = "export_stock", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor piutang", "export piutang", "ekspor hutang", "export hutang"))
            {
                return new DeterministicIntent { Kind = "export_receivables", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor lengkap", "export lengkap", "export semua", "ekspor semua data"))
            {
                return new DeterministicIntent { Kind = "export_bundle", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "ekspor penjualan", "export penjualan", "kirim file penjualan"))
            {
                return new DeterministicIntent
                {
                    Kind = "export_sales",
                    Argument = TryExtractSalesPeriodArgument(userMessage) ?? "today",
                    OwnerOnly = true
                };
            }

            string? salesPeriod = TryExtractSalesPeriodArgument(userMessage);
            if (salesPeriod != null &&
                (LooksLikeSalesSummaryQuery(normalized) || LooksLikeStandaloneSalesPeriodQuery(normalized)))
            {
                return new DeterministicIntent
                {
                    Kind = "sales_summary",
                    Argument = salesPeriod,
                    OwnerOnly = true
                };
            }

            if (ContainsAny(normalized, "daftar piutang", "daftar hutang", "hutang pelanggan", "piutang pelanggan", "kredit pelanggan"))
            {
                return new DeterministicIntent { Kind = "receivables_list", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "piutang siapa saja", "siapa saja piutang", "siapa yang punya piutang"))
            {
                return new DeterministicIntent { Kind = "receivables_list", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "piutang ", "hutang "))
            {
                string? name = ExtractKeywordAfterAny(userMessage, "piutang", "hutang");
                if (LooksLikeCustomerNameCandidate(name))
                {
                    return new DeterministicIntent
                    {
                        Kind = "receivables_detail",
                        Argument = name,
                        OwnerOnly = true
                    };
                }
            }

            if (ContainsAny(normalized,
                "produk terlaris",
                "produk terlaku",
                "barang terlaris",
                "barang terlaku",
                "produk terbanyak"))
            {
                return new DeterministicIntent { Kind = "product_rank", Argument = "terlaris", OwnerOnly = true };
            }

            if (ContainsAny(normalized,
                "produk profit",
                "profit tertinggi",
                "margin tertinggi",
                "produk paling untung"))
            {
                return new DeterministicIntent { Kind = "product_rank", Argument = "profit", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "tampilkan semua produk", "daftar semua produk", "semua produk", "daftar produk"))
            {
                return new DeterministicIntent { Kind = "products", Argument = string.Empty, OwnerOnly = true };
            }

            if (LooksLikeCategoryStockQuery(normalized))
            {
                return new DeterministicIntent
                {
                    Kind = "category_search",
                    Argument = ExtractCategoryKeyword(userMessage),
                    OwnerOnly = true
                };
            }

            if (ContainsAny(normalized, "stok ", "stock ", "cek stok", "detail produk", "cek detail produk", "harga "))
            {
                string? keyword = ExtractKeywordAfterAny(userMessage, "cek detail produk", "detail produk", "cek stok", "stok", "stock", "harga");
                if (!string.IsNullOrWhiteSpace(keyword) && !ContainsAny(NormalizeText(keyword), "rendah", "kritis", "minus", "habis"))
                {
                    return new DeterministicIntent { Kind = "stock_lookup", Argument = keyword, OwnerOnly = false };
                }
            }

            if (ContainsAny(normalized, "statistik", "insight bisnis", "ringkasan bisnis", "kinerja bisnis"))
            {
                return new DeterministicIntent { Kind = "statistics", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "tren harian", "trend harian", "tren penjualan", "omzet 7 hari", "penjualan 7 hari"))
            {
                return new DeterministicIntent { Kind = "daily_trend", Argument = "7", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "supplier terbesar", "supplier paling sering", "supplier terbanyak", "top supplier", "pembelian supplier"))
            {
                return new DeterministicIntent { Kind = "top_supplier", OwnerOnly = true };
            }

            if (ContainsAny(normalized, "kasir terbaik", "performa kasir", "kinerja kasir", "laporan kasir"))
            {
                return new DeterministicIntent { Kind = "cashier_performance", OwnerOnly = true };
            }

            if (LooksLikeCategoryStockQuery(normalized))
            {
                return new DeterministicIntent
                {
                    Kind = "category_search",
                    Argument = ExtractCategoryKeyword(userMessage),
                    OwnerOnly = true
                };
            }

            if (ContainsAny(normalized,
                "cek transaksi", "transaksi pelanggan", "detail pelanggan", "riwayat pelanggan", "belanja pelanggan",
                "cek detail transaksi", "detail transaksi"))
            {
                string? customerName = ExtractKeywordAfterAny(userMessage,
                    "cek detail transaksi",
                    "detail transaksi",
                    "cek transaksi",
                    "transaksi pelanggan",
                    "detail pelanggan",
                    "riwayat pelanggan",
                    "belanja pelanggan");

                if (LooksLikeCustomerNameCandidate(customerName))
                {
                    return new DeterministicIntent
                    {
                        Kind = "customer_transactions",
                        Argument = customerName,
                        OwnerOnly = true
                    };
                }
            }

            string? customerKeyword = ExtractKeywordAfterAny(userMessage, "namanya", "nama pelanggan", "pelanggan namanya");
            if (!string.IsNullOrWhiteSpace(customerKeyword))
            {
                return new DeterministicIntent { Kind = "customers", Argument = customerKeyword, OwnerOnly = true };
            }

            if (normalized.Contains("daftar pelanggan", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("pelanggan ", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("pelanggan", StringComparison.OrdinalIgnoreCase))
            {
                string? keyword = ExtractKeywordAfterAny(userMessage, "daftar pelanggan", "pelanggan");
                return new DeterministicIntent { Kind = "customers", Argument = keyword, OwnerOnly = true };
            }

            if (normalized.Contains("supplier", StringComparison.OrdinalIgnoreCase))
            {
                string? keyword = ExtractKeywordAfterAny(userMessage, "daftar supplier", "supplier");
                return new DeterministicIntent { Kind = "suppliers", Argument = keyword, OwnerOnly = true };
            }

            if (normalized.Contains("kasir", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("user", StringComparison.OrdinalIgnoreCase))
            {
                string? keyword = ExtractKeywordAfterAny(userMessage, "daftar kasir", "daftar user", "kasir", "user");
                return new DeterministicIntent { Kind = "users", Argument = keyword, OwnerOnly = true };
            }

            if (normalized.Contains("data penjualan produk", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("penjualan produk", StringComparison.OrdinalIgnoreCase))
            {
                string? keyword = ExtractKeywordAfterAny(userMessage, "data penjualan produk", "data penjualan", "penjualan produk", "penjualan");
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    return new DeterministicIntent { Kind = "product_sales", Argument = keyword, OwnerOnly = true };
                }
            }

            return null;
        }

        private async Task<string> HandleTopCustomersIntentAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var customers = (await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true))
                .Where(customer => customer.PurchaseCount >= 8 || customer.TotalSpent >= 5_000_000m)
                .OrderByDescending(customer => customer.PurchaseCount)
                .ThenByDescending(customer => customer.TotalSpent)
                .ThenBy(customer => customer.Name)
                .Take(10)
                .ToList();

            if (!customers.Any())
            {
                return "Belum ada data pelanggan loyal.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconCustomer} PELANGGAN LOYAL");
            sb.AppendLine("Kriteria: >= 8 transaksi atau total belanja >= Rp 5.000.000. Urut utama: frekuensi transaksi.");
            sb.AppendLine();

            for (int i = 0; i < customers.Count; i++)
            {
                string medal = i switch
                {
                    0 => "\U0001F947",
                    1 => "\U0001F948",
                    2 => "\U0001F949",
                    _ => $"{i + 1}."
                };

                var customer = customers[i];
                sb.AppendLine($"{medal} {FormatOptional(customer.Name)} | {customer.PurchaseCount} transaksi | {FormatCurrency(customer.TotalSpent)}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleCustomerTransactionsAsync(string query, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return $"Gunakan format: cek transaksi <nama pelanggan>";
            }

            var matches = await _posDbService.GetCustomersAsync(query, 5, onlyCustomers: true);
            if (!matches.Any())
            {
                return $"Pelanggan dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var exactMatch = matches.FirstOrDefault(customer =>
                string.Equals(NormalizeText(customer.Name ?? string.Empty), NormalizeText(query), StringComparison.Ordinal));
            if (exactMatch != null || matches.Count == 1)
            {
                return await BuildCustomerDetailResponseAsync(exactMatch ?? matches[0], context);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconCustomer} PELANGGAN - \"{query}\"");
            sb.AppendLine();
            sb.AppendLine("Ada beberapa kandidat. Perjelas nama pelanggan:");
            foreach (var customer in matches)
            {
                sb.AppendLine(BuildCustomerListLine(customer, null));
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleCustomerDocumentsAsync(string query)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return "Gunakan format: cek dokumen pelanggan <nama pelanggan>";
            }

            var matches = await _posDbService.GetCustomersAsync(query, 5, onlyCustomers: true);
            if (!matches.Any())
            {
                return $"Pelanggan dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var exactMatch = matches.FirstOrDefault(candidate =>
                string.Equals(NormalizeText(candidate.Name ?? string.Empty), NormalizeText(query), StringComparison.Ordinal));
            if (exactMatch == null && matches.Count > 1)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{IconCustomer} PELANGGAN - \"{query}\"");
                sb.AppendLine();
                sb.AppendLine("Ada beberapa kandidat. Perjelas nama pelanggan:");
                foreach (var match in matches)
                {
                    sb.AppendLine(BuildCustomerListLine(match, null));
                }

                return sb.ToString().TrimEnd();
            }

            var customer = exactMatch ?? matches[0];
            if (string.IsNullOrWhiteSpace(customer.Id))
            {
                return $"Pelanggan dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var documents = await _posDbService.GetCustomerRecentDocumentsAsync(customer.Id, 5);
            if (!documents.Any())
            {
                return $"Belum ada struk penjualan untuk {FormatOptional(customer.Name)}.";
            }

            var response = new StringBuilder();
            response.AppendLine($"{IconDocument} DOKUMEN PELANGGAN - {FormatOptional(customer.Name)}");
            response.AppendLine();

            foreach (var document in documents)
            {
                string outstandingLabel = document.OutstandingBalance > 0
                    ? $" | Sisa {FormatCurrency(document.OutstandingBalance)}"
                    : " | Lunas";
                response.AppendLine(
                    $"{FormatShortDate(document.Date)} | {FormatCompactDocumentNumber(document.DocumentNumber)} | Total {FormatCurrency(document.Total)}{outstandingLabel}");
            }

            return response.ToString().TrimEnd();
        }

        private async Task<string> HandleStoreCountIntentAsync(string target)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            return target switch
            {
                "customers" => $"{IconCustomer} Total pelanggan terdaftar: {await _posDbService.GetTotalCustomersAsync()}",
                "suppliers" => $"\U0001F3ED Total supplier terdaftar: {await _posDbService.GetTotalSuppliersAsync()}",
                "products" => $"{IconPackage} Total produk aktif: {await _posDbService.GetProductCountAsync()}",
                _ => "Data total tidak tersedia."
            };
        }

        private async Task<string> HandleSalesTransactionCountAsync(string period)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var (startDate, endDate, _, titleLabel, dateLabel) = ResolveSalesPeriod(period);
            int transactionCount = await _posDbService.GetSalesTransactionCountAsync(startDate, endDate);
            return $"{IconReceipt} Jumlah transaksi {titleLabel.ToLowerInvariant()}: {transactionCount} ({dateLabel}).";
        }

        private async Task<string> HandleSalesSummaryAsync(string period, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var (startDate, endDate, periodKey, titleLabel, dateLabel) = ResolveSalesPeriod(period);
            int transactionCount = await _posDbService.GetSalesTransactionCountAsync(startDate, endDate);
            decimal revenue = await _posDbService.GetSalesRevenueAsync(startDate, endDate);
            decimal profit = await _posDbService.GetSalesProfitAsync(startDate, endDate);
            var topSelling = await _posDbService.GetTopSellingProductsAsync(startDate, endDate, 1);
            decimal average = transactionCount == 0 ? 0 : revenue / transactionCount;

            bool hasData = transactionCount > 0;
            if (hasData)
            {
                SetPendingExport(context, "sales", period);
            }
            else
            {
                ClearPendingExport(context);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconCheck} Saya siapkan data penjualan {titleLabel.ToLowerInvariant()}.");
            sb.AppendLine();
            sb.AppendLine($"{IconChart} Ringkasan Penjualan {titleLabel} ({dateLabel})");
            sb.AppendLine($"• Total transaksi : {transactionCount}");
            sb.AppendLine($"• Total omzet     : {FormatCurrency(revenue)}");
            sb.AppendLine($"• Total profit    : {FormatCurrency(profit)}");
            sb.AppendLine($"• Rata-rata       : {FormatCurrency(average)} per transaksi");
            sb.AppendLine($"• Produk terlaris : {BuildTopSellingLabel(topSelling.FirstOrDefault())}");
            sb.AppendLine();
            sb.Append(hasData
                ? "Balas YA atau ketik EKSPOR PENJUALAN untuk kirim file CSV detail."
                : "Belum ada transaksi pada periode ini, jadi file export belum disiapkan.");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleExportCustomersAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var topic = GetActiveTopicState(context);
            var customers = topic?.TopicType switch
            {
                TopicType.LoyalCustomers => await GetLoyalCustomerSourceAsync(),
                TopicType.AtRiskCustomers => await GetAtRiskCustomerSourceAsync(),
                _ => (await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true))
                    .OrderByDescending(customer => customer.PurchaseCount)
                    .ThenByDescending(customer => customer.TotalSpent)
                    .ThenBy(customer => customer.Name)
                    .ToList()
            };

            if (!customers.Any())
            {
                return "Tidak ada data pelanggan untuk diekspor.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"pelanggan_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateCustomerCsv(customers),
                $"Data pelanggan lengkap ({customers.Count} pelanggan)",
                $"{IconCheck} File CSV pelanggan telah dikirim. ({customers.Count} pelanggan)");
        }

        private async Task<string> HandleExportSuppliersAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var suppliers = (await _posDbService.GetSuppliersAsync(null, null))
                .OrderBy(supplier => supplier.Name)
                .ToList();

            if (!suppliers.Any())
            {
                return "Tidak ada data supplier untuk diekspor.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"supplier_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateSupplierCsv(suppliers),
                $"Data supplier lengkap ({suppliers.Count} supplier)",
                $"{IconCheck} File CSV supplier telah dikirim. ({suppliers.Count} supplier)");
        }

        private async Task<string> HandleExportSalesAsync(string period, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var (startDate, endDate, periodKey, titleLabel, _) = ResolveSalesPeriod(period);
            var items = await _posDbService.GetSalesLineItemsAsync(startDate, endDate);
            if (!items.Any())
            {
                return $"Belum ada data penjualan untuk {titleLabel.ToLowerInvariant()}.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"penjualan_{periodKey}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateSalesCsv(items, titleLabel),
                $"Detail penjualan {titleLabel.ToLowerInvariant()} ({items.Count} baris)",
                $"{IconCheck} File CSV penjualan {titleLabel.ToLowerInvariant()} telah dikirim. ({items.Count} baris)");
        }

        private async Task<string> HandleExportSalesIntentAsync(string? requestedPeriod, AutomationExecutionContext context)
        {
            string resolvedPeriod = ResolveExportSalesPeriod(requestedPeriod, context);
            if (string.Equals(resolvedPeriod, "today", StringComparison.OrdinalIgnoreCase) &&
                _pendingExportBySender.TryGetValue(BuildSenderStateKey(context), out var pending) &&
                string.Equals(pending.Kind, "sales", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pending.Argument))
            {
                return await HandlePendingExportConfirmationAsync(context);
            }

            return await HandleExportSalesAsync(resolvedPeriod, context);
        }

        private async Task<string> HandleExportStockAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var products = (await _posDbService.GetAllProductsAsync())
                .OrderBy(product => product.Name)
                .ToList();

            if (!products.Any())
            {
                return "Tidak ada data stok untuk diekspor.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"produk_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateStockCsv(products),
                $"Data produk lengkap ({products.Count} produk)",
                $"{IconCheck} File CSV produk telah dikirim. ({products.Count} produk)");
        }

        private async Task<string> HandleExportZeroCostAsync(AutomationExecutionContext context, bool includeAllZeroCostProducts = false)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (includeAllZeroCostProducts)
            {
                var allRows = (await _posDbService.GetNoCostProductsForExportAsync(includeAllZeroCostProducts: true))
                    .OrderByDescending(product => product.QuantitySold)
                    .ThenBy(product => product.ProductName)
                    .ToList();

                if (!allRows.Any())
                {
                    return "Semua produk sudah memiliki harga modal.";
                }

                return await SendCsvDocumentAsync(
                    context,
                    $"produk_tanpa_modal_semua_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    CsvExportHelper.GenerateZeroCostAllCsv(allRows),
                    $"Audit semua produk tanpa modal ({allRows.Count} produk)",
                    $"{IconCheck} File CSV semua produk tanpa modal dikirim. ({allRows.Count} produk)");
            }

            var tierAProducts = (await _posDbService.GetZeroCostProductsAsync())
                .OrderByDescending(product => product.Revenue30Days)
                .ThenBy(product => product.ProductName)
                .ToList();

            if (!tierAProducts.Any())
            {
                return "Semua produk sudah memiliki harga modal.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"produk_tanpa_modal_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateZeroCostTierACsv(tierAProducts),
                $"Produk tanpa modal Tier A ({tierAProducts.Count} produk)",
                $"{IconCheck} File CSV produk tanpa modal Tier A dikirim. ({tierAProducts.Count} produk)");
        }

        private async Task<string> HandleReceivablesListAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var receivables = await _posDbService.GetCustomerReceivablesAsync();
            decimal total = receivables.Sum(item => item.TotalOwed);
            if (!receivables.Any())
            {
                return "Tidak ada piutang pelanggan saat ini.";
            }

            SetPendingExport(context, "receivables");
            SetTopicState(
                context,
                "receivable_list",
                entityName: "piutang pelanggan",
                currentPage: 1,
                exportType: "piutang.csv",
                lastData: receivables);

            var sb = new StringBuilder();
            sb.AppendLine("\U0001F4B3 PIUTANG PELANGGAN");
            sb.AppendLine();
            sb.AppendLine($"Total piutang  : {FormatCurrency(total)}");
            sb.AppendLine($"Pelanggan aktif: {receivables.Count}");
            sb.AppendLine();
            sb.AppendLine($"{IconRed} TERBESAR / OVERDUE");

            for (int i = 0; i < Math.Min(receivables.Count, 5); i++)
            {
                var item = receivables[i];
                string dueLabel = item.OldestDueDate.HasValue ? $" | JT {FormatShortDate(item.OldestDueDate)}" : string.Empty;
                sb.AppendLine($"{i + 1}. {item.CustomerName} | {item.InvoiceCount} faktur | {FormatCurrency(item.TotalOwed)}{dueLabel}");
            }

            sb.AppendLine();
            sb.AppendLine($"{IconChart} Insight:");
            sb.AppendLine($"- {receivables[0].CustomerName} piutang terbesar.");
            if (receivables.Any(item => item.OldestDueDate.HasValue && item.OldestDueDate.Value.Date < DateTime.Today))
            {
                sb.AppendLine("- Ada beberapa faktur sudah melewati jatuh tempo.");
            }
            sb.AppendLine();
            sb.AppendLine("\U0001F4A1 Ketik:");
            sb.AppendLine($"- /piutang {receivables[0].CustomerName}");
            sb.AppendLine($"- /pelanggan {receivables[0].CustomerName}");
            sb.Append("- EKSPOR PIUTANG");
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleReceivableDetailAsync(string query, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return "Gunakan format: piutang <nama pelanggan>";
            }

            var matches = await _posDbService.GetCustomersAsync(query, 5, onlyCustomers: true);
            if (!matches.Any())
            {
                return $"Pelanggan dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var exactMatch = matches.FirstOrDefault(customer =>
                string.Equals(NormalizeText(customer.Name ?? string.Empty), NormalizeText(query), StringComparison.Ordinal));
            var customer = exactMatch ?? matches.FirstOrDefault();
            if (customer == null || string.IsNullOrWhiteSpace(customer.Id))
            {
                return $"Pelanggan dengan kata kunci \"{query}\" tidak ditemukan.";
            }

            var invoices = await _posDbService.GetCustomerReceivableDetailAsync(customer.Id);
            if (!invoices.Any())
            {
                return $"Pelanggan {FormatOptional(customer.Name)} tidak memiliki piutang aktif.";
            }

            decimal total = invoices.Sum(item => item.OutstandingBalance);
            int overdueCount = invoices.Count(item => item.DueDate.HasValue && item.DueDate.Value.Date < DateTime.Today);

            SetPendingExport(context, "receivables_detail", customer.Id);
            SetTopicState(
                context,
                "receivable_detail",
                entityId: customer.Id,
                entityName: customer.Name,
                currentPage: 1,
                exportType: $"piutang_{MakeSafeFileToken(customer.Name)}.csv",
                customerId: customer.Id,
                customerName: customer.Name,
                relatedDocumentNumbers: invoices
                    .Select(invoice => invoice.DocumentNumber)
                    .Where(number => !string.IsNullOrWhiteSpace(number))
                    .Select(number => number!)
                    .ToList(),
                lastData: invoices);

            var sb = new StringBuilder();
            sb.AppendLine($"\U0001F4B3 DETAIL PIUTANG - {FormatOptional(customer.Name)}");
            sb.AppendLine();
            sb.AppendLine($"Total hutang  : {FormatCurrency(total)}");
            sb.AppendLine($"Jumlah faktur : {invoices.Count}");
            sb.AppendLine();
            sb.AppendLine($"{IconClipboard} Faktur Belum Lunas");

            int index = 1;
            foreach (var invoice in invoices.Take(10))
            {
                string dueLabel = invoice.DueDate.HasValue ? FormatShortDate(invoice.DueDate) : "-";
                sb.AppendLine($"{index}. {FormatCompactDocumentNumber(invoice.DocumentNumber)} | {FormatShortDate(invoice.Date)} | JT {dueLabel}");
                sb.AppendLine($"   Sisa: {FormatCurrency(invoice.OutstandingBalance)}");
                sb.AppendLine();
                index++;
            }

            if (overdueCount > 0)
            {
                sb.AppendLine(overdueCount == invoices.Count
                    ? $"{IconWarning} Semua faktur sudah melewati jatuh tempo."
                    : $"{IconWarning} Ada {overdueCount} faktur melewati jatuh tempo.");
            }

            sb.AppendLine();
            sb.AppendLine("\U0001F4A1 Ketik:");
            sb.AppendLine($"- /pelanggan {FormatOptional(customer.Name)}");
            string? firstDocument = invoices.FirstOrDefault()?.DocumentNumber;
            if (!string.IsNullOrWhiteSpace(firstDocument))
            {
                sb.AppendLine($"- DETAIL NOTA {GetDocumentNumberSuffix(firstDocument)}");
            }
            sb.Append("- EKSPOR PIUTANG");

            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleTotalReceivableAsync()
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            decimal total = await _posDbService.GetTotalReceivableAsync();
            int customerCount = (await _posDbService.GetCustomerReceivablesAsync()).Count;
            return $"\U0001F4B3 Total piutang pelanggan: {FormatCurrency(total)} ({customerCount} pelanggan).";
        }

        private async Task<string> HandleExportReceivablesAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var receivables = await _posDbService.GetCustomerReceivablesAsync();
            if (!receivables.Any())
            {
                return "Tidak ada data piutang untuk diekspor.";
            }

            decimal total = receivables.Sum(item => item.TotalOwed);
            return await SendCsvDocumentAsync(
                context,
                $"piutang_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateReceivableCsv(receivables),
                $"Data piutang pelanggan ({receivables.Count} pelanggan)",
                $"{IconCheck} File CSV piutang telah dikirim. ({receivables.Count} pelanggan, total {FormatCurrency(total)})");
        }

        private async Task<string> HandlePendingExportConfirmationAsync(AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (!_pendingExportBySender.TryGetValue(senderKey, out var pending))
            {
                return "Tidak ada export yang sedang menunggu konfirmasi. Coba ketik EKSPOR PENJUALAN HARI INI atau EKSPOR PELANGGAN.";
            }

            return pending.Kind switch
            {
                "sales" => await HandleExportSalesAsync(pending.Argument ?? "today", context),
                "customers" => await HandleExportCustomersAsync(context),
                "customers_loyal" => await HandleExportLoyalCustomersAsync(context),
                "customers_at_risk" => await HandleExportAtRiskCustomersAsync(context),
                "suppliers" => await HandleExportSuppliersAsync(context),
                "stock" => await HandleExportStockAsync(context),
                "receivables" => await HandleExportReceivablesAsync(context),
                "receivables_detail" => await HandleExportReceivableDetailAsync(context, pending.Argument),
                _ => "Jenis export tidak dikenali."
            };
        }

        private async Task<string> HandleExportByContextAsync(string text, AutomationExecutionContext context)
        {
            string normalized = NormalizeText(text);
            var topic = GetActiveTopicState(context);

            if (ContainsAny(normalized, "at risk", "at_risk", "atrisk"))
            {
                return await HandleExportAtRiskCustomersAsync(context);
            }

            if (IsZeroCostExportAllKeyword(normalized))
            {
                return await HandleExportZeroCostAsync(context, includeAllZeroCostProducts: true);
            }

            if (IsZeroCostExportKeyword(normalized))
            {
                return await HandleExportZeroCostAsync(context);
            }

            if (ContainsAny(normalized, "pelanggan", "customer"))
            {
                return topic?.TopicType switch
                {
                    TopicType.AtRiskCustomers => await HandleExportAtRiskCustomersAsync(context),
                    TopicType.LoyalCustomers => await HandleExportLoyalCustomersAsync(context),
                    _ => await HandleExportCustomersAsync(context)
                };
            }

            if (ContainsAny(normalized, "piutang", "hutang"))
            {
                return topic?.TopicType == TopicType.ReceivableDetail
                    ? await HandleExportReceivableDetailAsync(context, topic.EntityId)
                    : await HandleExportReceivablesAsync(context);
            }

            if (ContainsAny(normalized, "nota", "dokumen", "transaksi"))
            {
                if (topic?.TopicType == TopicType.SalesDocumentDetail)
                {
                    return await HandleExportSalesDocumentAsync(context, topic.LastDocumentNumber ?? topic.EntityName);
                }

                if (topic?.TopicType == TopicType.CustomerDetail)
                {
                    return await HandleExportCustomerDocumentsAsync(context, topic.EntityId, topic.EntityName);
                }

                return await HandleExportSalesIntentAsync(null, context);
            }

            if (topic != null)
            {
                return topic.TopicType switch
                {
                    TopicType.ReceivableList => await HandleExportReceivablesAsync(context),
                    TopicType.ReceivableDetail => await HandleExportReceivableDetailAsync(context, topic.EntityId),
                    TopicType.LoyalCustomers => await HandleExportLoyalCustomersAsync(context),
                    TopicType.AtRiskCustomers => await HandleExportAtRiskCustomersAsync(context),
                    TopicType.CustomerDetail => await HandleExportCustomerDocumentsAsync(context, topic.EntityId, topic.EntityName),
                    TopicType.SalesDocumentDetail => await HandleExportSalesDocumentAsync(context, topic.LastDocumentNumber ?? topic.EntityName),
                    _ => HasPendingExport(context) ? await HandlePendingExportConfirmationAsync(context) : BuildExportMenuResponse()
                };
            }

            return HasPendingExport(context)
                ? await HandlePendingExportConfirmationAsync(context)
                : BuildExportMenuResponse();
        }

        private async Task<string> HandleExportLoyalCustomersAsync(AutomationExecutionContext context)
        {
            var customers = await GetLoyalCustomerSourceAsync();
            if (!customers.Any())
            {
                return "Tidak ada data pelanggan loyal untuk diekspor.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"pelanggan_loyal_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateCustomerCsv(customers),
                $"Data pelanggan loyal ({customers.Count} pelanggan)",
                $"{IconCheck} File CSV pelanggan loyal telah dikirim. ({customers.Count} pelanggan)");
        }

        private async Task<string> HandleExportAtRiskCustomersAsync(AutomationExecutionContext context)
        {
            var customers = await GetAtRiskCustomerSourceAsync();
            if (!customers.Any())
            {
                return "Tidak ada data pelanggan at-risk untuk diekspor.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"pelanggan_at_risk_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateCustomerCsv(customers),
                $"Data pelanggan at-risk ({customers.Count} pelanggan)",
                $"{IconCheck} File CSV pelanggan at-risk telah dikirim. ({customers.Count} pelanggan)");
        }

        private async Task<string> HandleExportCustomerDocumentsAsync(AutomationExecutionContext context, string? customerId, string? customerName)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return "Tidak ada pelanggan aktif untuk export nota. Ketik /pelanggan <nama> dulu.";
            }

            var documents = await _posDbService.GetCustomerRecentDocumentsAsync(customerId, 200);
            if (!documents.Any())
            {
                return $"Tidak ada nota pelanggan {FormatOptional(customerName)} untuk diekspor.";
            }

            string safeName = MakeSafeFileToken(customerName);
            return await SendCsvDocumentAsync(
                context,
                $"transaksi_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateCustomerDocumentCsv(documents, customerName ?? string.Empty),
                $"Nota pelanggan {FormatOptional(customerName)} ({documents.Count} nota)",
                $"{IconCheck} File CSV nota pelanggan telah dikirim. ({documents.Count} nota)");
        }

        private async Task<string> HandleExportSalesDocumentAsync(AutomationExecutionContext context, string? documentNumber)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                return "Tidak ada nota aktif untuk diekspor. Ketik DETAIL NOTA <nomor> dulu.";
            }

            var document = await _posDbService.GetDocumentByNumberAsync(documentNumber);
            if (document == null || string.IsNullOrWhiteSpace(document.Id) || document.DocumentTypeId != 2)
            {
                return $"Nota penjualan \"{documentNumber}\" tidak ditemukan.";
            }

            var items = await _posDbService.GetDocumentItemsAsync(document.Id);
            return await SendCsvDocumentAsync(
                context,
                $"nota_{GetDocumentNumberSuffix(document.Number ?? documentNumber)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateDocumentItemCsv(document, items),
                $"Detail nota {document.Number} ({items.Count} item)",
                $"{IconCheck} File CSV nota telah dikirim. ({items.Count} item)");
        }

        private async Task<string> HandleExportReceivableDetailAsync(AutomationExecutionContext context, string? customerId)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            var topic = GetActiveTopicState(context);
            customerId ??= topic?.TopicType == TopicType.ReceivableDetail ? topic.EntityId : null;
            string? customerName = topic?.TopicType == TopicType.ReceivableDetail ? topic.EntityName : null;
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return await HandleExportReceivablesAsync(context);
            }

            var invoices = await _posDbService.GetCustomerReceivableDetailAsync(customerId);
            if (!invoices.Any())
            {
                return $"Tidak ada detail piutang {FormatOptional(customerName)} untuk diekspor.";
            }

            return await SendCsvDocumentAsync(
                context,
                $"piutang_{MakeSafeFileToken(customerName)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                CsvExportHelper.GenerateReceivableDetailCsv(customerName ?? string.Empty, invoices),
                $"Detail piutang {FormatOptional(customerName)} ({invoices.Count} faktur)",
                $"{IconCheck} File CSV detail piutang telah dikirim. ({invoices.Count} faktur)");
        }

        private async Task<string> HandleCustomerPaginationAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_customerPaginationBySender.TryGetValue(senderKey, out var state))
            {
                return "Tidak ada halaman pelanggan lanjutan. Ketik /pelanggan untuk mulai dari awal.";
            }

            var topic = GetActiveTopicState(context);
            var customers = topic?.TopicType switch
            {
                TopicType.LoyalCustomers => await GetLoyalCustomerSourceAsync(),
                TopicType.AtRiskCustomers => await GetAtRiskCustomerSourceAsync(),
                _ => (await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true))
                    .OrderByDescending(customer => customer.PurchaseCount)
                    .ThenByDescending(customer => customer.TotalSpent)
                    .ThenBy(customer => customer.Name)
                    .ToList()
            };

            var pageItems = customers.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _customerPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada halaman pelanggan berikutnya.";
            }

            int startNumber = state.NextOffset + 1;
            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= customers.Count)
            {
                _customerPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _customerPaginationBySender[senderKey] = state;
            }

            var sb = new StringBuilder();
            string title = topic?.TopicType switch
            {
                TopicType.LoyalCustomers => $"{IconCustomer} PELANGGAN LOYAL - LANJUTAN",
                TopicType.AtRiskCustomers => $"{IconWarning} PELANGGAN PERLU PERHATIAN - LANJUTAN",
                _ => $"{IconCustomer} PELANGGAN - LANJUTAN"
            };
            sb.AppendLine(title);
            sb.AppendLine();
            for (int i = 0; i < pageItems.Count; i++)
            {
                if (topic?.TopicType == TopicType.AtRiskCustomers)
                {
                    int days = GetDaysSince(pageItems[i].LastPurchaseDate);
                    sb.AppendLine($"{startNumber + i}. {FormatOptional(pageItems[i].Name)} | {pageItems[i].PurchaseCount} trx | terakhir {days} hari lalu {GetAtRiskIcon(days)}");
                }
                else
                {
                    sb.AppendLine(BuildCustomerListLine(pageItems[i], startNumber + i));
                }
            }

            if (state.NextOffset < customers.Count)
            {
                sb.AppendLine();
                sb.Append("Ketik LANJUT PELANGGAN untuk halaman berikutnya.");
            }

            SetTopicState(
                context,
                topic?.Topic ?? "pelanggan",
                entityName: topic?.EntityName ?? "daftar pelanggan",
                currentPage: (startNumber - 1) / state.PageSize + 1,
                pageSize: state.PageSize,
                exportType: topic?.ExportType);
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleBestAvailablePaginationAsync(AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (_documentPaginationBySender.ContainsKey(senderKey))
            {
                return await HandleDocumentPaginationAsync(context);
            }

            if (_customerDocumentPaginationBySender.ContainsKey(senderKey))
            {
                return await HandleNextCustomerDocumentsAsync(context);
            }

            if (_customerTxPaginationBySender.ContainsKey(senderKey))
            {
                return await HandleCustomerTransactionPaginationAsync(context);
            }

            if (_customerPaginationBySender.ContainsKey(senderKey))
            {
                return await HandleCustomerPaginationAsync(context);
            }

            if (_supplierPaginationBySender.ContainsKey(senderKey))
            {
                return await HandleSupplierPaginationAsync(context);
            }

            if (_productPaginationBySender.ContainsKey(senderKey))
            {
                return await HandleProductPaginationAsync(context);
            }

            return "Tidak ada data lanjutan yang aktif. Ulangi command atau pertanyaan sebelumnya.";
        }

        private async Task<string> HandleDocumentPaginationAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_documentPaginationBySender.TryGetValue(senderKey, out var state))
            {
                return "Tidak ada halaman dokumen lanjutan. Ketik /dokumen <nomor> untuk mulai lagi.";
            }

            var document = await _posDbService.GetDocumentByNumberAsync(state.DocumentNumber);
            if (document == null || string.IsNullOrWhiteSpace(document.Id))
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
                return $"Dokumen \"{state.DocumentNumber}\" tidak ditemukan.";
            }

            var items = await _posDbService.GetDocumentItemsAsync(document.Id);
            var pageItems = items.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada halaman dokumen berikutnya.";
            }

            int startNumber = state.NextOffset + 1;
            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= items.Count)
            {
                _documentPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _documentPaginationBySender[senderKey] = state;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconDocument} DOKUMEN {document.Number} - LANJUTAN");
            sb.AppendLine();
            AppendAlignedRows(
                sb,
                pageItems.Select((item, index) => (
                    Name: $"{startNumber + index}. {FormatOptional(item.ProductName)}",
                    Col2: $"{FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)}",
                    Col3: $"@ {FormatCurrency(item.Price)}",
                    Col4: $"= {FormatCurrency(item.Total)}")));

            if (state.NextOffset < items.Count)
            {
                sb.AppendLine();
                sb.Append($"Masih ada {items.Count - state.NextOffset} item lagi. Ketik LANJUT DOKUMEN untuk halaman berikutnya.");
            }

            SetTopicState(context, "dokumen", entityId: document.Id, entityName: document.Number ?? state.DocumentNumber, currentPage: (startNumber - 1) / state.PageSize + 1);
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleCustomerTransactionPaginationAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_customerTxPaginationBySender.TryGetValue(senderKey, out var state))
            {
                return "Tidak ada halaman transaksi lanjutan. Ketik cek transaksi <nama pelanggan> untuk mulai lagi.";
            }

            var transactions = await _posDbService.GetCustomerTransactionsAsync(state.CustomerId, 20);
            var pageItems = transactions.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _customerTxPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada halaman transaksi berikutnya.";
            }

            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= transactions.Count)
            {
                _customerTxPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _customerTxPaginationBySender[senderKey] = state;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconClipboard} TRANSAKSI - {FormatOptional(state.CustomerName)}");
            sb.AppendLine();
            foreach (var transaction in pageItems)
            {
                sb.AppendLine($"  {FormatShortDate(transaction.Date)} | {FormatCompactDocumentNumber(transaction.DocumentNumber)} | {FormatOptional(transaction.ProductName)} | {FormatCurrency(transaction.ItemTotal)}");
            }

            if (state.NextOffset < transactions.Count)
            {
                sb.AppendLine();
                sb.Append($"Ketik LANJUT TRANSAKSI untuk {transactions.Count - state.NextOffset} transaksi sebelumnya.");
            }

            SetTopicState(context, "transaksi", entityId: state.CustomerId, entityName: state.CustomerName);
            return sb.ToString().TrimEnd();
        }

        private async Task<string> HandleSupplierPaginationAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            if (!_supplierPaginationBySender.TryGetValue(senderKey, out var state))
            {
                return "Tidak ada halaman supplier lanjutan. Ketik /supplier untuk mulai dari awal.";
            }

            var suppliers = (await _posDbService.GetSuppliersAsync(null, null))
                .OrderBy(supplier => supplier.Name)
                .ToList();

            var pageItems = suppliers.Skip(state.NextOffset).Take(state.PageSize).ToList();
            if (!pageItems.Any())
            {
                _supplierPaginationBySender.TryRemove(senderKey, out _);
                return "Tidak ada halaman supplier berikutnya.";
            }

            int startNumber = state.NextOffset + 1;
            state.NextOffset += pageItems.Count;
            if (state.NextOffset >= suppliers.Count)
            {
                _supplierPaginationBySender.TryRemove(senderKey, out _);
            }
            else
            {
                _supplierPaginationBySender[senderKey] = state;
            }

            var sb = new StringBuilder();
            sb.AppendLine("\U0001F3ED SUPPLIER - LANJUTAN");
            sb.AppendLine();
            for (int i = 0; i < pageItems.Count; i++)
            {
                sb.AppendLine(BuildSupplierListLine(pageItems[i], startNumber + i));
            }

            if (state.NextOffset < suppliers.Count)
            {
                sb.AppendLine();
                sb.Append("Ketik LANJUT SUPPLIER untuk halaman berikutnya.");
            }

            SetTopicState(context, "supplier", entityName: "daftar supplier", currentPage: (startNumber - 1) / state.PageSize + 1);
            return sb.ToString().TrimEnd();
        }

        private string BuildProductPageResponse(
            AutomationExecutionContext context,
            List<Product> products,
            string mode,
            string? query,
            string title,
            string intro)
        {
            string senderKey = BuildSenderStateKey(context);
            int pageSize = Math.Min(10, products.Count);
            var pageItems = products.Take(pageSize).ToList();

            if (products.Count > pageItems.Count)
            {
                _productPaginationBySender[senderKey] = new ProductListPageState
                {
                    Mode = mode,
                    Query = query,
                    NextOffset = pageItems.Count,
                    PageSize = 10
                };
            }
            else
            {
                _productPaginationBySender.TryRemove(senderKey, out _);
            }

            SetPendingExport(context, "stock");
            SetTopicState(context, "produk", entityName: query ?? mode, currentPage: 1);

            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(intro);
            sb.AppendLine();
            for (int i = 0; i < pageItems.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {BuildProductListLine(pageItems[i], includeCategory: true, includeCost: true)}");
            }

            sb.AppendLine();
            sb.AppendLine("Ketik EKSPOR PRODUK untuk file CSV lengkap.");
            if (products.Count > pageItems.Count)
            {
                sb.Append("Ketik LANJUT PRODUK untuk halaman berikutnya.");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildProductListLine(Product product, bool includeCategory, bool includeCost)
        {
            var parts = new List<string>
            {
                FormatOptional(product.Name),
                $"stok {FormatDisplayQuantity(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}",
                $"jual {FormatCurrency(product.SellingPrice ?? 0)}"
            };

            if (includeCost)
            {
                parts.Add($"modal {FormatCurrency(product.PurchasePrice ?? 0)}");
            }

            if (includeCategory && !string.IsNullOrWhiteSpace(product.Category))
            {
                parts.Add($"kategori {product.Category}");
            }

            return string.Join(" | ", parts);
        }

        private static string BuildProductRankingResponse(string title, List<ProductSalesData> items, bool rankByProfit)
        {
            if (!items.Any())
            {
                return "Belum ada data produk untuk periode ini.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconChart} {title}");
            sb.AppendLine();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string metric = rankByProfit
                    ? $"profit {FormatCurrency(item.Profit)}"
                    : $"{FormatDisplayQuantity(item.QuantitySold)} {GetUnitLabel(item.Unit)}";
                sb.AppendLine($"{i + 1}. {FormatOptional(item.ProductName)} | {metric} | omzet {FormatCurrency(item.Revenue)}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> BuildAnomalyInsightAsync()
        {
            if (_posDbService == null)
            {
                return string.Empty;
            }

            DateTime today = DateTime.Today;
            DateTime start = today.AddDays(-7);
            var dailySales = await _posDbService.GetDailySalesAsync(start, today);
            if (!dailySales.Any())
            {
                return string.Empty;
            }

            decimal todayRevenue = dailySales.FirstOrDefault(item => item.Date.Date == today)?.Revenue ?? 0;
            var previousDays = dailySales.Where(item => item.Date.Date < today).ToList();
            if (!previousDays.Any())
            {
                return string.Empty;
            }

            decimal averagePreviousRevenue = previousDays.Average(item => item.Revenue);
            if (averagePreviousRevenue <= 0)
            {
                return string.Empty;
            }

            if (todayRevenue >= averagePreviousRevenue * 3)
            {
                return $"{IconWarning} Anomali positif: omzet hari ini {FormatCurrency(todayRevenue)} atau {todayRevenue / averagePreviousRevenue:0.##}x rata-rata 7 hari sebelumnya.";
            }

            if (todayRevenue > 0 && todayRevenue <= averagePreviousRevenue * 0.35m)
            {
                return $"{IconWarning} Anomali negatif: omzet hari ini {FormatCurrency(todayRevenue)} jauh di bawah rata-rata 7 hari sebelumnya ({FormatCurrency(averagePreviousRevenue)}).";
            }

            return string.Empty;
        }

        private async Task<string> HandleExportBundleAsync(AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (_documentSender == null)
            {
                return "Transport pengiriman file belum siap.";
            }

            var customers = (await _posDbService.GetCustomersAsync(null, null, onlyCustomers: true)).ToList();
            var suppliers = (await _posDbService.GetSuppliersAsync(null, null)).ToList();
            var products = (await _posDbService.GetAllProductsAsync()).OrderBy(product => product.Name).ToList();
            var receivables = await _posDbService.GetCustomerReceivablesAsync();
            DateTime monthStart = GetMonthStart(DateTime.Today);
            var monthSales = await _posDbService.GetSalesLineItemsAsync(monthStart, DateTime.Today);
            var criticalStock = await _posDbService.GetCriticalStockProductsAsync();

            string tempDir = Path.Combine(Path.GetTempPath(), $"ssa_export_{Guid.NewGuid():N}");
            string zipPath = Path.Combine(Path.GetTempPath(), $"ssa_bundle_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "produk.csv"), CsvExportHelper.GenerateStockCsv(products), new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(tempDir, "pelanggan.csv"), CsvExportHelper.GenerateCustomerCsv(customers), new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(tempDir, "supplier.csv"), CsvExportHelper.GenerateSupplierCsv(suppliers), new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(tempDir, "piutang.csv"), CsvExportHelper.GenerateReceivableCsv(receivables), new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(tempDir, "transaksi_bulan_ini.csv"), CsvExportHelper.GenerateSalesCsv(monthSales, "Bulan Ini"), new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(tempDir, "stok_kritis.csv"), CsvExportHelper.GenerateStockCsv(criticalStock), new UTF8Encoding(false));

                ZipFile.CreateFromDirectory(tempDir, zipPath);
                string caption = $"Bundle export lengkap Smart Sembako Assistant ({DateTime.Now:dd/MM/yyyy HH:mm})";
                await SendDocumentForChannelAsync(context, zipPath, caption, "export_bundle");
                ClearPendingExport(context);
                return $"{IconCheck} File ZIP export lengkap telah dikirim.";
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Gagal membuat bundle export: {ex.Message}", "Export", ex.ToString(), context.Identity.SenderId);
                return $"Gagal membuat export lengkap: {ex.Message}";
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                }

                try
                {
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }
                }
                catch
                {
                }
            }
        }

        private async Task<string> HandleMediaMessageAsync(InboundMessage message, AutomationExecutionContext context)
        {
            if (message.Channel != ChannelType.Telegram && message.Channel != ChannelType.Baileys)
            {
                return "Media diterima, tetapi OCR saat ini baru didukung untuk Telegram dan WhatsApp lokal.";
            }

            var ocrSettings = _configService.Config?.OcrReceipt;
            if (ocrSettings?.Enabled != true)
            {
                return "Foto diterima, tetapi OCR struk belum diaktifkan. Aktifkan `OcrReceipt.Enabled` lalu kirim foto dengan caption /struk.";
            }

            string caption = message.Text?.Trim() ?? string.Empty;
            string trigger = ocrSettings.TriggerCaption?.Trim() ?? "/struk";
            bool hasTriggerCaption = string.Equals(caption, trigger, StringComparison.OrdinalIgnoreCase) ||
                                     caption.StartsWith(trigger + " ", StringComparison.OrdinalIgnoreCase);
            OcrSession? activeSession = await _databaseService.GetActiveOcrSessionAsync(message.SenderId, message.Channel.ToString());

            if (activeSession != null)
            {
                return await HandleReceiptOcrSessionPageAsync(message, context, ocrSettings, activeSession);
            }

            if (!hasTriggerCaption)
            {
                return $"Kirim foto struk sebagai foto dengan caption {trigger} untuk memulai OCR pembelian.";
            }

            return await HandleReceiptOcrAsync(message, context, ocrSettings, hasTriggerCaption);
        }

        private async Task<string> HandleTextReceiptAsync(
            InboundMessage message,
            AutomationExecutionContext context,
            OcrReceiptSettings? settings,
            string rawText)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            if (settings?.Enabled != true)
            {
                return "OCR struk belum diaktifkan. Aktifkan `OcrReceipt.Enabled` di Settings.";
            }

            string receiptText = (rawText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(receiptText))
            {
                string trigger = settings.TextTriggerCaption?.Trim() ?? "/inputstruk";
                return $"Format: {trigger} <teks struk>\nContoh: kirim `{trigger}` lalu tempel isi faktur/struk setelahnya.";
            }

            try
            {
                ParsedReceipt? parsed = await ParseReceiptAsync(receiptText);
                if (parsed?.Items == null || !parsed.Items.Any())
                {
                    return "Teks struk diterima, tetapi item belum bisa diparse. Cek format teks atau tambahkan mapping OCR.";
                }

                return await FinalizeParsedReceiptAsync(message, settings, parsed);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"OCR text receipt gagal: {ex.Message}", "OCR", ex.ToString(), context.Identity.SenderId);
                return $"OCR teks gagal diproses: {ex.Message}";
            }
        }

        private async Task<string> HandleReceiptOcrAsync(InboundMessage message, AutomationExecutionContext context, OcrReceiptSettings settings, bool startSession)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string imagePath = message.MediaUrl ?? string.Empty;
            if (!File.Exists(imagePath))
            {
                return "File gambar OCR tidak ditemukan.";
            }

            try
            {
                OcrExtractionResult extraction = await ExtractReceiptTextAsync(imagePath, settings);
                string rawText = extraction.Text;
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    return "Teks struk tidak terbaca. Coba foto yang lebih terang dan tegak lurus.";
                }

                bool triedVision = false;
                ParsedReceipt? parsed = null;
                if (_groqService.HasGeminiFallbackConfigured)
                {
                    triedVision = true;
                    parsed = await TryParseReceiptVisionFallbackAsync(imagePath, rawText);
                }

                parsed ??= await ParseReceiptAsync(rawText);
                if (!triedVision && ShouldTryVisionFallback(extraction, parsed))
                {
                    ParsedReceipt? visionParsed = await TryParseReceiptVisionFallbackAsync(imagePath, rawText);
                    if (visionParsed?.Items?.Any() == true)
                    {
                        parsed = visionParsed;
                    }
                }

                bool isWings = string.Equals(parsed?.VendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(DetectReceiptVendor(rawText), "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase);
                bool isEndOfDoc = rawText.Contains("END OF DOCUMENT", StringComparison.OrdinalIgnoreCase);

                if (parsed == null || parsed.Items == null || !parsed.Items.Any())
                {
                    // Halaman Wings tanpa item: lanjutan header/summary atau END OF DOCUMENT
                    if (isWings && isEndOfDoc)
                    {
                        return "\u2705 Halaman penutup Surat Jalan Wings diterima (END OF DOCUMENT). Tidak ada item di halaman ini. Kirim halaman berikutnya jika ada.";
                    }

                    if (isWings)
                    {
                        return "\U0001F4CB Halaman lanjutan Surat Jalan Wings terdeteksi tetapi tidak mengandung item baru. " +
                               "Kirim halaman berikutnya dengan caption /struk untuk memulai sesi multi-halaman.";
                    }

                    return "Struk terbaca, tetapi item belum bisa dipetakan. Coba foto ulang atau lengkapi mapping OCR.";
                }

                // Auto-start session untuk Wings multi-halaman (tidak wajib pakai /struk caption)
                bool shouldStartSession = (startSession || isWings) &&
                    string.Equals(parsed.VendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase) &&
                    !parsed.IsLastPage;

                if (shouldStartSession)
                {
                    var session = new OcrSession
                    {
                        SenderId = message.SenderId,
                        Channel = message.Channel.ToString(),
                        SupplierName = parsed.SupplierName ?? parsed.StoreName,
                        ReceiptNumber = parsed.ReceiptNumber,
                        ReceiptDate = parsed.Date?.ToString("yyyy-MM-dd"),
                        ItemsJson = JsonSerializer.Serialize(parsed.Items),
                        PageCount = 1,
                        IsComplete = false,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddMinutes(30)
                    };

                    await _databaseService.CreateOcrSessionAsync(session);
                    return BuildOcrSessionProgressMessage(session.PageCount, parsed.Items.Count, parsed.Items.Count);
                }

                return await FinalizeParsedReceiptAsync(message, settings, parsed);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"OCR receipt gagal: {ex.Message}", "OCR", ex.ToString(), context.Identity.SenderId);
                return $"OCR gagal diproses: {ex.Message}";
            }
            finally
            {
                TryDeleteTempFile(imagePath);
            }
        }

        private async Task<string> HandleReceiptOcrSessionPageAsync(InboundMessage message, AutomationExecutionContext context, OcrReceiptSettings settings, OcrSession activeSession)
        {
            if (activeSession.ExpiresAt < DateTime.Now)
            {
                await _databaseService.CompleteOcrSessionAsync(activeSession.Id);
                return "Session OCR sudah expired (30 menit). Mulai ulang dari halaman pertama dengan caption /struk.";
            }

            string imagePath = message.MediaUrl ?? string.Empty;
            if (!File.Exists(imagePath))
            {
                return "File gambar OCR tidak ditemukan.";
            }

            try
            {
                OcrExtractionResult extraction = await ExtractReceiptTextAsync(imagePath, settings);
                string rawText = extraction.Text;
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    return "Teks struk tidak terbaca. Coba foto yang lebih terang dan tegak lurus.";
                }

                bool triedVision = false;
                ParsedReceipt? parsed = null;
                if (_groqService.HasGeminiFallbackConfigured)
                {
                    triedVision = true;
                    parsed = await TryParseReceiptVisionFallbackAsync(imagePath, rawText);
                }

                parsed ??= await ParseReceiptAsync(rawText);
                if (!triedVision && ShouldTryVisionFallback(extraction, parsed))
                {
                    ParsedReceipt? visionParsed = await TryParseReceiptVisionFallbackAsync(imagePath, rawText);
                    if (visionParsed?.Items?.Any() == true)
                    {
                        parsed = visionParsed;
                    }
                }

                bool isEndOfDoc = rawText.Contains("END OF DOCUMENT", StringComparison.OrdinalIgnoreCase);

                if (parsed?.Items == null || !parsed.Items.Any())
                {
                    // Halaman penutup Wings (END OF DOCUMENT tanpa item) → finalize session
                    if (isEndOfDoc)
                    {
                        int finalPageCount = activeSession.PageCount + 1;
                        await _databaseService.AppendOcrSessionItemsAsync(
                            activeSession.Id,
                            new List<ReceiptItem>(),
                            finalPageCount,
                            isComplete: true,
                            parsed?.SupplierName ?? parsed?.StoreName,
                            parsed?.ReceiptNumber,
                            parsed?.Date?.ToString("yyyy-MM-dd"));

                        var mergedClosingItems = DeserializeReceiptItems(activeSession.ItemsJson);
                        if (!mergedClosingItems.Any())
                        {
                            await _databaseService.CompleteOcrSessionAsync(activeSession.Id);
                            return "Session OCR selesai, tetapi item gabungan kosong. Coba ulang dari halaman pertama.";
                        }

                        var closingSession = new OcrSession
                        {
                            Id = activeSession.Id,
                            SenderId = activeSession.SenderId,
                            Channel = activeSession.Channel,
                            SupplierName = activeSession.SupplierName,
                            ReceiptNumber = activeSession.ReceiptNumber,
                            ReceiptDate = activeSession.ReceiptDate,
                            ItemsJson = activeSession.ItemsJson,
                            PageCount = finalPageCount,
                            IsComplete = true,
                            CreatedAt = activeSession.CreatedAt,
                            ExpiresAt = DateTime.Now
                        };

                        await _databaseService.CompleteOcrSessionAsync(closingSession.Id);
                        return await FinalizeOcrSessionAsync(message, settings, closingSession, parsed);
                    }

                    // Halaman lanjutan tanpa item (header/summary saja) → skip, session tetap aktif
                    int existingItems = DeserializeReceiptItems(activeSession.ItemsJson).Count;
                    return $"\U0001F4CB Halaman {activeSession.PageCount + 1} tidak mengandung item baru (mungkin halaman header/summary). " +
                           $"Session masih aktif dengan {existingItems} item dari {activeSession.PageCount} halaman. Kirim halaman berikutnya.";
                }

                int newPageCount = activeSession.PageCount + 1;
                await _databaseService.AppendOcrSessionItemsAsync(
                    activeSession.Id,
                    parsed.Items,
                    newPageCount,
                    parsed.IsLastPage,
                    parsed.SupplierName ?? parsed.StoreName,
                    parsed.ReceiptNumber,
                    parsed.Date?.ToString("yyyy-MM-dd"));

                if (!parsed.IsLastPage)
                {
                    int totalItems = DeserializeReceiptItems(activeSession.ItemsJson).Count + parsed.Items.Count;
                    return BuildOcrSessionProgressMessage(newPageCount, parsed.Items.Count, totalItems);
                }

                var completedItems = DeserializeReceiptItems(activeSession.ItemsJson);
                completedItems.AddRange(parsed.Items);
                var completedSession = new OcrSession
                {
                    Id = activeSession.Id,
                    SenderId = activeSession.SenderId,
                    Channel = activeSession.Channel,
                    SupplierName = parsed.SupplierName ?? parsed.StoreName ?? activeSession.SupplierName,
                    ReceiptNumber = parsed.ReceiptNumber ?? activeSession.ReceiptNumber,
                    ReceiptDate = parsed.Date?.ToString("yyyy-MM-dd") ?? activeSession.ReceiptDate,
                    ItemsJson = JsonSerializer.Serialize(completedItems),
                    PageCount = newPageCount,
                    IsComplete = true,
                    CreatedAt = activeSession.CreatedAt,
                    ExpiresAt = DateTime.Now
                };

                await _databaseService.CompleteOcrSessionAsync(completedSession.Id);
                return await FinalizeOcrSessionAsync(message, settings, completedSession, parsed);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"OCR session gagal: {ex.Message}", "OCR", ex.ToString(), context.Identity.SenderId);
                return $"OCR session gagal: {ex.Message}";
            }
            finally
            {
                TryDeleteTempFile(imagePath);
            }
        }

        private async Task<string> HandleFinishOcrSessionAsync(InboundMessage message, AutomationExecutionContext context)
        {
            var activeSession = await _databaseService.GetActiveOcrSessionAsync(message.SenderId, message.Channel.ToString());
            if (activeSession == null)
            {
                return "Tidak ada session OCR aktif.";
            }

            var ocrSettings = _configService.Config?.OcrReceipt;
            if (ocrSettings?.Enabled != true)
            {
                return "OCR struk belum diaktifkan.";
            }

            await _databaseService.CompleteOcrSessionAsync(activeSession.Id);
            return await FinalizeOcrSessionAsync(message, ocrSettings, activeSession, null);
        }

        private async Task<string> FinalizeOcrSessionAsync(InboundMessage message, OcrReceiptSettings settings, OcrSession session, ParsedReceipt? latestPage)
        {
            var mergedItems = DeserializeReceiptItems(session.ItemsJson);
            if (!mergedItems.Any())
            {
                return "Session OCR selesai, tetapi item gabungan kosong.";
            }

            var receipt = new ParsedReceipt
            {
                StoreName = latestPage?.StoreName ?? session.SupplierName,
                SupplierName = latestPage?.SupplierName ?? session.SupplierName,
                BuyerName = latestPage?.BuyerName,
                VendorType = latestPage?.VendorType ?? "WINGS_SURAT_JALAN",
                Date = !string.IsNullOrWhiteSpace(session.ReceiptDate) ? ParseFlexibleDate(session.ReceiptDate) : latestPage?.Date,
                ReceiptNumber = latestPage?.ReceiptNumber ?? session.ReceiptNumber,
                IsLastPage = true,
                Items = mergedItems,
                Total = latestPage?.Total
            };

            return await FinalizeParsedReceiptAsync(message, settings, receipt);
        }

        private async Task<string> FinalizeParsedReceiptAsync(InboundMessage message, OcrReceiptSettings settings, ParsedReceipt parsed)
        {
            var mappingOutcome = await MapReceiptItemsToBulkPendingItemsAsync(parsed.Items ?? new List<ReceiptItem>(), settings, message, parsed);
            if (!mappingOutcome.ValidItems.Any() && !mappingOutcome.ReviewItems.Any())
            {
                return "Item struk terbaca, tetapi belum ada produk yang cocok di database. Tambahkan mapping OCR atau gunakan /restock manual.";
            }

            if (!mappingOutcome.ValidItems.Any() && mappingOutcome.ReviewItems.Any())
            {
                await _databaseService.AddOcrReviewQueueItemsAsync(mappingOutcome.ReviewItems);
                return BuildOcrQueuedForReviewMessage(parsed, mappingOutcome.ReviewItems);
            }

            string key = GetConfirmationKey(message.Channel, message.SenderId);
            await _databaseService.SavePendingConfirmationAsync(new PendingConfirmation
            {
                Key = key,
                Command = "ocr_bulk_restock",
                ProductId = SerializeOcrBulkPayload(new OcrBulkPendingPayload
                {
                    StoreName = parsed.SupplierName ?? parsed.StoreName,
                    SupplierName = parsed.SupplierName ?? parsed.StoreName,
                    BuyerName = parsed.BuyerName,
                    ReceiptDate = parsed.Date,
                    ReceiptNumber = parsed.ReceiptNumber,
                    ReceiptTotal = parsed.Total,
                    Items = mappingOutcome.ValidItems,
                    ReviewItems = mappingOutcome.ReviewItems
                }),
                ProductName = $"OCR restock {mappingOutcome.ValidItems.Count} item",
                Quantity = mappingOutcome.ValidItems.Count,
                CorrelationId = message.CorrelationId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });

            return BuildOcrPreviewMessage(parsed, mappingOutcome);
        }

        private static bool TryExtractTextReceiptPayload(string text, OcrReceiptSettings? settings, out string payload)
        {
            payload = string.Empty;
            if (string.IsNullOrWhiteSpace(text) || settings?.Enabled != true)
            {
                return false;
            }

            string trigger = settings.TextTriggerCaption?.Trim() ?? "/inputstruk";
            if (string.IsNullOrWhiteSpace(trigger))
            {
                trigger = "/inputstruk";
            }

            string trimmed = text.Trim();
            if (string.Equals(trimmed, trigger, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!trimmed.StartsWith(trigger + " ", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith(trigger + "\n", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith(trigger + "\r\n", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            payload = trimmed[trigger.Length..].Trim();
            return true;
        }

        private static bool LooksLikeReceiptText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 80)
            {
                return false;
            }

            string normalized = NormalizeText(text);
            int lineCount = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            bool hasReceiptKeyword = ContainsAny(
                normalized,
                "faktur",
                "surat jalan",
                "tanggal",
                "supplier",
                "pelanggan",
                "total",
                "subtotal",
                "jmlh bersih",
                "jumlah",
                "qty",
                "harga");
            bool hasMoneyLikeValue = Regex.IsMatch(text, @"\b\d{1,3}(?:[.,]\d{3}){1,}\b");
            bool hasItemLikeLine = Regex.IsMatch(text, @"(?m)^[^\r\n]{3,}\s+\d+(?:[.,]\d+)?\s*(?:pcs|box|dus|bks|pak|rtg|rcg)?\s+\d", RegexOptions.IgnoreCase);

            return lineCount >= 4 && hasReceiptKeyword && hasMoneyLikeValue && hasItemLikeLine;
        }

        private async Task<OcrExtractionResult> ExtractReceiptTextAsync(string imagePath, OcrReceiptSettings settings)
        {
            string tessdataPath = ResolveTessdataPath(settings.TessdataPath);
            if (!Directory.Exists(tessdataPath))
            {
                throw new DirectoryNotFoundException($"Folder tessdata tidak ditemukan: {tessdataPath}");
            }

            return await Task.Run(() =>
            {
                var preprocessedPaths = new List<string>();
                using var engine = new TesseractEngine(tessdataPath, "ind+eng", EngineMode.Default);
                var candidates = new List<OcrTextCandidate>
                {
                    ReadOcrCandidate(engine, imagePath, isPreprocessed: false)
                };

                try
                {
                    preprocessedPaths = CreatePreprocessedOcrImages(imagePath);
                    foreach (string preprocessedPath in preprocessedPaths)
                    {
                        if (File.Exists(preprocessedPath))
                        {
                            candidates.Add(ReadOcrCandidate(engine, preprocessedPath, isPreprocessed: true));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarningAsync(
                        $"OCR preprocessing dilewati: {ex.Message}",
                        "OCR",
                        ex.ToString()).GetAwaiter().GetResult();
                }
                finally
                {
                    foreach (string preprocessedPath in preprocessedPaths)
                    {
                        TryDeleteTempFile(preprocessedPath);
                    }
                }

                OcrTextCandidate best = candidates
                    .OrderByDescending(candidate => candidate.Score)
                    .FirstOrDefault() ?? new OcrTextCandidate();

                _loggingService.LogInfoAsync(
                    $"[OCR] Extract source={(best.IsPreprocessed ? "preprocessed" : "original")}, confidence={best.Confidence:0.00}, chars={best.Text.Length}",
                    "OCR").GetAwaiter().GetResult();

                return new OcrExtractionResult
                {
                    Text = best.Text.Trim(),
                    Confidence = best.Confidence,
                    UsedPreprocessedImage = best.IsPreprocessed
                };
            });
        }

        private static string ResolveTessdataPath(string? configuredPath)
        {
            string value = string.IsNullOrWhiteSpace(configuredPath) ? "tessdata" : configuredPath.Trim();
            if (Path.IsPathRooted(value))
            {
                return Path.GetFullPath(value);
            }

            string localAppDataCandidate = Path.Combine(RuntimePaths.WritableRootDirectory, value);
            if (Directory.Exists(localAppDataCandidate))
            {
                return Path.GetFullPath(localAppDataCandidate);
            }

            string installCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            if (Directory.Exists(installCandidate))
            {
                return Path.GetFullPath(installCandidate);
            }

            string currentCandidate = Path.Combine(Environment.CurrentDirectory, value);
            if (Directory.Exists(currentCandidate))
            {
                return Path.GetFullPath(currentCandidate);
            }

            return Path.GetFullPath(installCandidate);
        }

        private static OcrTextCandidate ReadOcrCandidate(TesseractEngine engine, string imagePath, bool isPreprocessed)
        {
            using var image = Pix.LoadFromFile(imagePath);
            using var page = engine.Process(image);
            string text = page.GetText()?.Trim() ?? string.Empty;
            float confidence = NormalizeOcrConfidence(page.GetMeanConfidence());

            int lineCount = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            double score = (confidence * 100d) +
                           Math.Min(text.Length, 2500) / 180d +
                           Math.Min(lineCount, 40) * 0.4d -
                           ComputeOcrNoiseRatio(text) * 30d;

            return new OcrTextCandidate
            {
                Text = text,
                Confidence = confidence,
                IsPreprocessed = isPreprocessed,
                Score = score
            };
        }

        private static float NormalizeOcrConfidence(float rawConfidence)
        {
            if (rawConfidence > 1f)
            {
                rawConfidence /= 100f;
            }

            return Math.Clamp(rawConfidence, 0f, 1f);
        }

        private static List<string> CreatePreprocessedOcrImages(string imagePath)
        {
            using SKBitmap? original = SKBitmap.Decode(imagePath);
            if (original == null || original.Width <= 0 || original.Height <= 0)
            {
                return new List<string>();
            }

            int targetWidth = original.Width < 1400 ? Math.Min(original.Width * 2, 2400) : original.Width;
            int targetHeight = (int)Math.Round(original.Height * (targetWidth / (double)original.Width));

            using var resized = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(resized))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(original, new SKRect(0, 0, targetWidth, targetHeight), paint);
            }

            int otsuThreshold = ComputeOtsuThreshold(resized);
            return new[] { 128, 160, otsuThreshold }
                .Distinct()
                .Select(threshold => SaveThresholdedOcrImage(resized, threshold))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
        }

        private static string SaveThresholdedOcrImage(SKBitmap source, int threshold)
        {
            using var processed = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    SKColor color = source.GetPixel(x, y);
                    byte gray = (byte)((color.Red * 0.299) + (color.Green * 0.587) + (color.Blue * 0.114));
                    byte contrasted = gray < threshold ? (byte)0 : (byte)255;
                    processed.SetPixel(x, y, new SKColor(contrasted, contrasted, contrasted));
                }
            }

            string outputPath = Path.Combine(Path.GetTempPath(), $"ssa_ocr_{threshold}_{Guid.NewGuid():N}.png");
            using SKImage image = SKImage.FromBitmap(processed);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);
            return outputPath;
        }

        private static int ComputeOtsuThreshold(SKBitmap bitmap)
        {
            var histogram = new int[256];
            int total = bitmap.Width * bitmap.Height;
            if (total <= 0)
            {
                return 160;
            }

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    SKColor color = bitmap.GetPixel(x, y);
                    int gray = (int)((color.Red * 0.299) + (color.Green * 0.587) + (color.Blue * 0.114));
                    histogram[Math.Clamp(gray, 0, 255)]++;
                }
            }

            double sum = 0;
            for (int i = 0; i < 256; i++)
            {
                sum += i * histogram[i];
            }

            double sumBackground = 0;
            int weightBackground = 0;
            double maxVariance = 0;
            int threshold = 160;

            for (int i = 0; i < 256; i++)
            {
                weightBackground += histogram[i];
                if (weightBackground == 0)
                {
                    continue;
                }

                int weightForeground = total - weightBackground;
                if (weightForeground == 0)
                {
                    break;
                }

                sumBackground += i * histogram[i];
                double meanBackground = sumBackground / weightBackground;
                double meanForeground = (sum - sumBackground) / weightForeground;
                double varianceBetween = weightBackground * weightForeground * Math.Pow(meanBackground - meanForeground, 2);

                if (varianceBetween > maxVariance)
                {
                    maxVariance = varianceBetween;
                    threshold = i;
                }
            }

            return Math.Clamp(threshold, 96, 210);
        }

        private static bool ShouldTryVisionFallback(OcrExtractionResult extraction, ParsedReceipt? parsed)
        {
            if (parsed?.Items == null || !parsed.Items.Any())
            {
                return true;
            }

            return extraction.Confidence < OcrVisionFallbackConfidenceThreshold ||
                   ComputeOcrNoiseRatio(extraction.Text) >= 0.16d;
        }

        private static double ComputeOcrNoiseRatio(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 1d;
            }

            int meaningful = 0;
            int noisy = 0;
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                if (char.IsLetterOrDigit(ch) || ".,:/\\-+()[]%*&".Contains(ch))
                {
                    meaningful++;
                }
                else
                {
                    noisy++;
                }
            }

            int total = meaningful + noisy;
            return total == 0 ? 1d : noisy / (double)total;
        }

        private async Task<ParsedReceipt?> ParseReceiptAsync(string rawText)
        {
            string vendorType = DetectReceiptVendor(rawText);
            string sanitized;
            try
            {
                sanitized = (await _groqService.ParseReceiptAsync(rawText, vendorType)).Trim();
                if (sanitized.StartsWith("```", StringComparison.Ordinal))
                {
                    sanitized = Regex.Replace(sanitized, "^```(?:json)?|```$", string.Empty, RegexOptions.Multiline).Trim();
                }

                string snippet = sanitized.Length > 200
                    ? sanitized[..200]
                    : sanitized;
                await _loggingService.LogInfoAsync(
                    $"[OCR Debug] VendorType={vendorType}, RawJSON snippet={snippet}",
                    "OCR");
            }
            catch
            {
                return ParseReceiptFallback(rawText, vendorType);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(sanitized);
                ParsedReceipt receipt = ParseReceiptDocument(document.RootElement, vendorType);
                if ((receipt.Items == null || !receipt.Items.Any()) &&
                    string.Equals(vendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseWingsReceiptFallback(rawText);
                }

                return receipt;
            }
            catch
            {
                return ParseReceiptFallback(rawText, vendorType);
            }
        }

        private async Task<ParsedReceipt?> TryParseReceiptVisionFallbackAsync(string imagePath, string rawText)
        {
            if (!_groqService.HasGeminiFallbackConfigured)
            {
                return null;
            }

            string vendorType = DetectReceiptVendor(rawText);
            try
            {
                string sanitized = (await _groqService.ParseReceiptVisionAsync(imagePath, vendorType)).Trim();
                if (sanitized.StartsWith("```", StringComparison.Ordinal))
                {
                    sanitized = Regex.Replace(sanitized, "^```(?:json)?|```$", string.Empty, RegexOptions.Multiline).Trim();
                }

                using JsonDocument document = JsonDocument.Parse(sanitized);
                string resolvedVendorType = ResolveReceiptVendorFromParsedJson(vendorType, sanitized);
                ParsedReceipt receipt = ParseReceiptDocument(document.RootElement, resolvedVendorType);
                return receipt.Items?.Any() == true ? receipt : null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync(
                    $"OCR vision fallback gagal: {ex.Message}",
                    "OCR",
                    ex.ToString());
                return null;
            }
        }

        private static ParsedReceipt ParseReceiptDocument(JsonElement root, string vendorType)
        {
            string? supplierName = root.TryGetProperty("supplier_name", out var supplierProp)
                ? supplierProp.GetString()
                : root.TryGetProperty("store_name", out var legacyStoreProp)
                    ? legacyStoreProp.GetString()
                    : null;
            string? resolvedSupplierName = ResolveSupplierName(supplierName, vendorType);

            var receipt = new ParsedReceipt
            {
                StoreName = resolvedSupplierName,
                SupplierName = resolvedSupplierName,
                BuyerName = root.TryGetProperty("buyer_name", out var buyerProp) ? buyerProp.GetString() : null,
                VendorType = vendorType,
                ReceiptNumber = root.TryGetProperty("receipt_number", out var numberProp) ? numberProp.GetString() : null,
                IsLastPage = root.TryGetProperty("is_last_page", out var lastPageProp) &&
                             (lastPageProp.ValueKind == JsonValueKind.True ||
                              (lastPageProp.ValueKind == JsonValueKind.String &&
                               bool.TryParse(lastPageProp.GetString(), out var lastPageValue) && lastPageValue)),
                Total = root.TryGetProperty("total", out var totalProp) ? ReadJsonDecimal(totalProp) : null,
                Items = new List<ReceiptItem>()
            };

            if (root.TryGetProperty("date", out var dateProp))
            {
                receipt.RawDateText = dateProp.GetString();
                receipt.Date = ParseFlexibleDate(receipt.RawDateText);
            }

            if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsProp.EnumerateArray())
                {
                    string? rawProductName = item.TryGetProperty("product_name", out var productProp) ? productProp.GetString() : null;
                    decimal? qtyBox = item.TryGetProperty("qty_box", out var qtyBoxProp) ? ReadJsonDecimal(qtyBoxProp) : null;
                    int? isiPerBox = item.TryGetProperty("isi_per_box", out var isiPerBoxProp) ? ReadJsonInt32(isiPerBoxProp) : null;
                    string? rawQuantity = item.TryGetProperty("quantity", out var qtyProp) ? qtyProp.ToString() : null;
                    decimal? quantity = item.TryGetProperty("quantity", out var parsedQtyProp) ? ReadJsonDecimal(parsedQtyProp) : null;
                    string? unit = item.TryGetProperty("unit", out var unitProp) ? unitProp.GetString() : null;
                    decimal? unitPrice = item.TryGetProperty("unit_price", out var priceProp) ? ReadJsonDecimal(priceProp) : null;
                    decimal? lineTotal = item.TryGetProperty("total", out var lineProp) ? ReadJsonDecimal(lineProp) : null;
                    (quantity, unit) = NormalizeReceiptQuantityAndUnit(quantity, unit, rawQuantity);
                    (quantity, unit, unitPrice, lineTotal, qtyBox, isiPerBox) = ApplyReceiptPostParseGuards(
                        vendorType,
                        quantity,
                        unit,
                        unitPrice,
                        lineTotal,
                        qtyBox,
                        isiPerBox);

                    receipt.Items.Add(new ReceiptItem
                    {
                        ProductName = CleanProductName(rawProductName, vendorType),
                        QtyBox = qtyBox,
                        IsiPerBox = isiPerBox,
                        Quantity = quantity,
                        Unit = NormalizeReceiptUnit(unit),
                        UnitPrice = unitPrice,
                        Total = lineTotal
                    });
                }
            }

            return receipt;
        }

        private static string ResolveReceiptVendorFromParsedJson(string detectedVendorType, string sanitizedJson)
        {
            if (ContainsAny(sanitizedJson, "FASTRATA", "PT. FASTRATA BUANA", "Jumlah Setelah Potongan", "Harga Incl"))
            {
                return "FASTRATA_FAKTUR";
            }

            if (ContainsAny(sanitizedJson, "ARTABOGA", "ARINDO MAKMUR", "PT. ARTABOGA CEMERLANG", "JML NETTO"))
            {
                return "ARTABOGA_FAKTUR";
            }

            if ((string.Equals(detectedVendorType, "GENERIC", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(detectedVendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase)) &&
                ContainsAny(sanitizedJson, "WINGS", "SAYAP MAS", "SAYAP ANAS", "SMU"))
            {
                return "WINGS_SURAT_JALAN";
            }

            return detectedVendorType;
        }

        private static (decimal? Quantity, string? Unit, decimal? UnitPrice, decimal? Total, decimal? QtyBox, int? IsiPerBox)
            ApplyReceiptPostParseGuards(
                string vendorType,
                decimal? quantity,
                string? unit,
                decimal? unitPrice,
                decimal? total,
                decimal? qtyBox,
                int? isiPerBox)
        {
            bool hasQuantity = quantity.GetValueOrDefault() > 0;
            bool hasTotal = total.GetValueOrDefault() > 0;
            string normalizedVendor = vendorType.Trim().ToUpperInvariant();

            if (normalizedVendor == "FASTRATA_FAKTUR")
            {
                // FASTRATA has no explicit isi-per-box column; values here are usually prices misread as packaging.
                isiPerBox = null;
                if (qtyBox.GetValueOrDefault() <= 0 && hasQuantity)
                {
                    qtyBox = quantity;
                }
            }

            if (normalizedVendor == "ARTABOGA_FAKTUR" &&
                string.IsNullOrWhiteSpace(unit) &&
                hasQuantity)
            {
                unit = "Pak";
            }

            if ((normalizedVendor == "FASTRATA_FAKTUR" || normalizedVendor == "ARTABOGA_FAKTUR") &&
                hasTotal &&
                hasQuantity)
            {
                unitPrice = decimal.Round(total!.Value / quantity!.Value, 4);
            }
            else if (unitPrice.GetValueOrDefault() <= 0 && hasTotal && hasQuantity)
            {
                unitPrice = decimal.Round(total!.Value / quantity!.Value, 4);
            }
            else if (!hasTotal && unitPrice.GetValueOrDefault() > 0 && hasQuantity)
            {
                total = decimal.Round(unitPrice!.Value * quantity!.Value, 4);
            }

            return (quantity, unit, unitPrice, total, qtyBox, isiPerBox);
        }

        private static decimal? ReadJsonDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numericValue))
            {
                return numericValue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                decimal parsed = ParseLooseDecimal(element.GetString() ?? string.Empty);
                return parsed == 0 ? null : parsed;
            }

            return null;
        }

        private static int? ReadJsonInt32(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numericValue))
            {
                return numericValue;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue))
            {
                return stringValue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                decimal looseValue = ParseLooseDecimal(element.GetString() ?? string.Empty);
                if (looseValue > 0 && looseValue <= int.MaxValue)
                {
                    return (int)Math.Round(looseValue);
                }
            }

            return null;
        }

        private static ParsedReceipt ParseReceiptFallback(string rawText, string vendorType)
        {
            if (string.Equals(vendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase))
            {
                return ParseWingsReceiptFallback(rawText);
            }

            if (string.Equals(vendorType, "TANI_MAKMUR_POS", StringComparison.OrdinalIgnoreCase))
            {
                return ParseTaniMakmurReceiptFallback(rawText, vendorType);
            }

            string? supplierName = ResolveSupplierName(ExtractSupplierNameFallback(rawText, vendorType), vendorType);
            var receipt = new ParsedReceipt
            {
                StoreName = supplierName,
                SupplierName = supplierName,
                VendorType = vendorType,
                IsLastPage = rawText.Contains("END OF DOCUMENT", StringComparison.OrdinalIgnoreCase),
                Items = new List<ReceiptItem>()
            };

            foreach (string line in rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line.Trim(), @"^(?<name>[A-Za-z0-9\s\.\-\/]+?)\s+(?<qty>\d+(?:[.,]\d+)?)\s*x?\s*(?<price>\d[\d\.,]*)$", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                string name = match.Groups["name"].Value.Trim();
                decimal qty = ParseLooseDecimal(match.Groups["qty"].Value);
                decimal price = ParseLooseDecimal(match.Groups["price"].Value);
                if (string.IsNullOrWhiteSpace(name) ||
                    IsNonProductReceiptLine(name, vendorType) ||
                    qty <= 0 ||
                    price <= 0)
                {
                    continue;
                }

                receipt.Items!.Add(new ReceiptItem
                {
                    ProductName = name,
                    Quantity = qty,
                    UnitPrice = price,
                    Total = qty * price
                });
            }

            return receipt;
        }

        private static ParsedReceipt ParseTaniMakmurReceiptFallback(string rawText, string vendorType)
        {
            string? supplierName = ResolveSupplierName(ExtractSupplierNameFallback(rawText, vendorType), vendorType);
            var receipt = new ParsedReceipt
            {
                StoreName = supplierName,
                SupplierName = supplierName,
                BuyerName = ExtractTaniMakmurBuyerName(rawText),
                VendorType = vendorType,
                ReceiptNumber = ExtractTaniMakmurReceiptNumber(rawText),
                RawDateText = ExtractTaniMakmurDateText(rawText),
                Date = ExtractTaniMakmurDate(rawText),
                Items = new List<ReceiptItem>()
            };

            foreach (string rawLine in rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = Regex.Replace(rawLine.Trim(), @"\s+", " ");
                if (string.IsNullOrWhiteSpace(line) ||
                    line.Contains("ProdukHargaQtySatTotal", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Produk Harga Qty Sat Total", StringComparison.OrdinalIgnoreCase) ||
                    IsFooterItem(line) ||
                    line.StartsWith("Kasir", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Faktur No", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Nama Pelanggan", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Jln ", StringComparison.OrdinalIgnoreCase) ||
                    line.All(ch => ch == '=' || ch == '-' || char.IsWhiteSpace(ch)))
                {
                    continue;
                }

                var fullMatch = Regex.Match(
                    line,
                    @"^(?<name>.+?)\s+(?<price>\d[\d\.,]*)\s+(?<qty>\d+(?:[.,]\d+)?)\s+(?<unit>[A-Za-z]{2,})\s+(?<total>\d[\d\.,]*)$",
                    RegexOptions.IgnoreCase);
                if (fullMatch.Success)
                {
                    string name = fullMatch.Groups["name"].Value.Trim();
                    decimal price = ParseLooseDecimal(fullMatch.Groups["price"].Value);
                    decimal qty = ParseLooseDecimal(fullMatch.Groups["qty"].Value);
                    string? unit = NormalizeReceiptUnit(fullMatch.Groups["unit"].Value);
                    decimal total = ParseLooseDecimal(fullMatch.Groups["total"].Value);
                    if (!string.IsNullOrWhiteSpace(name) && price > 0 && qty > 0)
                    {
                        receipt.Items!.Add(new ReceiptItem
                        {
                            ProductName = name,
                            Quantity = qty,
                            Unit = unit,
                            UnitPrice = price,
                            Total = total > 0 ? total : qty * price
                        });
                    }

                    continue;
                }

                var compactMatch = Regex.Match(
                    line,
                    @"^(?<name>.+?)\s+(?<qty>\d+(?:[.,]\d+)?)\s+(?<unit>[A-Za-z]{2,})\s+(?<total>\d[\d\.,]*)$",
                    RegexOptions.IgnoreCase);
                if (!compactMatch.Success)
                {
                    continue;
                }

                string compactName = compactMatch.Groups["name"].Value.Trim();
                decimal compactQty = ParseLooseDecimal(compactMatch.Groups["qty"].Value);
                string? compactUnit = NormalizeReceiptUnit(compactMatch.Groups["unit"].Value);
                decimal compactTotal = ParseLooseDecimal(compactMatch.Groups["total"].Value);
                if (string.IsNullOrWhiteSpace(compactName) || compactQty <= 0 || compactTotal <= 0)
                {
                    continue;
                }

                receipt.Items!.Add(new ReceiptItem
                {
                    ProductName = compactName,
                    Quantity = compactQty,
                    Unit = compactUnit,
                    UnitPrice = compactTotal / compactQty,
                    Total = compactTotal
                });
            }

            return receipt;
        }

        private static string? ExtractTaniMakmurBuyerName(string rawText)
        {
            var match = Regex.Match(rawText, @"Nama\s+Pelanggan\s*:?\s*(?<buyer>.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["buyer"].Value.Trim() : null;
        }

        private static string? ExtractTaniMakmurReceiptNumber(string rawText)
        {
            var match = Regex.Match(rawText, @"Faktur\s+No\s*:?\s*(?<number>\S+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["number"].Value.Trim() : null;
        }

        private static DateTime? ExtractTaniMakmurDate(string rawText)
        {
            string? rawDate = ExtractTaniMakmurDateText(rawText);
            return rawDate == null ? null : ParseFlexibleDate(rawDate);
        }

        private static string? ExtractTaniMakmurDateText(string rawText)
        {
            var match = Regex.Match(rawText, @"\b(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s*\d{1,2}-[A-Za-z]{3}-\d{4}", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }

        private static ParsedReceipt ParseWingsReceiptFallback(string rawText)
        {
            string? receiptNumber = Regex.Match(rawText, @"\bJBM\d{6,}\b", RegexOptions.IgnoreCase) is { Success: true } numberMatch
                ? numberMatch.Value.ToUpperInvariant()
                : null;

            DateTime? receiptDate = null;
            var dateMatch = Regex.Match(rawText, @"\b\d{1,2}[./-]\d{1,2}[./-]\d{4}\b");
            if (dateMatch.Success)
            {
                receiptDate = ParseFlexibleDate(dateMatch.Value);
            }

            var receipt = new ParsedReceipt
            {
                StoreName = "WINGS / PT. SAYAP MAS UTAMA",
                SupplierName = "WINGS / PT. SAYAP MAS UTAMA",
                VendorType = "WINGS_SURAT_JALAN",
                ReceiptNumber = receiptNumber,
                Date = receiptDate,
                RawDateText = dateMatch.Success ? dateMatch.Value : null,
                IsLastPage = rawText.Contains("END OF DOCUMENT", StringComparison.OrdinalIgnoreCase),
                Items = new List<ReceiptItem>()
            };

            foreach (string rawLine in rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = Regex.Replace(rawLine.Trim(), @"\s+", " ");
                var rowMatch = Regex.Match(
                    line,
                    @"^\s*\d{1,3}\s+(?<qty>\d+(?:[.,]\d+)?)\s+(?<unit>BOX|RTG|RCG|PCS|PC|DUS|PAK|BKS)\s+(?<code>\d{4,})\s+(?<rest>.+)$",
                    RegexOptions.IgnoreCase);
                if (!rowMatch.Success)
                {
                    continue;
                }

                decimal qtyBox = ParseLooseDecimal(rowMatch.Groups["qty"].Value);
                string unit = NormalizeReceiptUnit(rowMatch.Groups["unit"].Value) ?? "Box";
                string rest = rowMatch.Groups["rest"].Value.Trim();
                var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (tokens.Count < 4 || qtyBox <= 0)
                {
                    continue;
                }

                var numericTail = new List<string>();
                for (int i = tokens.Count - 1; i >= 0; i--)
                {
                    if (!Regex.IsMatch(tokens[i], @"^\(?\d+(?:[.,]\d+)?\)?$"))
                    {
                        break;
                    }

                    numericTail.Insert(0, tokens[i].Trim('(', ')'));
                }

                if (numericTail.Count < 3)
                {
                    continue;
                }

                int nameTokenCount = tokens.Count - numericTail.Count;
                if (nameTokenCount <= 0)
                {
                    continue;
                }

                string productName = string.Join(" ", tokens.Take(nameTokenCount)).Trim();
                decimal lineTotal = ParseLooseDecimal(numericTail.Last());
                if (lineTotal < 1000 || qtyBox <= 0)
                {
                    continue;
                }

                int isiPerBox = 0;
                decimal firstNumeric = ParseLooseDecimal(numericTail.First());
                if (firstNumeric > 0 && firstNumeric <= 288)
                {
                    isiPerBox = (int)Math.Round(firstNumeric);
                }

                receipt.Items.Add(new ReceiptItem
                {
                    ProductName = CleanProductName(productName, "WINGS_SURAT_JALAN"),
                    QtyBox = qtyBox,
                    IsiPerBox = isiPerBox > 0 ? isiPerBox : null,
                    Quantity = qtyBox,
                    Unit = unit,
                    UnitPrice = qtyBox > 0 && lineTotal > 0 ? lineTotal / qtyBox : null,
                    Total = lineTotal
                });
            }

            return receipt;
        }

        private static string DetectReceiptVendor(string ocrText)
        {
            if (ocrText.Contains("FASTRATA", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("FAKTUR PENJUALAN", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("RTG,", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("Jumlah Setelah", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("Potongan Tambahan", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("Harga Incl", StringComparison.OrdinalIgnoreCase))
            {
                return "FASTRATA_FAKTUR";
            }

            if (ocrText.Contains("ARTABOGA", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("ARINDO MAKMUR", StringComparison.OrdinalIgnoreCase) ||
                (ocrText.Contains("BSR", StringComparison.OrdinalIgnoreCase) &&
                 ocrText.Contains("TGH", StringComparison.OrdinalIgnoreCase) &&
                 ocrText.Contains("KCL", StringComparison.OrdinalIgnoreCase) &&
                 (ocrText.Contains("JML NETTO", StringComparison.OrdinalIgnoreCase) ||
                  ocrText.Contains("JML HETTO", StringComparison.OrdinalIgnoreCase) ||
                  ocrText.Contains("HARGA(RP)", StringComparison.OrdinalIgnoreCase))) ||
                (ocrText.Contains("FAKTUR TUNAI", StringComparison.OrdinalIgnoreCase) &&
                 (ocrText.Contains("TGL FAKTUR", StringComparison.OrdinalIgnoreCase) ||
                  ocrText.Contains("JML NETTO", StringComparison.OrdinalIgnoreCase) ||
                  ocrText.Contains("JML. NETTO", StringComparison.OrdinalIgnoreCase))))
            {
                return "ARTABOGA_FAKTUR";
            }

            bool hasWingsIdentity =
                ocrText.Contains("WINGS", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("SAYAP MAS", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("SAYAP ANAS", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(ocrText, @"\bJBM\d{6,}\b", RegexOptions.IgnoreCase);
            bool hasWingsColumns =
                ocrText.Contains("JMLH.BERSIH", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("JMLH BERSIH", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("JML.BERSIH", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("JML BERSIH", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("JMLHBERSIH", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("CUST.DISC", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("CUST DISC", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("JTH.TEMPO", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("JTH TEMPO", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("NO.ORDER", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("NO ORDER", StringComparison.OrdinalIgnoreCase) ||
                (ocrText.Contains("NOMOR", StringComparison.OrdinalIgnoreCase) &&
                 ocrText.Contains("PEMBELI", StringComparison.OrdinalIgnoreCase) &&
                 ocrText.Contains("GUDANG", StringComparison.OrdinalIgnoreCase)) ||
                (ocrText.Contains("PEMBELI", StringComparison.OrdinalIgnoreCase) &&
                 ocrText.Contains("GUDANG", StringComparison.OrdinalIgnoreCase)) ||
                (ocrText.Contains("PEMBELI", StringComparison.OrdinalIgnoreCase) &&
                 (ocrText.Contains("NO.ORDER", StringComparison.OrdinalIgnoreCase) ||
                  ocrText.Contains("NO ORDER", StringComparison.OrdinalIgnoreCase))) ||
                (ocrText.Contains("PROD.DISC", StringComparison.OrdinalIgnoreCase) && ocrText.Contains("PEMBELI", StringComparison.OrdinalIgnoreCase)) ||
                (ocrText.Contains("PROD DISC", StringComparison.OrdinalIgnoreCase) && ocrText.Contains("PEMBELI", StringComparison.OrdinalIgnoreCase));

            if (hasWingsIdentity || (ocrText.Contains("SURAT JALAN", StringComparison.OrdinalIgnoreCase) && hasWingsColumns))
            {
                return "WINGS_SURAT_JALAN";
            }

            if (ocrText.Contains("TANI MAKMUR", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("Kasbon", StringComparison.OrdinalIgnoreCase) ||
                ocrText.Contains("Faktur No:", StringComparison.OrdinalIgnoreCase))
            {
                return "TANI_MAKMUR_POS";
            }

            if (Regex.IsMatch(ocrText, @"\b(total|subtotal|kasbon)\b", RegexOptions.IgnoreCase) &&
                !ocrText.Contains("WINGS", StringComparison.OrdinalIgnoreCase) &&
                !ocrText.Contains("FASTRATA", StringComparison.OrdinalIgnoreCase) &&
                !ocrText.Contains("SURAT JALAN", StringComparison.OrdinalIgnoreCase) &&
                !ocrText.Contains("FAKTUR PENJUALAN", StringComparison.OrdinalIgnoreCase))
            {
                return "KASIR_POS_GENERIC";
            }

            return "GENERIC";
        }

        private static string? ResolveSupplierName(string? parsedName, string vendorType)
        {
            static bool ContainsAnyKeyword(string? name, params string[] keywords)
            {
                return !string.IsNullOrWhiteSpace(name) &&
                       keywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            return vendorType switch
            {
                "WINGS_SURAT_JALAN" => ContainsAnyKeyword(parsedName, "WINGS", "PT. SAYAP MAS UTAMA", "SAYAP MAS", "SMU", "WINGS GROUP")
                    ? parsedName
                    : "WINGS / PT. SAYAP MAS UTAMA",
                "FASTRATA_FAKTUR" => ContainsAnyKeyword(parsedName, "FASTRATA", "PT. FASTRATA BUANA", "FASTRATA BUANA")
                    ? parsedName
                    : "PT. FASTRATA BUANA",
                "ARTABOGA_FAKTUR" => ContainsAnyKeyword(parsedName, "ARTABOGA", "ARINDO MAKMUR", "CEMERLANG")
                    ? parsedName
                    : "PT. ARTABOGA CEMERLANG",
                "TANI_MAKMUR_POS" => ContainsAnyKeyword(parsedName, "TANI MAKMUR", "TANI MAKMUR PUTRA")
                    ? parsedName
                    : "TANI MAKMUR PUTRA",
                _ => parsedName
            };
        }

        private static string? ExtractSupplierNameFallback(string rawText, string vendorType)
        {
            string[] lines = rawText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(8)
                .ToArray();

            return vendorType switch
            {
                "WINGS_SURAT_JALAN" => lines.FirstOrDefault(line =>
                    line.Contains("WINGS", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("PT.", StringComparison.OrdinalIgnoreCase)),
                "FASTRATA_FAKTUR" => lines.FirstOrDefault(line =>
                    line.Contains("FASTRATA", StringComparison.OrdinalIgnoreCase)),
                "TANI_MAKMUR_POS" => lines.FirstOrDefault(),
                _ => lines.FirstOrDefault()
            };
        }

        private static DateTime? ParseFlexibleDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string normalized = raw.Trim();
            normalized = Regex.Replace(normalized, @"^[A-Za-z]{3,},\s*", string.Empty);
            normalized = normalized.Split(',')[0].Trim();

            string[] formats =
            {
                "dd.MM.yyyy",
                "d.M.yyyy",
                "dd-MMM-yyyy",
                "d-MMM-yyyy",
                "dd MMM yyyy",
                "d MMM yyyy",
                "yyyy-MM-dd",
                "dd/MM/yyyy",
                "d/M/yyyy"
            };

            if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                return parsed;
            }

            if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
            {
                return parsed;
            }

            return ParseIndonesianDateExpression(normalized, DateTime.Today);
        }

        private static string? BuildReceiptDateWeekdayWarning(ParsedReceipt receipt)
        {
            if (!receipt.Date.HasValue || string.IsNullOrWhiteSpace(receipt.RawDateText))
            {
                return null;
            }

            var match = Regex.Match(receipt.RawDateText, @"^\s*(?<day>Mon|Tue|Wed|Thu|Fri|Sat|Sun)\b", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var expected = match.Groups["day"].Value.ToLowerInvariant() switch
            {
                "mon" => DayOfWeek.Monday,
                "tue" => DayOfWeek.Tuesday,
                "wed" => DayOfWeek.Wednesday,
                "thu" => DayOfWeek.Thursday,
                "fri" => DayOfWeek.Friday,
                "sat" => DayOfWeek.Saturday,
                "sun" => DayOfWeek.Sunday,
                _ => (DayOfWeek?)null
            };

            if (expected == null || expected.Value == receipt.Date.Value.DayOfWeek)
            {
                return null;
            }

            return $"Teks struk menulis {match.Groups["day"].Value}, tetapi {receipt.Date:dd/MM/yyyy} jatuh pada {FormatDayNameIndonesian(receipt.Date.Value.DayOfWeek)}. Sistem memakai {receipt.Date:dd/MM/yyyy}.";
        }

        private static string FormatDayNameIndonesian(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "Senin",
                DayOfWeek.Tuesday => "Selasa",
                DayOfWeek.Wednesday => "Rabu",
                DayOfWeek.Thursday => "Kamis",
                DayOfWeek.Friday => "Jumat",
                DayOfWeek.Saturday => "Sabtu",
                DayOfWeek.Sunday => "Minggu",
                _ => day.ToString()
            };
        }

        private static (decimal? Quantity, string? Unit) NormalizeReceiptQuantityAndUnit(decimal? quantity, string? unit, string? rawQuantity)
        {
            string? normalizedUnit = NormalizeReceiptUnit(unit);

            if (TryExtractQuantityAndUnit(rawQuantity, out var parsedQuantity, out var parsedUnit))
            {
                quantity = parsedQuantity;
                if (string.IsNullOrWhiteSpace(normalizedUnit))
                {
                    normalizedUnit = NormalizeReceiptUnit(parsedUnit);
                }
            }
            else if (TryExtractQuantityAndUnit(unit, out parsedQuantity, out parsedUnit))
            {
                if (!quantity.HasValue || quantity.Value <= 0 || quantity.Value == 1m)
                {
                    quantity = parsedQuantity;
                }

                normalizedUnit = NormalizeReceiptUnit(parsedUnit);
            }

            return (quantity, normalizedUnit);
        }

        private static bool TryExtractQuantityAndUnit(string? raw, out decimal quantity, out string? unit)
        {
            quantity = 0;
            unit = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var match = Regex.Match(raw.Trim(), @"^(?<qty>\d+(?:[.,]\d+)?)\s*(?<unit>[A-Za-z]{2,})\b", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            quantity = ParseLooseDecimal(match.Groups["qty"].Value);
            unit = match.Groups["unit"].Value;
            return quantity > 0;
        }

        private static string? NormalizeReceiptUnit(string? rawUnit)
        {
            if (string.IsNullOrWhiteSpace(rawUnit))
            {
                return null;
            }

            string normalized = rawUnit.Trim();
            string lookup = normalized.ToLowerInvariant();
            return lookup switch
            {
                "pcs" or "pc" or "piece" => "Pcs",
                "dus" => "Dus",
                "box" => "Box",
                "bal" => "Bal",
                "bks" => "Bks",
                "rtg" or "rcg" => "Rcg",
                "pak" or "pack" => "Pak",
                "rol" or "roll" => "Rol",
                "kg" => "Kg",
                "ltr" or "liter" => "Ltr",
                "kmpn" => "Kmpn",
                _ => normalized
            };
        }

        private static string CleanProductName(string? raw, string? vendorType)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string cleaned = raw.Trim();
            cleaned = Regex.Replace(cleaned, @"^\d+\s*[.)-]\s*", string.Empty);
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            bool isFastrataLike = string.Equals(vendorType, "FASTRATA_FAKTUR", StringComparison.OrdinalIgnoreCase) ||
                                  Regex.IsMatch(cleaned, @"^[A-Z0-9]{2,}\.[A-Z0-9]{2,}\.[A-Z0-9]{4,}\s+", RegexOptions.IgnoreCase);

            if (isFastrataLike)
            {
                cleaned = Regex.Replace(cleaned, @"^[A-Z0-9]{2,}\.[A-Z0-9]{2,}\.[A-Z0-9]{4,}\s+", string.Empty, RegexOptions.IgnoreCase);
                cleaned = Regex.Replace(cleaned, @"^\d{4,}[A-Z]{1,3}\s+", string.Empty, RegexOptions.IgnoreCase);
                cleaned = Regex.Replace(cleaned, @"\((?=[^)]*(?:RTG|\d+\s*GR|\d+X\d+))[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
                cleaned = Regex.Replace(cleaned, @"\bNB\s+\d{5,}\b", string.Empty, RegexOptions.IgnoreCase);
                cleaned = Regex.Replace(cleaned, @"\b\d{6,}\b$", string.Empty, RegexOptions.IgnoreCase);
            }

            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim(' ', '-', '.', ',', ';', ':');
            return cleaned;
        }

        private static bool IsFooterItem(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string normalized = NormalizeText(name);
            return OcrFooterKeywords.Any(keyword =>
            {
                string normalizedKeyword = NormalizeText(keyword);
                if (string.IsNullOrWhiteSpace(normalizedKeyword))
                {
                    return false;
                }

                if (normalizedKeyword.Contains(' '))
                {
                    return normalized.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase);
                }

                return Regex.IsMatch(normalized, $@"\b{Regex.Escape(normalizedKeyword)}\b", RegexOptions.IgnoreCase);
            });
        }

        private static bool IsNonProductReceiptLine(string name, string? vendorType)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string normalized = NormalizeText(name);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            if (Regex.IsMatch(normalized, @"\b(jl|jalan|alamat|ship to|tanggal|tgl|faktur|nomor|halaman|npwp|operator|wiraniaga|duta arta|purwakarta|jakarta|cengkareng|citamiang|palumbon)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (Regex.IsMatch(normalized, @"\b\d{1,2}\s+(jan|feb|mar|apr|may|mei|jun|jul|aug|agu|sep|oct|okt|nov|dec|des)\s+\d{4}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            string normalizedVendor = vendorType?.Trim().ToUpperInvariant() ?? string.Empty;
            if (normalizedVendor == "ARTABOGA_FAKTUR")
            {
                bool looksLikeArtabogaProduct =
                    Regex.IsMatch(normalized, @"\bbaterai\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(normalized, @"^\$?\d{2,5}\s+[a-z0-9]", RegexOptions.IgnoreCase);

                return !looksLikeArtabogaProduct &&
                       Regex.IsMatch(normalized, @"\b(rp|may|mei|pwk|manits|bayar|ditempat)\b", RegexOptions.IgnoreCase);
            }

            if (normalizedVendor == "FASTRATA_FAKTUR")
            {
                bool looksLikeFastrataProduct =
                    Regex.IsMatch(normalized, @"\b(rtg|sp mix|mocacinno|abc susu|ka one)\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(normalized, @"^[a-z0-9]{2,}\.[a-z0-9]{2,}\.[a-z0-9]{4,}\s+", RegexOptions.IgnoreCase);

                return !looksLikeFastrataProduct &&
                       Regex.IsMatch(normalized, @"\b(no kartu|surat pesanan|jatuh tempo|perhatian|transfer|dpp|ppn|terbilang)\b", RegexOptions.IgnoreCase);
            }

            return false;
        }

        private static string? GetReceiptSupplierName(ParsedReceipt receipt)
        {
            return string.IsNullOrWhiteSpace(receipt.SupplierName)
                ? receipt.StoreName
                : receipt.SupplierName;
        }

        private static int GetOcrQuantitySafetyThreshold(string? vendorType)
        {
            return vendorType?.Trim().ToUpperInvariant() switch
            {
                "WINGS_SURAT_JALAN" => 200,
                "FASTRATA_FAKTUR" => 100,
                "ARTABOGA_FAKTUR" => 120,
                _ => 50
            };
        }

        private async Task<ReceiptMappingOutcome> MapReceiptItemsToBulkPendingItemsAsync(
            IEnumerable<ReceiptItem> items,
            OcrReceiptSettings settings,
            InboundMessage message,
            ParsedReceipt receipt)
        {
            var outcome = new ReceiptMappingOutcome();
            foreach (var item in items)
            {
                string originalRawName = item.ProductName?.Trim() ?? string.Empty;
                string rawName = CleanProductName(originalRawName, receipt.VendorType);
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    rawName = originalRawName;
                }

                if (IsFooterItem(originalRawName) ||
                    IsFooterItem(rawName) ||
                    IsNonProductReceiptLine(originalRawName, receipt.VendorType) ||
                    IsNonProductReceiptLine(rawName, receipt.VendorType))
                {
                    continue;
                }

                decimal quantity = item.Quantity ?? 0;
                decimal parsedPrice = item.UnitPrice ?? 0;
                decimal parsedTotal = item.Total ?? 0;
                string? parsedUnit = NormalizeReceiptUnit(item.Unit);
                string? totalMismatchWarning = BuildLineTotalMismatchWarning(rawName, quantity, parsedPrice, parsedTotal);

                if (string.Equals(receipt.VendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase))
                {
                    decimal invoiceQuantity = item.QtyBox.GetValueOrDefault() > 0
                        ? item.QtyBox!.Value
                        : quantity;

                    if (invoiceQuantity > 0)
                    {
                        quantity = invoiceQuantity;
                        parsedUnit ??= "Box";
                    }

                    if (parsedTotal > 0 && quantity > 0)
                    {
                        parsedPrice = parsedTotal / quantity;
                    }
                }

                if (string.Equals(receipt.VendorType, "FASTRATA_FAKTUR", StringComparison.OrdinalIgnoreCase) &&
                    parsedTotal <= 0 &&
                    parsedPrice > 0 &&
                    quantity > 0)
                {
                    parsedTotal = parsedPrice * quantity;
                }

                if (string.Equals(receipt.VendorType, "WINGS_SURAT_JALAN", StringComparison.OrdinalIgnoreCase) &&
                    quantity > 0 &&
                    parsedPrice > 0 &&
                    parsedTotal > 0)
                {
                    decimal expectedTotal = parsedPrice * quantity;
                    bool totalMismatch = Math.Abs(expectedTotal - parsedTotal) / parsedTotal > 0.10m;
                    if (totalMismatch)
                    {
                        outcome.ReviewItems.Add(BuildOcrReviewQueueItem(
                            message,
                            receipt,
                            rawName,
                            quantity,
                            parsedUnit,
                            parsedPrice,
                            parsedTotal,
                            null,
                            $"Wings inkonsisten: qty {FormatStockValue(quantity)} x harga {FormatCurrency(parsedPrice)} tidak cocok dengan total {FormatCurrency(parsedTotal)}.",
                            item.IsiPerBox));
                        continue;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(totalMismatchWarning))
                {
                    outcome.Warnings.Add(totalMismatchWarning);
                }

                int safetyThreshold = GetOcrQuantitySafetyThreshold(receipt.VendorType);
                if (quantity >= safetyThreshold &&
                    (string.IsNullOrWhiteSpace(parsedUnit) ||
                     string.Equals(parsedUnit, "Box", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parsedUnit, "Dus", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parsedUnit, "Pak", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parsedUnit, "Bks", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parsedUnit, "Bal", StringComparison.OrdinalIgnoreCase)))
                {
                    outcome.ReviewItems.Add(BuildOcrReviewQueueItem(
                        message,
                        receipt,
                        rawName,
                        quantity,
                        parsedUnit,
                        parsedPrice,
                        parsedTotal,
                        null,
                        $"Qty={FormatStockValue(quantity)} sangat besar dan mencurigakan. Kemungkinan OCR mengambil angka dari baris total. Periksa manual.",
                        item.IsiPerBox));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rawName) || quantity <= 0)
                {
                    outcome.ReviewItems.Add(BuildOcrReviewQueueItem(
                        message,
                        receipt,
                        rawName,
                        quantity,
                        parsedUnit,
                        parsedPrice,
                        parsedTotal,
                        null,
                        "Nama produk kosong atau qty tidak valid.",
                        item.IsiPerBox));
                    continue;
                }

                if (string.Equals(receipt.VendorType, "FASTRATA_FAKTUR", StringComparison.OrdinalIgnoreCase) &&
                    quantity > 0 &&
                    parsedPrice <= 0 &&
                    parsedTotal <= 0)
                {
                    outcome.ReviewItems.Add(BuildOcrReviewQueueItem(
                        message,
                        receipt,
                        rawName,
                        quantity,
                        parsedUnit,
                        parsedPrice,
                        parsedTotal,
                        null,
                        "Harga tidak terbaca dari OCR. Input manual di review queue.",
                        item.IsiPerBox));
                    continue;
                }

                Product? mappedProduct = null;
                bool shouldLearnAlias = false;
                string aliasSource = "ocr";
                bool requiresExplicitMapping = RequiresExplicitOcrMapping(rawName, parsedUnit);

                var configuredMap = settings.ProductMappings.FirstOrDefault(map =>
                    rawName.Contains(map.InvoiceName, StringComparison.OrdinalIgnoreCase) ||
                    map.InvoiceName.Contains(rawName, StringComparison.OrdinalIgnoreCase) ||
                    originalRawName.Contains(map.InvoiceName, StringComparison.OrdinalIgnoreCase) ||
                    map.InvoiceName.Contains(originalRawName, StringComparison.OrdinalIgnoreCase));

                if (configuredMap != null)
                {
                    var configuredProduct = await _posDbService!.GetProductByIdAsync(configuredMap.DatabaseProductId);
                    if (IsReceiptUnitCompatible(parsedUnit, configuredProduct))
                    {
                        mappedProduct = configuredProduct;
                        shouldLearnAlias = mappedProduct != null;
                        aliasSource = "config-mapping";
                    }
                    else
                    {
                        await _loggingService.LogInfoAsync(
                            $"[OCR] Config mapping '{configuredMap.InvoiceName}' diabaikan karena unit struk '{parsedUnit}' tidak cocok dengan produk '{configuredProduct?.Name}' ({configuredProduct?.Unit}).",
                            "OCR");
                    }
                }

                if (mappedProduct == null)
                {
                    var alias = await _databaseService.GetProductAliasAsync(rawName);
                    if (alias == null &&
                        !string.Equals(rawName, originalRawName, StringComparison.OrdinalIgnoreCase))
                    {
                        alias = await _databaseService.GetProductAliasAsync(originalRawName);
                    }

                    if (alias != null && !requiresExplicitMapping)
                    {
                        var aliasProduct = await _posDbService!.GetProductByIdAsync(alias.ProductId);
                        if (IsReceiptUnitCompatible(parsedUnit, aliasProduct))
                        {
                            mappedProduct = aliasProduct;
                            aliasSource = alias.Source ?? "product-alias";
                        }
                        else
                        {
                            await _loggingService.LogInfoAsync(
                                $"[OCR] Alias '{rawName}' diabaikan karena unit struk '{parsedUnit}' tidak cocok dengan produk '{aliasProduct?.Name}' ({aliasProduct?.Unit}).",
                            "OCR");
                        }
                    }
                    else if (alias != null)
                    {
                        await _loggingService.LogInfoAsync(
                            $"[OCR] Alias '{rawName}' diabaikan karena produk kemasan perlu mapping OCR eksplisit.",
                            "OCR");
                    }

                    if (mappedProduct == null)
                    {
                        var resolved = await ResolveProductForReceiptAsync(rawName, parsedUnit);
                        var bestCandidate = resolved.Candidates.FirstOrDefault();
                        // Score scale is heuristic: exact match gets 100+, token overlap adds 10 each.
                        // OCR adds unit-aware score adjustments so bulk units prefer parent products and pcs/ecer units prefer child products.
                        bool strongMatch = resolved.BestMatch != null &&
                                           !resolved.IsAmbiguous &&
                                           (bestCandidate?.IsExactMatch == true ||
                                            (!requiresExplicitMapping && (bestCandidate?.Score ?? 0) >= 45));

                        if (strongMatch)
                        {
                            mappedProduct = resolved.BestMatch;
                            shouldLearnAlias = true;
                            aliasSource = "auto-match";
                        }
                        else
                        {
                            outcome.ReviewItems.Add(BuildOcrReviewQueueItem(
                                message,
                                receipt,
                                rawName,
                                quantity,
                                parsedUnit,
                                parsedPrice,
                                parsedTotal,
                                BuildCandidateSummary(resolved.Candidates),
                                requiresExplicitMapping
                                    ? "Produk kemasan perlu mapping OCR manual supaya parent/child tidak salah."
                                    : resolved.Reason ?? "Produk belum bisa dicocokkan otomatis.",
                                item.IsiPerBox));
                            continue;
                        }
                    }
                }

                if (mappedProduct == null || string.IsNullOrWhiteSpace(mappedProduct.Id))
                {
                    outcome.ReviewItems.Add(BuildOcrReviewQueueItem(
                        message,
                        receipt,
                        rawName,
                        quantity,
                        parsedUnit,
                        parsedPrice,
                        parsedTotal,
                        null,
                        "Produk tujuan tidak ditemukan di database.",
                        item.IsiPerBox));
                    continue;
                }

                if (shouldLearnAlias)
                {
                    await _databaseService.UpsertProductAliasAsync(new ProductAliasEntry
                    {
                        AliasName = rawName,
                        ProductId = mappedProduct.Id,
                        ProductName = mappedProduct.Name,
                        Source = aliasSource,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    });
                }

                decimal price = string.Equals(receipt.VendorType, "TANI_MAKMUR_POS", StringComparison.OrdinalIgnoreCase) && parsedPrice > 0
                    ? parsedPrice
                    : quantity > 0 && parsedTotal > 0
                        ? parsedTotal / quantity
                        : parsedPrice > 0
                            ? parsedPrice
                            : 0;
                if (price <= 0)
                {
                    price = mappedProduct.PurchasePrice ?? 0;
                }

                outcome.ValidItems.Add(new BulkPendingItem
                {
                    ProductId = mappedProduct.Id,
                    ProductName = mappedProduct.Name ?? rawName,
                    Quantity = quantity,
                    Price = price,
                    CurrentStock = mappedProduct.Stock,
                    Unit = mappedProduct.Unit,
                    IsiPerBox = item.IsiPerBox,
                    RawProductNames = new List<string> { rawName, originalRawName }
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }

            outcome.ValidItems = outcome.ValidItems
                .GroupBy(item => item.ProductId, StringComparer.Ordinal)
                .Select(group => new BulkPendingItem
                {
                    ProductId = group.Key,
                    ProductName = group.First().ProductName,
                    Quantity = group.Sum(item => item.Quantity),
                    Price = group.Last().Price,
                    CurrentStock = group.First().CurrentStock,
                    Unit = group.First().Unit,
                    IsiPerBox = group
                        .Select(item => item.IsiPerBox)
                        .FirstOrDefault(value => value.GetValueOrDefault() > 0),
                    RawProductNames = group
                        .SelectMany(item => item.RawProductNames ?? Enumerable.Empty<string>())
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            return outcome;
        }

        private OcrReviewQueueItem BuildOcrReviewQueueItem(
            InboundMessage message,
            ParsedReceipt receipt,
            string rawName,
            decimal quantity,
            string? unit,
            decimal unitPrice,
            decimal lineTotal,
            string? candidateSummary,
            string? note,
            int? isiPerBox = null)
        {
            decimal effectiveTotal = lineTotal > 0
                ? lineTotal
                : quantity > 0 && unitPrice > 0
                    ? quantity * unitPrice
                    : 0;

            return new OcrReviewQueueItem
            {
                ReceiptCorrelationId = message.CorrelationId ?? Guid.NewGuid().ToString("N"),
                SenderId = message.SenderId,
                SupplierName = GetReceiptSupplierName(receipt),
                ReceiptDate = receipt.Date,
                RawProductName = string.IsNullOrWhiteSpace(rawName) ? "(tanpa nama)" : rawName,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = effectiveTotal,
                Unit = unit,
                IsiPerBox = isiPerBox,
                Status = "pending",
                CandidateSummary = candidateSummary,
                Note = note,
                CreatedAt = DateTime.Now
            };
        }

        private static string? BuildLineTotalMismatchWarning(string productName, decimal quantity, decimal unitPrice, decimal lineTotal)
        {
            if (quantity <= 0 || unitPrice <= 0 || lineTotal <= 0)
            {
                return null;
            }

            decimal expectedTotal = quantity * unitPrice;
            decimal denominator = Math.Max(Math.Abs(expectedTotal), Math.Abs(lineTotal));
            if (denominator <= 0)
            {
                return null;
            }

            decimal delta = Math.Abs(expectedTotal - lineTotal);
            if (delta < 1m || (delta / denominator) <= 0.05m)
            {
                return null;
            }

            string label = string.IsNullOrWhiteSpace(productName) ? "baris struk" : productName;
            return $"{label}: total struk {FormatCurrency(lineTotal)} berbeda dari hitungan {FormatCurrency(expectedTotal)}. Sistem memakai harga x qty untuk dokumen.";
        }

        private static string? BuildCandidateSummary(IEnumerable<ProductMatchCandidate> candidates)
        {
            var top = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Product.Name))
                .Take(3)
                .Select(candidate => $"{candidate.Product.Name} ({candidate.Score})")
                .ToList();

            return top.Any() ? string.Join("; ", top) : null;
        }

        private string BuildOcrPreviewMessage(ParsedReceipt receipt, ReceiptMappingOutcome outcome)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconReceipt} PREVIEW STRUK OCR");
            string? supplierName = GetReceiptSupplierName(receipt);
            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                sb.AppendLine($"Supplier: {supplierName}");
            }
            if (receipt.Date.HasValue)
            {
                sb.AppendLine($"Tanggal: {receipt.Date:dd/MM/yyyy}");
            }

            sb.AppendLine();
            sb.AppendLine($"{IconCheck} {outcome.ValidItems.Count} item valid");
            if (outcome.ReviewItems.Any())
            {
                sb.AppendLine($"{IconWarning} {outcome.ReviewItems.Count} item perlu review");
            }

            decimal validTotal = outcome.ValidItems.Sum(item => item.Quantity * (item.Price ?? 0));
            if (validTotal > 0)
            {
                sb.AppendLine($"Total valid terbaca: {FormatCurrency(validTotal)}");
            }
            else if (receipt.Total.HasValue)
            {
                sb.AppendLine($"Total struk: {FormatCurrency(receipt.Total.Value)}");
            }

            string? dateWarning = BuildReceiptDateWeekdayWarning(receipt);
            var previewWarnings = outcome.Warnings
                .Concat(string.IsNullOrWhiteSpace(dateWarning) ? Enumerable.Empty<string>() : new[] { dateWarning })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
            if (previewWarnings.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Catatan:");
                foreach (string warning in previewWarnings)
                {
                    sb.AppendLine($"- {warning}");
                }
            }

            if (outcome.ValidItems.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Item valid:");
                foreach (var item in outcome.ValidItems.Take(6))
                {
                    sb.AppendLine($"- {item.ProductName} | {FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)} | {FormatCurrency(item.Price ?? 0)}");
                }
            }

            if (outcome.ReviewItems.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Perlu dicek:");
                foreach (var review in outcome.ReviewItems.Take(4))
                {
                    sb.AppendLine($"- {review.RawProductName} | qty {FormatStockValue(review.Quantity)} | {review.Note}");
                    if (!string.IsNullOrWhiteSpace(review.CandidateSummary))
                    {
                        sb.AppendLine($"  kandidat: {review.CandidateSummary}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Jika dikonfirmasi, item valid akan disimpan dan item bermasalah masuk OCR Review Queue di desktop.");
            }

            sb.AppendLine();
            sb.Append(BuildConfirmationActions());
            return sb.ToString().TrimEnd();
        }

        private string BuildOcrQueuedForReviewMessage(ParsedReceipt receipt, List<OcrReviewQueueItem> reviewItems)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} OCR perlu review manual");
            string? supplierName = GetReceiptSupplierName(receipt);
            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                sb.AppendLine($"Supplier: {supplierName}");
            }
            if (receipt.Date.HasValue)
            {
                sb.AppendLine($"Tanggal: {receipt.Date:dd/MM/yyyy}");
            }

            sb.AppendLine();
            sb.AppendLine($"Tidak ada item yang cukup yakin untuk disimpan otomatis.");
            sb.AppendLine($"{reviewItems.Count} item sudah dimasukkan ke OCR Review Queue di aplikasi desktop.");
            sb.AppendLine("Buka Settings > OCR Review Queue untuk memperbaiki dan membuat purchase document.");
            return sb.ToString().TrimEnd();
        }

        private static decimal ParseLooseDecimal(string raw)
        {
            string normalized = raw.Trim();
            bool looksLikeWesternDecimal = Regex.IsMatch(normalized, @"^\d+\.\d{1,2}$");
            if (!looksLikeWesternDecimal)
            {
                normalized = normalized.Replace(".", string.Empty);
            }

            normalized = normalized.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private static void TryDeleteTempFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static DateTime GetMonthStart(DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }

        private async Task<string> BuildRealStoreDataAsync(string userMessage, bool isOwner)
        {
            if (_posDbService == null)
            {
                return "Data toko tidak tersedia.";
            }

            var intent = DetectDeterministicIntent(userMessage);
            var shopName = await _posDbService.GetShopNameAsync() ?? "Toko";
            var sb = new StringBuilder();
            sb.AppendLine($"DATA REAL {shopName.ToUpperInvariant()}:");
            int totalCustomers = await _posDbService.GetTotalCustomersAsync();
            int totalSuppliers = await _posDbService.GetTotalSuppliersAsync();
            int totalProducts = await _posDbService.GetProductCountAsync();
            decimal totalReceivable = await _posDbService.GetTotalReceivableAsync();
            sb.AppendLine($"Total pelanggan terdaftar: {totalCustomers}");
            sb.AppendLine($"Total supplier: {totalSuppliers}");
            sb.AppendLine($"Total produk: {totalProducts}");
            if (totalReceivable > 0)
            {
                sb.AppendLine($"Total piutang pelanggan (belum lunas): {FormatCurrency(totalReceivable)}");
            }

            if (intent?.Kind == "customers")
            {
                var customers = await _posDbService.GetCustomersAsync(intent.Argument, 5, onlyCustomers: true);
                if (!customers.Any())
                {
                    return $"{sb}Pelanggan tidak ditemukan.";
                }

                sb.AppendLine("Pelanggan relevan:");
                foreach (var customer in customers)
                {
                    sb.AppendLine($"- {customer.Name} | HP {FormatOptional(customer.Phone)} | Email {FormatOptional(customer.Email)} | {customer.PurchaseCount} transaksi");
                }
                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "customer_transactions" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var customers = await _posDbService.GetCustomersAsync(intent.Argument, 3, onlyCustomers: true);
                var bestMatch = customers.FirstOrDefault();
                if (bestMatch == null || string.IsNullOrWhiteSpace(bestMatch.Id))
                {
                    return $"{sb}Pelanggan tidak ditemukan.";
                }

                sb.AppendLine($"Pelanggan: {FormatOptional(bestMatch.Name)}");
                sb.AppendLine($"- HP: {FormatOptional(bestMatch.Phone)}");
                sb.AppendLine($"- Total transaksi: {bestMatch.PurchaseCount}");
                sb.AppendLine($"- Total belanja: {FormatCurrency(bestMatch.TotalSpent)}");

                var transactions = await _posDbService.GetCustomerTransactionsAsync(bestMatch.Id, 5);
                if (transactions.Any())
                {
                    sb.AppendLine("Transaksi terakhir:");
                    foreach (var transaction in transactions)
                    {
                        sb.AppendLine($"- {FormatShortDate(transaction.Date)} | {FormatCompactDocumentNumber(transaction.DocumentNumber)} | {FormatOptional(transaction.ProductName)} | {FormatCurrency(transaction.ItemTotal)}");
                    }
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "suppliers")
            {
                var suppliers = await _posDbService.GetSuppliersAsync(intent.Argument, 5);
                if (!suppliers.Any())
                {
                    return $"{sb}Supplier tidak ditemukan.";
                }

                sb.AppendLine("Supplier relevan:");
                foreach (var supplier in suppliers)
                {
                    sb.AppendLine($"- {supplier.Name} | HP {FormatOptional(supplier.Phone)} | Email {FormatOptional(supplier.Email)}");
                }
                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "receivables_list" || intent?.Kind == "receivables_total")
            {
                var receivables = await _posDbService.GetCustomerReceivablesAsync();
                if (!receivables.Any())
                {
                    return $"{sb}Tidak ada piutang pelanggan.";
                }

                sb.AppendLine($"Total piutang: {FormatCurrency(receivables.Sum(item => item.TotalOwed))}");
                foreach (var item in receivables.Take(5))
                {
                    sb.AppendLine($"- {item.CustomerName}: {FormatCurrency(item.TotalOwed)} ({item.InvoiceCount} faktur)");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "receivables_detail" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var customers = await _posDbService.GetCustomersAsync(intent.Argument, 3, onlyCustomers: true);
                var customer = customers.FirstOrDefault();
                if (customer == null || string.IsNullOrWhiteSpace(customer.Id))
                {
                    return $"{sb}Pelanggan tidak ditemukan.";
                }

                var invoices = await _posDbService.GetCustomerReceivableDetailAsync(customer.Id);
                if (!invoices.Any())
                {
                    return $"{sb}Pelanggan tidak memiliki piutang aktif.";
                }

                sb.AppendLine($"Piutang pelanggan: {FormatOptional(customer.Name)}");
                sb.AppendLine($"- Total: {FormatCurrency(invoices.Sum(item => item.OutstandingBalance))}");
                foreach (var invoice in invoices.Take(5))
                {
                    sb.AppendLine($"- {FormatCompactDocumentNumber(invoice.DocumentNumber)} | {FormatShortDate(invoice.Date)} | JT {FormatShortDate(invoice.DueDate)} | Sisa {FormatCurrency(invoice.OutstandingBalance)}");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "transaction_count")
            {
                var (startDate, endDate, _, titleLabel, dateLabel) = ResolveSalesPeriod(intent.Argument);
                int transactionCount = await _posDbService.GetSalesTransactionCountAsync(startDate, endDate);
                sb.AppendLine($"Jumlah transaksi {titleLabel.ToLowerInvariant()}: {transactionCount}");
                sb.AppendLine($"Periode: {dateLabel}");
                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "customer_documents" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var customers = await _posDbService.GetCustomersAsync(intent.Argument, 3, onlyCustomers: true);
                var customer = customers.FirstOrDefault();
                if (customer == null || string.IsNullOrWhiteSpace(customer.Id))
                {
                    return $"{sb}Pelanggan tidak ditemukan.";
                }

                var documents = await _posDbService.GetCustomerRecentDocumentsAsync(customer.Id, 5);
                if (!documents.Any())
                {
                    return $"{sb}Belum ada dokumen penjualan pelanggan.";
                }

                sb.AppendLine($"Dokumen pelanggan: {FormatOptional(customer.Name)}");
                foreach (var document in documents)
                {
                    string outstandingLabel = document.OutstandingBalance > 0
                        ? $" | Sisa {FormatCurrency(document.OutstandingBalance)}"
                        : " | Lunas";
                    sb.AppendLine($"- {FormatCompactDocumentNumber(document.DocumentNumber)} | {FormatShortDate(document.Date)} | Total {FormatCurrency(document.Total)}{outstandingLabel}");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "users")
            {
                var users = await _posDbService.GetUsersAsync(intent.Argument, 5);
                if (!users.Any())
                {
                    return $"{sb}User tidak ditemukan.";
                }

                sb.AppendLine("User relevan:");
                foreach (var user in users)
                {
                    sb.AppendLine($"- {FormatOptional(user.FullName)} | {FormatOptional(user.Username)} | {user.Role} / level {user.RoleId}");
                }
                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "product_sales" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var (product, error) = await TryResolveProductAsync(intent.Argument, isMutation: false, actionLabel: "lihat data penjualan");
                if (product == null || !string.IsNullOrWhiteSpace(error))
                {
                    return $"{sb}{error ?? "Produk tidak ditemukan."}";
                }

                var summary = await _posDbService.GetProductSalesSummaryAsync(product.Id);
                if (summary == null)
                {
                    return $"{sb}Data penjualan produk tidak tersedia.";
                }

                sb.AppendLine($"Produk: {product.Name}");
                sb.AppendLine($"- Qty terjual: {FormatStockValue(summary.QuantitySold)} {GetUnitLabel(product.Unit)}");
                sb.AppendLine($"- Revenue: {FormatCurrency(summary.Revenue)}");
                sb.AppendLine($"- Profit: {FormatCurrency(summary.Profit)}");
                sb.AppendLine($"- Penjualan terakhir: {FormatDateTime(summary.LastSaleDate)}");
                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "purchase_history" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var (product, error) = await TryResolveProductAsync(intent.Argument, isMutation: false, actionLabel: "lihat riwayat restock");
                if (product == null || !string.IsNullOrWhiteSpace(error))
                {
                    return $"{sb}{error ?? "Produk tidak ditemukan."}";
                }

                var history = await _posDbService.GetRestockHistoryAsync(product.Id ?? string.Empty, 5);
                sb.AppendLine($"Riwayat restock produk: {product.Name}");
                if (!history.Any())
                {
                    sb.AppendLine("- Belum ada riwayat restock.");
                }
                else
                {
                    foreach (var item in history)
                    {
                        sb.AppendLine($"- {FormatShortDate(item.Date)} | {FormatCompactDocumentNumber(item.DocumentNumber)} | {FormatDisplayQuantity(item.Quantity)} {GetUnitLabel(product.Unit)} | {FormatCurrency(item.Price)}");
                    }
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "expiry_info")
            {
                sb.AppendLine("Batas data expired:");
                sb.AppendLine("- Tanggal expired tidak selalu tersedia sebagai data produk terstruktur di pos.db.");
                sb.AppendLine("- Jika ada catatan expired, biasanya perlu dicek dari dokumen pembelian/restock terakhir.");
                if (!string.IsNullOrWhiteSpace(intent.Argument))
                {
                    sb.AppendLine($"Produk yang ditanyakan: {intent.Argument}");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "slow_moving")
            {
                var slowMoving = await _posDbService.GetSlowMovingProductsAsync(30, 5, 10);
                sb.AppendLine("Definisi slow moving: stok > 0, terjual rendah dalam 30 hari, masih ada penjualan dalam 14 hari terakhir. Stok minus tidak dihitung.");
                foreach (var product in slowMoving)
                {
                    sb.AppendLine($"- {product.ProductName}: stok {FormatDisplayQuantity(product.CurrentStock)} {GetUnitLabel(product.Unit)}, terjual {FormatDisplayQuantity(product.QuantitySold)} 30 hari");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "profit_explain")
            {
                var profit = await _posDbService.GetProfitCalculationExplanationAsync(DateTime.Today, DateTime.Today);
                sb.AppendLine("Data profit hari ini:");
                sb.AppendLine($"- Transaksi: {profit.TransactionCount}");
                sb.AppendLine($"- Omzet: {FormatCurrency(profit.Revenue)}");
                sb.AppendLine($"- HPP/modal barang: {FormatCurrency(profit.CostOfGoodsSold)}");
                sb.AppendLine($"- Profit kotor: {FormatCurrency(profit.GrossProfit)}");
                sb.AppendLine($"- Margin: {profit.MarginPercent:0.##}%");
                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "category_search" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var products = await _posDbService.GetProductCategoryGroupAsync(intent.Argument, 10);
                sb.AppendLine($"Produk grup {intent.Argument} via keyword matching:");
                foreach (var product in products)
                {
                    sb.AppendLine($"- {product.Name}: stok {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}, jual {FormatCurrency(product.SellingPrice ?? 0)}");
                }

                if (!products.Any())
                {
                    sb.AppendLine("- Tidak ada produk cocok. Kategori terstruktur tidak tersedia, jadi pencarian memakai keyword nama/kategori.");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "top_supplier")
            {
                var suppliers = await _posDbService.GetSupplierPurchaseSummaryAsync(5);
                sb.AppendLine("Supplier pembelian terbesar:");
                foreach (var supplier in suppliers)
                {
                    sb.AppendLine($"- {supplier.SupplierName}: {supplier.PurchaseCount} dokumen, {FormatCurrency(supplier.TotalPurchase)}");
                }

                return sb.ToString().TrimEnd();
            }

            if (intent?.Kind == "document_lookup" && !string.IsNullOrWhiteSpace(intent.Argument))
            {
                var document = await _posDbService.GetDocumentByNumberAsync(intent.Argument);
                if (document == null)
                {
                    return $"{sb}Dokumen tidak ditemukan.";
                }

                sb.AppendLine($"Dokumen: {document.Number}");
                sb.AppendLine($"- Tipe: {document.DocumentTypeLabel}");
                sb.AppendLine($"- Tanggal: {FormatDateTime(document.Date)}");
                sb.AppendLine($"- Kasir: {FormatOptional(document.UserName)}");
                sb.AppendLine($"- Customer: {FormatOptional(document.CustomerName)}");
                sb.AppendLine($"- Total: {FormatCurrency(document.Total)}");
                return sb.ToString().TrimEnd();
            }

            var todayRevenue = await _posDbService.GetTodayRevenueAsync();
            var yesterdayRevenue = await _posDbService.GetYesterdayRevenueAsync();
            sb.AppendLine($"Hari ini omzet {FormatCurrency(todayRevenue)}");
            if (isOwner)
            {
                var todayProfit = await _posDbService.GetTodayProfitAsync();
                sb.AppendLine($"Hari ini profit {FormatCurrency(todayProfit)}");
            }
            sb.AppendLine($"Kemarin omzet {FormatCurrency(yesterdayRevenue)}");

            bool includeTopSelling = normalizedContainsAny(userMessage, "laris", "terlaris", "analisa", "penjualan", "omzet");
            bool includeLowStock = normalizedContainsAny(userMessage, "stok", "restock", "habis", "rendah", "minus");
            bool includeRelevantProducts = normalizedContainsAny(userMessage, "stok", "produk", "barang", "harga", "restock", "inventory", "jual");
            var relevantProducts = includeRelevantProducts
                ? await FindProductsAsync(userMessage, 6)
                : new List<Product>();

            if (includeTopSelling)
            {
                var topSelling = await _posDbService.GetTopSellingProductsAsync(3);
                if (topSelling.Any())
                {
                    sb.AppendLine("Produk terlaris:");
                    foreach (var product in topSelling)
                    {
                        sb.AppendLine($"- {product.Name}: terjual {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}");
                    }
                }
            }

            if (includeLowStock)
            {
                var lowStock = await _posDbService.GetLowStockProductsAsync(5);
                if (lowStock.Any())
                {
                    sb.AppendLine("Stok rendah:");
                    foreach (var product in lowStock)
                    {
                        sb.AppendLine($"- {product.Name}: {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}");
                    }
                }
            }

            if (relevantProducts.Any())
            {
                sb.AppendLine("Produk relevan:");
                foreach (var product in relevantProducts)
                {
                    if (isOwner)
                    {
                        sb.AppendLine($"- {product.Name}: stok {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}, modal {FormatCurrency(product.PurchasePrice ?? 0)}, jual {FormatCurrency(product.SellingPrice ?? 0)}");
                    }
                    else
                    {
                        sb.AppendLine($"- {product.Name}: stok {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}, jual {FormatCurrency(product.SellingPrice ?? 0)}");
                    }
                }
            }

            return sb.ToString().TrimEnd();

            static bool normalizedContainsAny(string message, params string[] keywords)
            {
                return keywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }
        }

        private async Task<(Product? Product, string? Error)> TryResolveProductAsync(string query, bool isMutation, string actionLabel)
        {
            var match = await ResolveProductAsync(query);
            if (match.BestMatch == null)
            {
                return (null, $"Produk \"{query}\" tidak ditemukan.");
            }

            if (match.IsAmbiguous)
            {
                return (null, BuildAmbiguousProductMatchMessage(query, match, isMutation, actionLabel));
            }

            return (match.BestMatch, null);
        }

        private async Task<List<Product>> FindProductsAsync(string query, int limit)
        {
            var matches = await FindProductMatchesAsync(query, limit);
            return matches.Select(match => match.Product).ToList();
        }

        private async Task<List<ProductMatchCandidate>> FindProductMatchesAsync(string query, int limit)
        {
            if (_posDbService == null)
            {
                return new List<ProductMatchCandidate>();
            }

            string normalized = NormalizeText(query);
            var queryTokens = GetSearchTokens(query);
            if (string.IsNullOrWhiteSpace(normalized) || !queryTokens.Any())
            {
                return new List<ProductMatchCandidate>();
            }

            var products = await _posDbService.GetAllProductsAsync();
            return products
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new
                {
                    Product = p,
                    NormalizedName = NormalizeText(p.Name!),
                    NameTokens = GetSearchTokens(p.Name!)
                })
                .Select(x => new ProductMatchCandidate
                {
                    Product = x.Product,
                    Score = ScoreProductMatch(normalized, queryTokens, x.NormalizedName, x.NameTokens),
                    IsExactMatch = x.NormalizedName.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Product.Name)
                .Take(limit)
                .ToList();
        }

        private async Task<ProductMatchResult> ResolveProductAsync(string query)
        {
            var candidates = await FindProductMatchesAsync(query, 20);
            if (!candidates.Any())
            {
                return new ProductMatchResult
                {
                    Reason = $"Produk \"{query}\" tidak ditemukan."
                };
            }

            var exactMatches = candidates.Where(candidate => candidate.IsExactMatch).ToList();
            if (exactMatches.Count == 1)
            {
                return new ProductMatchResult
                {
                    BestMatch = exactMatches[0].Product,
                    Candidates = candidates
                };
            }

            if (exactMatches.Count > 1)
            {
                return new ProductMatchResult
                {
                    BestMatch = exactMatches[0].Product,
                    Candidates = exactMatches,
                    IsAmbiguous = true,
                    Reason = "Ada beberapa produk dengan nama yang sama persis."
                };
            }

            var best = candidates[0];
            var second = candidates.Skip(1).FirstOrDefault();
            var queryTokens = GetSearchTokens(query);
            bool tooBroadSingleToken = queryTokens.Count <= 1 && candidates.Count > 1 && !best.IsExactMatch;
            bool closeRace = second != null && !best.IsExactMatch && best.Score - second.Score <= 8;
            if (tooBroadSingleToken || closeRace)
            {
                return new ProductMatchResult
                {
                    BestMatch = best.Product,
                    Candidates = candidates,
                    IsAmbiguous = true,
                    Reason = tooBroadSingleToken
                        ? "Kata kunci terlalu umum dan cocok ke banyak produk."
                        : "Ada beberapa kandidat dengan skor pencarian yang mirip."
                };
            }

            return new ProductMatchResult
            {
                BestMatch = best.Product,
                Candidates = candidates
            };
        }

        private async Task<ProductMatchResult> ResolveProductForReceiptAsync(string query, string? receiptUnit)
        {
            var candidates = await FindProductMatchesAsync(query, 20);
            if (!candidates.Any())
            {
                return new ProductMatchResult
                {
                    Reason = $"Produk \"{query}\" tidak ditemukan."
                };
            }

            string? normalizedReceiptUnit = NormalizeReceiptUnit(receiptUnit);
            if (string.IsNullOrWhiteSpace(normalizedReceiptUnit))
            {
                return await ResolveProductAsync(query);
            }

            candidates = candidates
                .Select(candidate => new ProductMatchCandidate
                {
                    Product = candidate.Product,
                    IsExactMatch = candidate.IsExactMatch,
                    Score = Math.Max(0, candidate.Score + ScoreReceiptUnitCompatibility(query, normalizedReceiptUnit, candidate.Product))
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Product.Name)
                .ToList();

            if (!candidates.Any())
            {
                return new ProductMatchResult
                {
                    Reason = $"Produk \"{query}\" tidak cocok dengan satuan struk \"{normalizedReceiptUnit}\"."
                };
            }

            var exactMatches = candidates.Where(candidate => candidate.IsExactMatch).ToList();
            if (exactMatches.Count == 1)
            {
                return new ProductMatchResult
                {
                    BestMatch = exactMatches[0].Product,
                    Candidates = candidates
                };
            }

            if (exactMatches.Count > 1)
            {
                return new ProductMatchResult
                {
                    BestMatch = exactMatches[0].Product,
                    Candidates = exactMatches,
                    IsAmbiguous = true,
                    Reason = "Ada beberapa produk dengan nama yang sama persis."
                };
            }

            var best = candidates[0];
            var second = candidates.Skip(1).FirstOrDefault();
            var queryTokens = GetSearchTokens(query);
            bool tooBroadSingleToken = queryTokens.Count <= 1 && candidates.Count > 1 && !best.IsExactMatch;
            bool closeRace = second != null && !best.IsExactMatch && best.Score - second.Score <= 8;
            if (tooBroadSingleToken || closeRace)
            {
                return new ProductMatchResult
                {
                    BestMatch = best.Product,
                    Candidates = candidates,
                    IsAmbiguous = true,
                    Reason = tooBroadSingleToken
                        ? "Kata kunci terlalu umum dan cocok ke banyak produk."
                        : "Ada beberapa kandidat dengan skor pencarian dan satuan yang mirip."
                };
            }

            return new ProductMatchResult
            {
                BestMatch = best.Product,
                Candidates = candidates
            };
        }

        private static int ScoreReceiptUnitCompatibility(string rawName, string receiptUnit, Product product)
        {
            bool receiptIsBulk = IsBulkReceiptUnit(receiptUnit);
            bool receiptIsChild = IsChildReceiptUnit(receiptUnit);
            bool productIsBulk = IsBulkReceiptUnit(product.Unit);
            bool productIsChild = IsChildReceiptUnit(product.Unit);
            bool sameUnit = !string.IsNullOrWhiteSpace(product.Unit) &&
                            string.Equals(NormalizeReceiptUnit(product.Unit), receiptUnit, StringComparison.OrdinalIgnoreCase);

            int score = 0;
            if (sameUnit)
            {
                score += 35;
            }
            else if (receiptIsBulk && productIsBulk)
            {
                score += 20;
            }
            else if (receiptIsChild && productIsChild)
            {
                score += 25;
            }

            if (receiptIsBulk && productIsChild)
            {
                score -= 35;
            }
            else if (receiptIsChild && productIsBulk)
            {
                score -= 40;
            }

            bool rawHasPackageMarker = HasPackageMarker(rawName);
            bool productHasPackageMarker = HasPackageMarker(product.Name);
            if (receiptIsBulk && rawHasPackageMarker && productHasPackageMarker)
            {
                score += 15;
            }
            else if (receiptIsChild && productHasPackageMarker)
            {
                score -= 20;
            }

            return score;
        }

        private static bool IsReceiptUnitCompatible(string? receiptUnit, Product? product)
        {
            if (product == null || string.IsNullOrWhiteSpace(receiptUnit))
            {
                return true;
            }

            string? normalizedReceiptUnit = NormalizeReceiptUnit(receiptUnit);
            if (string.IsNullOrWhiteSpace(normalizedReceiptUnit))
            {
                return true;
            }

            bool receiptIsBulk = IsBulkReceiptUnit(normalizedReceiptUnit);
            bool receiptIsChild = IsChildReceiptUnit(normalizedReceiptUnit);
            bool productIsBulk = IsBulkReceiptUnit(product.Unit);
            bool productIsChild = IsChildReceiptUnit(product.Unit);
            if (receiptIsBulk && productIsChild)
            {
                return false;
            }

            if (receiptIsChild && productIsBulk)
            {
                return false;
            }

            return true;
        }

        private static bool IsBulkReceiptUnit(string? unit)
        {
            string? normalized = NormalizeReceiptUnit(unit);
            return string.Equals(normalized, "Pak", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Box", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Dus", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Bal", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Bks", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChildReceiptUnit(string? unit)
        {
            string? normalized = NormalizeReceiptUnit(unit);
            return string.Equals(normalized, "Pcs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Rcg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Ecer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Satuan", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPackageMarker(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   Regex.IsMatch(value, @"\b(?:1\s*pk|1pk|pak|pack|box|dus|bal|bks)\b", RegexOptions.IgnoreCase);
        }

        private static bool RequiresExplicitOcrMapping(string rawName, string? receiptUnit)
        {
            return IsBulkReceiptUnit(receiptUnit) && HasPackageMarker(rawName);
        }

        private string BuildAmbiguousProductMatchMessage(string query, ProductMatchResult match, bool isMutation, string actionLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine(isMutation
                ? $"Produk \"{query}\" ambigu untuk {actionLabel}."
                : $"Pencarian produk \"{query}\" ambigu.");
            if (!string.IsNullOrWhiteSpace(match.Reason))
            {
                sb.AppendLine(match.Reason);
            }

            sb.AppendLine("Kandidat terdekat:");
            foreach (var candidate in match.Candidates.Take(3))
            {
                var product = candidate.Product;
                sb.AppendLine($"- {product.Name} | stok {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)} | jual {FormatCurrency(product.SellingPrice ?? 0)}");
            }

            sb.Append(isMutation
                ? "Perjelas nama produk lalu kirim ulang command."
                : $"Pilih salah satu nama produk di atas lalu ulangi permintaan {actionLabel}.");
            return sb.ToString();
        }

        private string BuildCriticalStockResponse(
            IEnumerable<Product> products,
            IReadOnlyCollection<ProductFamilyStock>? dualStockDeficits = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconSiren} STOK KRITIS");
            sb.AppendLine();
            var productList = products.ToList();
            if (productList.Any())
            {
                AppendStockAttentionLines(sb, productList, maxPerGroup: 10);
            }
            else
            {
                sb.AppendLine("  Tidak ada stok produk reguler yang kritis.");
            }

            if (dualStockDeficits?.Any() == true)
            {
                sb.AppendLine();
                sb.AppendLine("  DEFISIT DUAL STOK:");
                foreach (var family in dualStockDeficits.Take(10))
                {
                    sb.AppendLine($"    {IconWarning} {BuildDualStockFamilyCompactLine(family)}");
                }
            }

            sb.AppendLine();
            sb.Append("Gunakan /restock atau /inventory untuk memperbarui.");
            return sb.ToString();
        }

        private static void AppendStockAttentionLines(StringBuilder sb, IEnumerable<Product> products, int maxPerGroup)
        {
            var list = products.ToList();
            var oversold = list.Where(product => product.Stock.GetValueOrDefault() < 0).Take(maxPerGroup).ToList();
            var empty = list.Where(product => product.Stock.GetValueOrDefault() == 0).Take(maxPerGroup).ToList();
            var critical = list.Where(product => product.Stock.GetValueOrDefault() > 0 && product.Stock.GetValueOrDefault() <= 5).Take(maxPerGroup).ToList();

            if (oversold.Any())
            {
                sb.AppendLine("  OVERSOLD (jual melebihi stok):");
                foreach (var product in oversold)
                {
                    sb.AppendLine($"    {IconWarning} {FormatOptional(product.Name).PadRight(22)} {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)} -> cek opname/restock");
                }
            }

            if (empty.Any())
            {
                sb.AppendLine("  HABIS (stok = 0):");
                foreach (var product in empty)
                {
                    sb.AppendLine($"    {IconRed} {FormatOptional(product.Name).PadRight(22)} 0 {GetUnitLabel(product.Unit)}");
                }
            }

            if (critical.Any())
            {
                sb.AppendLine("  KRITIS (stok 1-5):");
                foreach (var product in critical)
                {
                    sb.AppendLine($"    {IconYellow} {FormatOptional(product.Name).PadRight(22)} {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}");
                }
            }
        }

        private static string BuildDocumentTypeGuideResponse()
        {
            return "Jenis dokumen:\n" +
                   "- 100: pembelian\n" +
                   "- 200: penjualan\n" +
                   "- 300: inventory\n" +
                   "Contoh: 26-100-000066 adalah dokumen pembelian.";
        }

        private static string? ExtractDocumentNumber(string text)
        {
            var match = DocumentNumberRegex.Match(text ?? string.Empty);
            return match.Success ? match.Value : null;
        }

        private static string? ExtractKeywordAfterAny(string text, params string[] markers)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string source = text.Trim();
            foreach (var marker in markers)
            {
                int index = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                string value = source[(index + marker.Length)..].Trim(' ', ':', '-', '?');
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string normalized = NormalizeText(value);
                if (normalized is "daftar" or "semua" or "yang ada")
                {
                    return null;
                }

                return value;
            }

            return null;
        }

        private static string? ExtractPurchaseHistoryProductKeyword(string text)
        {
            string? direct = ExtractKeywordAfterAny(
                text,
                "riwayat beli",
                "riwayat pembelian",
                "kapan terakhir beli",
                "history purchase",
                "riwayat restock",
                "history restock",
                "pas input /purchase",
                "input /purchase",
                "input purchase");

            string cleaned = direct ?? text;
            return CleanProductKeyword(cleaned,
                "cek", "cari", "lihat", "riwayat", "beli", "pembelian", "purchase", "history",
                "restock", "kapan", "terakhir", "dokumen", "di", "didokumen", "pas", "input", "nya");
        }

        private static string? ExtractExpiryProductKeyword(string text)
        {
            string normalizedText = NormalizeText(text);
            if (ContainsAny(normalizedText,
                "produk yang mempunyai tanggal expired",
                "produk yang punya tanggal expired",
                "produk yang ada tanggal expired",
                "produk expired",
                "cek produk expired",
                "cek expired",
                "tanggal kadaluarsa",
                "tanggal kedaluwarsa") &&
                !Regex.IsMatch(normalizedText, @"\b(?:ka|kapal|sedap|scorpion|abc|signature|mix)\b", RegexOptions.IgnoreCase))
            {
                return null;
            }

            string? direct = ExtractKeywordAfterAny(text, "expired", "kadaluarsa", "kedaluwarsa", "exp");
            string cleaned = direct ?? text;
            string? result = CleanProductKeyword(cleaned,
                "cek", "cari", "lihat", "ada", "tanggal", "expired", "kadaluarsa", "kedaluwarsa",
                "exp", "produk", "barang", "dengan", "yang", "punya", "nya", "ga", "nggak", "tidak");
            return string.IsNullOrWhiteSpace(result) || result.Length <= 2 ? null : result;
        }

        private static string ExtractCategoryKeyword(string text)
        {
            string normalized = NormalizeText(text);
            if (ContainsAny(normalized, "bumbu", "perbumbuan", "rempah"))
            {
                return "bumbu";
            }

            if (normalized.Contains("sembako", StringComparison.OrdinalIgnoreCase))
            {
                return "sembako";
            }

            if (ContainsAny(normalized, "rokok", "kretek"))
            {
                return "rokok";
            }

            if (ContainsAny(normalized, "minuman", "drink"))
            {
                return "minuman";
            }

            if (ContainsAny(normalized, "mie", "mi instant", "mie instant", "mi instan", "mie instan"))
            {
                return "mie";
            }

            if (ContainsAny(normalized, "obat", "obat nyamuk"))
            {
                return "obat";
            }

            if (ContainsAny(normalized, "kopi"))
            {
                return "kopi";
            }

            if (ContainsAny(normalized, "shampo", "sampo"))
            {
                return "shampo";
            }

            if (ContainsAny(normalized, "bayi"))
            {
                return "bayi";
            }

            if (ContainsAny(normalized, "permen"))
            {
                return "permen";
            }

            if (ContainsAny(normalized, "eskrim", "es krim"))
            {
                return "eskrim";
            }

            return text.Trim();
        }

        private static bool LooksLikeCategoryStockQuery(string normalized)
        {
            if (!ContainsAny(normalized,
                    "bumbu", "perbumbuan", "rempah", "rokok", "minuman", "mie", "mi instant",
                    "mie instant", "mi instan", "mie instan", "obat", "sembako", "kopi",
                    "shampo", "sampo", "bayi", "permen", "eskrim", "es krim"))
            {
                return false;
            }

            return ContainsAny(normalized,
                "stok", "stock", "produk", "barang", "tampilkan", "lihat", "daftar", "kategori", "apa saja");
        }

        private static string? CleanProductKeyword(string value, params string[] wordsToRemove)
        {
            string cleaned = value ?? string.Empty;
            cleaned = Regex.Replace(cleaned, @"/purchase|/riwayat_restock|/dokumen", " ", RegexOptions.IgnoreCase);
            foreach (string word in wordsToRemove)
            {
                cleaned = Regex.Replace(cleaned, $@"\b{Regex.Escape(word)}\b", " ", RegexOptions.IgnoreCase);
            }

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ':', '-', '?', '.', ',');
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        private async Task<string> BuildCustomerDetailResponseAsync(CustomerInfo customer, AutomationExecutionContext context)
        {
            if (_posDbService == null)
            {
                return "Database pos.db belum dikonfigurasi.";
            }

            string senderKey = BuildSenderStateKey(context);
            var documents = !string.IsNullOrWhiteSpace(customer.Id)
                ? await _posDbService.GetCustomerRecentDocumentsAsync(customer.Id, 50)
                : new List<CustomerDocumentSummary>();
            var favorites = !string.IsNullOrWhiteSpace(customer.Id)
                ? await _posDbService.GetCustomerFavoriteProductsAsync(customer.Id, 5)
                : new List<CustomerFavoriteProduct>();
            var receivables = !string.IsNullOrWhiteSpace(customer.Id)
                ? await _posDbService.GetCustomerReceivableDetailAsync(customer.Id)
                : new List<ReceivableInvoice>();

            const int pageSize = 5;
            var firstDocuments = documents.Take(pageSize).ToList();
            if (!string.IsNullOrWhiteSpace(customer.Id) && documents.Count > firstDocuments.Count)
            {
                _customerDocumentPaginationBySender[senderKey] = new CustomerDocumentPageState
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name ?? string.Empty,
                    NextOffset = firstDocuments.Count,
                    PageSize = pageSize
                };
            }
            else
            {
                _customerDocumentPaginationBySender.TryRemove(senderKey, out _);
            }

            _customerTxPaginationBySender.TryRemove(senderKey, out _);

            SetTopicState(
                context,
                "customer_detail",
                entityId: customer.Id,
                entityName: customer.Name,
                currentPage: 1,
                pageSize: pageSize,
                exportType: $"transaksi_{MakeSafeFileToken(customer.Name)}.csv",
                relatedDocumentNumbers: firstDocuments
                    .Select(document => document.DocumentNumber)
                    .Where(number => !string.IsNullOrWhiteSpace(number))
                    .Select(number => number!)
                    .ToList(),
                lastData: documents);

            var sb = new StringBuilder();
            sb.AppendLine($"{IconCustomer} DETAIL PELANGGAN - {FormatOptional(customer.Name)}");
            sb.AppendLine();
            sb.AppendLine($"{IconChart} Ringkasan");
            sb.AppendLine($"{IconReceipt} Total transaksi : {customer.PurchaseCount} nota");
            sb.AppendLine($"{IconMoney} Total belanja   : {FormatCurrency(customer.TotalSpent)}");
            sb.AppendLine($"{IconChart} Rata-rata/nota  : {FormatCurrency(customer.PurchaseCount > 0 ? customer.TotalSpent / customer.PurchaseCount : 0)}");
            sb.AppendLine($"{IconCalendar} Terakhir belanja: {FormatDateTime(customer.LastPurchaseDate)}");
            sb.AppendLine($"{IconTag} Status          : {BuildCustomerStatus(customer)}");
            if (receivables.Any())
            {
                sb.AppendLine($"\U0001F4B3 Piutang         : {FormatCurrency(receivables.Sum(item => item.OutstandingBalance))} ({receivables.Count} faktur)");
            }

            if (favorites.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconPackage} Produk Favorit");
                foreach (var favorite in favorites)
                {
                    sb.AppendLine($"- {FormatOptional(favorite.ProductName)}");
                }
            }

            if (firstDocuments.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconClipboard} Nota Terakhir");
                for (int i = 0; i < firstDocuments.Count; i++)
                {
                    var document = firstDocuments[i];
                    sb.AppendLine($"{i + 1}. {FormatShortDate(document.Date)} | {FormatCompactDocumentNumber(document.DocumentNumber)} | {FormatCurrency(document.Total)} | {document.ItemCount} item");
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("Belum ada nota penjualan untuk pelanggan ini.");
            }

            sb.AppendLine();
            sb.AppendLine("\U0001F4A1 Ketik:");
            string? firstShortNumber = firstDocuments.FirstOrDefault()?.DocumentNumber;
            if (!string.IsNullOrWhiteSpace(firstShortNumber))
            {
                sb.AppendLine($"- DETAIL NOTA {GetDocumentNumberSuffix(firstShortNumber)}");
            }
            if (documents.Count > firstDocuments.Count)
            {
                sb.AppendLine("- LANJUT NOTA");
            }
            sb.AppendLine("- PRODUK FAVORIT");
            sb.AppendLine("- PIUTANG PELANGGAN");
            sb.Append("- EKSPOR NOTA");

            return sb.ToString().TrimEnd();
        }

        private static string BuildSupplierDetailResponse(CustomerInfo supplier, string query)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\U0001F3ED SUPPLIER - \"{query}\"");
            sb.AppendLine();
            sb.AppendLine($"  {FormatOptional(supplier.Name)}");
            sb.AppendLine($"  {IconPhone} HP    : {FormatOptional(supplier.Phone)}");
            sb.AppendLine($"  {IconEmail} Email : {FormatOptional(supplier.Email)}");
            return sb.ToString().TrimEnd();
        }

        private static string BuildCustomerListLine(CustomerInfo customer, int? index)
        {
            string prefix = index.HasValue ? $"{index.Value}. " : "- ";
            string amount = customer.TotalSpent > 0 ? $" | {FormatCurrency(customer.TotalSpent)}" : string.Empty;
            return $"{prefix}{FormatOptional(customer.Name)} | {FormatOptional(customer.Phone)} | {customer.PurchaseCount} transaksi{amount}";
        }

        private static string BuildSupplierListLine(CustomerInfo supplier, int? index)
        {
            string prefix = index.HasValue ? $"{index.Value}. " : "- ";
            return $"{prefix}{FormatOptional(supplier.Name)} | {FormatOptional(supplier.Phone)} | {FormatOptional(supplier.Email)}";
        }

        private async Task<string> SendCsvDocumentAsync(
            AutomationExecutionContext context,
            string fileName,
            string content,
            string caption,
            string successMessage)
        {
            if (_documentSender == null)
            {
                return "Transport pengiriman file belum siap.";
            }

            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            try
            {
                await System.IO.File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(false));
                await SendDocumentForChannelAsync(context, tempPath, caption, "export_csv");
                ClearPendingExport(context);
                return successMessage;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Gagal mengirim file export: {ex.Message}",
                    "Export",
                    ex.ToString(),
                    context.Identity.SenderId);
                return $"Gagal mengirim file export: {ex.Message}";
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath))
                    {
                        System.IO.File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Abaikan kegagalan cleanup file temp.
                }
            }
        }

        private async Task SendDocumentForChannelAsync(
            AutomationExecutionContext context,
            string filePath,
            string caption,
            string eventName)
        {
            if (_documentSender == null)
            {
                throw new InvalidOperationException("Transport pengiriman file belum siap.");
            }

            if (context.Identity.Channel == ChannelType.WhatsApp)
            {
                throw new InvalidOperationException("Export file untuk WhatsApp Cloud belum aktif. Gunakan WhatsApp lokal Baileys atau Telegram.");
            }

            if (context.Identity.Channel != ChannelType.Telegram && context.Identity.Channel != ChannelType.Baileys)
            {
                throw new InvalidOperationException($"Export file belum didukung untuk channel {context.Identity.Channel}.");
            }

            var fileInfo = new FileInfo(filePath);
            const long whatsAppDocumentLimitBytes = 64L * 1024L * 1024L;
            if (context.Identity.Channel == ChannelType.Baileys && fileInfo.Length > whatsAppDocumentLimitBytes)
            {
                throw new InvalidOperationException("File terlalu besar untuk WhatsApp. Coba export per kategori/periode lebih pendek.");
            }

            string? externalId = await _documentSender(context.Identity.Channel, context.Identity.SenderId, filePath, caption);
            await _loggingService.LogInfoAsync(
                $"Dokumen export terkirim: channel={context.Identity.Channel}, file={fileInfo.Name}, size={fileInfo.Length}, event={eventName}, messageId={externalId ?? "-"}",
                "Export",
                userId: context.Identity.SenderId);
        }

        private void SetPendingExport(AutomationExecutionContext context, string kind, string? argument = null)
        {
            _pendingExportBySender[BuildSenderStateKey(context)] = new PendingExportRequest
            {
                Kind = kind,
                Argument = argument,
                CreatedAt = DateTime.Now
            };
        }

        private void ClearPendingExport(AutomationExecutionContext context)
        {
            _pendingExportBySender.TryRemove(BuildSenderStateKey(context), out _);
        }

        private void SetTopicState(
            AutomationExecutionContext context,
            string topic,
            string? entityId = null,
            string? entityName = null,
            int? currentPage = null,
            int? pageSize = null,
            string? exportType = null,
            string? lastDocumentNumber = null,
            string? customerId = null,
            string? customerName = null,
            List<string>? relatedDocumentNumbers = null,
            List<string>? candidateDocuments = null,
            object? lastData = null,
            DateTime? expiryDate = null,
            int? daysLeft = null,
            decimal? stock = null,
            string? unit = null)
        {
            _lastTopicBySender[BuildSenderStateKey(context)] = new TopicState
            {
                Topic = topic,
                TopicType = MapTopicType(topic),
                EntityId = entityId,
                EntityName = entityName,
                CurrentPage = currentPage,
                PageSize = pageSize ?? 5,
                ExportType = exportType,
                LastDocumentNumber = lastDocumentNumber,
                CustomerId = customerId,
                CustomerName = customerName,
                RelatedDocumentNumbers = relatedDocumentNumbers ?? new List<string>(),
                CandidateDocuments = candidateDocuments ?? new List<string>(),
                LastData = lastData,
                ExpiryDate = expiryDate,
                DaysLeft = daysLeft,
                Stock = stock,
                Unit = unit,
                ExpiresAt = DateTime.Now.AddMinutes(10)
            };
        }

        private static TopicType MapTopicType(string? topic)
        {
            return NormalizeText(topic ?? string.Empty) switch
            {
                "pelanggan loyal" or "pelanggan_loyal" or "loyalcustomers" => TopicType.LoyalCustomers,
                "pelanggan at risk" or "pelanggan_at_risk" or "atriskcustomers" => TopicType.AtRiskCustomers,
                "customer detail" or "customer_detail" => TopicType.CustomerDetail,
                "receivable list" or "receivable_list" => TopicType.ReceivableList,
                "receivable detail" or "receivable_detail" => TopicType.ReceivableDetail,
                "sales document detail" or "sales_document_detail" => TopicType.SalesDocumentDetail,
                "document pick pending" or "document_pick_pending" => TopicType.DocumentPickPending,
                "product detail" or "product_detail" => TopicType.ProductDetail,
                "set family pending" or "set_family_pending" => TopicType.SetFamilyPending,
                "expired" or "expired context" or "expired_context" => TopicType.ExpiredContext,
                _ => TopicType.None
            };
        }

        private TopicState? GetActiveTopicState(AutomationExecutionContext context)
        {
            string senderKey = BuildSenderStateKey(context);
            if (!_lastTopicBySender.TryGetValue(senderKey, out var state))
            {
                return null;
            }

            if (state.ExpiresAt <= DateTime.Now)
            {
                _lastTopicBySender.TryRemove(senderKey, out _);
                return null;
            }

            return state;
        }

        private bool HasPendingExport(AutomationExecutionContext context)
        {
            return _pendingExportBySender.ContainsKey(BuildSenderStateKey(context));
        }

        private string ResolveExportSalesPeriod(string? requestedPeriod, AutomationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(requestedPeriod) &&
                !string.Equals(requestedPeriod, "today", StringComparison.OrdinalIgnoreCase))
            {
                return requestedPeriod;
            }

            if (_pendingExportBySender.TryGetValue(BuildSenderStateKey(context), out var pending) &&
                string.Equals(pending.Kind, "sales", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pending.Argument))
            {
                return pending.Argument!;
            }

            return requestedPeriod ?? "today";
        }

        private static string BuildSenderStateKey(AutomationExecutionContext context)
        {
            return BuildSenderStateKey(context.Identity.Channel, context.Identity.SenderId);
        }

        private static string BuildSenderStateKey(ChannelType channel, string senderId)
        {
            string normalizedSenderId = channel == ChannelType.Telegram
                ? senderId.Trim()
                : NormalizeWhatsAppNumber(senderId);
            return $"{channel}:{normalizedSenderId}";
        }

        private static (DateTime StartDate, DateTime EndDate, string PeriodKey, string TitleLabel, string DateLabel) ResolveSalesPeriod(string? period)
        {
            DateTime today = DateTime.Today;
            string value = period?.Trim() ?? "today";

            if (string.Equals(value, "yesterday", StringComparison.OrdinalIgnoreCase))
            {
                DateTime date = today.AddDays(-1);
                return (date, date, "yesterday", "Kemarin", FormatDateRangeLabel(date, date));
            }

            if (string.Equals(value, "week", StringComparison.OrdinalIgnoreCase))
            {
                DateTime startOfWeek = GetStartOfWeek(today);
                return (startOfWeek, today, "week", "Minggu Ini", FormatDateRangeLabel(startOfWeek, today));
            }

            if (string.Equals(value, "month", StringComparison.OrdinalIgnoreCase))
            {
                DateTime startOfMonth = new(today.Year, today.Month, 1);
                return (startOfMonth, today, "month", "Bulan Ini", FormatDateRangeLabel(startOfMonth, today));
            }

            if (string.Equals(value, "last_week", StringComparison.OrdinalIgnoreCase))
            {
                DateTime currentWeekStart = GetStartOfWeek(today);
                DateTime start = currentWeekStart.AddDays(-7);
                DateTime end = currentWeekStart.AddDays(-1);
                return (start, end, "last_week", "Minggu Lalu", FormatDateRangeLabel(start, end));
            }

            if (string.Equals(value, "last_month", StringComparison.OrdinalIgnoreCase))
            {
                DateTime currentMonthStart = new(today.Year, today.Month, 1);
                DateTime start = currentMonthStart.AddMonths(-1);
                DateTime end = currentMonthStart.AddDays(-1);
                return (start, end, "last_month", "Bulan Lalu", FormatDateRangeLabel(start, end));
            }

            if (value.StartsWith("last_N_months:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value["last_N_months:".Length..], out int monthCount) &&
                monthCount > 0)
            {
                DateTime targetDate = today.AddMonths(-monthCount);
                DateTime start = new(targetDate.Year, targetDate.Month, 1);
                DateTime end = start.AddMonths(1).AddDays(-1);
                string title = $"{monthCount} Bulan Lalu ({FormatMonthYearIndonesian(start)})";
                return (start, end, $"month_{monthCount}_ago", title, FormatDateRangeLabel(start, end));
            }

            if (value.StartsWith("last_N_days:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value["last_N_days:".Length..], out int dayCount) &&
                dayCount > 0)
            {
                DateTime start = today.AddDays(-(dayCount - 1));
                return (start, today, $"last_{dayCount}_days", $"{dayCount} Hari Terakhir", FormatDateRangeLabel(start, today));
            }

            if (value.StartsWith("specific_day:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value["specific_day:".Length..], out int day) &&
                day >= 1 &&
                day <= 31)
            {
                DateTime date = ResolveRecentDayOfMonth(day, today);
                return (date, date, $"day_{date:yyyyMMdd}", FormatDateIndonesian(date), FormatDateRangeLabel(date, date));
            }

            if (value.StartsWith("specific_date:", StringComparison.OrdinalIgnoreCase) &&
                DateTime.TryParseExact(value["specific_date:".Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime specificDate))
            {
                return (specificDate, specificDate, $"date_{specificDate:yyyyMMdd}", FormatDateIndonesian(specificDate), FormatDateRangeLabel(specificDate, specificDate));
            }

            if (value.StartsWith("year:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value["year:".Length..], out int year) &&
                year >= 2000 && year <= 2100)
            {
                DateTime start = new(year, 1, 1);
                DateTime end = new(year, 12, 31);
                return (start, end, $"year_{year}", $"Tahun {year}", FormatDateRangeLabel(start, end));
            }

            if (value.StartsWith("month_name:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    IndonesianMonths.TryGetValue(parts[1], out int monthNumber))
                {
                    int monthYear = today.Year;
                    if (parts.Length >= 3)
                    {
                        _ = int.TryParse(parts[2], out monthYear);
                    }

                    DateTime start = new(monthYear, monthNumber, 1);
                    DateTime end = start.AddMonths(1).AddDays(-1);
                    string title = FormatMonthYearIndonesian(start);
                    return (start, end, $"month_{monthYear}_{monthNumber:00}", title, FormatDateRangeLabel(start, end));
                }
            }

            if (value.StartsWith("quarter:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 &&
                    int.TryParse(parts[1], out int quarter) &&
                    int.TryParse(parts[2], out int quarterYear) &&
                    quarter is >= 1 and <= 4)
                {
                    int startMonth = ((quarter - 1) * 3) + 1;
                    DateTime start = new(quarterYear, startMonth, 1);
                    DateTime end = start.AddMonths(3).AddDays(-1);
                    return (start, end, $"quarter_{quarterYear}_Q{quarter}", $"Q{quarter} {quarterYear}", FormatDateRangeLabel(start, end));
                }
            }

            if (value.StartsWith("range:", StringComparison.OrdinalIgnoreCase))
            {
                string raw = value["range:".Length..];
                string[] parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 &&
                    DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start) &&
                    DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end))
                {
                    if (end < start)
                    {
                        (start, end) = (end, start);
                    }

                    return (start, end, $"range_{start:yyyyMMdd}_{end:yyyyMMdd}", FormatDateRangeLabel(start, end), FormatDateRangeLabel(start, end));
                }
            }

            return (today, today, "today", "Hari Ini", FormatDateRangeLabel(today, today));
        }

        private static string BuildTopSellingLabel(ProductSalesData? item)
        {
            if (item == null)
            {
                return "Belum ada data";
            }

            return $"{FormatOptional(item.ProductName)} ({FormatDisplayQuantity(item.QuantitySold)} {GetUnitLabel(item.Unit)})";
        }

        private static bool IsIdentityQuestion(string text)
        {
            string normalized = NormalizeText(text);
            return ContainsAny(normalized, "namamu siapa", "siapa kamu", "kamu siapa", "nama bot", "kamu apa", "ssa itu apa");
        }

        private static bool IsCapabilityQuestion(string text)
        {
            return ContainsAny(text,
                "apa yang bisa kamu bantu",
                "kamu bisa bantu apa",
                "bisa bantu apa",
                "bisa bantu saya",
                "fitur apa saja",
                "fitur kamu apa",
                "bisa apa");
        }

        private string BuildBotIdentityResponse()
        {
            return $"{IconRobot} Saya Smart Sembako Assistant (SSA).\n\n" +
                   "Saya membantu pemilik toko untuk cek stok, laporan penjualan, data pelanggan, supplier, dan analisa toko.\n" +
                   "Ketik /help untuk daftar command lengkap.";
        }

        private static string BuildUserIdentityResponse(AutomationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.Identity.SenderName))
            {
                return $"Nama yang saya terima di channel ini: {context.Identity.SenderName}.";
            }

            return "Nama pengirim tidak tersedia di metadata channel ini.";
        }

        private static string FormatDateIndonesian(DateTime date)
        {
            return date.ToString("dd MMM yyyy", IndonesianCulture);
        }

        private static string FormatMonthYearIndonesian(DateTime date)
        {
            return IndonesianCulture.TextInfo.ToTitleCase(date.ToString("MMMM yyyy", IndonesianCulture));
        }

        private static bool ContainsAny(string text, params string[] phrases)
        {
            return phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAllKeyword(string normalized)
        {
            return normalized is "semua" or "all" or "seluruh" or "semuanya";
        }

        private static bool IsZeroCostExportAllKeyword(string normalized)
        {
            return ContainsAny(normalized,
                "ekspor tanpa modal semua", "export tanpa modal semua",
                "ekspor produk tanpa modal semua", "export produk tanpa modal semua");
        }

        private static bool IsZeroCostExportKeyword(string normalized)
        {
            return IsZeroCostExportAllKeyword(normalized) ||
                   ContainsAny(normalized,
                       "ekspor tanpa modal", "export tanpa modal",
                       "ekspor produk tanpa modal", "export produk tanpa modal",
                       "kirim tanpa modal");
        }

        private static bool ContainsCountKeyword(string normalized, string entityKeyword)
        {
            return normalized.Contains(entityKeyword, StringComparison.OrdinalIgnoreCase) &&
                   (normalized.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("jumlah", StringComparison.OrdinalIgnoreCase));
        }

        private static string? TryExtractSalesPeriodArgument(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string normalized = NormalizeText(text);
            DateTime today = DateTime.Today;

            string? parsedRange = TryParseIndonesianDateRange(text, today);
            if (parsedRange != null)
            {
                return parsedRange;
            }

            if (normalized.Contains("hari ini", StringComparison.OrdinalIgnoreCase))
            {
                return "today";
            }

            var monthRangeMatch = Regex.Match(normalized, @"\b(\d{1,2})\s*bulan\s*(?:lalu|yang lalu|kemarin)\b", RegexOptions.IgnoreCase);
            if (monthRangeMatch.Success && int.TryParse(monthRangeMatch.Groups[1].Value, out int monthCount) && monthCount > 0)
            {
                return $"last_N_months:{monthCount}";
            }

            if (normalized.Contains("bulan", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains("kemarin", StringComparison.OrdinalIgnoreCase))
            {
                return "last_month";
            }

            if (normalized.Contains("minggu ini", StringComparison.OrdinalIgnoreCase))
            {
                return "week";
            }

            if (normalized.Contains("bulan ini", StringComparison.OrdinalIgnoreCase))
            {
                return "month";
            }

            if (normalized.Contains("minggu lalu", StringComparison.OrdinalIgnoreCase))
            {
                return "last_week";
            }

            if (normalized.Contains("bulan lalu", StringComparison.OrdinalIgnoreCase))
            {
                return "last_month";
            }

            if (normalized.Contains("kemarin", StringComparison.OrdinalIgnoreCase))
            {
                return "yesterday";
            }

            if (normalized.Contains("tahun ini", StringComparison.OrdinalIgnoreCase))
            {
                return $"year:{today.Year}";
            }

            var dayRangeMatch = Regex.Match(normalized, @"\b(\d{1,3})\s+hari(?:\s+(?:terakhir|ini))?\b", RegexOptions.IgnoreCase);
            if (dayRangeMatch.Success && int.TryParse(dayRangeMatch.Groups[1].Value, out int days) && days > 0)
            {
                return $"last_N_days:{days}";
            }

            var fullDateMatch = Regex.Match(text, @"\b(\d{1,2}\s+[A-Za-z]+\s+\d{4})\b", RegexOptions.IgnoreCase);
            if (fullDateMatch.Success)
            {
                DateTime? date = ParseIndonesianDateExpression(fullDateMatch.Groups[1].Value, today);
                if (date.HasValue)
                {
                    return $"specific_date:{date.Value:yyyy-MM-dd}";
                }
            }

            var shortDateMatch = Regex.Match(text, @"\b(\d{1,2}\s+[A-Za-z]{3,9})\b", RegexOptions.IgnoreCase);
            if (shortDateMatch.Success)
            {
                DateTime? date = ParseIndonesianDateExpression(shortDateMatch.Groups[1].Value, today);
                if (date.HasValue)
                {
                    return $"specific_date:{date.Value:yyyy-MM-dd}";
                }
            }

            var specificDayMatch = Regex.Match(normalized, @"\b(?:tanggal|tgl)\s+(\d{1,2})\b", RegexOptions.IgnoreCase);
            if (specificDayMatch.Success && int.TryParse(specificDayMatch.Groups[1].Value, out int day) && day >= 1 && day <= 31)
            {
                return $"specific_day:{day}";
            }

            var quarterMatch = Regex.Match(normalized, @"\bq([1-4])\s*(20\d{2})\b", RegexOptions.IgnoreCase);
            if (quarterMatch.Success)
            {
                return $"quarter:{quarterMatch.Groups[1].Value}:{quarterMatch.Groups[2].Value}";
            }

            var yearMatch = Regex.Match(normalized, @"\btahun\s+(20\d{2})\b", RegexOptions.IgnoreCase);
            if (yearMatch.Success)
            {
                return $"year:{yearMatch.Groups[1].Value}";
            }

            foreach (var month in IndonesianMonths.Keys)
            {
                if (normalized.Contains(month, StringComparison.OrdinalIgnoreCase))
                {
                    var monthYearMatch = Regex.Match(normalized, $@"\b{month}\s+(20\d{{2}})\b", RegexOptions.IgnoreCase);
                    string year = monthYearMatch.Success ? monthYearMatch.Groups[1].Value : today.Year.ToString(CultureInfo.InvariantCulture);
                    return $"month_name:{month}:{year}";
                }
            }

            return null;
        }

        private static bool LooksLikeCustomerNameCandidate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = NormalizeText(value);
            return !ContainsAny(normalized, "hari ini", "kemarin", "minggu ini", "bulan ini", "semua", "daftar", "total", "jumlah");
        }

        private static bool LooksLikeSalesSummaryQuery(string normalized)
        {
            return ContainsAny(normalized,
                "penjualan",
                "omzet",
                "laporan penjualan",
                "data penjualan",
                "cek penjualan",
                "info penjualan");
        }

        private static bool LooksLikeStandaloneSalesPeriodQuery(string normalized)
        {
            return Regex.IsMatch(normalized, @"\b\d{1,2}\s+[a-z]{3,9}\s+\d{4}\s*(?:-|sampai|hingga|s\/d|sd|ke)\s+((\d{1,2}\s+[a-z]{3,9}\s+\d{4})|hari ini|sekarang|saat ini|kemarin)\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\b\d{1,2}\s+[a-z]{3,9}\s+\d{4}\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\b\d{1,2}\s+[a-z]{3,9}\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\b\d{1,2}\s*bulan\s*(?:lalu|yang lalu|kemarin)\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\b\d{1,3}\s+hari(?:\s+(?:terakhir|ini))?\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\b(?:tanggal|tgl)\s+\d{1,2}\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(normalized, @"\bq[1-4]\s*20\d{2}\b", RegexOptions.IgnoreCase);
        }

        private static string BuildExportMenuResponse()
        {
            return "Mau ekspor apa?\n• EKSPOR PELANGGAN\n• EKSPOR SUPPLIER\n• EKSPOR PENJUALAN\n• EKSPOR PRODUK\n• EKSPOR PIUTANG\n• EKSPOR LENGKAP";
        }

        private static DateTime GetStartOfWeek(DateTime date)
        {
            int daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-daysFromMonday);
        }

        private static string FormatDateRangeLabel(DateTime start, DateTime end)
        {
            return start.Date == end.Date
                ? FormatDateIndonesian(start)
                : $"{FormatDateIndonesian(start)} - {FormatDateIndonesian(end)}";
        }

        private static DateTime? ParseIndonesianDateExpression(string raw, DateTime referenceDate)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string normalized = NormalizeText(raw);
            if (normalized == "hari ini")
            {
                return referenceDate.Date;
            }

            if (normalized is "sekarang" or "saat ini")
            {
                return referenceDate.Date;
            }

            if (normalized == "kemarin")
            {
                return referenceDate.Date.AddDays(-1);
            }

            var match = Regex.Match(normalized, @"\b(\d{1,2})\s+([a-z]+)(?:\s+(20\d{2}))?\b", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            if (!int.TryParse(match.Groups[1].Value, out int day))
            {
                return null;
            }

            string monthName = match.Groups[2].Value;
            if (!IndonesianMonths.TryGetValue(monthName, out int month))
            {
                return null;
            }

            int year = referenceDate.Year;
            bool explicitYear = match.Groups[3].Success;
            if (explicitYear && !int.TryParse(match.Groups[3].Value, out year))
            {
                return null;
            }

            if (day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return null;
            }

            DateTime candidate = new(year, month, day);
            if (!explicitYear && candidate.Date > referenceDate.Date)
            {
                candidate = candidate.AddYears(-1);
            }

            return candidate;
        }

        private static string? TryParseIndonesianDateRange(string raw, DateTime referenceDate)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var rangeMatch = Regex.Match(
                raw,
                @"\b(\d{1,2}\s+[A-Za-z]+(?:\s+\d{4})?)\s*(?:-|sampai|hingga|s\/d|sd|ke)\s*((?:\d{1,2}\s+[A-Za-z]+(?:\s+\d{4})?)|hari ini|sekarang|saat ini|kemarin)\b",
                RegexOptions.IgnoreCase);

            if (!rangeMatch.Success)
            {
                return null;
            }

            DateTime? start = ParseIndonesianDateExpression(rangeMatch.Groups[1].Value, referenceDate);
            DateTime? end = ParseIndonesianDateExpression(rangeMatch.Groups[2].Value, referenceDate);
            if (!start.HasValue || !end.HasValue)
            {
                return null;
            }

            return $"range:{start.Value:yyyy-MM-dd}|{end.Value:yyyy-MM-dd}";
        }

        private static DateTime ResolveRecentDayOfMonth(int day, DateTime referenceDate)
        {
            int targetYear = referenceDate.Year;
            int targetMonth = referenceDate.Month;
            int daysInCurrentMonth = DateTime.DaysInMonth(targetYear, targetMonth);
            int clampedDay = Math.Min(day, daysInCurrentMonth);
            DateTime candidate = new(targetYear, targetMonth, clampedDay);
            if (candidate.Date <= referenceDate.Date)
            {
                return candidate;
            }

            DateTime previousMonth = new DateTime(referenceDate.Year, referenceDate.Month, 1).AddMonths(-1);
            int previousMonthDay = Math.Min(day, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month));
            return new DateTime(previousMonth.Year, previousMonth.Month, previousMonthDay);
        }

        private async Task<string> BuildDailySummaryAsync()
        {
            if (_posDbService == null)
            {
                return "Daily summary gagal: database belum tersedia.";
            }

            var revenue = await _posDbService.GetTodayRevenueAsync();
            var profit = await _posDbService.GetTodayProfitAsync();
            var lowStock = await _posDbService.GetLowStockProductsAsync(10);
            var lowStockLines = await BuildDualThresholdLowStockLinesAsync(lowStock.Take(10).ToList());

            var sb = new StringBuilder();
            sb.AppendLine($"Daily summary {DateTime.Now:dd/MM/yyyy}");
            sb.AppendLine($"Omzet: {FormatCurrency(revenue)}");
            sb.AppendLine($"Profit: {FormatCurrency(profit)}");
            sb.AppendLine($"Stok rendah: {lowStock.Count} produk");
            if (lowStockLines.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Alert stok:");
                foreach (string line in lowStockLines)
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<List<string>> BuildDualThresholdLowStockLinesAsync(List<Product> lowStock)
        {
            var lines = new List<string>();
            if (_posDbService == null || !lowStock.Any())
            {
                return lines;
            }

            var mappings = await _databaseService.GetAllUnitConversionsAsync();
            var productsById = (await _posDbService.GetAllProductsAsync())
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .GroupBy(product => product.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var product in lowStock)
            {
                var childMapping = mappings.FirstOrDefault(mapping =>
                    string.Equals(mapping.ChildProductId, product.Id, StringComparison.OrdinalIgnoreCase) &&
                    mapping.ConversionRate > 0);
                if (childMapping != null &&
                    productsById.TryGetValue(childMapping.ParentProductId, out var parent))
                {
                    decimal childStock = product.Stock ?? 0;
                    decimal parentStock = parent.Stock ?? 0;
                    decimal effectiveChild = parentStock * childMapping.ConversionRate + childStock;
                    if (effectiveChild > 10)
                    {
                        lines.Add($"- {FormatOptional(product.Name)}: {FormatStockValue(childStock)} {GetUnitLabel(product.Unit)} -> cukup repack/konversi dari {FormatOptional(parent.Name)} (efektif {FormatStockValue(effectiveChild)} {GetUnitLabel(product.Unit)})");
                        continue;
                    }
                }

                lines.Add($"- {FormatOptional(product.Name)}: {FormatStockValue(product.Stock ?? 0)} {GetUnitLabel(product.Unit)} -> segera cek/restock");
            }

            return lines;
        }

        private async Task<string?> BuildReceivableAlertAsync()
        {
            if (_posDbService == null)
            {
                return null;
            }

            var receivables = await _posDbService.GetCustomerReceivablesAsync();
            var overdue = receivables
                .Where(item => item.OldestDueDate.HasValue && item.OldestDueDate.Value.Date < DateTime.Today)
                .OrderBy(item => item.OldestDueDate)
                .Take(10)
                .ToList();

            if (!overdue.Any())
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} PIUTANG JATUH TEMPO");
            sb.AppendLine();
            foreach (var item in overdue)
            {
                sb.AppendLine($"- {item.CustomerName} | {FormatCurrency(item.TotalOwed)} | JT {FormatShortDate(item.OldestDueDate)}");
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string?> BuildExpiryAlertAsync()
        {
            if (_posDbService == null)
            {
                return null;
            }

            var expiring = await _posDbService.GetExpiringProductsAsync(7);
            if (!expiring.Any())
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconWarning} PRODUK MENDEKATI EXPIRY");
            sb.AppendLine();
            foreach (var product in expiring.Take(10))
            {
                sb.AppendLine($"- {FormatOptional(product.Name)} | exp {FormatDateTime(product.ExpiryDate)} | batch {FormatOptional(product.BatchNumber)}");
            }

            return sb.ToString().TrimEnd();
        }

        private List<OutboundMessage> BuildOwnerBroadcasts(string text, string triggerType)
        {
            var outputs = new List<OutboundMessage>();
            string correlationId = Guid.NewGuid().ToString("N");

            var telegramOwners = _configService.Config?.Telegram?.OwnerChatIds ?? new List<long>();
            foreach (var ownerId in telegramOwners)
            {
                outputs.Add(new OutboundMessage
                {
                    Channel = ChannelType.Telegram,
                    RecipientId = ownerId.ToString(CultureInfo.InvariantCulture),
                    Text = text,
                    CorrelationId = correlationId,
                    OutboundSourceType = "scheduled_alert"
                });
            }

            var waOwners = _configService.Config?.WhatsApp?.OwnerNumbers ?? new List<string>();
            if (IsWhatsAppCloudOutboundConfigured())
            {
                foreach (var number in waOwners)
                {
                    var templateOutbound = CreateWhatsAppTemplateOutbound(number, text, correlationId, triggerType);
                    if (templateOutbound != null)
                    {
                        outputs.Add(templateOutbound);
                    }
                }
            }

            var baileysOwners = _configService.Config?.Baileys?.OwnerNumbers ?? new List<string>();
            if (IsBaileysTransportConfigured())
            {
                foreach (var number in baileysOwners)
                {
                    outputs.Add(new OutboundMessage
                    {
                        Channel = ChannelType.Baileys,
                        RecipientId = NormalizeWhatsAppNumber(number),
                        Text = text,
                        CorrelationId = correlationId,
                        OutboundSourceType = "scheduled_alert"
                    });
                }
            }

            if (outputs.Any())
            {
                _ = _databaseService.AddAutomationExecutionAsync(new AutomationExecutionRecord
                {
                    CorrelationId = correlationId,
                    TriggerType = triggerType,
                    Channel = ChannelType.System.ToString(),
                    SenderId = "system",
                    UserRole = "System",
                    Status = "queued",
                    Details = $"Broadcast queued to {outputs.Count} owner recipient(s)."
                });
            }

            return outputs;
        }

        private List<OutboundMessage> BuildLowStockAlertBroadcasts(string text, string triggerType)
        {
            var outputs = new List<OutboundMessage>();
            string correlationId = Guid.NewGuid().ToString("N");
            var automation = _configService.Config?.Automation;

            if (automation?.EnableTelegramLowStockAlerts == true)
            {
                var telegramOwners = _configService.Config?.Telegram?.OwnerChatIds ?? new List<long>();
                foreach (var ownerId in telegramOwners)
                {
                    outputs.Add(new OutboundMessage
                    {
                        Channel = ChannelType.Telegram,
                        RecipientId = ownerId.ToString(CultureInfo.InvariantCulture),
                        Text = text,
                        CorrelationId = correlationId,
                        OutboundSourceType = "scheduled_alert"
                    });
                }
            }

            if (automation?.EnableWhatsAppCloudLowStockAlerts == true && IsWhatsAppCloudOutboundConfigured())
            {
                var waOwners = _configService.Config?.WhatsApp?.OwnerNumbers ?? new List<string>();
                foreach (var number in waOwners)
                {
                    var templateOutbound = CreateWhatsAppTemplateOutbound(number, text, correlationId, triggerType);
                    if (templateOutbound != null)
                    {
                        outputs.Add(templateOutbound);
                    }
                }
            }

            if (automation?.EnableBaileysLowStockAlerts == true && IsBaileysTransportConfigured())
            {
                var baileysOwners = _configService.Config?.Baileys?.OwnerNumbers ?? new List<string>();
                foreach (var number in baileysOwners)
                {
                    outputs.Add(new OutboundMessage
                    {
                        Channel = ChannelType.Baileys,
                        RecipientId = NormalizeWhatsAppNumber(number),
                        Text = text,
                        CorrelationId = correlationId,
                        OutboundSourceType = "scheduled_alert"
                    });
                }
            }

            if (outputs.Any())
            {
                _ = _databaseService.AddAutomationExecutionAsync(new AutomationExecutionRecord
                {
                    CorrelationId = correlationId,
                    TriggerType = triggerType,
                    Channel = ChannelType.System.ToString(),
                    SenderId = "system",
                    UserRole = "System",
                    Status = "queued",
                    Details = $"Low stock alert queued to {outputs.Count} recipient(s)."
                });
            }

            return outputs;
        }

        private List<OutboundMessage> BuildDualStockAlertBroadcasts(string text, string triggerType)
        {
            var outputs = new List<OutboundMessage>();
            string correlationId = Guid.NewGuid().ToString("N");
            var automation = _configService.Config?.Automation;

            if (automation?.EnableTelegramDualStockAlerts != false)
            {
                var telegramOwners = _configService.Config?.Telegram?.OwnerChatIds ?? new List<long>();
                foreach (var ownerId in telegramOwners)
                {
                    outputs.Add(new OutboundMessage
                    {
                        Channel = ChannelType.Telegram,
                        RecipientId = ownerId.ToString(CultureInfo.InvariantCulture),
                        Text = text,
                        CorrelationId = correlationId,
                        OutboundSourceType = "scheduled_alert"
                    });
                }
            }

            if (automation?.EnableWhatsAppCloudDualStockAlerts == true && IsWhatsAppCloudOutboundConfigured())
            {
                var waOwners = _configService.Config?.WhatsApp?.OwnerNumbers ?? new List<string>();
                foreach (var number in waOwners)
                {
                    var templateOutbound = CreateWhatsAppTemplateOutbound(number, text, correlationId, triggerType);
                    if (templateOutbound != null)
                    {
                        outputs.Add(templateOutbound);
                    }
                }
            }

            if (automation?.EnableBaileysDualStockAlerts != false && IsBaileysTransportConfigured())
            {
                var baileysOwners = _configService.Config?.Baileys?.OwnerNumbers ?? new List<string>();
                foreach (var number in baileysOwners)
                {
                    outputs.Add(new OutboundMessage
                    {
                        Channel = ChannelType.Baileys,
                        RecipientId = NormalizeWhatsAppNumber(number),
                        Text = text,
                        CorrelationId = correlationId,
                        OutboundSourceType = "scheduled_alert"
                    });
                }
            }

            if (outputs.Any())
            {
                _ = _databaseService.AddAutomationExecutionAsync(new AutomationExecutionRecord
                {
                    CorrelationId = correlationId,
                    TriggerType = triggerType,
                    Channel = ChannelType.System.ToString(),
                    SenderId = "system",
                    UserRole = "System",
                    Status = "queued",
                    Details = $"Dual stock alert queued to {outputs.Count} recipient(s)."
                });
            }

            return outputs;
        }

        private string GetConfirmationKey(ChannelType channel, string senderId)
        {
            return $"{channel}:{NormalizeWhatsAppNumber(senderId)}";
        }

        private static string BuildOwnerOnlyDeniedMessage()
        {
            return "Akses ditolak.";
        }

        private static string FormatOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value!;
        }

        private static string FormatDateTime(DateTime? value, bool includeTime = false)
        {
            if (!value.HasValue)
            {
                return "-";
            }

            return includeTime
                ? value.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                : value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static string NormalizeText(string value)
        {
            // Strip tanda baca di awal/akhir token agar 'Antaka.' bisa match 'ANTAKA'
            return string.Join(" ", value
                .ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim('.', ',', ':', ';', '?', '!', '(', ')', '[', ']', '"', '\''))
                .Where(token => !string.IsNullOrWhiteSpace(token)));
        }

        private static List<string> GetSearchTokens(string value)
        {
            return value
                .ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ':', ';', '/', '\\', '-', '_', '?', '!', '(', ')', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length >= 2)
                .Where(token => !SearchStopWords.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int ScoreProductMatch(string normalizedQuery, IReadOnlyCollection<string> queryTokens, string normalizedName, IReadOnlyCollection<string> nameTokens)
        {
            int overlap = queryTokens.Count(token => nameTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            if (overlap == 0 && !normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            int score = overlap * 10;
            if (normalizedName.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            else if (normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }

            string firstToken = queryTokens.FirstOrDefault() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(firstToken) &&
                nameTokens.Contains(firstToken, StringComparer.OrdinalIgnoreCase))
            {
                score += 5;
            }

            return score;
        }

        public static string NormalizeWhatsAppNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Split('@')[0].Split(':')[0];
            var chars = normalized.Where(char.IsDigit).ToArray();
            return new string(chars);
        }

        private static bool TryParseDecimal(string input, out decimal value)
        {
            return decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
                   decimal.TryParse(input, out value);
        }

        private static bool LooksLikeOperationalMutationRequest(string message)
        {
            string normalized = NormalizeText(message);
            return normalized.StartsWith("restock ", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("inventory ", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("quick inventory ", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("quick_inventory ", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSlashGuidance(string message)
        {
            string normalized = NormalizeText(message);
            if (normalized.StartsWith("restock ", StringComparison.OrdinalIgnoreCase))
            {
                return "Untuk aksi restock, gunakan command /restock <produk> <qty> [harga_modal].";
            }

            return "Untuk koreksi stok, gunakan command /inventory <produk> <stok_target>. Angka yang Anda kirim adalah stok akhir, bukan stok tambahan.";
        }

        private bool IsWhatsAppCloudOutboundConfigured()
        {
            string mode = WhatsAppModes.Normalize(_configService.Config?.WhatsApp?.Mode);
            return _configService.Config?.WhatsApp?.Enabled == true &&
                   WhatsAppModes.UsesCloudApi(mode) &&
                   !string.IsNullOrWhiteSpace(_configService.Config?.WhatsApp?.AccessToken) &&
                   !string.IsNullOrWhiteSpace(_configService.Config?.WhatsApp?.PhoneNumberId);
        }

        private OutboundMessage? CreateWhatsAppTemplateOutbound(string number, string text, string correlationId, string triggerType)
        {
            var settings = _configService.Config?.WhatsApp;
            if (settings?.EnableTemplateMessages != true)
            {
                return null;
            }

            var mapping = ResolveWhatsAppTemplateMapping(settings, triggerType);
            if (mapping == null)
            {
                return null;
            }

            return new OutboundMessage
            {
                Channel = ChannelType.WhatsApp,
                RecipientId = NormalizeWhatsAppNumber(number),
                Text = text,
                CorrelationId = correlationId,
                MessageKind = "template",
                TemplateName = mapping.TemplateName,
                TemplateLanguageCode = string.IsNullOrWhiteSpace(mapping.LanguageCode)
                    ? settings.DefaultTemplateLanguageCode ?? "id"
                    : mapping.LanguageCode,
                TemplateBodyParameterCount = Math.Max(0, mapping.BodyParameterCount),
                OutboundSourceType = "scheduled_alert"
            };
        }

        private static WhatsAppTemplateMapping? ResolveWhatsAppTemplateMapping(WhatsAppSettings settings, string triggerType)
        {
            if (settings.TemplateMappings == null)
            {
                return null;
            }

            var candidateKeys = GetWhatsAppTemplateCandidateKeys(triggerType).ToList();
            return settings.TemplateMappings
                .FirstOrDefault(mapping =>
                    candidateKeys.Any(key => string.Equals(mapping.Key, key, StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(mapping.TemplateName));
        }

        private static IEnumerable<string> GetWhatsAppTemplateCandidateKeys(string triggerType)
        {
            yield return triggerType;

            if (string.Equals(triggerType, "StockAlert", StringComparison.OrdinalIgnoreCase))
            {
                yield return "low_stock";
                yield return "ssa_low_stock_alert";
            }
            else if (string.Equals(triggerType, "Schedule", StringComparison.OrdinalIgnoreCase))
            {
                yield return "daily_summary";
                yield return "ssa_daily_summary";
            }
            else if (string.Equals(triggerType, "ReceivableAlert", StringComparison.OrdinalIgnoreCase))
            {
                yield return "receivable";
                yield return "ssa_receivable_alert";
            }
            else if (string.Equals(triggerType, "ExpiryAlert", StringComparison.OrdinalIgnoreCase))
            {
                yield return "expiry";
                yield return "ssa_expiry_alert";
            }
            else if (string.Equals(triggerType, "AnomalyAlert", StringComparison.OrdinalIgnoreCase))
            {
                yield return "anomaly";
                yield return "ssa_anomaly_alert";
            }
        }

        private bool IsBaileysTransportConfigured()
        {
            string mode = WhatsAppModes.Normalize(_configService.Config?.WhatsApp?.Mode);
            return _configService.Config?.Baileys?.Enabled == true &&
                   WhatsAppModes.UsesBaileys(mode) &&
                   !string.IsNullOrWhiteSpace(_configService.Config?.Baileys?.BotPhoneNumber) &&
                   !string.IsNullOrWhiteSpace(_configService.Config?.Baileys?.NodeBinaryPath) &&
                   !string.IsNullOrWhiteSpace(_configService.Config?.Baileys?.SidecarEntryPath) &&
                   !string.IsNullOrWhiteSpace(_configService.Config?.Baileys?.SessionPath);
        }

        private static string BuildWhatsAppActionHint(bool cloudEnabled, bool cloudOutboundReady, bool baileysEnabled, bool baileysConfigured)
        {
            if (!cloudEnabled && !baileysEnabled)
            {
                return "WhatsApp nonaktif.";
            }

            if (cloudEnabled && !cloudOutboundReady)
            {
                return "Cloud API belum siap kirim. Lengkapi Access Token dan Phone Number ID.";
            }

            if (baileysEnabled && !baileysConfigured)
            {
                return "Baileys belum siap. Lengkapi nomor bot, Node, sidecar, dan session path.";
            }

            if (cloudEnabled)
            {
                return "WhatsApp Cloud API siap kirim.";
            }

            return "WhatsApp memakai jalur Baileys.";
        }

        private static string GetStockIndicator(decimal? stock)
        {
            if (!stock.HasValue || stock.Value <= 0)
            {
                return IconRed;
            }

            return stock.Value <= 10 ? IconYellow : IconGreen;
        }

        private string? BuildWebhookUrl(string? tunnelPublicUrl)
        {
            return BuildPublicWebhookUrl(tunnelPublicUrl) ??
                   $"http://localhost:{_configService.Config?.WhatsApp?.LocalWebhookPort ?? 8090}/whatsapp/webhook";
        }

        private string? BuildPublicWebhookUrl(string? tunnelPublicUrl)
        {
            return BuildWebhookUrlFromBase(tunnelPublicUrl) ??
                   BuildWebhookUrlFromBase(_configService.Config?.WhatsApp?.PublicWebhookUrl);
        }

        private static string? BuildWebhookUrlFromBase(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string normalized = value.Trim().TrimEnd('/');
            if (normalized.EndsWith("/whatsapp/webhook", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return $"{normalized}/whatsapp/webhook";
        }

        private IEnumerable<AutomationRule> GetMatchingRules(
            string triggerType,
            AutomationExecutionContext context,
            InboundMessage? message,
            decimal? stockLevel)
        {
            var rules = _configService.Config?.Automation?.Rules ?? new List<AutomationRule>();

            return rules
                .Where(r => r.Enabled)
                .Where(r => string.Equals(r.TriggerType, triggerType, StringComparison.OrdinalIgnoreCase))
                .Where(r => RuleMatches(r, context, message, stockLevel))
                .OrderByDescending(r => r.Priority);
        }

        private bool RuleMatches(
            AutomationRule rule,
            AutomationExecutionContext context,
            InboundMessage? message,
            decimal? stockLevel)
        {
            if (rule.Conditions == null || !rule.Conditions.Any())
            {
                return true;
            }

            foreach (var condition in rule.Conditions)
            {
                if (!string.IsNullOrWhiteSpace(condition.Channel) &&
                    !string.Equals(condition.Channel, context.Identity.Channel.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(condition.UserRole) &&
                    !string.Equals(condition.UserRole, context.UserRole, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (condition.RequiresAuthorization == true && !context.IsAuthorized)
                {
                    return false;
                }

                if (condition.RequiresCommand == true && (message == null || !message.Text.StartsWith("/", StringComparison.Ordinal)))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(condition.Command) &&
                    (message == null || !message.Text.StartsWith(condition.Command, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (condition.MinimumStockLevel.HasValue &&
                    (!stockLevel.HasValue || stockLevel.Value < condition.MinimumStockLevel.Value))
                {
                    return false;
                }

                if (condition.MaximumStockLevel.HasValue &&
                    (!stockLevel.HasValue || stockLevel.Value > condition.MaximumStockLevel.Value))
                {
                    return false;
                }

                if (!IsWithinBusinessHours(condition.BusinessHoursStart, condition.BusinessHoursEnd))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsWithinBusinessHours(string? start, string? end)
        {
            if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            {
                return true;
            }

            if (!TimeSpan.TryParse(start, out var startTime) || !TimeSpan.TryParse(end, out var endTime))
            {
                return true;
            }

            var now = DateTime.Now.TimeOfDay;
            return startTime <= endTime
                ? now >= startTime && now <= endTime
                : now >= startTime || now <= endTime;
        }

        private bool ShouldExecuteBackgroundTrigger(string triggerType, decimal? stockLevel, out string matchedRules)
        {
            var systemContext = new AutomationExecutionContext
            {
                Identity = new ChannelIdentity
                {
                    Channel = ChannelType.System,
                    SenderId = "system",
                    SenderName = "system"
                },
                UserRole = "System",
                IsAuthorized = true,
                TriggerType = triggerType,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.Now
            };

            var rules = GetMatchingRules(triggerType, systemContext, null, stockLevel).ToList();
            matchedRules = string.Join(", ", rules.Select(GetRuleName));

            return !(_configService.Config?.Automation?.EnableTemplates == true) || !(_configService.Config?.Automation?.Rules?.Any(r => string.Equals(r.TriggerType, triggerType, StringComparison.OrdinalIgnoreCase)) == true) || rules.Any();
        }

        private static bool HasRuleAction(IEnumerable<AutomationRule> rules, string actionType)
        {
            return rules.SelectMany(r => r.Actions ?? Enumerable.Empty<AutomationRuleAction>())
                .Any(a => string.Equals(a.Type, actionType, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetRuleName(AutomationRule rule)
        {
            return string.IsNullOrWhiteSpace(rule.Description) ? rule.TriggerType : rule.Description!;
        }

        private OutboundMessage CreateOutboundMessage(InboundMessage inbound, string text, string correlationId)
        {
            return new OutboundMessage
            {
                Channel = inbound.Channel,
                RecipientId = inbound.SenderId,
                Text = text,
                RequiresConfirmation = NeedsConfirmation(text),
                MenuKeyboardType = ResolveMenuKeyboardType(inbound),
                CorrelationId = correlationId,
                AppInstanceId = CurrentAppInstanceId,
                SourceInboundMessageId = inbound.MessageId,
                SourceInboundReceivedAt = inbound.Timestamp == default ? DateTime.Now : inbound.Timestamp,
                OutboundSourceType = "inbound_reply"
            };
        }

        private static string? ResolveMenuKeyboardType(InboundMessage inbound)
        {
            string text = (inbound.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string[] parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();
            string args = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : string.Empty;

            return command switch
            {
                "/start" => "start",
                "/menu" => "main",
                "/help" when string.IsNullOrWhiteSpace(args) => "help",
                _ => null
            };
        }

        private static bool NeedsConfirmation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.Contains("/confirm", StringComparison.OrdinalIgnoreCase) &&
                   text.Contains("/cancel", StringComparison.OrdinalIgnoreCase);

            return text.StartsWith("Konfirmasi restock:", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("Konfirmasi inventory:", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("Konfirmasi bulk restock:", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("Konfirmasi bulk inventory:", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("📦 KONFIRMASI INVENTORY", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("📦 KONFIRMASI INVENTORY BULK", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildInventoryConfirmationMessage(
            string productName,
            string? unit,
            decimal currentStock,
            decimal targetStock,
            decimal adjustment)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconInventory} KONFIRMASI INVENTORY");
            sb.AppendLine();
            sb.AppendLine($"  Produk : {productName}");
            sb.AppendLine($"  Dari   : {FormatStockValue(currentStock)} {GetUnitLabel(unit)}");
            sb.AppendLine($"  Ke     : {FormatStockValue(targetStock)} {GetUnitLabel(unit)}");
            sb.AppendLine($"  Selisih: {FormatSignedStockValue(adjustment)} {GetUnitLabel(unit)}");
            sb.AppendLine();
            sb.Append(BuildConfirmationActions());
            return sb.ToString();
        }

        private static string BuildInventorySuccessMessage(
            string? documentNumber,
            string productName,
            string? unit,
            decimal oldStock,
            decimal newStock,
            decimal adjustment)
        {
            var modern = new StringBuilder();
            modern.AppendLine($"{IconCheck} INVENTORY SELESAI");
            modern.AppendLine();
            modern.AppendLine($"  {IconDocument} Dokumen : {FormatOptional(documentNumber)}");
            modern.AppendLine($"  {IconPackage} Produk  : {productName}");
            modern.AppendLine($"  {IconInventory} Stok    : {FormatStockValue(oldStock)} -> {FormatStockValue(newStock)} {GetUnitLabel(unit)}");
            modern.AppendLine($"  {IconChart} Selisih : {FormatSignedStockValue(adjustment)} {GetUnitLabel(unit)}");
            return modern.ToString();

            var sb = new StringBuilder();
            sb.AppendLine("✅ INVENTORY BERHASIL");
            sb.AppendLine();
            sb.AppendLine("📦 Detail:");
            sb.AppendLine($"• Dokumen: {documentNumber}");
            sb.AppendLine($"• Produk: {productName}");
            sb.AppendLine($"• Stok sebelumnya: {FormatStockValue(oldStock)} {GetUnitLabel(unit)}");
            sb.AppendLine($"• Stok akhir: {FormatStockValue(newStock)} {GetUnitLabel(unit)}");
            sb.AppendLine($"• Perubahan: {FormatSignedStockValue(adjustment)} {GetUnitLabel(unit)}");
            sb.AppendLine();
            sb.Append("📝 Stok telah dikoreksi sesuai target akhir.");
            return sb.ToString();
        }

        private static bool HasInventoryAnomalyWarning(decimal currentStock, decimal targetStock, decimal adjustment)
        {
            decimal absoluteAdjustment = Math.Abs(adjustment);
            if (absoluteAdjustment >= InventoryLargeAdjustmentThreshold)
            {
                return true;
            }

            if (currentStock > 0 && targetStock > currentStock * InventorySpikeMultiplier)
            {
                return true;
            }

            return false;
        }

        private static string FormatStockValue(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatDisplayQuantity(decimal value)
        {
            if (decimal.Truncate(value) == value)
            {
                return value.ToString("#,0", CultureInfo.InvariantCulture);
            }

            return value.ToString("#,0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatSignedStockValue(decimal value)
        {
            return value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
        }

        private static string FormatCurrency(decimal value)
        {
            return $"Rp {value.ToString("N0", IndonesianCulture)}";
        }

        private static bool IsGenericCustomerName(string? customerName)
        {
            string normalized = NormalizeText(customerName ?? string.Empty);
            return normalized is "umum" or "walk in customer" or "walk-in customer" or "walkin customer";
        }

        private static int GetDaysSince(DateTime? date)
        {
            return date.HasValue
                ? Math.Max(0, (int)(DateTime.Today - date.Value.Date).TotalDays)
                : int.MaxValue;
        }

        private static string GetAtRiskIcon(int days)
        {
            if (days > 120)
            {
                return IconRed;
            }

            if (days > 60)
            {
                return "\U0001F7E0";
            }

            return IconYellow;
        }

        private static string BuildCustomerStatus(CustomerInfo customer)
        {
            int days = GetDaysSince(customer.LastPurchaseDate);
            if (days > 120)
            {
                return "Hampir hilang";
            }

            if (days > 60)
            {
                return "Risiko tinggi";
            }

            if (days > 30)
            {
                return "Mulai jarang";
            }

            return customer.PurchaseCount >= 8 || customer.TotalSpent >= 5_000_000m
                ? "Loyal aktif"
                : "Aktif";
        }

        private static string NormalizeShortDocumentNumber(string value)
        {
            string digits = Regex.Replace(value ?? string.Empty, @"\D", string.Empty);
            if (digits.Length >= 6)
            {
                return digits[^6..];
            }

            return digits.PadLeft(6, '0');
        }

        private static string GetDocumentNumberSuffix(string? documentNumber)
        {
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                return string.Empty;
            }

            string raw = documentNumber.Trim();
            string[] parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries);
            string last = parts.LastOrDefault() ?? raw;
            return Regex.IsMatch(last, @"^\d+$") ? last : NormalizeShortDocumentNumber(raw);
        }

        private static string? ResolveRelatedSalesDocument(string suffix, TopicState? topic)
        {
            if (topic == null)
            {
                return null;
            }

            var related = new List<string>();
            if (!string.IsNullOrWhiteSpace(topic.LastDocumentNumber))
            {
                related.Add(topic.LastDocumentNumber);
            }

            related.AddRange(topic.RelatedDocumentNumbers);
            return related
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .FirstOrDefault(number =>
                    number.Contains("-200-", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(GetDocumentNumberSuffix(number), suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildPurchaseFoundForSalesNoteResponse(string documentNumber, string suffix)
        {
            return $"{IconWarning} Nota {suffix} tidak ditemukan sebagai transaksi penjualan.\n\n" +
                   $"Saya menemukan dokumen {FormatOptional(documentNumber)}, tapi DETAIL NOTA hanya untuk penjualan.\n\n" +
                   $"Untuk membukanya, ketik:\n/dokumen {documentNumber}";
        }

        private static string BuildSalesNoteNotFoundResponse(string suffix, DocumentInfo? nonSalesDocument)
        {
            if (nonSalesDocument != null && !string.IsNullOrWhiteSpace(nonSalesDocument.Number))
            {
                return BuildPurchaseFoundForSalesNoteResponse(nonSalesDocument.Number!, suffix);
            }

            return $"{IconWarning} Nota {suffix} tidak ditemukan.\n\n" +
                   "Kemungkinan:\n" +
                   "- Nomor lengkap berbeda, misal 26-200-" + suffix + "\n" +
                   "- Nota ini dokumen pembelian\n" +
                   "- Tahun/periode berbeda\n\n" +
                   "Coba:\n" +
                   "- /dokumen 26-200-" + suffix + "\n" +
                   "- /pelanggan [nama]\n" +
                   "- /piutang [nama]";
        }

        private static void AppendSalesItemRows(StringBuilder sb, IReadOnlyList<DocumentItemInfo> items, int startNumber)
        {
            AppendAlignedRows(
                sb,
                items.Select((item, index) => (
                    Name: $"{startNumber + index}. {FormatOptional(item.ProductName)}",
                    Col2: FormatCurrency(item.Total > 0 ? item.Total : item.Price * item.Quantity),
                    Col3: string.Empty,
                    Col4: string.Empty)));
        }

        private static string MakeSafeFileToken(string? value)
        {
            string normalized = NormalizeText(value ?? string.Empty);
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "_", RegexOptions.IgnoreCase).Trim('_');
            return string.IsNullOrWhiteSpace(normalized) ? "data" : normalized.ToLowerInvariant();
        }

        private static string FormatShortDate(DateTime? value)
        {
            return value?.ToString("dd/MM", CultureInfo.InvariantCulture) ?? "-";
        }

        private static string FormatCompactDocumentNumber(string? documentNumber)
        {
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                return "#-";
            }

            string raw = documentNumber.Trim();
            string[] parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries);
            string lastPart = parts.LastOrDefault() ?? raw;
            return lastPart.All(char.IsDigit) ? $"#{lastPart}" : raw;
        }

        private static string ShortenCounterparty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Contains("walk-in", StringComparison.OrdinalIgnoreCase)
                ? "Walk-in"
                : value.Trim();
        }

        private static string BuildConfirmationActions()
        {
            return $"{IconCheck} /confirm  |  {IconCross} /cancel";
        }

        private static string BuildOcrSessionProgressMessage(int pageNumber, int newItemCount, int totalItemCount)
        {
            return $"{IconDocument} Hal {pageNumber} - {newItemCount} item baru. Total: {totalItemCount} item. Lanjut atau /selesai_struk";
        }

        private static string BuildStockSearchLine(Product product)
        {
            string productName = TruncateWithEllipsis(FormatOptional(product.Name), 22);
            return $"  {GetStockIndicator(product.Stock)} {productName.PadRight(22)} {FormatDisplayQuantity(product.Stock ?? 0)} {GetUnitLabel(product.Unit)}  {FormatCurrency(product.SellingPrice ?? 0)}";
        }

        private static string BuildRestockConfirmationMessage(string productName, string? unit, decimal quantity, decimal price)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} KONFIRMASI RESTOCK");
            sb.AppendLine();
            sb.AppendLine($"  Produk : {productName}");
            sb.AppendLine($"  Qty    : {FormatStockValue(quantity)} {GetUnitLabel(unit)}");
            sb.AppendLine($"  Modal  : {FormatCurrency(price)}/{GetUnitLabel(unit).ToLowerInvariant()}");
            sb.AppendLine($"  Total  : {FormatCurrency(quantity * price)}");
            sb.AppendLine();
            sb.Append(BuildConfirmationActions());
            return sb.ToString();
        }

        private static string BuildRestockSuccessMessage(string? documentNumber, string productName, string? unit, decimal quantity, decimal total)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconCheck} RESTOCK BERHASIL");
            sb.AppendLine();
            sb.AppendLine($"  {IconDocument} Dokumen : {FormatOptional(documentNumber)}");
            sb.AppendLine($"  {IconPackage} Produk  : {productName}");
            sb.AppendLine($"  {IconChart} Qty     : {FormatStockValue(quantity)} {GetUnitLabel(unit)}");
            sb.AppendLine($"  {IconMoney} Total   : {FormatCurrency(total)}");
            return sb.ToString();
        }

        private static string BuildBulkRestockConfirmationMessage(
            List<BulkPendingItem> items,
            List<string> warnings)
        {
            decimal estimateTotal = items.Sum(item => item.Quantity * (item.Price ?? 0));
            var sb = new StringBuilder();
            sb.AppendLine($"{IconPackage} BULK RESTOCK - 1 DOKUMEN ({items.Count} item)");
            sb.AppendLine();
            AppendAlignedRows(
                sb,
                items.Select(item => (
                    Name: item.ProductName,
                    Col2: $"{FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)}",
                    Col3: $"@ {FormatCurrency(item.Price ?? 0)}",
                    Col4: string.Empty)));
            sb.AppendLine();
            sb.AppendLine($"{IconMoney} Estimasi total: {FormatCurrency(estimateTotal)}");

            if (warnings.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconWarning} Dilewati (perlu perjelas):");
                foreach (var warning in warnings.Take(10))
                {
                    sb.AppendLine($"  - {SanitizeWarning(warning)}");
                }
            }

            sb.AppendLine();
            sb.Append(BuildConfirmationActions());
            return sb.ToString();
        }

        private static string BuildBulkRestockSuccessMessage(string? documentNumber, List<BulkDocumentItemResult> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconCheck} BULK RESTOCK SELESAI");
            sb.AppendLine();
            sb.AppendLine($"  {IconDocument} Dokumen : {FormatOptional(documentNumber)}");
            sb.AppendLine($"  {IconCheck} Berhasil: {items.Count}/{items.Count} produk");
            sb.AppendLine();
            AppendAlignedRows(
                sb,
                items.Take(10).Select(item => (
                    Name: item.ProductName,
                    Col2: $"{FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)}",
                    Col3: string.Empty,
                    Col4: string.Empty)));
            return sb.ToString().TrimEnd();
        }

        private static string BuildBulkInventoryConfirmationMessage(
            List<BulkPendingItem> items,
            List<string> skippedSameStock,
            List<string> warnings)
        {
            var rows = new List<(string Name, string Col2, string Col3, string Col4)>();
            foreach (var item in items)
            {
                decimal currentStock = item.CurrentStock ?? 0;
                decimal adjustment = item.Quantity - currentStock;
                rows.Add((
                    item.ProductName,
                    FormatStockValue(currentStock),
                    $"{FormatStockValue(item.Quantity)} {GetUnitLabel(item.Unit)}",
                    $"({FormatSignedStockValue(adjustment)})"));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{IconInventory} BULK INVENTORY - 1 DOKUMEN ({items.Count} item)");
            sb.AppendLine();
            sb.AppendLine("Produk akan di-SET ke stok berikut:");
            sb.AppendLine();
            AppendAlignedRows(
                sb,
                rows.Select(row => (
                    row.Name,
                    row.Col2,
                    $"\u2192 {row.Col3}",
                    row.Col4)));

            if (skippedSameStock.Any())
            {
                sb.AppendLine();
                sb.AppendLine("\u2139\uFE0F Tidak berubah (target = stok saat ini):");
                foreach (var item in skippedSameStock.Take(10))
                {
                    sb.AppendLine($"  - {SanitizeWarning(item)}");
                }
            }

            if (warnings.Any())
            {
                sb.AppendLine();
                sb.AppendLine($"{IconWarning} Dilewati:");
                foreach (var item in warnings.Take(10))
                {
                    sb.AppendLine($"  - {SanitizeWarning(item)}");
                }
            }

            sb.AppendLine();
            sb.Append(BuildConfirmationActions());
            return sb.ToString();
        }

        private static string BuildBulkInventorySuccessMessage(string? documentNumber, List<BulkDocumentItemResult> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{IconCheck} BULK INVENTORY SELESAI");
            sb.AppendLine();
            sb.AppendLine($"  {IconDocument} Dokumen : {FormatOptional(documentNumber)}");
            sb.AppendLine($"  {IconCheck} Berhasil: {items.Count}/{items.Count} produk");
            sb.AppendLine();
            AppendAlignedRows(
                sb,
                items.Take(10).Select(item => (
                    Name: item.ProductName,
                    Col2: $"\u2192 {FormatStockValue(item.NewStock)} {GetUnitLabel(item.Unit)}",
                    Col3: string.Empty,
                    Col4: string.Empty)));
            return sb.ToString().TrimEnd();
        }

        private static string GetInventoryDirectionIcon(decimal change)
        {
            if (change > 0)
            {
                return IconUp;
            }

            if (change < 0)
            {
                return IconDown;
            }

            return IconRight;
        }

        private static string DescribeInventoryDirection(decimal change)
        {
            if (change > 0)
            {
                return "koreksi naik";
            }

            if (change < 0)
            {
                return "koreksi turun";
            }

            return "tidak berubah";
        }

        private static string SanitizeWarning(string warning)
        {
            return warning
                .Replace("⚠️", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("↔️", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("—", "-", StringComparison.OrdinalIgnoreCase)
                .Replace("âš ï¸", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("â†”ï¸", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("â€”", "-", StringComparison.OrdinalIgnoreCase)
                .Replace("dilewati.", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("dilewati:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        private static void AppendAlignedRows(
            StringBuilder sb,
            IEnumerable<(string Name, string Col2, string Col3, string Col4)> rows)
        {
            var materialized = rows.ToList();
            if (!materialized.Any())
            {
                return;
            }

            int nameWidth = materialized.Max(row => row.Name.Length);
            int col2Width = materialized.Max(row => row.Col2.Length);
            int col3Width = materialized.Max(row => row.Col3.Length);

            foreach (var row in materialized)
            {
                var line = new StringBuilder("  ");
                line.Append(row.Name.PadRight(nameWidth));

                if (!string.IsNullOrWhiteSpace(row.Col2))
                {
                    line.Append("  ");
                    line.Append(row.Col2.PadLeft(col2Width));
                }

                if (!string.IsNullOrWhiteSpace(row.Col3))
                {
                    line.Append("  ");
                    line.Append(row.Col3.PadLeft(col3Width));
                }

                if (!string.IsNullOrWhiteSpace(row.Col4))
                {
                    line.Append("  ");
                    line.Append(row.Col4);
                }

                sb.AppendLine(line.ToString().TrimEnd());
            }
        }

        private static string GetUnitLabel(string? unit)
        {
            return string.IsNullOrWhiteSpace(unit) ? DefaultStockUnit : unit!;
        }

        private static string TruncateWithEllipsis(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            if (maxLength <= 3)
            {
                return value[..maxLength];
            }

            return value[..(maxLength - 3)] + "...";
        }

        private static string SerializeBulkItems(List<BulkPendingItem> items)
        {
            return JsonSerializer.Serialize(items);
        }

        private static string SerializeOcrBulkPayload(OcrBulkPendingPayload payload)
        {
            return JsonSerializer.Serialize(payload);
        }

        private static string SerializePriceOverridePayload(PriceOverridePendingPayload payload)
        {
            return JsonSerializer.Serialize(payload);
        }

        private static List<BulkPendingItem> DeserializeBulkItems(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<BulkPendingItem>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<BulkPendingItem>>(payload) ?? new List<BulkPendingItem>();
            }
            catch
            {
                return new List<BulkPendingItem>();
            }
        }

        private static List<ReceiptItem> DeserializeReceiptItems(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new List<ReceiptItem>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<ReceiptItem>>(payload) ?? new List<ReceiptItem>();
            }
            catch
            {
                return new List<ReceiptItem>();
            }
        }

        private static OcrBulkPendingPayload DeserializeOcrBulkPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new OcrBulkPendingPayload();
            }

            try
            {
                return JsonSerializer.Deserialize<OcrBulkPendingPayload>(payload) ?? new OcrBulkPendingPayload();
            }
            catch
            {
                return new OcrBulkPendingPayload
                {
                    Items = DeserializeBulkItems(payload)
                };
            }
        }

        private static PriceOverridePendingPayload DeserializePriceOverridePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new PriceOverridePendingPayload();
            }

            try
            {
                return JsonSerializer.Deserialize<PriceOverridePendingPayload>(payload) ?? new PriceOverridePendingPayload();
            }
            catch
            {
                return new PriceOverridePendingPayload();
            }
        }

        private static string BuildOcrPurchaseNote(InboundMessage message, OcrBulkPendingPayload payload)
        {
            var parts = new List<string>
            {
                $"OCR receipt confirmed via {message.Channel} by {message.SenderId}"
            };

            string? supplierName = payload.SupplierName ?? payload.StoreName;
            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                parts.Add($"Supplier: {supplierName}");
            }

            if (!string.IsNullOrWhiteSpace(payload.BuyerName))
            {
                parts.Add($"Buyer: {payload.BuyerName}");
            }

            if (payload.ReceiptDate.HasValue)
            {
                parts.Add($"Tanggal: {payload.ReceiptDate:yyyy-MM-dd}");
            }

            if (!string.IsNullOrWhiteSpace(payload.ReceiptNumber))
            {
                parts.Add($"No: {payload.ReceiptNumber}");
            }

            return string.Join(" | ", parts);
        }

        private async Task<List<ShadowConversionResult>> ApplyShadowConversionAsync(
            IEnumerable<BulkPendingItem> items,
            PriceOverridePendingPayload? pricePayload = null,
            PriceOverrideDecision? decision = null)
        {
            var results = new List<ShadowConversionResult>();
            if (_posDbService == null)
            {
                return results;
            }

            foreach (var item in items ?? Enumerable.Empty<BulkPendingItem>())
            {
                ShadowConversionResult result = await TryApplySingleShadowAsync(item, pricePayload, decision);
                if (result.Status != ShadowConversionStatus.NoMapping || ShouldReportMissingShadowMapping(item))
                {
                    results.Add(result);
                }
            }

            return results;
        }

        private async Task<ShadowConversionResult> TryApplySingleShadowAsync(
            BulkPendingItem item,
            PriceOverridePendingPayload? pricePayload = null,
            PriceOverrideDecision? decision = null)
        {
            var result = new ShadowConversionResult
            {
                ParentProductId = item.ProductId,
                ParentProductName = item.ProductName,
                ParentQuantity = item.Quantity,
                Status = ShadowConversionStatus.NoMapping,
                Message = "Produk belum punya unit conversion mapping."
            };

            if (_posDbService == null ||
                string.IsNullOrWhiteSpace(item.ProductId) ||
                item.Quantity <= 0)
            {
                result.Status = ShadowConversionStatus.Failed;
                result.Message = "Data parent shadow conversion tidak valid.";
                return result;
            }

            UnitConversionMapping? conversion = await _databaseService.GetConversionByParentIdAsync(item.ProductId);
            conversion ??= await TryCreateKnownShadowConversionAsync(item);
            string? childProductId = null;
            string? childProductName = null;
            decimal effectiveConversionRate = 0;
            string rateSource = "none";

            if (conversion != null &&
                !string.Equals(conversion.ParentProductId, conversion.ChildProductId, StringComparison.OrdinalIgnoreCase))
            {
                effectiveConversionRate = item.IsiPerBox.GetValueOrDefault() > 0
                    ? item.IsiPerBox!.Value
                    : conversion.ConversionRate;
                rateSource = item.IsiPerBox.GetValueOrDefault() > 0 ? "invoice-isi" : "db-mapping";
                childProductId = conversion.ChildProductId;
                childProductName = conversion.ChildProductName ?? conversion.ChildProductId;
            }
            else if (item.IsiPerBox.GetValueOrDefault() > 0)
            {
                ChildProductDiscovery discovery = await TryDiscoverChildProductAsync(item);
                if (discovery.IsAmbiguous)
                {
                    result.Status = ShadowConversionStatus.Ambiguous;
                    result.RateUsed = item.IsiPerBox;
                    result.RateSource = "invoice-isi";
                    result.Message = "Beberapa kandidat produk eceran ditemukan: " +
                                     string.Join("; ", discovery.Candidates.Take(3).Select(product => product.Name));
                    return result;
                }

                if (discovery.Product == null || string.IsNullOrWhiteSpace(discovery.Product.Id))
                {
                    result.Status = ShadowConversionStatus.NoChildFound;
                    result.RateUsed = item.IsiPerBox;
                    result.RateSource = "invoice-isi";
                    result.Message = "Produk eceran/child tidak ditemukan otomatis.";
                    return result;
                }

                effectiveConversionRate = item.IsiPerBox!.Value;
                rateSource = "auto-discovered";
                childProductId = discovery.Product.Id;
                childProductName = discovery.Product.Name ?? discovery.Product.Id;

                await _databaseService.UpsertUnitConversionAsync(new UnitConversionMapping
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ParentProductId = item.ProductId,
                    ParentProductName = item.ProductName,
                    ChildProductId = childProductId,
                    ChildProductName = childProductName,
                    ConversionRate = effectiveConversionRate,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }
            else
            {
                result.Message = $"Shadow child tidak jalan: {item.ProductName} belum punya Unit Conversion Mapping dan IsiPerBox kosong.";
                await _loggingService.LogInfoAsync(
                    $"Shadow conversion skipped: parent {item.ProductId} ({item.ProductName}) tidak punya mapping dan IsiPerBox kosong.",
                    "OCR");
                return result;
            }

            if (string.IsNullOrWhiteSpace(childProductId) || effectiveConversionRate <= 0)
            {
                result.Status = ShadowConversionStatus.Failed;
                result.Message = "Data child atau rasio shadow conversion tidak valid.";
                return result;
            }

            decimal childQuantity = item.Quantity * effectiveConversionRate;
            decimal? childUnitCost = CalculateShadowChildUnitCost(item.Price, effectiveConversionRate);
            decimal childUnitCostValue = childUnitCost.GetValueOrDefault();
            bool hasChildUnitCost = childUnitCostValue > 0;
            Product? childProductBeforeAdjust = await _posDbService.GetProductByIdAsync(childProductId);
            decimal existingChildCost = childProductBeforeAdjust?.PurchasePrice ?? 0;
            PriceChangeItem? childPriceChange = FindPriceChange(pricePayload, childProductId, childUnitCost);
            bool updateChildMasterCost = decision?.UpdateCost == true && childPriceChange != null;
            string? internalNote = hasChildUnitCost
                ? $"SSA shadow conversion | Parent {item.ProductName} | {FormatStockValue(item.Quantity)} x {FormatStockValue(effectiveConversionRate)} -> {FormatStockValue(childQuantity)} {childProductName} | harga beli {FormatCurrency(childUnitCostValue)}"
                : null;
            var adjustResult = hasChildUnitCost
                ? await _posDbService.AdjustStockWithCostAsync(childProductId, childQuantity, childUnitCostValue, updateMasterCost: updateChildMasterCost, internalNote: internalNote)
                : await _posDbService.AdjustStockAsync(childProductId, childQuantity);
            result.ChildProductId = childProductId;
            result.ChildProductName = childProductName;
            result.ChildQuantity = childQuantity;
            result.RateUsed = effectiveConversionRate;
            result.RateSource = rateSource;
            result.ChildUnitCost = childUnitCost;
            result.ChildTotalCost = childUnitCost.HasValue ? childUnitCost.Value * childQuantity : null;

            if (!adjustResult.Success)
            {
                result.Status = ShadowConversionStatus.Failed;
                result.Message = adjustResult.Error ?? "Adjust stock child gagal.";
                await _loggingService.LogWarningAsync(
                    $"Shadow conversion gagal untuk parent {item.ProductId} ke child {childProductId} rate={effectiveConversionRate}: {adjustResult.Error}",
                    "OCR");
                return result;
            }

            result.Status = rateSource == "auto-discovered"
                ? ShadowConversionStatus.AutoMapped
                : ShadowConversionStatus.Applied;
            string costInfo = hasChildUnitCost
                ? $" | harga beli ecer {FormatCurrency(childUnitCostValue)} (total {FormatCurrency(childUnitCostValue * childQuantity)})"
                : string.Empty;
            if (hasChildUnitCost)
            {
                string? masterCostStatus = BuildShadowMasterCostStatus(existingChildCost, childUnitCostValue, childPriceChange, decision, updateChildMasterCost);
                if (!string.IsNullOrWhiteSpace(masterCostStatus))
                {
                    costInfo += $" | {masterCostStatus}";
                }
            }
            result.Message = $"{FormatStockValue(item.Quantity)} {item.ProductName} x {FormatStockValue(effectiveConversionRate)} ({rateSource}) -> +{FormatStockValue(childQuantity)} {childProductName}{costInfo}";

            await _loggingService.LogInfoAsync(
                $"Shadow conversion OCR: {item.ProductId} ({item.ProductName}) -> {childProductId} ({childProductName}) rate={effectiveConversionRate} source={rateSource} qty +{childQuantity} childUnitCost={childUnitCost?.ToString(CultureInfo.InvariantCulture) ?? "-"}.",
                "OCR");

            return result;
        }

        private static decimal? CalculateShadowChildUnitCost(decimal? parentUnitCost, decimal conversionRate)
        {
            if (parentUnitCost.GetValueOrDefault() <= 0 || conversionRate <= 0)
            {
                return null;
            }

            return Math.Round(parentUnitCost!.Value / conversionRate, 2, MidpointRounding.AwayFromZero);
        }

        private static string? BuildShadowMasterCostStatus(
            decimal existingCost,
            decimal transactionCost,
            PriceChangeItem? childPriceChange,
            PriceOverrideDecision? decision,
            bool updated)
        {
            if (updated)
            {
                return "harga beli data produk ecer diupdate";
            }

            if (Math.Abs(existingCost - transactionCost) < 0.01m)
            {
                return "harga beli data produk ecer sudah sama";
            }

            if (decision != null && !decision.UpdateCost)
            {
                return "harga beli data produk ecer dilewati sesuai pilihan user";
            }

            if (childPriceChange == null && existingCost > 0)
            {
                decimal deltaPercent = ((transactionCost - existingCost) / existingCost) * 100;
                return $"harga beli data produk ecer tidak diubah karena selisih {FormatPercent(deltaPercent)} di bawah batas 1%";
            }

            return "harga beli data produk ecer tidak diubah";
        }

        private async Task<UnitConversionMapping?> TryCreateKnownShadowConversionAsync(BulkPendingItem item)
        {
            if (_posDbService == null ||
                string.IsNullOrWhiteSpace(item.ProductId) ||
                !IsScorpionPackProductName(item.ProductName))
            {
                return null;
            }

            var products = await _posDbService.GetAllProductsAsync();
            var child = products.FirstOrDefault(product =>
                !string.IsNullOrWhiteSpace(product.Id) &&
                !string.Equals(product.Id, item.ProductId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeText(product.Name ?? string.Empty), "scorpion", StringComparison.OrdinalIgnoreCase) &&
                IsChildReceiptUnit(product.Unit));
            if (child == null || string.IsNullOrWhiteSpace(child.Id))
            {
                return null;
            }

            var mapping = new UnitConversionMapping
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentProductId = item.ProductId,
                ParentProductName = item.ProductName,
                ChildProductId = child.Id,
                ChildProductName = child.Name,
                ConversionRate = 10,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            await _databaseService.UpsertUnitConversionAsync(mapping);
            await _loggingService.LogInfoAsync(
                $"Shadow conversion default dibuat: {item.ProductId} ({item.ProductName}) -> {child.Id} ({child.Name}) rate=10.",
                "OCR");
            return mapping;
        }

        private static bool IsScorpionPackProductName(string? productName)
        {
            string normalized = NormalizeText(productName ?? string.Empty);
            return string.Equals(normalized, "scorpion 1 pak", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "scorpion 1pk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldReportMissingShadowMapping(BulkPendingItem item)
        {
            return IsBulkReceiptUnit(item.Unit) || HasPackageMarker(item.ProductName);
        }

        private async Task<ChildProductDiscovery> TryDiscoverChildProductAsync(BulkPendingItem parent)
        {
            var discovery = new ChildProductDiscovery();
            if (_posDbService == null || string.IsNullOrWhiteSpace(parent.ProductName))
            {
                return discovery;
            }

            var eceranUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pcs", "pcs.", "pc", "rcg", "ecer", "satuan", "biji", "buah"
            };

            var candidates = (await _posDbService.GetAllProductsAsync())
                .Where(product => !string.IsNullOrWhiteSpace(product.Id) &&
                                  !string.Equals(product.Id, parent.ProductId, StringComparison.OrdinalIgnoreCase) &&
                                  eceranUnits.Contains((product.Unit ?? string.Empty).Trim()))
                .Select(product => new
                {
                    Product = product,
                    Score = ComputeShadowProductSimilarity(parent.ProductName, product.Name ?? string.Empty)
                })
                .Where(candidate => candidate.Score >= 70)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Product.Name)
                .Take(3)
                .ToList();

            if (!candidates.Any())
            {
                return discovery;
            }

            var best = candidates[0];
            var second = candidates.Skip(1).FirstOrDefault();
            discovery.Candidates = candidates.Select(candidate => candidate.Product).ToList();
            if (second != null && best.Score - second.Score < 10)
            {
                discovery.IsAmbiguous = true;
                return discovery;
            }

            discovery.Product = best.Product;
            return discovery;
        }

        private static int ComputeShadowProductSimilarity(string parentName, string childName)
        {
            var parentTokens = GetShadowNameTokens(parentName);
            var childTokens = GetShadowNameTokens(childName);
            if (!parentTokens.Any() || !childTokens.Any())
            {
                return 0;
            }

            int overlap = parentTokens.Count(token => childTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
            double coverage = overlap / (double)Math.Max(parentTokens.Count, childTokens.Count);
            int score = (int)Math.Round(coverage * 100);

            string normalizedParent = string.Join(" ", parentTokens);
            string normalizedChild = string.Join(" ", childTokens);
            if (string.Equals(normalizedParent, normalizedChild, StringComparison.OrdinalIgnoreCase))
            {
                score = 100;
            }
            else if (normalizedChild.Contains(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                     normalizedParent.Contains(normalizedChild, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Min(100, score + 15);
            }

            return score;
        }

        private static List<string> GetShadowNameTokens(string value)
        {
            var packagingTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "box", "dus", "pak", "bks", "bal", "rtg", "rcg", "pcs", "pc", "ecer", "satuan", "biji", "buah", "isi"
            };

            return GetSearchTokens(value)
                .Where(token => !packagingTokens.Contains(token))
                .ToList();
        }

        private static string FormatShadowConversionResult(ShadowConversionResult result)
        {
            string prefix = result.Status switch
            {
                ShadowConversionStatus.Applied => IconCheck,
                ShadowConversionStatus.AutoMapped => IconCheck,
                ShadowConversionStatus.Ambiguous => IconWarning,
                ShadowConversionStatus.Failed => IconWarning,
                ShadowConversionStatus.NoChildFound => IconWarning,
                ShadowConversionStatus.NoMapping => IconWarning,
                _ => IconRight
            };

            return $"{prefix} {result.Message ?? result.Status.ToString()}";
        }

        private async Task RecordInboundRuntimeAsync(InboundMessage message, string status)
        {
            if (message.Channel == ChannelType.WhatsApp || message.Channel == ChannelType.Baileys)
            {
                _lastWebhookReceivedAt = DateTime.Now;
                _lastWebhookStatus = status;
                await SaveRuntimeDateAsync(StateLastWebhookReceivedAt, _lastWebhookReceivedAt.Value);
                await _databaseService.SetRuntimeStateAsync(StateLastWebhookStatus, status);
            }
        }

        private void LoadRuntimeState()
        {
            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastDailySummaryDate), out var dailySummaryDate))
            {
                _lastDailySummaryDate = dailySummaryDate.Date;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastLowStockAlertDate), out var lowStockAlertDate))
            {
                _lastLowStockAlertDate = lowStockAlertDate.Date;
            }
            else if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLegacyLastLowStockAlertAt), out var legacyLowStockAlertAt))
            {
                _lastLowStockAlertDate = legacyLowStockAlertAt.Date;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastReceivableAlertDate), out var receivableAlertDate))
            {
                _lastReceivableAlertDate = receivableAlertDate.Date;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastExpiryAlertDate), out var expiryAlertDate))
            {
                _lastExpiryAlertDate = expiryAlertDate.Date;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastAnomalyAlertDate), out var anomalyAlertDate))
            {
                _lastAnomalyAlertDate = anomalyAlertDate.Date;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastWebhookReceivedAt), out var lastWebhookAt))
            {
                _lastWebhookReceivedAt = lastWebhookAt;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastOutboundSentAt), out var outboundSentAt))
            {
                _lastOutboundSentAt = outboundSentAt;
            }

            if (DateTime.TryParse(_databaseService.GetRuntimeState(StateLastOutboundFailureAt), out var outboundFailureAt))
            {
                _lastOutboundFailureAt = outboundFailureAt;
            }

            _lastWebhookStatus = _databaseService.GetRuntimeState(StateLastWebhookStatus);
            _lastFailureMessage = _databaseService.GetRuntimeState(StateLastOutboundFailureMessage);
        }

        private async Task SaveRuntimeDateAsync(string key, DateTime value)
        {
            await _databaseService.SetRuntimeStateAsync(key, value.ToString("o"));
        }

        private static string ComputePayloadHash(InboundMessage message)
        {
            string source = $"{message.Channel}|{message.SenderId}|{message.RawSenderJid}|{message.ResolvedSenderJid}|{message.MessageId}|{message.Text}|{message.MediaUrl}|{message.MediaMimeType}|{message.Timestamp:O}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private int GetMaxRetries(ChannelType channel)
        {
            if (channel == ChannelType.WhatsApp || channel == ChannelType.Baileys)
            {
                return Math.Max(1, _configService.Config?.WhatsApp?.OutboundMaxRetries ?? 5);
            }

            return 3;
        }

        private TimeSpan GetRetryDelay(ChannelType channel, int attemptNumber)
        {
            int baseDelaySeconds = channel == ChannelType.WhatsApp || channel == ChannelType.Baileys
                ? Math.Max(1, _configService.Config?.WhatsApp?.InitialRetryDelaySeconds ?? 15)
                : 5;

            double seconds = Math.Min(baseDelaySeconds * Math.Pow(2, Math.Max(0, attemptNumber - 1)), 300);
            return TimeSpan.FromSeconds(seconds);
        }

        private void SeedAutomationDefaults()
        {
            bool changed = false;
            var config = _configService.Config;
            if (config == null)
            {
                return;
            }

            config.Automation ??= new AutomationSettings();
            config.WhatsApp ??= new WhatsAppSettings();
            config.Baileys ??= new BaileysSettings();
            config.Tunnel ??= new TunnelSettings();

            if (config.Automation.Templates == null || !config.Automation.Templates.Any())
            {
                config.Automation.Templates = new List<AutomationTemplate>
                {
                    CreateTemplate("inbound-routing-telegram", "Inbound routing Telegram", "TelegramMessage", "route-command"),
                    CreateTemplate("inbound-routing-whatsapp", "Inbound routing WhatsApp", "WhatsAppMessage", "route-command"),
                    CreateTemplate("natural-ai", "Natural AI assistant", "TelegramMessage", "route-ai"),
                    CreateTemplate("owner-access", "Owner/Kasir authorization", "WhatsAppMessage", "log"),
                    CreateTemplate("low-stock-alert", "Low stock alert", "StockAlert", "log"),
                    CreateTemplate("daily-summary", "Daily summary", "Schedule", "log"),
                    CreateTemplate("restock-confirm", "Restock confirmation", "TelegramMessage", "log"),
                    CreateTemplate("inventory-confirm", "Inventory confirmation", "WhatsAppMessage", "log")
                };
                changed = true;
            }

            if (config.Automation.Rules == null || !config.Automation.Rules.Any())
            {
                config.Automation.Rules = config.Automation.Templates
                    .SelectMany(t => t.DefaultRules ?? new List<AutomationRule>())
                    .ToList();
                changed = true;
            }

            changed |= EnsureRouteCommandConditions(config.Automation.Templates);
            changed |= EnsureRouteCommandConditions(config.Automation.Rules);

            if (string.IsNullOrWhiteSpace(config.Tunnel.Provider))
            {
                config.Tunnel.Provider = "cloudflared";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(config.Tunnel.ArgsTemplate))
            {
                config.Tunnel.ArgsTemplate = "tunnel --url http://localhost:{port} --http-host-header localhost:{port}";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(config.Automation.LowStockAlertTime))
            {
                config.Automation.LowStockAlertTime = "07:00";
                changed = true;
            }

            string normalizedMode = WhatsAppModes.Normalize(config.WhatsApp.Mode);
            if (!string.Equals(config.WhatsApp.Mode, normalizedMode, StringComparison.Ordinal))
            {
                config.WhatsApp.Mode = normalizedMode;
                changed = true;
            }

            if (config.WhatsApp.TemplateMappings == null || !config.WhatsApp.TemplateMappings.Any())
            {
                config.WhatsApp.TemplateMappings = new List<WhatsAppTemplateMapping>
                {
                    new() { Key = "StockAlert", TemplateName = "ssa_low_stock_alert", LanguageCode = "id", BodyParameterCount = 1 },
                    new() { Key = "Schedule", TemplateName = "ssa_daily_summary", LanguageCode = "id", BodyParameterCount = 1 },
                    new() { Key = "ReceivableAlert", TemplateName = "ssa_receivable_alert", LanguageCode = "id", BodyParameterCount = 1 },
                    new() { Key = "ExpiryAlert", TemplateName = "ssa_expiry_alert", LanguageCode = "id", BodyParameterCount = 1 },
                    new() { Key = "AnomalyAlert", TemplateName = "ssa_anomaly_alert", LanguageCode = "id", BodyParameterCount = 1 },
                    new() { Key = "Test", TemplateName = "ssa_test_message", LanguageCode = "id", BodyParameterCount = 1 }
                };
                changed = true;
            }

            if (changed)
            {
                _configService.SaveConfig();
            }
        }

        private static AutomationTemplate CreateTemplate(string key, string name, string triggerType, string actionType)
        {
            return new AutomationTemplate
            {
                Key = key,
                Name = name,
                DefaultRules = new List<AutomationRule>
                {
                    new()
                    {
                        TriggerType = triggerType,
                        Enabled = true,
                        Priority = 100,
                        Description = name,
                        Actions = new List<AutomationRuleAction>
                        {
                            new() { Type = actionType, Value = key },
                            new() { Type = "log", Value = key }
                        }
                    }
                }
            };
        }

        private static bool EnsureRouteCommandConditions(IEnumerable<AutomationTemplate>? templates)
        {
            if (templates == null)
            {
                return false;
            }

            bool changed = false;
            foreach (var rule in templates.SelectMany(t => t.DefaultRules ?? new List<AutomationRule>()))
            {
                changed |= EnsureRouteCommandCondition(rule);
            }

            return changed;
        }

        private static bool EnsureRouteCommandConditions(IEnumerable<AutomationRule>? rules)
        {
            if (rules == null)
            {
                return false;
            }

            bool changed = false;
            foreach (var rule in rules)
            {
                changed |= EnsureRouteCommandCondition(rule);
            }

            return changed;
        }

        private static bool EnsureRouteCommandCondition(AutomationRule rule)
        {
            bool isRouteCommand = rule.Actions?.Any(a => string.Equals(a.Type, "route-command", StringComparison.OrdinalIgnoreCase)) == true;
            if (!isRouteCommand)
            {
                return false;
            }

            rule.Conditions ??= new List<AutomationRuleCondition>();
            if (rule.Conditions.Any(c => c.RequiresCommand == true))
            {
                return false;
            }

            rule.Conditions.Add(new AutomationRuleCondition
            {
                RequiresCommand = true
            });
            return true;
        }
    }
}
