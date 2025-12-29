namespace Stationnement.Web.Models;

public class AdminSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? IpAddress { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation
    public string? UserEmail { get; set; }
}
