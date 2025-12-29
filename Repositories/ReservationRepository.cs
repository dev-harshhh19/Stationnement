using Npgsql;
using Dapper;
using Stationnement.Web.Models;

namespace Stationnement.Web.Repositories;

public interface IReservationRepository
{
    Task<IEnumerable<Reservation>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<Reservation>> GetUpcomingByUserIdAsync(Guid userId);
    Task<Reservation?> GetByIdAsync(Guid id);
    Task<Reservation?> GetByQrCodeAsync(string qrCode);
    Task<Reservation> CreateAsync(Reservation reservation);
    Task UpdateAsync(Reservation reservation);
    Task<bool> IsSlotAvailableAsync(Guid slotId, DateTime startTime, DateTime endTime, Guid? excludeReservationId = null);
    Task<IEnumerable<Reservation>> GetAllAsync(int page = 1, int pageSize = 20, string? status = null);
    Task<int> GetTotalCountAsync(string? status = null);
}

public class ReservationRepository : IReservationRepository
{
    private readonly string _connectionString;

    public ReservationRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection GetConnection() => new(_connectionString);

    public async Task<IEnumerable<Reservation>> GetByUserIdAsync(Guid userId)
    {
        using var conn = GetConnection();
        return await conn.QueryAsync<Reservation>(
            @"SELECT r.*, s.slot_code as SlotCode, l.name as LocationName, l.address as LocationAddress
              FROM reservations r
              JOIN parking_slots s ON r.slot_id = s.id
              JOIN parking_locations l ON s.location_id = l.id
              WHERE r.user_id = @UserId ORDER BY r.start_time DESC",
            new { UserId = userId });
    }

    public async Task<IEnumerable<Reservation>> GetUpcomingByUserIdAsync(Guid userId)
    {
        using var conn = GetConnection();
        return await conn.QueryAsync<Reservation>(
            @"SELECT r.*, s.slot_code as SlotCode, l.name as LocationName, l.address as LocationAddress
              FROM reservations r
              JOIN parking_slots s ON r.slot_id = s.id
              JOIN parking_locations l ON s.location_id = l.id
              WHERE r.user_id = @UserId AND r.status IN ('confirmed', 'active') AND r.end_time >= NOW()
              ORDER BY r.start_time ASC",
            new { UserId = userId });
    }

    public async Task<Reservation?> GetByIdAsync(Guid id)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<Reservation>(
            @"SELECT r.*, s.slot_code as SlotCode, l.name as LocationName, l.address as LocationAddress
              FROM reservations r
              JOIN parking_slots s ON r.slot_id = s.id
              JOIN parking_locations l ON s.location_id = l.id
              WHERE r.id = @Id", new { Id = id });
    }

    public async Task<Reservation?> GetByQrCodeAsync(string qrCode)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<Reservation>(
            @"SELECT r.*, s.slot_code as SlotCode, l.name as LocationName, l.address as LocationAddress
              FROM reservations r
              JOIN parking_slots s ON r.slot_id = s.id
              JOIN parking_locations l ON s.location_id = l.id
              WHERE r.qr_code = @QrCode", new { QrCode = qrCode });
    }

    public async Task<Reservation> CreateAsync(Reservation reservation)
    {
        using var conn = GetConnection();
        reservation.Id = Guid.NewGuid();
        reservation.CreatedAt = DateTime.UtcNow;
        reservation.UpdatedAt = DateTime.UtcNow;
        reservation.QrCode = $"STN-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

        await conn.ExecuteAsync(
            @"INSERT INTO reservations (id, user_id, slot_id, start_time, end_time, status, qr_code,
                                        vehicle_plate, vehicle_type, base_amount, discount_amount, 
                                        surcharge_amount, total_amount, created_at, updated_at)
              VALUES (@Id, @UserId, @SlotId, @StartTime, @EndTime, @Status, @QrCode,
                      @VehiclePlate, @VehicleType, @BaseAmount, @DiscountAmount,
                      @SurchargeAmount, @TotalAmount, @CreatedAt, @UpdatedAt)", reservation);
        return reservation;
    }

    public async Task UpdateAsync(Reservation reservation)
    {
        using var conn = GetConnection();
        reservation.UpdatedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(
            @"UPDATE reservations SET status = @Status, actual_entry_time = @ActualEntryTime,
              actual_exit_time = @ActualExitTime, surcharge_amount = @SurchargeAmount,
              total_amount = @TotalAmount, updated_at = @UpdatedAt WHERE id = @Id", reservation);
    }

    public async Task<bool> IsSlotAvailableAsync(Guid slotId, DateTime startTime, DateTime endTime, Guid? excludeReservationId = null)
    {
        using var conn = GetConnection();
        var conflictCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM reservations 
              WHERE slot_id = @SlotId AND status IN ('confirmed', 'active')
              AND start_time < @EndTime AND end_time > @StartTime
              AND (@ExcludeId IS NULL OR id != @ExcludeId)",
            new { SlotId = slotId, StartTime = startTime, EndTime = endTime, ExcludeId = excludeReservationId });
        return conflictCount == 0;
    }

    public async Task<IEnumerable<Reservation>> GetAllAsync(int page = 1, int pageSize = 20, string? status = null)
    {
        using var conn = GetConnection();
        var offset = (page - 1) * pageSize;
        var sql = @"SELECT r.*, s.slot_code as SlotCode, l.name as LocationName
                    FROM reservations r
                    JOIN parking_slots s ON r.slot_id = s.id
                    JOIN parking_locations l ON s.location_id = l.id
                    WHERE (@Status IS NULL OR r.status = @Status)
                    ORDER BY r.created_at DESC LIMIT @PageSize OFFSET @Offset";
        return await conn.QueryAsync<Reservation>(sql, new { Status = status, PageSize = pageSize, Offset = offset });
    }

    public async Task<int> GetTotalCountAsync(string? status = null)
    {
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reservations WHERE (@Status IS NULL OR status = @Status)",
            new { Status = status });
    }
}
