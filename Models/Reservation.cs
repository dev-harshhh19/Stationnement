namespace Stationnement.Web.Models;

public class Reservation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SlotId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public DateTime? ActualEntryTime { get; set; }
    public DateTime? ActualExitTime { get; set; }
    public string Status { get; set; } = "pending";
    public string QrCode { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public string? VehicleType { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal SurchargeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string? RefundMethod { get; set; }
    public string? RefundUpiId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Navigation
    public string? SlotCode { get; set; }
    public string? LocationName { get; set; }
    public string? LocationAddress { get; set; }
}

public class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PlanType { get; set; } = "free";
    public decimal MonthlyPrice { get; set; }
    public int DiscountPercentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool AutoRenew { get; set; } = true;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
}
