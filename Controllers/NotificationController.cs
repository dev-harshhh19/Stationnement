using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly string _connectionString;

    public NotificationController(IConfiguration configuration)
    {
        // Build connection string from environment variables if available
        var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
        var dbName = Environment.GetEnvironmentVariable("DB_NAME");
        var dbUser = Environment.GetEnvironmentVariable("DB_USERNAME");
        var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
        var dbSsl = Environment.GetEnvironmentVariable("DB_SSLMODE") ?? "Require";

        if (!string.IsNullOrEmpty(dbHost) && !string.IsNullOrEmpty(dbName) && !string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPass))
        {
            _connectionString = $"Host={dbHost};Database={dbName};Username={dbUser};Password={dbPass};SslMode={dbSsl}";
        }
        else
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }
    }

    /// <summary>
    /// Get unread notifications for user
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Ok(new { success = true, data = new List<object>() });

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Ensure table and columns exist
        await new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS notifications (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id UUID REFERENCES users(id) ON DELETE CASCADE,
                title VARCHAR(200) NOT NULL,
                message TEXT,
                type VARCHAR(50) DEFAULT 'info',
                is_read BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            )", conn).ExecuteNonQueryAsync();
        
        // Add type column if it doesn't exist
        try {
            await new NpgsqlCommand("ALTER TABLE notifications ADD COLUMN IF NOT EXISTS type VARCHAR(50) DEFAULT 'info'", conn).ExecuteNonQueryAsync();
        } catch { /* Column might already exist */ }

        await using var cmd = new NpgsqlCommand(@"
            SELECT id, title, COALESCE(message, ''), COALESCE(type, 'info'), is_read, created_at 
            FROM notifications 
            WHERE user_id = @userId 
            ORDER BY created_at DESC 
            LIMIT 20", conn);
        cmd.Parameters.AddWithValue("userId", userId.Value);

        var notifications = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notifications.Add(new
            {
                id = reader.GetGuid(0),
                title = reader.GetString(1),
                message = reader.GetString(2),
                type = reader.GetString(3),
                isRead = reader.GetBoolean(4),
                createdAt = reader.GetDateTime(5)
            });
        }

        return Ok(new { success = true, data = notifications });
    }

    /// <summary>
    /// Get unread count
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Ok(new { success = true, count = 0 });

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM notifications WHERE user_id = @userId AND is_read = FALSE", conn);
        cmd.Parameters.AddWithValue("userId", userId.Value);

        var count = await cmd.ExecuteScalarAsync();
        return Ok(new { success = true, count = Convert.ToInt32(count) });
    }

    /// <summary>
    /// Mark notification as read
    /// </summary>
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Unauthorized();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE notifications SET is_read = TRUE WHERE id = @id AND user_id = @userId", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("userId", userId.Value);
        await cmd.ExecuteNonQueryAsync();

        return Ok(new { success = true });
    }

    /// <summary>
    /// Mark all as read
    /// </summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Unauthorized();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE notifications SET is_read = TRUE WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId.Value);
        await cmd.ExecuteNonQueryAsync();

        return Ok(new { success = true });
    }

    /// <summary>
    /// Create notification (internal API)
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationRequest request)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Ensure table exists
        await new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS notifications (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id UUID REFERENCES users(id) ON DELETE CASCADE,
                title VARCHAR(200) NOT NULL,
                message TEXT,
                type VARCHAR(50) DEFAULT 'info',
                is_read BOOLEAN DEFAULT FALSE,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
            )", conn).ExecuteNonQueryAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO notifications (user_id, title, message, type)
            VALUES (@userId, @title, @message, @type)", conn);
        cmd.Parameters.AddWithValue("userId", request.UserId);
        cmd.Parameters.AddWithValue("title", request.Title);
        cmd.Parameters.AddWithValue("message", request.Message ?? "");
        cmd.Parameters.AddWithValue("type", request.Type ?? "info");
        await cmd.ExecuteNonQueryAsync();

        return Ok(new { success = true });
    }

    private Guid? GetUserIdFromToken()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;
        
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    public class CreateNotificationRequest
    {
        public Guid UserId { get; set; }
        public string Title { get; set; } = "";
        public string? Message { get; set; }
        public string? Type { get; set; } = "info"; // info, success, warning, error
    }
}
