using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stationnement.Web.Models;
using Stationnement.Web.Repositories;
using Stationnement.Web.Services;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/admin/auth")]
public class AdminAuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IAdminSessionRepository _adminSessionRepository;

    public AdminAuthController(IAuthService authService, IAdminSessionRepository adminSessionRepository)
    {
        _authService = authService;
        _adminSessionRepository = adminSessionRepository;
    }

    public class AdminLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Admin-only login endpoint with single-session enforcement.
    /// Only one admin can be logged in at a time.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> AdminLogin([FromBody] AdminLoginRequest request)
    {
        // First, authenticate normally
        var (success, message, user, accessToken, refreshToken) = 
            await _authService.LoginAsync(request.Email, request.Password);

        if (!success)
            return BadRequest(new { success = false, message });

        // Check if user has Admin role
        if (user?.RoleName != "Admin")
        {
            Console.WriteLine($"[ADMIN AUTH] User {request.Email} attempted admin login but has role '{user?.RoleName}'");
            return Unauthorized(new { success = false, message = "Access denied. Admin privileges required." });
        }

        // Ensure admin_sessions table exists
        await _adminSessionRepository.EnsureTableExistsAsync();

        // Revoke ALL previous admin sessions (single-session enforcement)
        await _adminSessionRepository.RevokeAllSessionsAsync();
        Console.WriteLine($"[ADMIN AUTH] Revoked all previous admin sessions");

        // Create new admin session
        var sessionId = Guid.NewGuid();
        var tokenHash = HashToken(accessToken!);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var adminSession = new AdminSession
        {
            Id = sessionId,
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(2), // Admin session expires in 2 hours
            IpAddress = ipAddress,
            IsActive = true
        };

        await _adminSessionRepository.CreateSessionAsync(adminSession);
        Console.WriteLine($"[ADMIN AUTH] Created new admin session for {user.Email}, Session ID: {sessionId}");

        // Set cookies
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(2)
        };

        Response.Cookies.Append("adminAccessToken", accessToken!, cookieOptions);
        Response.Cookies.Append("adminSessionId", sessionId.ToString(), cookieOptions);

        return Ok(new
        {
            success = true,
            message = "Admin login successful. Previous sessions have been terminated.",
            data = new
            {
                user = new { user.Id, user.Email, user.FirstName, user.LastName, user.RoleName },
                accessToken,
                sessionId,
                expiresAt = adminSession.ExpiresAt
            }
        });
    }

    /// <summary>
    /// Validate current admin session is still active
    /// </summary>
    [HttpGet("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateSession()
    {
        // Get token from Authorization header or cookie
        var accessToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "").Trim();
        
        if (string.IsNullOrEmpty(accessToken))
        {
            accessToken = Request.Cookies["adminAccessToken"];
        }
        
        if (string.IsNullOrEmpty(accessToken))
        {
            return Ok(new { valid = false, success = false, message = "No access token found" });
        }

        var tokenHash = HashToken(accessToken);
        var session = await _adminSessionRepository.GetByTokenHashAsync(tokenHash);

        if (session == null)
        {
            return Ok(new { valid = false, success = false, message = "Session not found" });
        }
        
        if (!session.IsActive)
        {
            return Ok(new { valid = false, success = false, message = "Session revoked" });
        }
        
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            return Ok(new { valid = false, success = false, message = "Session expired" });
        }

        return Ok(new { 
            valid = true, 
            success = true, 
            message = "Session valid", 
            email = session.UserEmail,
            expiresAt = session.ExpiresAt 
        });
    }

    /// <summary>
    /// Admin logout - revokes the current session
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> AdminLogout()
    {
        var sessionIdStr = Request.Cookies["adminSessionId"];
        if (!string.IsNullOrEmpty(sessionIdStr) && Guid.TryParse(sessionIdStr, out var sessionId))
        {
            await _adminSessionRepository.RevokeSessionAsync(sessionId);
            Console.WriteLine($"[ADMIN AUTH] Admin session {sessionId} logged out");
        }

        Response.Cookies.Delete("adminAccessToken");
        Response.Cookies.Delete("adminSessionId");

        return Ok(new { success = true, message = "Admin logged out successfully" });
    }

    /// <summary>
    /// Get current active admin session info (for debugging/monitoring)
    /// </summary>
    [HttpGet("active-session")]
    [Authorize]
    public async Task<IActionResult> GetActiveSession()
    {
        var session = await _adminSessionRepository.GetActiveSessionAsync();
        
        if (session == null)
        {
            return Ok(new { success = true, hasActiveSession = false });
        }

        return Ok(new { 
            success = true, 
            hasActiveSession = true,
            session = new {
                sessionId = session.Id,
                userEmail = session.UserEmail,
                createdAt = session.CreatedAt,
                expiresAt = session.ExpiresAt,
                ipAddress = session.IpAddress
            }
        });
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
