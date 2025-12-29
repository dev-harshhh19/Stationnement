using Npgsql;
using Dapper;
using Stationnement.Web.Models;

namespace Stationnement.Web.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task<User> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task<Role?> GetRoleByNameAsync(string name);
    Task<RefreshToken?> GetRefreshTokenAsync(string token);
    Task SaveRefreshTokenAsync(RefreshToken token);
    Task RevokeRefreshTokenAsync(string token, string? ip);
    Task<IEnumerable<User>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<int> GetTotalCountAsync();
}

public class UserRepository : IUserRepository
{
    private readonly string _connectionString;

    public UserRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection GetConnection() => new(_connectionString);

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            @"SELECT u.id, u.email, u.password_hash as PasswordHash, 
                     u.first_name as FirstName, u.last_name as LastName,
                     u.phone_number as PhoneNumber, u.email_verified as EmailVerified,
                     u.role_id as RoleId, u.is_active as IsActive,
                     u.created_at as CreatedAt, u.updated_at as UpdatedAt,
                     r.name as RoleName 
              FROM users u 
              LEFT JOIN roles r ON u.role_id = r.id 
              WHERE u.id = @Id", new { Id = id });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            @"SELECT u.id, u.email, u.password_hash as PasswordHash, 
                     u.first_name as FirstName, u.last_name as LastName,
                     u.phone_number as PhoneNumber, u.email_verified as EmailVerified,
                     u.email_verification_token as EmailVerificationToken,
                     u.role_id as RoleId, u.is_active as IsActive,
                     u.created_at as CreatedAt, u.updated_at as UpdatedAt,
                     r.name as RoleName 
              FROM users u 
              LEFT JOIN roles r ON u.role_id = r.id 
              WHERE LOWER(u.email) = LOWER(@Email)", new { Email = email });
    }

    public async Task<User> CreateAsync(User user)
    {
        using var conn = GetConnection();
        user.Id = Guid.NewGuid();
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(
            @"INSERT INTO users (id, email, password_hash, first_name, last_name, phone_number, 
                                 email_verified, email_verification_token, role_id, is_active, created_at, updated_at)
              VALUES (@Id, @Email, @PasswordHash, @FirstName, @LastName, @PhoneNumber,
                      @EmailVerified, @EmailVerificationToken, @RoleId, @IsActive, @CreatedAt, @UpdatedAt)", user);
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        using var conn = GetConnection();
        user.UpdatedAt = DateTime.UtcNow;
        
        // Build dynamic update query based on what fields are being updated
        var updates = new List<string> { "updated_at = @UpdatedAt" };
        var parameters = new Dictionary<string, object?> { { "Id", user.Id }, { "UpdatedAt", user.UpdatedAt } };
        
        if (!string.IsNullOrEmpty(user.Email))
        {
            updates.Add("email = @Email");
            parameters["Email"] = user.Email;
        }
        if (!string.IsNullOrEmpty(user.FirstName))
        {
            updates.Add("first_name = @FirstName");
            parameters["FirstName"] = user.FirstName;
        }
        if (!string.IsNullOrEmpty(user.LastName))
        {
            updates.Add("last_name = @LastName");
            parameters["LastName"] = user.LastName;
        }
        if (user.PhoneNumber != null)
        {
            updates.Add("phone_number = @PhoneNumber");
            parameters["PhoneNumber"] = user.PhoneNumber;
        }
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            updates.Add("password_hash = @PasswordHash");
            parameters["PasswordHash"] = user.PasswordHash;
        }
        
        var sql = $@"UPDATE users SET {string.Join(", ", updates)} WHERE id = @Id";
        await conn.ExecuteAsync(sql, parameters);
    }

    public async Task<Role?> GetRoleByNameAsync(string name)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<Role>(
            "SELECT * FROM roles WHERE name = @Name", new { Name = name });
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        using var conn = GetConnection();
        return await conn.QueryFirstOrDefaultAsync<RefreshToken>(
            "SELECT * FROM refresh_tokens WHERE token = @Token AND revoked_at IS NULL AND expires_at > NOW()",
            new { Token = token });
    }

    public async Task SaveRefreshTokenAsync(RefreshToken token)
    {
        using var conn = GetConnection();
        token.Id = Guid.NewGuid();
        token.CreatedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(
            @"INSERT INTO refresh_tokens (id, user_id, token, expires_at, created_at, created_by_ip)
              VALUES (@Id, @UserId, @Token, @ExpiresAt, @CreatedAt, @CreatedByIp)", token);
    }

    public async Task RevokeRefreshTokenAsync(string token, string? ip)
    {
        using var conn = GetConnection();
        await conn.ExecuteAsync(
            @"UPDATE refresh_tokens SET revoked_at = NOW(), revoked_by_ip = @Ip WHERE token = @Token",
            new { Token = token, Ip = ip });
    }

    public async Task<IEnumerable<User>> GetAllAsync(int page = 1, int pageSize = 20)
    {
        using var conn = GetConnection();
        var offset = (page - 1) * pageSize;
        return await conn.QueryAsync<User>(
            @"SELECT u.*, r.name as RoleName FROM users u 
              LEFT JOIN roles r ON u.role_id = r.id 
              ORDER BY u.created_at DESC LIMIT @PageSize OFFSET @Offset",
            new { PageSize = pageSize, Offset = offset });
    }

    public async Task<int> GetTotalCountAsync()
    {
        using var conn = GetConnection();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users");
    }
}
