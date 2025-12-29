using Dapper;
using Npgsql;
using Stationnement.Web.Models;

namespace Stationnement.Web.Repositories;

public interface IAdminSessionRepository
{
    Task<AdminSession?> GetActiveSessionAsync();
    Task<AdminSession?> GetByTokenHashAsync(string tokenHash);
    Task CreateSessionAsync(AdminSession session);
    Task RevokeAllSessionsAsync();
    Task RevokeSessionAsync(Guid sessionId);
    Task EnsureTableExistsAsync();
}

public class AdminSessionRepository : IAdminSessionRepository
{
    private readonly string _connectionString;

    public AdminSessionRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection GetConnection() => new NpgsqlConnection(_connectionString);

    public async Task EnsureTableExistsAsync()
    {
        await using var conn = GetConnection();
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS admin_sessions (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id UUID REFERENCES users(id) ON DELETE CASCADE,
                token_hash VARCHAR(255) NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
                ip_address VARCHAR(50),
                is_active BOOLEAN DEFAULT TRUE
            )");
    }

    public async Task<AdminSession?> GetActiveSessionAsync()
    {
        await using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<AdminSession>(@"
            SELECT s.*, u.email as user_email 
            FROM admin_sessions s
            JOIN users u ON s.user_id = u.id
            WHERE s.is_active = TRUE AND s.expires_at > NOW()
            ORDER BY s.created_at DESC
            LIMIT 1");
    }

    public async Task<AdminSession?> GetByTokenHashAsync(string tokenHash)
    {
        await using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<AdminSession>(@"
            SELECT s.*, u.email as user_email 
            FROM admin_sessions s
            JOIN users u ON s.user_id = u.id
            WHERE s.token_hash = @tokenHash AND s.is_active = TRUE AND s.expires_at > NOW()",
            new { tokenHash });
    }

    public async Task CreateSessionAsync(AdminSession session)
    {
        await using var conn = GetConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO admin_sessions (id, user_id, token_hash, created_at, expires_at, ip_address, is_active)
            VALUES (@Id, @UserId, @TokenHash, @CreatedAt, @ExpiresAt, @IpAddress, @IsActive)",
            session);
    }

    public async Task RevokeAllSessionsAsync()
    {
        await using var conn = GetConnection();
        await conn.ExecuteAsync("UPDATE admin_sessions SET is_active = FALSE WHERE is_active = TRUE");
    }

    public async Task RevokeSessionAsync(Guid sessionId)
    {
        await using var conn = GetConnection();
        await conn.ExecuteAsync("UPDATE admin_sessions SET is_active = FALSE WHERE id = @sessionId",
            new { sessionId });
    }
}
