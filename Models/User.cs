namespace Stationnement.Web.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool EmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpires { get; set; }
    public Guid RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public string? RoleName { get; set; }
}

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReplacedByToken { get; set; }
}

public class Vehicle
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public string VehicleType { get; set; } = "car";
    public bool IsDefault { get; set; }
}
