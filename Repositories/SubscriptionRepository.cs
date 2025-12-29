using Npgsql;

namespace Stationnement.Web.Repositories;

public interface ISubscriptionRepository
{
    Task<UserSubscription?> GetByUserIdAsync(Guid userId);
    Task<UserSubscription> CreateOrUpdateAsync(Guid userId, string tier, DateTime expiresAt);
    Task<bool> DeactivateAsync(Guid userId);
}

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly string _connectionString;

    public SubscriptionRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<UserSubscription?> GetByUserIdAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT id, user_id, tier, starts_at, expires_at, is_active, created_at
            FROM user_subscriptions 
            WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new UserSubscription
            {
                Id = reader.GetGuid(0),
                UserId = reader.GetGuid(1),
                Tier = reader.GetString(2),
                StartsAt = reader.GetDateTime(3),
                ExpiresAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                IsActive = reader.GetBoolean(5),
                CreatedAt = reader.GetDateTime(6)
            };
        }
        return null;
    }

    public async Task<UserSubscription> CreateOrUpdateAsync(Guid userId, string tier, DateTime expiresAt)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO user_subscriptions (id, user_id, tier, starts_at, expires_at, is_active, created_at)
            VALUES (@id, @userId, @tier, NOW(), @expiresAt, TRUE, NOW())
            ON CONFLICT (user_id) 
            DO UPDATE SET tier = @tier, starts_at = NOW(), expires_at = @expiresAt, is_active = TRUE
            RETURNING id, user_id, tier, starts_at, expires_at, is_active, created_at", conn);
        
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tier", tier);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new UserSubscription
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            Tier = reader.GetString(2),
            StartsAt = reader.GetDateTime(3),
            ExpiresAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            IsActive = reader.GetBoolean(5),
            CreatedAt = reader.GetDateTime(6)
        };
    }

    public async Task<bool> DeactivateAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE user_subscriptions SET is_active = FALSE WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}

public class UserSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Tier { get; set; } = "free";
    public DateTime StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
