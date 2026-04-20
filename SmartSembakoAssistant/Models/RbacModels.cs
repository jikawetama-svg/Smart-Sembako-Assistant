namespace SmartSembakoAssistant.Models
{
    public class RbacUser
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? TelegramId { get; set; }
        public string? WhatsappNumber { get; set; }
        public long RoleId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class Role
    {
        public long Id { get; set; }
        public string RoleName { get; set; } = "";
    }

    public class Permission
    {
        public long Id { get; set; }
        public long RoleId { get; set; }
        public bool CanOcr { get; set; }
        public bool CanPurchase { get; set; }
        public bool CanEditStock { get; set; }
        public bool CanViewReport { get; set; }
        public bool CanManageUsers { get; set; }
    }

    public class ConversationSession
    {
        public long Id { get; set; }
        public string UserId { get; set; } = "";
        public string State { get; set; } = "";
        public string? ReceiptId { get; set; }
        public int CurrentUnknownIndex { get; set; }
        public string? TempProductName { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}