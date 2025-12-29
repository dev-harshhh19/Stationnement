using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Stationnement.Web.Models;
using Stationnement.Web.Repositories;

namespace Stationnement.Web.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, User? User)> RegisterAsync(string email, string password, string firstName, string lastName);
    Task<(bool Success, string Message, User? User, string? AccessToken, string? RefreshToken)> LoginAsync(string email, string password);
    Task<(bool Success, string? AccessToken, string? RefreshToken)> RefreshTokenAsync(string refreshToken, string? ipAddress);
    Task LogoutAsync(string refreshToken, string? ipAddress);
    Task<(bool Success, string Message)> UpdateProfileAsync(Guid userId, string firstName, string lastName, string? phoneNumber);
    Task<(bool Success, string Message)> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<User?> GetUserByIdAsync(Guid userId);
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public AuthService(IUserRepository userRepository, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    public async Task<(bool Success, string Message, User? User)> RegisterAsync(string email, string password, string firstName, string lastName)
    {
        // Check if user exists
        var existingUser = await _userRepository.GetByEmailAsync(email);
        if (existingUser != null)
            return (false, "Email already registered", null);

        // Get default role
        var role = await _userRepository.GetRoleByNameAsync("User");
        if (role == null)
            return (false, "System error: Default role not found", null);

        // Hash password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

        // Create user
        var user = new User
        {
            Email = email,
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            RoleId = role.Id,
            RoleName = role.Name,
            EmailVerificationToken = Guid.NewGuid().ToString("N"),
            IsActive = true
        };

        await _userRepository.CreateAsync(user);
        return (true, "Registration successful", user);
    }

    public async Task<(bool Success, string Message, User? User, string? AccessToken, string? RefreshToken)> LoginAsync(string email, string password)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        if (user == null)
            return (false, "Invalid email or password", null, null, null);

        if (!user.IsActive)
            return (false, "Account is disabled", null, null, null);

        if (string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (false, "Invalid email or password", null, null, null);

        var accessToken = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();

        // Save refresh token
        await _userRepository.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "7"))
        });

        return (true, "Login successful", user, accessToken, refreshToken);
    }

    public async Task<(bool Success, string? AccessToken, string? RefreshToken)> RefreshTokenAsync(string refreshToken, string? ipAddress)
    {
        var storedToken = await _userRepository.GetRefreshTokenAsync(refreshToken);
        if (storedToken == null)
            return (false, null, null);

        var user = await _userRepository.GetByIdAsync(storedToken.UserId);
        if (user == null || !user.IsActive)
            return (false, null, null);

        // Revoke old token
        await _userRepository.RevokeRefreshTokenAsync(refreshToken, ipAddress);

        // Generate new tokens
        var newAccessToken = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken();

        await _userRepository.SaveRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "7")),
            CreatedByIp = ipAddress
        });

        return (true, newAccessToken, newRefreshToken);
    }

    public async Task LogoutAsync(string refreshToken, string? ipAddress)
    {
        await _userRepository.RevokeRefreshTokenAsync(refreshToken, ipAddress);
    }

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName),
            new Claim(ClaimTypes.Role, user.RoleName ?? "User")
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(_configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15")),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    public async Task<(bool Success, string Message)> UpdateProfileAsync(Guid userId, string firstName, string lastName, string? phoneNumber)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return (false, "User not found");

        user.FirstName = firstName;
        user.LastName = lastName;
        user.PhoneNumber = phoneNumber;
        await _userRepository.UpdateAsync(user);

        return (true, "Profile updated successfully");
    }

    public async Task<(bool Success, string Message)> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return (false, "User not found");

        if (string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return (false, "Current password is incorrect");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "New password must be at least 6 characters long");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _userRepository.UpdateAsync(user);

        return (true, "Password updated successfully");
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _userRepository.GetByIdAsync(userId);
    }
}
