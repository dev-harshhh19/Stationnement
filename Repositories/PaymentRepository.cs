using Npgsql;
using Dapper;
using Stationnement.Web.Models;

namespace Stationnement.Web.Repositories;

public interface IPaymentRepository
{
    Task<IEnumerable<Payment>> GetByUserIdAsync(Guid userId);
    Task<Payment?> GetByIdAsync(Guid id);
    Task<Payment> CreateAsync(Payment payment);
    Task UpdateAsync(Payment payment);
    Task<Subscription?> GetActiveSubscriptionAsync(Guid userId);
    Task<Subscription> CreateSubscriptionAsync(Subscription subscription);
    Task<IEnumerable<AuditLog>> GetAuditLogsAsync(int page = 1, int pageSize = 50);
}

public class PaymentRepository : IPaymentRepository
{
    private readonly string _connectionString;

    public PaymentRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection GetConnection() => new(_connectionString);

    public async Task<IEnumerable<Payment>> GetByUserIdAsync(Guid userId)
    {
        using var conn = GetConnection();
        return await conn.QueryAsync<Payment>(
            @"SELECT p.*, r.qr_code as ReservationQrCode FROM payments p
              LEFT JOIN reservations r ON p.reservation_id = r.id
              WHERE p.user_id = @UserId ORDER BY p.created_at DESC",
            new { UserId = userId });
    }

    public async Task<Payment?> GetByIdAsync(Guid id)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<Payment>(
            "SELECT * FROM payments WHERE id = @Id", new { Id = id });
    }

    public async Task<Payment> CreateAsync(Payment payment)
    {
        using var conn = GetConnection();
        await conn.OpenAsync();
        
        payment.Id = Guid.NewGuid();
        payment.CreatedAt = DateTime.UtcNow;
        
        // Try the simplest INSERT first - only core required columns
        // NeonDB payments table has: id, user_id, reservation_id, amount, payment_type (NOT NULL), status, created_at
        var insertAttempts = new[]
        {
            // Attempt 1: With payment_type (required NOT NULL column in NeonDB)
            new {
                Sql = @"INSERT INTO payments (id, user_id, reservation_id, amount, payment_type, status, created_at)
                        VALUES (@Id, @UserId, @ReservationId, @Amount, @PaymentType, @Status, @CreatedAt)",
                Params = (object)new { payment.Id, payment.UserId, payment.ReservationId, payment.Amount, PaymentType = payment.PaymentMethod ?? "upi", payment.Status, payment.CreatedAt }
            },
            // Attempt 2: Minimal without payment_type (for databases without this column)
            new {
                Sql = @"INSERT INTO payments (id, user_id, reservation_id, amount, status, created_at)
                        VALUES (@Id, @UserId, @ReservationId, @Amount, @Status, @CreatedAt)",
                Params = (object)new { payment.Id, payment.UserId, payment.ReservationId, payment.Amount, payment.Status, payment.CreatedAt }
            },
            // Attempt 3: With payment_method (older schema)
            new {
                Sql = @"INSERT INTO payments (id, user_id, reservation_id, amount, payment_method, status, created_at)
                        VALUES (@Id, @UserId, @ReservationId, @Amount, @PaymentMethod, @Status, @CreatedAt)",
                Params = (object)new { payment.Id, payment.UserId, payment.ReservationId, payment.Amount, PaymentMethod = payment.PaymentMethod ?? "UPI", payment.Status, payment.CreatedAt }
            }
        };
        
        Exception? lastException = null;
        int attemptNum = 0;
        
        foreach (var attempt in insertAttempts)
        {
            attemptNum++;
            try
            {
                Console.WriteLine($"[PAYMENT] Attempt {attemptNum}: Trying to insert payment...");
                await conn.ExecuteAsync(attempt.Sql, attempt.Params);
                Console.WriteLine($"[PAYMENT] Successfully created payment {payment.Id} for reservation {payment.ReservationId}, amount: ₹{payment.Amount}");
                return payment;
            }
            catch (PostgresException ex)
            {
                Console.WriteLine($"[PAYMENT] Attempt {attemptNum} failed: {ex.SqlState} - {ex.Message}");
                lastException = ex;
                
                // Only continue if it's a column-not-found error (42703) or undefined table (42P01)
                if (ex.SqlState != "42703" && ex.SqlState != "42P01")
                {
                    throw; // Rethrow if it's a different kind of error
                }
            }
        }
        
        // If all attempts failed, log and rethrow
        Console.WriteLine($"[ERROR] Failed to create payment record after {attemptNum} attempts. Last error: {lastException?.Message}");
        throw new InvalidOperationException("Failed to create payment record - database schema mismatch", lastException);
    }

    public async Task UpdateAsync(Payment payment)
    {
        using var conn = GetConnection();
        // Only update status - avoid referencing columns that may not exist
        await conn.ExecuteAsync(
            "UPDATE payments SET status = @Status WHERE id = @Id", 
            new { payment.Status, payment.Id });
    }

    public async Task<Subscription?> GetActiveSubscriptionAsync(Guid userId)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<Subscription>(
            @"SELECT * FROM subscriptions 
              WHERE user_id = @UserId AND status = 'active' AND (end_date IS NULL OR end_date > NOW())
              ORDER BY created_at DESC LIMIT 1",
            new { UserId = userId });
    }

    public async Task<Subscription> CreateSubscriptionAsync(Subscription subscription)
    {
        using var conn = GetConnection();
        subscription.Id = Guid.NewGuid();
        subscription.CreatedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(
            @"INSERT INTO subscriptions (id, user_id, plan_type, monthly_price, discount_percentage,
                                         start_date, end_date, auto_renew, status, created_at)
              VALUES (@Id, @UserId, @PlanType, @MonthlyPrice, @DiscountPercentage,
                      @StartDate, @EndDate, @AutoRenew, @Status, @CreatedAt)", subscription);
        return subscription;
    }

    public async Task<IEnumerable<AuditLog>> GetAuditLogsAsync(int page = 1, int pageSize = 50)
    {
        using var conn = GetConnection();
        var offset = (page - 1) * pageSize;
        return await conn.QueryAsync<AuditLog>(
            @"SELECT a.*, u.email as UserEmail FROM audit_logs a
              LEFT JOIN users u ON a.user_id = u.id
              ORDER BY a.created_at DESC LIMIT @PageSize OFFSET @Offset",
            new { PageSize = pageSize, Offset = offset });
    }
}
