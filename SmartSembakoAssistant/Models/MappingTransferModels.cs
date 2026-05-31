using System.Text.Json.Serialization;

namespace SmartSembakoAssistant.Models
{
    public enum MappingImportMode
    {
        MergeSafe,
        ImportNewOnly,
        OverwriteExisting
    }

    public class MappingTransferPackage
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
        public string AppVersion { get; set; } = "SmartSembakoAssistant";
        public string MachineName { get; set; } = Environment.MachineName;
        public MappingTransferPayload Payload { get; set; } = new();
    }

    public class MappingTransferPayload
    {
        public List<OcrProductMappingExportRow> OcrProductMappings { get; set; } = new();
        public List<ProductAliasExportRow> ProductAliases { get; set; } = new();
        public List<UnitConversionExportRow> UnitConversions { get; set; } = new();
        public List<SharedStockGroupExportRow> SharedStockGroups { get; set; } = new();
    }

    public class ProductIdentitySnapshot
    {
        public string? ProductId { get; set; }
        public string? Name { get; set; }
        public string? Sku { get; set; }
        public string? Unit { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class OcrProductMappingExportRow
    {
        public OcrProductMapping Mapping { get; set; } = new();
        public ProductIdentitySnapshot Product { get; set; } = new();
    }

    public class ProductAliasExportRow
    {
        public ProductAliasEntry Alias { get; set; } = new();
        public ProductIdentitySnapshot Product { get; set; } = new();
    }

    public class UnitConversionExportRow
    {
        public UnitConversionMapping Mapping { get; set; } = new();
        public ProductIdentitySnapshot ParentProduct { get; set; } = new();
        public ProductIdentitySnapshot ChildProduct { get; set; } = new();
    }

    public class SharedStockGroupExportRow
    {
        public SharedStockGroup Group { get; set; } = new();
        public List<ProductIdentitySnapshot> MemberProducts { get; set; } = new();
    }

    public class MappingImportPreview
    {
        public MappingTransferPackage Package { get; set; } = new();
        public MappingImportMode Mode { get; set; }
        public List<MappingImportPreviewRow> Rows { get; set; } = new();
        public MappingImportSummary Summary { get; set; } = new();
    }

    public class MappingImportPreviewRow
    {
        public string Type { get; set; } = "";
        public string Key { get; set; } = "";
        public string Existing { get; set; } = "";
        public string Import { get; set; } = "";
        public string Status { get; set; } = "";
        public string Action { get; set; } = "";
        public string Message { get; set; } = "";
        public object? Source { get; set; }
        public List<string> ResolvedProductIds { get; set; } = new();

        [JsonIgnore]
        public bool CanApply => Status is "New" or "Overwrite" or "Warning";
    }

    public class MappingImportSummary
    {
        public int New { get; set; }
        public int Same { get; set; }
        public int Conflict { get; set; }
        public int Missing { get; set; }
        public int Warning { get; set; }
        public int Applied { get; set; }
        public int Skipped { get; set; }

        public override string ToString()
        {
            return $"New {New} | Same {Same} | Conflict {Conflict} | Missing {Missing} | Warning {Warning} | Applied {Applied} | Skipped {Skipped}";
        }
    }
}
