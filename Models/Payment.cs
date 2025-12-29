namespace Stationnement.Web.Models;

public class Payment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ReservationId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string PaymentMethod { get; set; } = "card";
    public string Status { get; set; } = "pending";
    public string? StripePaymentId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public string? ReservationQrCode { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public string? UserEmail { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string NotificationType { get; set; } = "info";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
