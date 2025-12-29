using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stationnement.Web.Repositories;
using Stationnement.Web.Services;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReservationController : ControllerBase
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IReservationService _reservationService;

    public ReservationController(IReservationRepository reservationRepository, IReservationService reservationService)
    {
        _reservationRepository = reservationRepository;
        _reservationService = reservationService;
    }

    public class CreateReservationRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("slotId")]
        public Guid SlotId { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("startTime")]
        public DateTimeOffset StartTime { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("endTime")]
        public DateTimeOffset EndTime { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("vehiclePlate")]
        public string? VehiclePlate { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("vehicleType")]
        public string? VehicleType { get; set; }
        
        // Pricing fields from frontend calculation - use these exact amounts
        [System.Text.Json.Serialization.JsonPropertyName("totalAmount")]
        public decimal? TotalAmount { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("baseAmount")]
        public decimal? BaseAmount { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("discountAmount")]
        public decimal? DiscountAmount { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("surchargeAmount")]
        public decimal? SurchargeAmount { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetMyReservations()
    {
        var userId = GetUserId();
        var reservations = await _reservationRepository.GetByUserIdAsync(userId);
        return Ok(new { success = true, data = reservations });
    }

    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming()
    {
        var userId = GetUserId();
        var reservations = await _reservationRepository.GetUpcomingByUserIdAsync(userId);
        return Ok(new { success = true, data = reservations });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var reservation = await _reservationRepository.GetByIdAsync(id);
        if (reservation == null)
            return NotFound(new { success = false, message = "Reservation not found" });

        if (reservation.UserId != GetUserId() && !User.IsInRole("Admin"))
            return Forbid();

        return Ok(new { success = true, data = reservation });
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateReservationRequest request)
    {
        var userId = GetUserId();
        
        // Log for debugging
        Console.WriteLine($"[BOOKING] Received: SlotId={request.SlotId}, Start={request.StartTime}, End={request.EndTime}, Plate={request.VehiclePlate}");
        Console.WriteLine($"[BOOKING] Frontend pricing: Total={request.TotalAmount}, Base={request.BaseAmount}, Discount={request.DiscountAmount}, Surcharge={request.SurchargeAmount}");
        
        // Convert DateTimeOffset to UTC DateTime
        var startUtc = request.StartTime.UtcDateTime;
        var endUtc = request.EndTime.UtcDateTime;
        
        var (success, message, reservation) = await _reservationService.CreateReservationAsync(
            userId, request.SlotId, startUtc, endUtc, 
            request.VehiclePlate, request.VehicleType,
            request.TotalAmount, request.BaseAmount, request.DiscountAmount, request.SurchargeAmount);

        if (!success)
            return BadRequest(new { success = false, message });

        // Create notification for successful booking
        try {
            using var client = new HttpClient();
            client.BaseAddress = new Uri($"{Request.Scheme}://{Request.Host}");
            await client.PostAsJsonAsync("/api/notification/create", new {
                userId = userId,
                title = "Booking Confirmed!",
                message = $"Your parking slot has been booked for ₹{reservation!.TotalAmount:F2}",
                type = "success"
            });
        } catch { /* Notification is optional */ }

        return Ok(new
        {
            success = true,
            message,
            data = new { reservation!.Id, reservation.QrCode, reservation.TotalAmount }
        });
    }

    public class CancelRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("refundMethod")]
        public string? RefundMethod { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("refundUpiId")]
        public string? RefundUpiId { get; set; }
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest? request = null)
    {
        var userId = GetUserId();
        var refundMethod = request?.RefundMethod ?? "UPI";
        var refundUpiId = request?.RefundUpiId;
        
        Console.WriteLine($"[CANCEL] User {userId} cancelling reservation {id}, refund method: {refundMethod}, UPI ID: {refundUpiId ?? "N/A"}");
        
        var (success, message, refundAmount, actualRefundMethod, actualUpiId) = await _reservationService.CancelReservationAsync(id, userId, refundMethod, refundUpiId);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data = new { refundAmount, refundMethod = actualRefundMethod, refundUpiId = actualUpiId } });
    }

    [HttpPost("checkin")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckIn([FromQuery] string qrCode)
    {
        var (success, message) = await _reservationService.CheckInAsync(qrCode);
        if (!success)
            return BadRequest(new { success = false, message });
        return Ok(new { success = true, message });
    }

    [HttpPost("checkout")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckOut([FromQuery] string qrCode)
    {
        var (success, message) = await _reservationService.CheckOutAsync(qrCode);
        if (!success)
            return BadRequest(new { success = false, message });
        return Ok(new { success = true, message });
    }

    /// <summary>
    /// Verify a reservation by QR/barcode - shows full info and checks if still valid
    /// </summary>
    [HttpGet("verify/{qrCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyReservation(string qrCode)
    {
        if (string.IsNullOrEmpty(qrCode))
            return BadRequest(new { success = false, message = "QR code is required", valid = false });

        // Get reservation by QR code
        var reservation = await _reservationRepository.GetByQrCodeAsync(qrCode);
        
        if (reservation == null)
            return NotFound(new { 
                success = false, 
                message = "Invalid barcode - No reservation found", 
                valid = false 
            });

        var now = DateTime.UtcNow;
        var isExpired = now > reservation.EndTime;
        var isNotStarted = now < reservation.StartTime;
        var isActive = !isExpired && !isNotStarted;
        var isCancelled = reservation.Status == "cancelled";

        // Determine validity status
        string validityStatus;
        bool isValid;
        
        if (isCancelled)
        {
            validityStatus = "CANCELLED";
            isValid = false;
        }
        else if (reservation.ActualExitTime != null)
        {
            validityStatus = "COMPLETED"; // Already checked out
            isValid = false;
        }
        else if (reservation.ActualEntryTime != null)
        {
            validityStatus = "ALREADY_SCANNED"; // Already checked in
            // We set valid to true so the admin can see details, but the UI should show it as "Already Scanned"
            // and perhaps only allow Check-Out, not Check-In.
            // User said "no longer to scan", implying it shouldn't work for entry.
            // I'll set isValid = true so we can show the "Already Scanned" UI with checks,
            // rather than a generic "Invalid" error.
            isValid = true; 
        }
        else if (isExpired)
        {
            validityStatus = "EXPIRED";
            isValid = false;
        }
        else if (isNotStarted)
        {
            validityStatus = "NOT_STARTED";
            isValid = true;
        }
        else
        {
            validityStatus = "ACTIVE";
            isValid = true;
        }

        // Format time remaining or time since expiry
        string timeMessage;
        if (isExpired)
        {
            var expiredAgo = now - reservation.EndTime;
            timeMessage = $"Expired {FormatTimeSpan(expiredAgo)} ago";
        }
        else if (isNotStarted)
        {
            var startsIn = reservation.StartTime - now;
            timeMessage = $"Starts in {FormatTimeSpan(startsIn)}";
        }
        else
        {
            var endsIn = reservation.EndTime - now;
            timeMessage = $"Valid for {FormatTimeSpan(endsIn)} more";
        }

        return Ok(new { 
            success = true, 
            valid = isValid,
            validityStatus,
            timeMessage,
            data = new {
                reservationCode = reservation.QrCode,
                status = reservation.Status,
                startTime = reservation.StartTime,
                endTime = reservation.EndTime,
                checkInTime = reservation.ActualEntryTime,
                checkOutTime = reservation.ActualExitTime,
                vehiclePlate = reservation.VehiclePlate,
                vehicleType = reservation.VehicleType,
                totalAmount = reservation.TotalAmount,
                discountAmount = reservation.DiscountAmount,
                slotCode = reservation.SlotCode,
                locationName = reservation.LocationName,
                locationAddress = reservation.LocationAddress,
                isExpired,
                isActive,
                createdAt = reservation.CreatedAt
            }
        });
    }

    private string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var userId = GetUserId();
        var allReservations = await _reservationRepository.GetByUserIdAsync(userId);
        
        var now = DateTime.UtcNow;
        var activeCount = 0;
        var upcomingCount = 0;
        decimal totalHoursParked = 0;
        decimal totalSaved = 0;
        
        foreach (var r in allReservations)
        {
            // Count active (currently happening)
            if (r.Status == "confirmed" && r.StartTime <= now && r.EndTime >= now)
                activeCount++;
            // Count upcoming (future reservations)
            else if (r.Status == "confirmed" && r.StartTime > now)
                upcomingCount++;
            
            // Calculate total hours for completed/active reservations
            if (r.Status == "confirmed" || r.Status == "completed" || r.Status == "active")
            {
                var endTime = r.EndTime < now ? r.EndTime : now;
                if (r.StartTime <= endTime)
                {
                    totalHoursParked += (decimal)(endTime - r.StartTime).TotalHours;
                }
            }
            
            // Sum up discount amounts as "saved"
            if (r.DiscountAmount > 0)
                totalSaved += r.DiscountAmount;
        }
        
        return Ok(new { 
            success = true, 
            data = new { 
                activeCount,
                upcomingCount,
                totalHoursParked = Math.Round(totalHoursParked, 1),
                totalSaved = Math.Round(totalSaved, 2)
            }
        });
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }
}
