namespace Stationnement.Web.Services;

public interface IPricingService
{
    PricingResult CalculatePrice(PricingRequest request);
}

public class PricingService : IPricingService
{
    /// <summary>
    /// Calculate final price with all factors
    /// </summary>
    public PricingResult CalculatePrice(PricingRequest request)
    {
        var result = new PricingResult
        {
            BaseHourlyRate = request.BaseHourlyRate,
            DurationHours = request.DurationHours
        };

        // 1. Calculate base amount
        var baseAmount = request.BaseHourlyRate * (decimal)request.DurationHours;
        result.BaseAmount = baseAmount;

        // 2. Time-based multiplier
        result.TimeMultiplier = GetTimeMultiplier(request.StartTime);
        result.TimeMultiplierName = GetTimeMultiplierName(request.StartTime);

        // 3. Vehicle tier multiplier
        result.VehicleMultiplier = GetVehicleMultiplier(request.VehicleType);
        result.VehicleMultiplierName = request.VehicleType;

        // 4. Duration discount
        result.DurationDiscount = GetDurationDiscount(request.DurationHours, request.VehicleType);

        // 5. Demand-based pricing (based on availability percentage)
        result.DemandMultiplier = GetDemandMultiplier(request.AvailabilityPercentage, request.VehicleType);

        // 6. Weekend/Holiday surcharge
        result.WeekendSurcharge = IsWeekend(request.StartTime) ? 0.15m : 0m;

        // 7. Calculate final price
        // Formula: BaseAmount × TimeMultiplier × VehicleMultiplier × DemandMultiplier × (1 - Discount) × (1 + WeekendSurcharge)
        var afterTimeMultiplier = baseAmount * result.TimeMultiplier;
        var afterVehicleMultiplier = afterTimeMultiplier * result.VehicleMultiplier;
        var afterDemand = afterVehicleMultiplier * result.DemandMultiplier;
        var afterDiscount = afterDemand * (1 - result.DurationDiscount);
        var finalAmount = afterDiscount * (1 + result.WeekendSurcharge);

        // Apply EV off-peak bonus
        if (request.VehicleType.ToLower().Contains("ev") || request.VehicleType.ToLower().Contains("hybrid"))
        {
            if (result.TimeMultiplierName == "Off-Peak")
            {
                finalAmount *= 0.95m; // Extra 5% off for EVs during off-peak
                result.EvBonus = 0.05m;
            }
        }

        // Protection rules for Hyper-Sports during peak
        if (request.VehicleType.ToLower().Contains("hyper"))
        {
            if (result.TimeMultiplierName == "Peak" || result.TimeMultiplierName == "Special Peak")
            {
                // No discounts apply for Hyper-Sports during peak
                result.DurationDiscount = 0;
                finalAmount = afterDemand * (1 + result.WeekendSurcharge);
            }
        }

        result.FinalAmount = Math.Round(finalAmount, 2);
        result.TotalDiscount = result.DurationDiscount + result.EvBonus;
        result.TotalSurcharge = result.WeekendSurcharge + (result.DemandMultiplier - 1);

        return result;
    }

    private decimal GetTimeMultiplier(DateTime startTime)
    {
        var hour = startTime.Hour;

        // Off-Peak: 10 PM – 6 AM (0.8×)
        if (hour >= 22 || hour < 6)
            return 0.8m;

        // Peak Hours: 8–11 AM, 5–9 PM (1.25×)
        if ((hour >= 8 && hour < 11) || (hour >= 17 && hour < 21))
            return 1.25m;

        // Normal Hours (1.0×)
        return 1.0m;
    }

    private string GetTimeMultiplierName(DateTime startTime)
    {
        var hour = startTime.Hour;
        var isWeekend = startTime.DayOfWeek == DayOfWeek.Saturday || startTime.DayOfWeek == DayOfWeek.Sunday;

        if (isWeekend && (hour >= 10 && hour < 22))
            return "Special Peak";

        if (hour >= 22 || hour < 6)
            return "Off-Peak";

        if ((hour >= 8 && hour < 11) || (hour >= 17 && hour < 21))
            return "Peak";

        return "Normal";
    }

    private decimal GetVehicleMultiplier(string vehicleType)
    {
        return vehicleType.ToLower() switch
        {
            "mini" or "hatchback" => 1.0m,
            "sedan" or "compact_suv" => 1.1m,
            "luxury_sedan" or "luxury_suv" or "suv" => 1.25m,
            "ev" or "hybrid" or "electric" => 1.2m,
            "utility" or "van" => 1.15m,
            "sports" => 1.5m,
            "hyper_sports" or "hypercar" => 2.0m,
            _ => 1.0m // Default for unknown types
        };
    }

    private decimal GetDurationDiscount(double hours, string vehicleType)
    {
        var isSportsOrHyper = vehicleType.ToLower().Contains("sports") || vehicleType.ToLower().Contains("hyper");
        var maxDiscount = isSportsOrHyper ? 0.10m : 0.30m; // Sports/Hyper limited to 10%

        if (hours < 3)
            return 0m;
        if (hours < 6)
            return Math.Min(0.10m, maxDiscount);
        if (hours < 24)
            return Math.Min(0.20m, maxDiscount);
        
        return maxDiscount; // 30% for 24+ hours (or 10% for sports)
    }

    private decimal GetDemandMultiplier(double availabilityPercentage, string vehicleType)
    {
        var isSportsOrHyper = vehicleType.ToLower().Contains("sports") || vehicleType.ToLower().Contains("hyper");

        // Sports/Hyper jump to +50% when availability <20%
        if (isSportsOrHyper && availabilityPercentage < 20)
            return 1.50m;

        if (availabilityPercentage > 50)
            return 1.0m;
        if (availabilityPercentage > 30)
            return 1.10m;
        if (availabilityPercentage > 10)
            return 1.20m;
        
        return 1.35m; // <10% available
    }

    private bool IsWeekend(DateTime date)
    {
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
    }
}

public class PricingRequest
{
    public decimal BaseHourlyRate { get; set; }
    public double DurationHours { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string VehicleType { get; set; } = "hatchback";
    public double AvailabilityPercentage { get; set; } = 100; // Default to full availability
    public bool IsSubscriber { get; set; }
    public string? SubscriptionTier { get; set; } // "basic", "premium", "premium_plus"
}

public class PricingResult
{
    public decimal BaseHourlyRate { get; set; }
    public double DurationHours { get; set; }
    public decimal BaseAmount { get; set; }
    
    public decimal TimeMultiplier { get; set; }
    public string TimeMultiplierName { get; set; } = "";
    
    public decimal VehicleMultiplier { get; set; }
    public string VehicleMultiplierName { get; set; } = "";
    
    public decimal DurationDiscount { get; set; }
    
    public decimal DemandMultiplier { get; set; }
    
    public decimal WeekendSurcharge { get; set; }
    
    public decimal EvBonus { get; set; }
    
    public decimal TotalDiscount { get; set; }
    public decimal TotalSurcharge { get; set; }
    
    public decimal FinalAmount { get; set; }
}
