namespace Stationnement.Web.Models;

public class ParkingLocation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "France";
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int TotalSlots { get; set; }
    public decimal BasePricePerHour { get; set; }
    public string? Amenities { get; set; }
    public string? OperatingHours { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Computed
    public int AvailableSlots { get; set; }
}

public class ParkingSlot
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string SlotCode { get; set; } = string.Empty;
    public string SlotType { get; set; } = "standard";
    public string? Floor { get; set; }
    public string? Section { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? PriceMultiplier { get; set; }
    public decimal BasePricePerHour { get; set; }
    public string Status { get; set; } = "available";
    public bool IsPremium { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public string? LocationName { get; set; }
}

public class SlotWithAvailability
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string SlotCode { get; set; } = string.Empty;
    public string SlotType { get; set; } = "standard";
    public string? Floor { get; set; }
    public string? Section { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal BasePricePerHour { get; set; }
    public string Status { get; set; } = "available";
    public bool IsPremium { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Availability status for the requested time range
    public bool IsAvailable { get; set; }
    public bool IsBooked { get; set; }
    public string? BookedBy { get; set; } // Reserved for future use
}

public class PricingRule
{
    public Guid Id { get; set; }
    public Guid? LocationId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public decimal Multiplier { get; set; } = 1.0m;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? DaysOfWeek { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
