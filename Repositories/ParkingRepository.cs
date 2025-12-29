using Npgsql;
using Dapper;
using Stationnement.Web.Models;

namespace Stationnement.Web.Repositories;

public interface IParkingRepository
{
    Task<IEnumerable<ParkingLocation>> GetAllLocationsAsync();
    Task<ParkingLocation?> GetLocationByIdAsync(Guid id);
    Task<ParkingLocation> CreateLocationAsync(ParkingLocation location);
    Task UpdateLocationAsync(ParkingLocation location);
    Task DeleteLocationAsync(Guid id);
    Task<IEnumerable<ParkingSlot>> GetSlotsByLocationAsync(Guid locationId);
    Task<IEnumerable<ParkingSlot>> GetAvailableSlotsAsync(Guid locationId, DateTime startTime, DateTime endTime);
    Task<IEnumerable<SlotWithAvailability>> GetSlotsWithAvailabilityAsync(Guid locationId, DateTime startTime, DateTime endTime);
    Task<ParkingSlot?> GetSlotByIdAsync(Guid id);
    Task<ParkingSlot> CreateSlotAsync(ParkingSlot slot);
    Task UpdateSlotAsync(ParkingSlot slot);
    Task<IEnumerable<PricingRule>> GetPricingRulesAsync(Guid? locationId);
}

public class ParkingRepository : IParkingRepository
{
    private readonly string _connectionString;

    public ParkingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection GetConnection() => new(_connectionString);

    public async Task<IEnumerable<ParkingLocation>> GetAllLocationsAsync()
    {
        using var conn = GetConnection();
        var locations = await conn.QueryAsync<ParkingLocation>(
            @"SELECT l.id, l.name, l.address, l.city, l.postal_code as PostalCode, 
                     l.latitude, l.longitude, l.total_slots as TotalSlots, 
                     l.base_price_per_hour as BasePricePerHour,
                     l.is_active as IsActive, l.created_at as CreatedAt
              FROM parking_locations l WHERE l.is_active = true ORDER BY l.name");
        
        // Calculate real-time available slots for each location based on current reservations
        var locationsList = locations.ToList();
        var now = DateTime.UtcNow;
        
        foreach (var location in locationsList)
        {
            // Get total slots for this location
            var totalSlots = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM parking_slots WHERE location_id = @LocationId AND status = 'available'",
                new { LocationId = location.Id });
            
            // Count currently booked slots (any reservation that overlaps with current time or future)
            // We check for reservations that are active or confirmed and have time overlap
            var bookedSlots = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT r.slot_id) 
                  FROM reservations r
                  INNER JOIN parking_slots s ON r.slot_id = s.id
                  WHERE s.location_id = @LocationId
                  AND r.status IN ('confirmed', 'active')
                  AND r.start_time <= NOW() + INTERVAL '1 day'
                  AND r.end_time >= NOW()",
                new { LocationId = location.Id });
            
            location.TotalSlots = totalSlots;
            location.AvailableSlots = Math.Max(0, totalSlots - bookedSlots);
        }
        
        return locationsList;
    }

    public async Task<ParkingLocation?> GetLocationByIdAsync(Guid id)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<ParkingLocation>(
            "SELECT * FROM parking_locations WHERE id = @Id", new { Id = id });
    }

    public async Task<ParkingLocation> CreateLocationAsync(ParkingLocation location)
    {
        using var conn = GetConnection();
        location.Id = Guid.NewGuid();
        location.CreatedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(
            @"INSERT INTO parking_locations (id, name, slug, address, city, postal_code, country, 
                                             latitude, longitude, total_slots, base_price_per_hour, 
                                             amenities, operating_hours, is_active, created_at)
              VALUES (@Id, @Name, @Slug, @Address, @City, @PostalCode, @Country, 
                      @Latitude, @Longitude, @TotalSlots, @BasePricePerHour,
                      @Amenities, @OperatingHours, @IsActive, @CreatedAt)", location);
        return location;
    }

    public async Task UpdateLocationAsync(ParkingLocation location)
    {
        using var conn = GetConnection();
        await conn.ExecuteAsync(
            @"UPDATE parking_locations SET name = @Name, address = @Address, city = @City,
              postal_code = @PostalCode, total_slots = @TotalSlots, base_price_per_hour = @BasePricePerHour,
              amenities = @Amenities, operating_hours = @OperatingHours, is_active = @IsActive
              WHERE id = @Id", location);
    }

    public async Task DeleteLocationAsync(Guid id)
    {
        using var conn = GetConnection();
        await conn.ExecuteAsync("UPDATE parking_locations SET is_active = false WHERE id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<ParkingSlot>> GetSlotsByLocationAsync(Guid locationId)
    {
        using var conn = GetConnection();
        return await conn.QueryAsync<ParkingSlot>(
            @"SELECT s.id, s.location_id as LocationId, s.slot_code as SlotCode, 
                     s.floor, s.slot_type as SlotType, s.status,
                     s.base_price_per_hour as BasePricePerHour, s.created_at as CreatedAt,
                     l.name as LocationName 
              FROM parking_slots s
              JOIN parking_locations l ON s.location_id = l.id
              WHERE s.location_id = @LocationId ORDER BY s.slot_code",
            new { LocationId = locationId });
    }

    public async Task<IEnumerable<ParkingSlot>> GetAvailableSlotsAsync(Guid locationId, DateTime startTime, DateTime endTime)
    {
        using var conn = GetConnection();
        return await conn.QueryAsync<ParkingSlot>(
            @"SELECT s.id, s.location_id as LocationId, s.slot_code as SlotCode, 
                     s.floor, s.slot_type as SlotType, s.status,
                     s.base_price_per_hour as BasePricePerHour, s.created_at as CreatedAt,
                     COALESCE(s.is_premium, FALSE) as IsPremium
              FROM parking_slots s
              WHERE s.location_id = @LocationId AND s.status = 'available'
              AND s.id NOT IN (
                  SELECT slot_id FROM reservations 
                  WHERE status IN ('confirmed', 'active')
                  AND start_time < @EndTime AND end_time > @StartTime
              ) ORDER BY s.is_premium DESC, s.slot_code",
            new { LocationId = locationId, StartTime = startTime, EndTime = endTime });
    }

    public async Task<IEnumerable<SlotWithAvailability>> GetSlotsWithAvailabilityAsync(Guid locationId, DateTime startTime, DateTime endTime)
    {
        using var conn = GetConnection();
        
        // Get all slots for the location
        var allSlots = await conn.QueryAsync<SlotWithAvailability>(
            @"SELECT s.id, s.location_id as LocationId, s.slot_code as SlotCode, 
                     s.floor, s.slot_type as SlotType, s.status,
                     s.base_price_per_hour as BasePricePerHour, s.created_at as CreatedAt,
                     COALESCE(s.is_premium, FALSE) as IsPremium, s.is_active as IsActive
              FROM parking_slots s
              WHERE s.location_id = @LocationId AND s.status = 'available'
              ORDER BY s.is_premium DESC, s.slot_code",
            new { LocationId = locationId });
        
        // Check which slots are booked for the time range
        var bookedSlotIds = await conn.QueryAsync<Guid>(
            @"SELECT DISTINCT slot_id 
              FROM reservations 
              WHERE status IN ('confirmed', 'active')
              AND start_time < @EndTime AND end_time > @StartTime",
            new { StartTime = startTime, EndTime = endTime });
        
        var bookedSet = bookedSlotIds.ToHashSet();
        
        // Mark availability status
        return allSlots.Select(slot =>
        {
            slot.IsBooked = bookedSet.Contains(slot.Id);
            slot.IsAvailable = !slot.IsBooked;
            return slot;
        });
    }

    public async Task<ParkingSlot?> GetSlotByIdAsync(Guid id)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<ParkingSlot>(
            @"SELECT s.*, l.name as LocationName FROM parking_slots s
              JOIN parking_locations l ON s.location_id = l.id
              WHERE s.id = @Id", new { Id = id });
    }

    public async Task<ParkingSlot> CreateSlotAsync(ParkingSlot slot)
    {
        using var conn = GetConnection();
        slot.Id = Guid.NewGuid();
        await conn.ExecuteAsync(
            @"INSERT INTO parking_slots (id, location_id, slot_code, slot_type, floor, section, is_active, price_multiplier)
              VALUES (@Id, @LocationId, @SlotCode, @SlotType, @Floor, @Section, @IsActive, @PriceMultiplier)", slot);
        return slot;
    }

    public async Task UpdateSlotAsync(ParkingSlot slot)
    {
        using var conn = GetConnection();
        await conn.ExecuteAsync(
            @"UPDATE parking_slots SET slot_code = @SlotCode, slot_type = @SlotType, 
              floor = @Floor, section = @Section, is_active = @IsActive, 
              price_multiplier = @PriceMultiplier WHERE id = @Id", slot);
    }

    public async Task<IEnumerable<PricingRule>> GetPricingRulesAsync(Guid? locationId)
    {
        using var conn = GetConnection();
        try
        {
            // Check if pricing_rules table exists by trying to query it
            // If table doesn't exist, return empty list
            return await conn.QueryAsync<PricingRule>(
                @"SELECT * FROM pricing_rules 
                  WHERE is_active = true AND (location_id IS NULL OR location_id = @LocationId)
                  ORDER BY priority DESC",
                new { LocationId = locationId });
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // Table doesn't exist
        {
            // Return empty list if pricing_rules table doesn't exist
            Console.WriteLine($"[WARNING] pricing_rules table does not exist, returning empty rules list");
            return Enumerable.Empty<PricingRule>();
        }
        catch (Exception ex)
        {
            // Catch any other exception (e.g., if PostgresException is not available)
            var message = ex.Message.ToLower();
            if (message.Contains("pricing_rules") && message.Contains("does not exist") || 
                (message.Contains("relation") && message.Contains("does not exist")))
            {
                Console.WriteLine($"[WARNING] pricing_rules table does not exist, returning empty rules list: {ex.Message}");
                return Enumerable.Empty<PricingRule>();
            }
            // Re-throw if it's a different error
            throw;
        }
    }
}
