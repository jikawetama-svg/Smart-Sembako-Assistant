using System.Text;
using System.Text.Json;
using System.IO;
using SmartSembakoAssistant.Models;

namespace SmartSembakoAssistant.Services
{
    public class MappingTransferService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly PosDbService? _posDbService;

        public MappingTransferService(ConfigService configService, DatabaseService databaseService, PosDbService? posDbService)
        {
            _configService = configService;
            _databaseService = databaseService;
            _posDbService = posDbService;
        }

        public async Task<MappingTransferPackage> BuildExportPackageAsync()
        {
            var products = _posDbService == null
                ? new List<Product>()
                : await _posDbService.GetAllProductsAsync();
            var productById = products
                .Where(product => !string.IsNullOrWhiteSpace(product.Id))
                .GroupBy(product => product.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            ProductIdentitySnapshot Snapshot(string? productId)
            {
                if (!string.IsNullOrWhiteSpace(productId) && productById.TryGetValue(productId, out var product))
                {
                    return ToSnapshot(product);
                }

                return new ProductIdentitySnapshot { ProductId = productId };
            }

            var payload = new MappingTransferPayload();
            foreach (var mapping in _configService.Config?.OcrReceipt?.ProductMappings ?? Enumerable.Empty<OcrProductMapping>())
            {
                payload.OcrProductMappings.Add(new OcrProductMappingExportRow
                {
                    Mapping = CloneOcrMapping(mapping),
                    Product = Snapshot(mapping.DatabaseProductId)
                });
            }

            foreach (var alias in await _databaseService.GetProductAliasesAsync())
            {
                payload.ProductAliases.Add(new ProductAliasExportRow
                {
                    Alias = alias,
                    Product = Snapshot(alias.ProductId)
                });
            }

            foreach (var mapping in await _databaseService.GetAllUnitConversionsAsync())
            {
                payload.UnitConversions.Add(new UnitConversionExportRow
                {
                    Mapping = mapping,
                    ParentProduct = Snapshot(mapping.ParentProductId),
                    ChildProduct = Snapshot(mapping.ChildProductId)
                });
            }

            foreach (var group in await _databaseService.GetAllSharedStockGroupsAsync(includeDisabled: true))
            {
                payload.SharedStockGroups.Add(new SharedStockGroupExportRow
                {
                    Group = group,
                    MemberProducts = group.Members.Select(member => Snapshot(member.ProductId)).ToList()
                });
            }

            return new MappingTransferPackage
            {
                SchemaVersion = 1,
                ExportedAt = DateTimeOffset.Now,
                AppVersion = "SmartSembakoAssistant",
                MachineName = Environment.MachineName,
                Payload = payload
            };
        }

        public async Task SaveExportPackageAsync(string filePath)
        {
            var package = await BuildExportPackageAsync();
            string json = JsonSerializer.Serialize(package, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        public async Task<MappingImportPreview> PreviewImportAsync(string filePath, MappingImportMode mode)
        {
            string json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var package = JsonSerializer.Deserialize<MappingTransferPackage>(json, JsonOptions)
                ?? throw new InvalidOperationException("File import tidak valid.");
            if (package.SchemaVersion != 1)
            {
                throw new InvalidOperationException($"SchemaVersion {package.SchemaVersion} belum didukung.");
            }

            var preview = new MappingImportPreview
            {
                Package = package,
                Mode = mode
            };

            var products = _posDbService == null
                ? new List<Product>()
                : await _posDbService.GetAllProductsAsync();
            var aliases = await _databaseService.GetProductAliasesAsync();
            var unitConversions = await _databaseService.GetAllUnitConversionsAsync();
            var sharedGroups = await _databaseService.GetAllSharedStockGroupsAsync(includeDisabled: true);
            var ocrMappings = _configService.Config?.OcrReceipt?.ProductMappings ?? new List<OcrProductMapping>();

            foreach (var row in package.Payload.OcrProductMappings)
            {
                string? resolvedProductId = ResolveProductId(row.Product, products);
                string key = $"{ConfigService.NormalizeOcrSupplierKey(row.Mapping.SupplierKey)}:{ConfigService.NormalizeOcrName(row.Mapping.InvoiceName)}";
                var existing = ocrMappings.FirstOrDefault(mapping =>
                    string.Equals(ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey), ConfigService.NormalizeOcrSupplierKey(row.Mapping.SupplierKey), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ConfigService.NormalizeOcrName(mapping.InvoiceName), ConfigService.NormalizeOcrName(row.Mapping.InvoiceName), StringComparison.OrdinalIgnoreCase));

                preview.Rows.Add(BuildPreviewRow(
                    "OCR",
                    key,
                    existing?.DatabaseProductName ?? "",
                    row.Mapping.DatabaseProductName,
                    existing == null,
                    existing != null && string.Equals(existing.DatabaseProductId, resolvedProductId, StringComparison.OrdinalIgnoreCase),
                    resolvedProductId == null,
                    mode,
                    row,
                    resolvedProductId == null ? new List<string>() : new List<string> { resolvedProductId },
                    existing != null && string.Equals(existing.TrustLevel, "blocked", StringComparison.OrdinalIgnoreCase)
                        ? "Existing blocked; overwrite hanya di mode Overwrite Existing."
                        : ""));
            }

            foreach (var row in package.Payload.ProductAliases)
            {
                string? resolvedProductId = ResolveProductId(row.Product, products);
                string key = Normalize(row.Alias.AliasName);
                var existing = aliases.FirstOrDefault(alias => string.Equals(Normalize(alias.AliasName), key, StringComparison.OrdinalIgnoreCase));
                preview.Rows.Add(BuildPreviewRow(
                    "Alias",
                    row.Alias.AliasName,
                    existing?.ProductName ?? "",
                    row.Alias.ProductName ?? row.Product.Name ?? "",
                    existing == null,
                    existing != null && string.Equals(existing.ProductId, resolvedProductId, StringComparison.OrdinalIgnoreCase),
                    resolvedProductId == null,
                    mode,
                    row,
                    resolvedProductId == null ? new List<string>() : new List<string> { resolvedProductId }));
            }

            foreach (var row in package.Payload.UnitConversions)
            {
                string? parentId = ResolveProductId(row.ParentProduct, products);
                string? childId = ResolveProductId(row.ChildProduct, products);
                var existing = parentId == null
                    ? null
                    : unitConversions.FirstOrDefault(mapping => string.Equals(mapping.ParentProductId, parentId, StringComparison.OrdinalIgnoreCase));
                bool same = existing != null &&
                            string.Equals(existing.ChildProductId, childId, StringComparison.OrdinalIgnoreCase) &&
                            Math.Abs(existing.ConversionRate - row.Mapping.ConversionRate) < 0.0001m;
                string warning = BuildSameUnitRatioOneWarning(parentId, childId, row.Mapping.ConversionRate, products);
                preview.Rows.Add(BuildPreviewRow(
                    "UnitConversion",
                    $"{row.Mapping.ParentProductName} -> {row.Mapping.ChildProductName}",
                    existing == null ? "" : $"{existing.ParentProductName} -> {existing.ChildProductName} @ {existing.ConversionRate:0.##}",
                    $"{row.Mapping.ParentProductName} -> {row.Mapping.ChildProductName} @ {row.Mapping.ConversionRate:0.##}",
                    existing == null,
                    same,
                    parentId == null || childId == null,
                    mode,
                    row,
                    new[] { parentId, childId }.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToList(),
                    warning));
            }

            foreach (var row in package.Payload.SharedStockGroups)
            {
                var resolvedIds = row.Group.Members
                    .Select((member, index) => ResolveProductId(row.MemberProducts.ElementAtOrDefault(index) ?? new ProductIdentitySnapshot { ProductId = member.ProductId, Name = member.ProductName }, products))
                    .ToList();
                var existing = sharedGroups.FirstOrDefault(group => string.Equals(Normalize(group.GroupName), Normalize(row.Group.GroupName), StringComparison.OrdinalIgnoreCase));
                bool same = existing != null &&
                            resolvedIds.All(id => id != null) &&
                            existing.Members.Select(member => member.ProductId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                                .SequenceEqual(resolvedIds.Where(id => id != null).Cast<string>().OrderBy(id => id, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                preview.Rows.Add(BuildPreviewRow(
                    "SharedStock",
                    row.Group.GroupName,
                    existing == null ? "" : string.Join(", ", existing.Members.Select(member => member.ProductName ?? member.ProductId)),
                    string.Join(", ", row.Group.Members.Select(member => member.ProductName ?? member.ProductId)),
                    existing == null,
                    same,
                    resolvedIds.Any(id => id == null),
                    mode,
                    row,
                    resolvedIds.Where(id => id != null).Cast<string>().ToList()));
            }

            preview.Summary = BuildSummary(preview.Rows);
            return preview;
        }

        public async Task<MappingImportSummary> ApplyImportAsync(MappingImportPreview preview)
        {
            var summary = BuildSummary(preview.Rows);
            var applicable = preview.Rows.Where(row => row.CanApply).ToList();
            summary.Applied = 0;
            summary.Skipped = preview.Rows.Count - applicable.Count;

            var currentMappings = (_configService.Config?.OcrReceipt?.ProductMappings ?? new List<OcrProductMapping>())
                .Select(CloneOcrMapping)
                .ToList();

            foreach (var row in applicable)
            {
                switch (row.Type)
                {
                    case "OCR" when row.Source is OcrProductMappingExportRow ocr && row.ResolvedProductIds.Count == 1:
                        UpsertOcrMapping(currentMappings, ocr.Mapping, row.ResolvedProductIds[0]);
                        summary.Applied++;
                        break;
                    case "Alias" when row.Source is ProductAliasExportRow alias && row.ResolvedProductIds.Count == 1:
                        alias.Alias.ProductId = row.ResolvedProductIds[0];
                        await _databaseService.UpsertProductAliasAsync(alias.Alias);
                        summary.Applied++;
                        break;
                    case "UnitConversion" when row.Source is UnitConversionExportRow conversion && row.ResolvedProductIds.Count == 2:
                        conversion.Mapping.ParentProductId = row.ResolvedProductIds[0];
                        conversion.Mapping.ChildProductId = row.ResolvedProductIds[1];
                        await _databaseService.UpsertUnitConversionAsync(conversion.Mapping);
                        summary.Applied++;
                        break;
                    case "SharedStock" when row.Source is SharedStockGroupExportRow shared && row.ResolvedProductIds.Count == shared.Group.Members.Count:
                        for (int i = 0; i < shared.Group.Members.Count; i++)
                        {
                            shared.Group.Members[i].ProductId = row.ResolvedProductIds[i];
                        }

                        await _databaseService.UpsertSharedStockGroupAsync(shared.Group);
                        summary.Applied++;
                        break;
                }
            }

            _configService.ReplaceOcrMappings(currentMappings);
            return summary;
        }

        private static MappingImportPreviewRow BuildPreviewRow(
            string type,
            string key,
            string existing,
            string import,
            bool isNew,
            bool isSame,
            bool missing,
            MappingImportMode mode,
            object source,
            List<string> resolvedProductIds,
            string message = "")
        {
            string status;
            string action;
            if (missing)
            {
                status = "Missing";
                action = "Skip";
            }
            else if (isSame)
            {
                status = "Same";
                action = "Skip";
            }
            else if (isNew)
            {
                status = string.IsNullOrWhiteSpace(message) ? "New" : "Warning";
                action = "Apply";
            }
            else if (mode == MappingImportMode.OverwriteExisting)
            {
                status = "Overwrite";
                action = "Replace";
            }
            else
            {
                status = "Conflict";
                action = "Skip";
            }

            if (mode == MappingImportMode.ImportNewOnly && !isNew)
            {
                status = isSame ? "Same" : "Conflict";
                action = "Skip";
            }

            return new MappingImportPreviewRow
            {
                Type = type,
                Key = key,
                Existing = existing,
                Import = import,
                Status = status,
                Action = action,
                Message = message,
                Source = source,
                ResolvedProductIds = resolvedProductIds
            };
        }

        private static MappingImportSummary BuildSummary(IEnumerable<MappingImportPreviewRow> rows)
        {
            var list = rows.ToList();
            return new MappingImportSummary
            {
                New = list.Count(row => row.Status == "New"),
                Same = list.Count(row => row.Status == "Same"),
                Conflict = list.Count(row => row.Status is "Conflict" or "Overwrite"),
                Missing = list.Count(row => row.Status == "Missing"),
                Warning = list.Count(row => row.Status == "Warning")
            };
        }

        private static string? ResolveProductId(ProductIdentitySnapshot snapshot, List<Product> products)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ProductId))
            {
                var byId = products.FirstOrDefault(product => string.Equals(product.Id, snapshot.ProductId, StringComparison.OrdinalIgnoreCase));
                if (byId != null && IsCompatibleSnapshot(snapshot, byId))
                {
                    return byId.Id;
                }
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Sku))
            {
                var bySku = products.FirstOrDefault(product => string.Equals(product.Sku, snapshot.Sku, StringComparison.OrdinalIgnoreCase));
                if (bySku != null)
                {
                    return bySku.Id;
                }
            }

            string name = Normalize(snapshot.Name);
            string unit = Normalize(snapshot.Unit);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var exactNameUnit = products.Where(product =>
                    string.Equals(Normalize(product.Name), name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(unit) || string.Equals(Normalize(product.Unit), unit, StringComparison.OrdinalIgnoreCase))).ToList();
                if (exactNameUnit.Count == 1)
                {
                    return exactNameUnit[0].Id;
                }

                var exactName = products.Where(product => string.Equals(Normalize(product.Name), name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exactName.Count == 1)
                {
                    return exactName[0].Id;
                }
            }

            return null;
        }

        private static bool IsCompatibleSnapshot(ProductIdentitySnapshot snapshot, Product product)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Name))
            {
                return true;
            }

            if (string.Equals(Normalize(snapshot.Name), Normalize(product.Name), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(snapshot.Sku) &&
                   string.Equals(snapshot.Sku, product.Sku, StringComparison.OrdinalIgnoreCase);
        }

        private static ProductIdentitySnapshot ToSnapshot(Product product)
        {
            return new ProductIdentitySnapshot
            {
                ProductId = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                Unit = product.Unit,
                IsActive = product.IsActive
            };
        }

        private static OcrProductMapping CloneOcrMapping(OcrProductMapping mapping)
        {
            return new OcrProductMapping
            {
                SupplierKey = mapping.SupplierKey,
                InvoiceName = mapping.InvoiceName,
                NormalizedInvoiceName = mapping.NormalizedInvoiceName,
                DatabaseProductId = mapping.DatabaseProductId,
                DatabaseProductName = mapping.DatabaseProductName,
                Source = mapping.Source,
                TrustLevel = mapping.TrustLevel,
                Confidence = mapping.Confidence,
                CreatedAt = mapping.CreatedAt,
                UpdatedAt = mapping.UpdatedAt,
                LastSeenAt = mapping.LastSeenAt,
                LastConfirmedAt = mapping.LastConfirmedAt,
                Note = mapping.Note
            };
        }

        private static void UpsertOcrMapping(List<OcrProductMapping> mappings, OcrProductMapping import, string resolvedProductId)
        {
            var existing = mappings.FirstOrDefault(mapping =>
                string.Equals(ConfigService.NormalizeOcrSupplierKey(mapping.SupplierKey), ConfigService.NormalizeOcrSupplierKey(import.SupplierKey), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ConfigService.NormalizeOcrName(mapping.InvoiceName), ConfigService.NormalizeOcrName(import.InvoiceName), StringComparison.OrdinalIgnoreCase));
            import.DatabaseProductId = resolvedProductId;
            import.NormalizedInvoiceName = ConfigService.NormalizeOcrName(import.InvoiceName);
            import.UpdatedAt = DateTime.Now;

            if (existing == null)
            {
                mappings.Add(import);
                return;
            }

            int index = mappings.IndexOf(existing);
            mappings[index] = import;
        }

        private static string BuildSameUnitRatioOneWarning(string? parentId, string? childId, decimal rate, List<Product> products)
        {
            if (string.IsNullOrWhiteSpace(parentId) ||
                string.IsNullOrWhiteSpace(childId) ||
                Math.Abs(rate - 1) > 0.0001m)
            {
                return string.Empty;
            }

            var parent = products.FirstOrDefault(product => string.Equals(product.Id, parentId, StringComparison.OrdinalIgnoreCase));
            var child = products.FirstOrDefault(product => string.Equals(product.Id, childId, StringComparison.OrdinalIgnoreCase));
            if (parent == null || child == null)
            {
                return string.Empty;
            }

            return string.Equals(Normalize(parent.Unit), Normalize(child.Unit), StringComparison.OrdinalIgnoreCase)
                ? "Unit sama dan ratio 1; lebih cocok Shared Stock."
                : string.Empty;
        }

        private static string Normalize(string? text)
        {
            return (text ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
