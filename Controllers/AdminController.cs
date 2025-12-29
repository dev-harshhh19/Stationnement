using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using Npgsql;
using Stationnement.Web.Repositories;
using Stationnement.Web.Services;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IPricingService _pricingService;
    private readonly IParkingRepository _parkingRepository;
    private readonly string _connectionString;

    public AdminController(
        IReservationRepository reservationRepository, 
        IPricingService pricingService,
        IParkingRepository parkingRepository,
        IConfiguration configuration)
    {
        _reservationRepository = reservationRepository;
        _pricingService = pricingService;
        _parkingRepository = parkingRepository;
        
        // Build connection string
        var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
        var dbName = Environment.GetEnvironmentVariable("DB_NAME");
        var dbUser = Environment.GetEnvironmentVariable("DB_USERNAME");
        var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
        var dbSsl = Environment.GetEnvironmentVariable("DB_SSLMODE") ?? "Require";

        if (!string.IsNullOrEmpty(dbHost) && !string.IsNullOrEmpty(dbPass))
            _connectionString = $"Host={dbHost};Database={dbName};Username={dbUser};Password={dbPass};SslMode={dbSsl}";
        else
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    private NpgsqlConnection GetConnection() => new NpgsqlConnection(_connectionString);

    /// <summary>
    /// Get dashboard statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();

        var totalUsers = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users WHERE is_active = TRUE");
        var activeReservations = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reservations WHERE status IN ('confirmed', 'checked_in', 'active')");
        var totalRevenue = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM payments WHERE status = 'completed'") ?? 0;
        var totalSlots = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM parking_slots");
        var occupiedSlots = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(DISTINCT slot_id) FROM reservations 
              WHERE status IN ('confirmed', 'checked_in', 'active') AND end_time > NOW()");
        var occupancyRate = totalSlots > 0 ? Math.Round((double)occupiedSlots / totalSlots * 100, 1) : 0;
        var newUsersThisMonth = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM users WHERE created_at >= DATE_TRUNC('month', CURRENT_DATE)");
        var reservationsThisWeek = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reservations WHERE created_at >= DATE_TRUNC('week', CURRENT_DATE)");
        var revenueThisMonth = await conn.ExecuteScalarAsync<decimal?>(
            @"SELECT COALESCE(SUM(amount), 0) FROM payments 
              WHERE status = 'completed' AND created_at >= DATE_TRUNC('month', CURRENT_DATE)") ?? 0;
        var totalLocations = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM parking_locations WHERE is_active = TRUE");

        return Ok(new
        {
            success = true,
            data = new { totalUsers, activeReservations, totalRevenue, occupancyRate, totalSlots, occupiedSlots, totalLocations, newUsersThisMonth, reservationsThisWeek, revenueThisMonth }
        });
    }

    [HttpGet("recent-reservations")]
    public async Task<IActionResult> GetRecentReservations([FromQuery] int limit = 5)
    {
        await using var conn = GetConnection();
        var reservations = await conn.QueryAsync<dynamic>(@"
            SELECT r.id, r.qr_code as reservation_code, r.status, r.start_time, r.end_time, r.total_amount,
                   u.email as user_email, u.first_name, u.last_name,
                   pl.name as location_name, ps.slot_code
            FROM reservations r
            JOIN users u ON r.user_id = u.id
            LEFT JOIN parking_slots ps ON r.slot_id = ps.id
            LEFT JOIN parking_locations pl ON ps.location_id = pl.id
            ORDER BY r.created_at DESC LIMIT @limit", new { limit });
        return Ok(new { success = true, data = reservations });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        await using var conn = GetConnection();
        var offset = (page - 1) * pageSize;
        var users = await conn.QueryAsync<dynamic>(@"
            SELECT u.id, u.email, u.first_name, u.last_name, u.phone_number, 
                   u.is_active, u.email_verified, u.created_at, r.name as role_name
            FROM users u LEFT JOIN roles r ON u.role_id = r.id
            ORDER BY u.created_at DESC LIMIT @pageSize OFFSET @offset", new { pageSize, offset });
        var totalCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users");
        return Ok(new { success = true, data = users, totalCount, page, pageSize });
    }

    [HttpGet("reservations")]
    public async Task<IActionResult> GetAllReservations([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null)
    {
        await using var conn = GetConnection();
        var offset = (page - 1) * pageSize;
        var whereClause = string.IsNullOrEmpty(status) ? "" : "WHERE r.status = @status";
        var reservations = await conn.QueryAsync<dynamic>($@"
            SELECT r.*, u.email as user_email, u.first_name, u.last_name,
                   pl.name as location_name, ps.slot_code
            FROM reservations r JOIN users u ON r.user_id = u.id
            LEFT JOIN parking_slots ps ON r.slot_id = ps.id
            LEFT JOIN parking_locations pl ON ps.location_id = pl.id
            {whereClause} ORDER BY r.created_at DESC LIMIT @pageSize OFFSET @offset", new { pageSize, offset, status });
        var totalCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM reservations r {whereClause}", new { status });
        return Ok(new { success = true, data = reservations, totalCount, page, pageSize });
    }

    [HttpPost("locations")]
    public async Task<IActionResult> AddLocation([FromBody] AddLocationRequest request)
    {
        await using var conn = GetConnection();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(@"
            INSERT INTO parking_locations (id, name, address, city, latitude, longitude, total_slots, base_price_per_hour, is_active, created_at)
            VALUES (@Id, @Name, @Address, @City, @Latitude, @Longitude, @TotalSlots, @BasePricePerHour, TRUE, NOW())",
            new { Id = id, request.Name, request.Address, request.City, request.Latitude, request.Longitude, request.TotalSlots, request.BasePricePerHour });

        for (int i = 1; i <= request.TotalSlots; i++)
        {
            var slotId = Guid.NewGuid();
            var floor = (i - 1) / 20 + 1;
            var slotNum = ((i - 1) % 20) + 1;
            var code = $"F{floor}-{(char)('A' + (slotNum - 1) / 10)}{slotNum % 10:D2}";
            var isPremium = i <= request.TotalSlots * 0.2;
            await conn.ExecuteAsync(@"
                INSERT INTO parking_slots (id, location_id, slot_code, slot_type, floor, status, is_premium, base_price_per_hour, created_at)
                VALUES (@id, @locationId, @code, @type, @floor, 'available', @premium, @price, NOW())",
                new { id = slotId, locationId = id, code, type = isPremium ? "Premium" : "Standard", floor, premium = isPremium, price = isPremium ? request.BasePricePerHour * 1.5m : request.BasePricePerHour });
        }
        return Ok(new { success = true, message = $"Location '{request.Name}' added with {request.TotalSlots} slots", data = new { id } });
    }

    [HttpPut("locations/{id}")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateLocationRequest request)
    {
        await using var conn = GetConnection();
        var affected = await conn.ExecuteAsync(@"
            UPDATE parking_locations SET name = @Name, address = @Address, city = @City, 
                latitude = @Latitude, longitude = @Longitude, base_price_per_hour = @BasePricePerHour WHERE id = @Id",
            new { Id = id, request.Name, request.Address, request.City, request.Latitude, request.Longitude, request.BasePricePerHour });
        if (affected == 0) return NotFound(new { success = false, message = "Location not found" });
        return Ok(new { success = true, message = "Location updated" });
    }

    [HttpDelete("locations/{id}")]
    public async Task<IActionResult> DeleteLocation(Guid id)
    {
        await using var conn = GetConnection();
        var affected = await conn.ExecuteAsync("UPDATE parking_locations SET is_active = FALSE WHERE id = @id", new { id });
        if (affected == 0) return NotFound(new { success = false, message = "Location not found" });
        return Ok(new { success = true, message = "Location deleted" });
    }

    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations()
    {
        await using var conn = GetConnection();
        var locations = await conn.QueryAsync<dynamic>(@"
            SELECT pl.*, (SELECT COUNT(*) FROM parking_slots WHERE location_id = pl.id) as slot_count
            FROM parking_locations pl WHERE pl.is_active = TRUE ORDER BY pl.name");
        return Ok(new { success = true, data = locations });
    }

    [HttpGet("slots")]
    public async Task<IActionResult> GetSlots([FromQuery] Guid? locationId)
    {
        await using var conn = GetConnection();
        var now = DateTime.UtcNow;
        
        // Query slots with their current booking status
        var sql = @"
            SELECT s.id, s.slot_code, s.slot_type, s.floor, s.status as base_status, s.is_premium, 
                   s.base_price_per_hour, l.name as location_name, l.id as location_id,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM reservations r 
                       WHERE r.slot_id = s.id 
                       AND r.status IN ('confirmed', 'active')
                       AND r.start_time <= @Now 
                       AND r.end_time >= @Now
                   ) THEN 'booked' ELSE 'available' END as current_status,
                   (SELECT r2.end_time FROM reservations r2 
                    WHERE r2.slot_id = s.id AND r2.status IN ('confirmed', 'active')
                    AND r2.start_time <= @Now AND r2.end_time >= @Now
                    LIMIT 1) as booked_until
            FROM parking_slots s
            JOIN parking_locations l ON s.location_id = l.id
            WHERE l.is_active = TRUE";

        if (locationId.HasValue)
            sql += " AND s.location_id = @LocationId";
        
        sql += " ORDER BY l.name, s.floor, s.slot_code";

        var slots = await conn.QueryAsync<dynamic>(sql, new { Now = now, LocationId = locationId });
        return Ok(new { success = true, data = slots });
    }

    [HttpPost("recalculate-prices")]
    public async Task<IActionResult> RecalculatePrices()
    {
        var userId = GetUserId();
        var allReservations = await _reservationRepository.GetByUserIdAsync(userId);
        int updated = 0;
        foreach (var reservation in allReservations)
        {
            try
            {
                var slot = await _parkingRepository.GetSlotByIdAsync(reservation.SlotId);
                if (slot == null) continue;
                var location = await _parkingRepository.GetLocationByIdAsync(slot.LocationId);
                if (location == null) continue;
                var hours = (reservation.EndTime - reservation.StartTime).TotalHours;
                double availabilityPercentage = location.TotalSlots > 0 ? ((double)location.AvailableSlots / location.TotalSlots) * 100 : 100;
                var pricingRequest = new PricingRequest
                {
                    BaseHourlyRate = location.BasePricePerHour * (slot.PriceMultiplier ?? 1.0m),
                    DurationHours = hours,
                    StartTime = reservation.StartTime,
                    EndTime = reservation.EndTime,
                    VehicleType = reservation.VehicleType ?? "hatchback",
                    AvailabilityPercentage = availabilityPercentage
                };
                var pricingResult = _pricingService.CalculatePrice(pricingRequest);
                reservation.BaseAmount = pricingResult.BaseAmount;
                reservation.DiscountAmount = pricingResult.BaseAmount * pricingResult.TotalDiscount;
                reservation.SurchargeAmount = pricingResult.BaseAmount * pricingResult.TotalSurcharge;
                reservation.TotalAmount = pricingResult.FinalAmount;
                await _reservationRepository.UpdateAsync(reservation);
                updated++;
            }
            catch { }
        }
        return Ok(new { success = true, message = $"Recalculated prices for {updated} reservations" });
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    public class AddLocationRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int TotalSlots { get; set; } = 50;
        public decimal BasePricePerHour { get; set; } = 25;
    }

    public class UpdateLocationRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public decimal BasePricePerHour { get; set; }
    }
}

