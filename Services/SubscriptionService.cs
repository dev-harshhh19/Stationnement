using Stationnement.Web.Repositories;

namespace Stationnement.Web.Services;

public interface ISubscriptionService
{
    Task<SubscriptionInfo> GetUserSubscriptionAsync(Guid userId);
    Task<bool> CanAccessPremiumSlotsAsync(Guid userId);
    Task<bool> CanUseVehicleTypeAsync(Guid userId, string vehicleType);
    decimal GetDiscountPercentage(string tier);
    SubscriptionTierInfo[] GetAllTiers();
    Task<SubscriptionInfo> ActivateSubscriptionAsync(Guid userId, string tier);
    Task<bool> CancelSubscriptionAsync(Guid userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _repository;

    private static readonly Dictionary<string, SubscriptionTierInfo> Tiers = new()
    {
        ["free"] = new SubscriptionTierInfo 
        { 
            Id = "free", Name = "Free", 
            PricePerMonth = 0, Discount = 0,
            CanAccessPremiumSlots = false, 
            CanUseSportsVehicles = false, 
            CanUseHyperSports = false,
            Features = new[] { "Standard parking slots", "Basic booking", "Email support" }
        },
        ["basic"] = new SubscriptionTierInfo 
        { 
            Id = "basic", Name = "Basic", 
            PricePerMonth = 199, Discount = 0.05m,
            CanAccessPremiumSlots = false, 
            CanUseSportsVehicles = false, 
            CanUseHyperSports = false,
            Features = new[] { "5% discount on all bookings", "Priority booking", "SMS notifications" }
        },
        ["pro"] = new SubscriptionTierInfo 
        { 
            Id = "pro", Name = "Pro", 
            PricePerMonth = 499, Discount = 0.15m,
            CanAccessPremiumSlots = true, 
            CanUseSportsVehicles = true, 
            CanUseHyperSports = false,
            Features = new[] { "15% discount on all bookings", "Premium parking slots", "Sports vehicle parking", "Priority support" }
        },
        ["premium_plus"] = new SubscriptionTierInfo 
        { 
            Id = "premium_plus", Name = "Premium+", 
            PricePerMonth = 999, Discount = 0.25m,
            CanAccessPremiumSlots = true, 
            CanUseSportsVehicles = true, 
            CanUseHyperSports = true,
            Features = new[] { "25% discount on all bookings", "All premium slots", "Hyper-Sports parking", "VIP support", "Free cancellation" }
        }
    };

    public SubscriptionService(ISubscriptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<SubscriptionInfo> GetUserSubscriptionAsync(Guid userId)
    {
        var sub = await _repository.GetByUserIdAsync(userId);
        
        if (sub == null || !sub.IsActive || (sub.ExpiresAt.HasValue && sub.ExpiresAt < DateTime.UtcNow))
        {
            return new SubscriptionInfo 
            { 
                Tier = "free",
                TierInfo = Tiers["free"],
                IsActive = true, 
                ExpiresAt = null 
            };
        }

        return new SubscriptionInfo
        {
            Tier = sub.Tier,
            TierInfo = Tiers.GetValueOrDefault(sub.Tier, Tiers["free"]),
            IsActive = sub.IsActive,
            StartsAt = sub.StartsAt,
            ExpiresAt = sub.ExpiresAt
        };
    }

    public async Task<bool> CanAccessPremiumSlotsAsync(Guid userId)
    {
        var sub = await GetUserSubscriptionAsync(userId);
        return sub.TierInfo.CanAccessPremiumSlots;
    }

    public async Task<bool> CanUseVehicleTypeAsync(Guid userId, string vehicleType)
    {
        var sub = await GetUserSubscriptionAsync(userId);
        var vt = vehicleType.ToLower();

        if (vt.Contains("hyper"))
            return sub.TierInfo.CanUseHyperSports;
        
        if (vt.Contains("sport"))
            return sub.TierInfo.CanUseSportsVehicles;
        
        return true; // Standard vehicles allowed for all
    }

    public decimal GetDiscountPercentage(string tier)
    {
        return Tiers.GetValueOrDefault(tier, Tiers["free"]).Discount;
    }

    public SubscriptionTierInfo[] GetAllTiers()
    {
        return Tiers.Values.ToArray();
    }

    public async Task<SubscriptionInfo> ActivateSubscriptionAsync(Guid userId, string tier)
    {
        if (!Tiers.ContainsKey(tier))
            throw new ArgumentException("Invalid subscription tier");

        var expiresAt = DateTime.UtcNow.AddMonths(1);
        await _repository.CreateOrUpdateAsync(userId, tier, expiresAt);
        
        return await GetUserSubscriptionAsync(userId);
    }

    public async Task<bool> CancelSubscriptionAsync(Guid userId)
    {
        var sub = await _repository.GetByUserIdAsync(userId);
        if (sub == null || sub.Tier == "free")
            return false;

        await _repository.DeactivateAsync(userId);
        return true;
    }
}

public class SubscriptionInfo
{
    public string Tier { get; set; } = "free";
    public SubscriptionTierInfo TierInfo { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    
    // Helper properties for frontend
    public string TierName => TierInfo?.Name ?? "Free";
    public decimal DiscountPercent => (TierInfo?.Discount ?? 0) * 100;
    public decimal PricePerMonth => TierInfo?.PricePerMonth ?? 0;
}

public class SubscriptionTierInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PricePerMonth { get; set; }
    public decimal Discount { get; set; }
    public bool CanAccessPremiumSlots { get; set; }
    public bool CanUseSportsVehicles { get; set; }
    public bool CanUseHyperSports { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
}
