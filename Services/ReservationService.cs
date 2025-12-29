using Stationnement.Web.Models;
using Stationnement.Web.Repositories;

namespace Stationnement.Web.Services;

public interface IReservationService
{
    Task<(bool Success, string Message, Reservation? Reservation)> CreateReservationAsync(
        Guid userId, Guid slotId, DateTime startTime, DateTime endTime, string? vehiclePlate, string? vehicleType,
        decimal? frontendTotalAmount = null, decimal? frontendBaseAmount = null, decimal? frontendDiscountAmount = null, decimal? frontendSurchargeAmount = null);
    Task<(bool Success, string Message, decimal RefundAmount, string? RefundMethod, string? RefundUpiId)> CancelReservationAsync(Guid reservationId, Guid userId, string? refundMethod = null, string? refundUpiId = null);
    Task<(decimal BaseAmount, decimal DiscountAmount, decimal SurchargeAmount, decimal TotalAmount, string? DiscountSource)> CalculatePriceAsync(
        Guid slotId, DateTime startTime, DateTime endTime, Guid? userId = null, string? vehicleType = null);
    Task<(bool Success, string Message)> CheckInAsync(string qrCode);
    Task<(bool Success, string Message)> CheckOutAsync(string qrCode);
}

public class ReservationService : IReservationService
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IParkingRepository _parkingRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IPricingService _pricingService;

    public ReservationService(
        IReservationRepository reservationRepository, 
        IParkingRepository parkingRepository,
        IPaymentRepository paymentRepository,
        ISubscriptionService subscriptionService,
        IPricingService pricingService)
    {
        _reservationRepository = reservationRepository;
        _parkingRepository = parkingRepository;
        _paymentRepository = paymentRepository;
        _subscriptionService = subscriptionService;
        _pricingService = pricingService;
    }

    public async Task<(bool Success, string Message, Reservation? Reservation)> CreateReservationAsync(
        Guid userId, Guid slotId, DateTime startTime, DateTime endTime, string? vehiclePlate, string? vehicleType,
        decimal? frontendTotalAmount = null, decimal? frontendBaseAmount = null, decimal? frontendDiscountAmount = null, decimal? frontendSurchargeAmount = null)
    {
        // Log for debugging
        Console.WriteLine($"[BOOKING SERVICE] Creating reservation: Start={startTime}, End={endTime}, Now={DateTime.UtcNow}");
        
        // Validate vehicle type restrictions
        if (!string.IsNullOrEmpty(vehicleType))
        {
            var vehicleTypeLower = vehicleType.ToLower();
            if (vehicleTypeLower.Contains("sports") || vehicleTypeLower.Contains("hyper"))
            {
                var canUse = await _subscriptionService.CanUseVehicleTypeAsync(userId, vehicleType);
                if (!canUse)
                {
                    if (vehicleTypeLower.Contains("hyper"))
                        return (false, "Hyper-Sports vehicles require a Premium subscription. Please upgrade to book this vehicle type.", null);
                    else
                        return (false, "Sports vehicles require a Pro or Premium subscription. Please upgrade to book this vehicle type.", null);
                }
            }
        }
        
        // Validate times
        if (startTime >= endTime)
            return (false, "End time must be after start time", null);

        // Be lenient with timezone differences - allow 24h in past for testing
        if (startTime < DateTime.UtcNow.AddHours(-24))
            return (false, "Start time cannot be more than 24 hours in the past", null);

        // Check availability - this checks across ALL users to prevent double booking
        var isAvailable = await _reservationRepository.IsSlotAvailableAsync(slotId, startTime, endTime);
        if (!isAvailable)
            return (false, "This slot has already been booked by another user for the selected time. Please choose a different slot or time.", null);

        // Use frontend-calculated pricing if provided, otherwise calculate on backend
        decimal baseAmount, discountAmount, surchargeAmount, totalAmount;
        string? discountSource = null;
        
        if (frontendTotalAmount.HasValue && frontendTotalAmount.Value > 0)
        {
            // Use exact frontend pricing - this ensures displayed price matches stored price
            totalAmount = frontendTotalAmount.Value;
            baseAmount = frontendBaseAmount ?? frontendTotalAmount.Value;
            discountAmount = frontendDiscountAmount ?? 0;
            surchargeAmount = frontendSurchargeAmount ?? 0;
            Console.WriteLine($"[PRICING] Using frontend pricing: Total=₹{totalAmount}, Base=₹{baseAmount}, Discount=₹{discountAmount}, Surcharge=₹{surchargeAmount}");
        }
        else
        {
            // Fallback: calculate on backend (for API calls without frontend pricing)
            (baseAmount, discountAmount, surchargeAmount, totalAmount, discountSource) = 
                await CalculatePriceAsync(slotId, startTime, endTime, userId, vehicleType);
            Console.WriteLine($"[PRICING] Calculated backend pricing: Total=₹{totalAmount}, Base=₹{baseAmount}");
        }

        // Create reservation
        var reservation = new Reservation
        {
            UserId = userId,
            SlotId = slotId,
            StartTime = startTime,
            EndTime = endTime,
            Status = "confirmed",
            VehiclePlate = vehiclePlate,
            VehicleType = vehicleType,
            BaseAmount = baseAmount,
            DiscountAmount = discountAmount,
            SurchargeAmount = surchargeAmount,
            TotalAmount = totalAmount
        };

        await _reservationRepository.CreateAsync(reservation);
        
        // Create payment record for the reservation
        try
        {
            var payment = new Models.Payment
            {
                UserId = userId,
                ReservationId = reservation.Id,
                Amount = totalAmount,
                Currency = "INR",
                PaymentMethod = "UPI",
                Status = "completed", // Payment is considered completed upon reservation creation
                CreatedAt = DateTime.UtcNow
            };
            var createdPayment = await _paymentRepository.CreateAsync(payment);
            Console.WriteLine($"[SUCCESS] Payment record created: {createdPayment.Id} for reservation {reservation.Id}, amount: ₹{totalAmount}");
        }
        catch (Exception ex)
        {
            // Log but don't fail the reservation if payment record creation fails
            Console.WriteLine($"[WARNING] Failed to create payment record for reservation {reservation.Id}: {ex.Message}");
            Console.WriteLine($"[WARNING] Stack trace: {ex.StackTrace}");
        }
        
        return (true, "Reservation created successfully", reservation);
    }

    public async Task<(bool Success, string Message, decimal RefundAmount, string? RefundMethod, string? RefundUpiId)> CancelReservationAsync(Guid reservationId, Guid userId, string? refundMethod = null, string? refundUpiId = null)
    {
        var reservation = await _reservationRepository.GetByIdAsync(reservationId);
        if (reservation == null)
            return (false, "Reservation not found", 0, null, null);

        if (reservation.UserId != userId)
            return (false, "Not authorized to cancel this reservation", 0, null, null);

        if (reservation.Status == "cancelled")
            return (false, "Reservation is already cancelled", 0, null, null);

        if (reservation.Status == "completed")
            return (false, "Cannot cancel a completed reservation", 0, null, null);

        if (reservation.Status == "active")
            return (false, "Cannot cancel an active reservation (already checked in)", 0, null, null);

        // Check 1-hour before start time restriction
        var hoursUntilStart = (reservation.StartTime - DateTime.UtcNow).TotalHours;
        if (hoursUntilStart < 1)
            return (false, "Cancellation is only allowed at least 1 hour before the parking start time", 0, null, null);

        // Calculate refund with 10% cancellation fee
        const decimal cancellationFeePercent = 0.10m; // 10% fee
        var cancellationFee = Math.Round(reservation.TotalAmount * cancellationFeePercent, 2);
        var refundAmount = Math.Round(reservation.TotalAmount - cancellationFee, 2);

        // Default refund method to UPI if not specified
        var finalRefundMethod = string.IsNullOrEmpty(refundMethod) ? "UPI" : refundMethod;
        var finalUpiId = finalRefundMethod == "UPI" ? refundUpiId : null;

        // Update reservation status
        reservation.Status = "cancelled";
        reservation.CancelledAt = DateTime.UtcNow;
        reservation.RefundAmount = refundAmount;
        reservation.RefundMethod = finalRefundMethod;
        reservation.RefundUpiId = finalUpiId;
        await _reservationRepository.UpdateAsync(reservation);

        // Log the refund processing
        var upiIdLog = !string.IsNullOrEmpty(finalUpiId) ? $" to UPI ID: {finalUpiId}" : "";
        Console.WriteLine($"[REFUND] Processing refund of ₹{refundAmount} via {finalRefundMethod}{upiIdLog} for reservation {reservationId}");

        return (true, $"Reservation cancelled. Refund of ₹{refundAmount} will be processed via {finalRefundMethod} (₹{cancellationFee} cancellation fee deducted)", refundAmount, finalRefundMethod, finalUpiId);
    }

    public async Task<(decimal BaseAmount, decimal DiscountAmount, decimal SurchargeAmount, decimal TotalAmount, string? DiscountSource)> CalculatePriceAsync(
        Guid slotId, DateTime startTime, DateTime endTime, Guid? userId = null, string? vehicleType = null)
    {
        var slot = await _parkingRepository.GetSlotByIdAsync(slotId);
        if (slot == null)
            return (0, 0, 0, 0, null);

        var location = await _parkingRepository.GetLocationByIdAsync(slot.LocationId);
        if (location == null)
            return (0, 0, 0, 0, null);

        // Calculate hours
        var hours = (endTime - startTime).TotalHours;
        
        // Get availability percentage for demand-based pricing
        double availabilityPercentage = 100;
        try
        {
            // Calculate availability: (available / total) * 100
            if (location.TotalSlots > 0)
            {
                availabilityPercentage = ((double)location.AvailableSlots / location.TotalSlots) * 100;
            }
        }
        catch { /* Use default 100% if calculation fails */ }
        
        // Use PricingService for consistent pricing with frontend
        var pricingRequest = new PricingRequest
        {
            BaseHourlyRate = location.BasePricePerHour * (slot.PriceMultiplier ?? 1.0m),
            DurationHours = hours,
            StartTime = startTime,
            EndTime = endTime,
            VehicleType = vehicleType ?? "hatchback",
            AvailabilityPercentage = availabilityPercentage
        };
        
        var pricingResult = _pricingService.CalculatePrice(pricingRequest);
        
        // Calculate breakdown for reservation storage
        var baseAmount = pricingResult.BaseAmount;
        var discountAmount = pricingResult.BaseAmount * pricingResult.TotalDiscount;
        var surchargeAmount = pricingResult.BaseAmount * pricingResult.TotalSurcharge;
        var totalAmount = pricingResult.FinalAmount;
        string? discountSource = null;
        
        Console.WriteLine($"[PRICING] Base: ₹{baseAmount}, Hours: {hours:F2}, Vehicle: {vehicleType}, TimeMultiplier: {pricingResult.TimeMultiplier}, VehicleMultiplier: {pricingResult.VehicleMultiplier}, Final: ₹{totalAmount}");
        
        // Apply subscription discount on top of PricingService calculation
        if (userId.HasValue)
        {
            Console.WriteLine($"[PRICING] Checking subscription for user: {userId.Value}");
            var subInfo = await _subscriptionService.GetUserSubscriptionAsync(userId.Value);
            Console.WriteLine($"[PRICING] Subscription info: Tier={subInfo?.Tier ?? "null"}, IsActive={subInfo?.IsActive}");
            if (subInfo != null && subInfo.Tier != "free")
            {
                var discountPercent = _subscriptionService.GetDiscountPercentage(subInfo.Tier);
                if (discountPercent > 0)
                {
                    var subscriptionDiscount = totalAmount * discountPercent;
                    discountAmount += subscriptionDiscount;
                    totalAmount = Math.Max(0, totalAmount - subscriptionDiscount);
                    discountSource = $"{subInfo.TierName} subscription ({discountPercent * 100:F0}% off final price)";
                    Console.WriteLine($"[PRICING] Subscription discount applied: -{subscriptionDiscount:F2}, New total: ₹{totalAmount}");
                }
            }
        }
        else
        {
            Console.WriteLine($"[PRICING] No userId provided, skipping subscription check");
        }

        return (Math.Round(baseAmount, 2), Math.Round(discountAmount, 2), Math.Round(surchargeAmount, 2), Math.Round(totalAmount, 2), discountSource);
    }

    public async Task<(bool Success, string Message)> CheckInAsync(string qrCode)
    {
        var reservation = await _reservationRepository.GetByQrCodeAsync(qrCode);
        if (reservation == null)
            return (false, "Invalid QR code");

        if (reservation.Status != "confirmed")
            return (false, $"Reservation status is {reservation.Status}");

        reservation.Status = "active";
        reservation.ActualEntryTime = DateTime.UtcNow;
        await _reservationRepository.UpdateAsync(reservation);
        return (true, "Check-in successful");
    }

    public async Task<(bool Success, string Message)> CheckOutAsync(string qrCode)
    {
        var reservation = await _reservationRepository.GetByQrCodeAsync(qrCode);
        if (reservation == null)
            return (false, "Invalid QR code");

        if (reservation.Status != "active")
            return (false, $"Reservation status is {reservation.Status}");

        reservation.Status = "completed";
        reservation.ActualExitTime = DateTime.UtcNow;

        // Check for overstay
        if (DateTime.UtcNow > reservation.EndTime)
        {
            var overstayHours = (decimal)(DateTime.UtcNow - reservation.EndTime).TotalHours;
            var overstayCharge = overstayHours * (reservation.BaseAmount / (decimal)(reservation.EndTime - reservation.StartTime).TotalHours) * 1.5m;
            reservation.SurchargeAmount += Math.Round(overstayCharge, 2);
            reservation.TotalAmount += Math.Round(overstayCharge, 2);
        }

        await _reservationRepository.UpdateAsync(reservation);
        return (true, "Check-out successful");
    }
}
