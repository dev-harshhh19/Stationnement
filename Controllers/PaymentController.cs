using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using System.Drawing;
using Stationnement.Web.Services;
using Stationnement.Web.Repositories;
using Stationnement.Web.Models;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IPricingService _pricingService;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IReservationRepository _reservationRepository;

    public PaymentController(
        IConfiguration configuration, 
        IPricingService pricingService,
        IPaymentRepository paymentRepository,
        IReservationRepository reservationRepository)
    {
        _configuration = configuration;
        _pricingService = pricingService;
        _paymentRepository = paymentRepository;
        _reservationRepository = reservationRepository;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetMyPayments()
    {
        var userId = GetUserId();
        var payments = await _paymentRepository.GetByUserIdAsync(userId);
        
        // Enrich with reservation details
        var paymentList = new List<object>();
        foreach (var payment in payments)
        {
            var paymentObj = new
            {
                id = payment.Id,
                amount = payment.Amount,
                currency = payment.Currency,
                paymentMethod = payment.PaymentMethod,
                status = payment.Status,
                createdAt = payment.CreatedAt,
                reservationId = payment.ReservationId,
                transactionId = payment.StripePaymentId ?? $"STN-{payment.Id.ToString()[..8].ToUpper()}",
                reservationDetails = payment.ReservationId.HasValue 
                    ? await GetReservationDetails(payment.ReservationId.Value)
                    : null
            };
            paymentList.Add(paymentObj);
        }
        
        return Ok(new { success = true, data = paymentList });
    }
    
    private async Task<object?> GetReservationDetails(Guid reservationId)
    {
        var reservation = await _reservationRepository.GetByIdAsync(reservationId);
        if (reservation == null) return null;
        
        return new
        {
            locationName = reservation.LocationName,
            slotCode = reservation.SlotCode,
            startTime = reservation.StartTime,
            endTime = reservation.EndTime,
            vehiclePlate = reservation.VehiclePlate,
            qrCode = reservation.QrCode
        };
    }
    
    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    /// <summary>
    /// Calculate dynamic pricing with all factors
    /// </summary>
    [HttpPost("calculate-price")]
    public IActionResult CalculatePrice([FromBody] CalculatePriceRequest request)
    {
        try
        {
            var pricingRequest = new PricingRequest
            {
                BaseHourlyRate = request.BaseHourlyRate,
                DurationHours = request.DurationHours,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                VehicleType = request.VehicleType ?? "hatchback",
                AvailabilityPercentage = request.AvailabilityPercentage
            };

            var result = _pricingService.CalculatePrice(pricingRequest);

            return Ok(new
            {
                success = true,
                data = new
                {
                    baseAmount = result.BaseAmount,
                    timeMultiplier = result.TimeMultiplier,
                    timeMultiplierName = result.TimeMultiplierName,
                    vehicleMultiplier = result.VehicleMultiplier,
                    vehicleType = result.VehicleMultiplierName,
                    durationDiscount = result.DurationDiscount,
                    demandMultiplier = result.DemandMultiplier,
                    weekendSurcharge = result.WeekendSurcharge,
                    evBonus = result.EvBonus,
                    totalDiscount = result.TotalDiscount,
                    totalSurcharge = result.TotalSurcharge,
                    finalAmount = result.FinalAmount,
                    durationHours = result.DurationHours
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    public class CalculatePriceRequest
    {
        public decimal BaseHourlyRate { get; set; }
        public double DurationHours { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? VehicleType { get; set; }
        public double AvailabilityPercentage { get; set; } = 100;
    }

    public class UpiPaymentRequest
    {
        public decimal Amount { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generates a UPI QR code for payment
    /// </summary>
    [HttpPost("generate-upi-qr")]
    public IActionResult GenerateUpiQr([FromBody] UpiPaymentRequest request)
    {
        try
        {
            // UPI Configuration - can be moved to appsettings.json
            var upiId = _configuration["UpiSettings:UpiId"] ?? "merchant@upi";
            var merchantName = _configuration["UpiSettings:MerchantName"] ?? "Stationnement Parking";
            
            // Amount is already in INR
            var amountInr = Math.Round(request.Amount, 2);
            
            // Build UPI payment string
            // Format: upi://pay?pa=UPI_ID&pn=NAME&am=AMOUNT&tr=TXN_ID&tn=NOTE&cu=INR
            var upiString = $"upi://pay?" +
                $"pa={Uri.EscapeDataString(upiId)}" +
                $"&pn={Uri.EscapeDataString(merchantName)}" +
                $"&am={amountInr}" +
                $"&tr={Uri.EscapeDataString(request.TransactionId)}" +
                $"&tn={Uri.EscapeDataString(request.Note)}" +
                $"&cu=INR";

            // Generate QR code using QRCoder
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(upiString, QRCodeGenerator.ECCLevel.M);
            
            // Generate as PNG
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(10); // 10 pixels per module
            
            // Convert to base64
            var base64Image = Convert.ToBase64String(qrCodeBytes);
            
            return Ok(new
            {
                success = true,
                data = new
                {
                    qrCodeBase64 = $"data:image/png;base64,{base64Image}",
                    upiId = upiId,
                    merchantName = merchantName,
                    amountInr = amountInr,
                    transactionId = request.TransactionId,
                    upiString = upiString
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Verifies a UPI payment (mock implementation - integrate with actual payment gateway)
    /// </summary>
    [HttpPost("verify-payment")]
    public IActionResult VerifyPayment([FromBody] VerifyPaymentRequest request)
    {
        // In production, this would:
        // 1. Query your payment gateway API with the transaction ID
        // 2. Check if payment status is "SUCCESS"
        // 3. Return appropriate response

        // For demo purposes, we'll simulate a successful payment
        return Ok(new
        {
            success = true,
            data = new
            {
                transactionId = request.TransactionId,
                status = "SUCCESS",
                message = "Payment verified successfully"
            }
        });
    }

    /// <summary>
    /// Generate a downloadable receipt
    /// </summary>
    [HttpGet("receipt/{paymentOrReservationId}")]
    [AllowAnonymous] // Allow access for receipt downloads
    public async Task<IActionResult> GenerateReceipt(Guid paymentOrReservationId, [FromQuery] string location = "", 
        [FromQuery] string date = "", [FromQuery] decimal amount = 0, [FromQuery] string slot = "", 
        [FromQuery] string transactionId = "")
    {
        // Try to get payment details first, fallback to reservation
        Models.Payment? payment = null;
        Models.Reservation? reservation = null;
        
        try
        {
            payment = await _paymentRepository.GetByIdAsync(paymentOrReservationId);
            if (payment is not null && payment.ReservationId.HasValue)
            {
                reservation = await _reservationRepository.GetByIdAsync(payment.ReservationId.Value);
            }
        }
        catch { }
        
        // If no payment found, try as reservation ID
        if (payment is null)
        {
            try
            {
                reservation = await _reservationRepository.GetByIdAsync(paymentOrReservationId);
            }
            catch { }
        }
        
        // Use data from payment/reservation if available, otherwise use query params
        var finalLocation = reservation?.LocationName ?? location;
        var finalSlot = reservation?.SlotCode ?? slot;
        var finalDate = reservation?.StartTime.ToString("dd MMM yyyy") ?? date;
        var finalAmount = payment?.Amount ?? reservation?.TotalAmount ?? amount;
        var finalTransactionId = payment?.StripePaymentId ?? transactionId ?? paymentOrReservationId.ToString()[..8].ToUpper();
        
        // Build HTML using StringBuilder to avoid encoding issues
        var html = new System.Text.StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset=\"UTF-8\">");
        html.AppendLine($"    <title>Parking Receipt - {paymentOrReservationId}</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: 'Segoe UI', Arial, sans-serif; padding: 40px; max-width: 600px; margin: 0 auto; }");
        html.AppendLine("        .header { text-align: center; border-bottom: 2px solid #4F46E5; padding-bottom: 20px; margin-bottom: 30px; }");
        html.AppendLine("        .logo { font-size: 28px; font-weight: bold; color: #4F46E5; }");
        html.AppendLine("        .receipt-title { font-size: 14px; color: #666; margin-top: 10px; }");
        html.AppendLine("        .receipt-id { font-size: 12px; color: #999; }");
        html.AppendLine("        .details { margin: 20px 0; }");
        html.AppendLine("        .row { display: flex; justify-content: space-between; padding: 12px 0; border-bottom: 1px solid #eee; }");
        html.AppendLine("        .label { color: #666; }");
        html.AppendLine("        .value { font-weight: 600; color: #333; }");
        html.AppendLine("        .total { font-size: 24px; text-align: center; margin: 30px 0; padding: 20px; background: #F3F4F6; border-radius: 12px; }");
        html.AppendLine("        .total-label { color: #666; font-size: 14px; }");
        html.AppendLine("        .total-amount { color: #4F46E5; font-weight: bold; }");
        html.AppendLine("        .footer { text-align: center; margin-top: 40px; color: #999; font-size: 12px; }");
        html.AppendLine("        .status { display: inline-block; padding: 4px 12px; background: #D1FAE5; color: #059669; border-radius: 20px; font-size: 12px; font-weight: 600; }");
        html.AppendLine("        @media print { body { padding: 20px; } .no-print { display: none; } }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <div class=\"header\">");
        html.AppendLine("        <div class=\"logo\">STATIONNEMENT</div>");
        html.AppendLine("        <div class=\"receipt-title\">PARKING RECEIPT</div>");
        html.AppendLine($"        <div class=\"receipt-id\">Receipt #STN-{finalTransactionId}</div>");
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"details\">");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Transaction ID</span>");
        html.AppendLine($"            <span class=\"value\">STN-{finalTransactionId}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Location</span>");
        html.AppendLine($"            <span class=\"value\">{(string.IsNullOrEmpty(finalLocation) ? "Parking Location" : finalLocation)}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Slot</span>");
        html.AppendLine($"            <span class=\"value\">{(string.IsNullOrEmpty(finalSlot) ? "Standard" : finalSlot)}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Date</span>");
        html.AppendLine($"            <span class=\"value\">{(string.IsNullOrEmpty(finalDate) ? DateTime.Now.ToString("dd MMM yyyy") : finalDate)}</span>");
        html.AppendLine("        </div>");
        if (reservation is not null)
        {
            html.AppendLine("        <div class=\"row\">");
            html.AppendLine("            <span class=\"label\">Vehicle Plate</span>");
            html.AppendLine($"            <span class=\"value\">{(reservation.VehiclePlate ?? "N/A")}</span>");
            html.AppendLine("        </div>");
            html.AppendLine("        <div class=\"row\">");
            html.AppendLine("            <span class=\"label\">Time</span>");
            html.AppendLine($"            <span class=\"value\">{reservation.StartTime:HH:mm} - {reservation.EndTime:HH:mm}</span>");
            html.AppendLine("        </div>");
        }
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Status</span>");
        html.AppendLine("            <span class=\"status\">PAID</span>");
        html.AppendLine("        </div>");
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"total\">");
        html.AppendLine("        <div class=\"total-label\">Amount Paid</div>");
        html.AppendLine($"        <div class=\"total-amount\">Rs. {finalAmount:F2}</div>");
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"footer\">");
        html.AppendLine("        <p>Thank you for using Stationnement!</p>");
        html.AppendLine($"        <p>Generated on {DateTime.Now:dd MMM yyyy, hh:mm tt}</p>");
        html.AppendLine("        <button class=\"no-print\" onclick=\"window.print()\" style=\"margin-top:20px; padding:10px 30px; background:#4F46E5; color:white; border:none; border-radius:8px; cursor:pointer;\">Print Receipt</button>");
        html.AppendLine("    </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        
        var receiptHtml = html.ToString();

        Response.ContentType = "text/html; charset=utf-8";
        return Content(receiptHtml);
    }

    /// <summary>
    /// Generate a downloadable barcode for a reservation
    /// </summary>
    [HttpGet("barcode/{reservationId}")]
    [AllowAnonymous] // Allow access for barcode downloads
    public async Task<IActionResult> GenerateBarcode(Guid reservationId)
    {
        Models.Reservation? reservation = null;
        
        try
        {
            reservation = await _reservationRepository.GetByIdAsync(reservationId);
        }
        catch { }
        
        if (reservation == null)
        {
            return NotFound(new { success = false, message = "Reservation not found" });
        }
        
        var location = reservation.LocationName ?? "Parking Location";
        var slot = reservation.SlotCode ?? "Standard";
        var date = reservation.StartTime.ToString("dd MMM yyyy");
        var time = $"{reservation.StartTime:HH:mm} - {reservation.EndTime:HH:mm}";
        var vehiclePlate = reservation.VehiclePlate ?? "N/A";
        var amount = reservation.TotalAmount > 0 ? $"Rs. {reservation.TotalAmount:F2}" : "";
        var qrCode = reservation.QrCode ?? reservation.Id.ToString()[..8].ToUpper();
        
        // Build HTML using StringBuilder to avoid encoding issues
        var html = new System.Text.StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset=\"UTF-8\">");
        html.AppendLine($"    <title>Parking Barcode - {qrCode}</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: 'Segoe UI', Arial, sans-serif; padding: 40px; max-width: 600px; margin: 0 auto; }");
        html.AppendLine("        .header { text-align: center; border-bottom: 2px solid #4F46E5; padding-bottom: 20px; margin-bottom: 30px; }");
        html.AppendLine("        .logo { font-size: 28px; font-weight: bold; color: #4F46E5; }");
        html.AppendLine("        .barcode-title { font-size: 14px; color: #666; margin-top: 10px; }");
        html.AppendLine("        .barcode-id { font-size: 12px; color: #999; }");
        html.AppendLine("        .barcode-container { background: #f9fafb; padding: 30px; border-radius: 12px; margin: 30px 0; text-align: center; }");
        html.AppendLine("        .barcode { width: 100%; height: 100px; margin: 20px 0; }");
        html.AppendLine("        .code { font-family: monospace; font-size: 20px; font-weight: bold; color: #4F46E5; margin-top: 15px; letter-spacing: 2px; }");
        html.AppendLine("        .details { margin: 20px 0; }");
        html.AppendLine("        .row { display: flex; justify-content: space-between; padding: 12px 0; border-bottom: 1px solid #eee; }");
        html.AppendLine("        .label { color: #666; }");
        html.AppendLine("        .value { font-weight: 600; color: #333; }");
        html.AppendLine("        .footer { text-align: center; margin-top: 40px; color: #999; font-size: 12px; }");
        html.AppendLine("        @media print { body { padding: 20px; } .no-print { display: none; } }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <div class=\"header\">");
        html.AppendLine("        <div class=\"logo\">STATIONNEMENT</div>");
        html.AppendLine("        <div class=\"barcode-title\">PARKING BARCODE</div>");
        html.AppendLine($"        <div class=\"barcode-id\">Barcode #{qrCode}</div>");
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"barcode-container\">");
        html.AppendLine("        <svg id=\"barcode\"></svg>");
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"details\">");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Location</span>");
        html.AppendLine($"            <span class=\"value\">{location}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Slot</span>");
        html.AppendLine($"            <span class=\"value\">{slot}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Date</span>");
        html.AppendLine($"            <span class=\"value\">{date}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Time</span>");
        html.AppendLine($"            <span class=\"value\">{time}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("        <div class=\"row\">");
        html.AppendLine("            <span class=\"label\">Vehicle Plate</span>");
        html.AppendLine($"            <span class=\"value\">{vehiclePlate}</span>");
        html.AppendLine("        </div>");
        if (!string.IsNullOrEmpty(amount))
        {
            html.AppendLine("        <div class=\"row\">");
            html.AppendLine("            <span class=\"label\">Amount</span>");
            html.AppendLine($"            <span class=\"value\">{amount}</span>");
            html.AppendLine("        </div>");
        }
        html.AppendLine("    </div>");
        html.AppendLine("    <div class=\"footer\">");
        html.AppendLine("        <p>Present this barcode at the parking location</p>");
        html.AppendLine($"        <p>Generated on {DateTime.Now:dd MMM yyyy, hh:mm tt}</p>");
        html.AppendLine("        <button class=\"no-print\" onclick=\"window.print()\" style=\"margin-top:20px; padding:10px 30px; background:#4F46E5; color:white; border:none; border-radius:8px; cursor:pointer;\">Print Barcode</button>");
        html.AppendLine("    </div>");
        html.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/jsbarcode@3.11.5/dist/JsBarcode.all.min.js\"></script>");
        html.AppendLine("    <script>");
        html.AppendLine("        JsBarcode('#barcode', '" + qrCode + "', {");
        html.AppendLine("            format: 'CODE128',");
        html.AppendLine("            width: 2,");
        html.AppendLine("            height: 100,");
        html.AppendLine("            displayValue: true");
        html.AppendLine("        });");
        html.AppendLine("    </script>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        
        var barcodeHtml = html.ToString();
        
        Response.ContentType = "text/html; charset=utf-8";
        return Content(barcodeHtml);
    }

    public class VerifyPaymentRequest
    {
        public string TransactionId { get; set; } = string.Empty;
    }
}
