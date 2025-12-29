using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stationnement.Web.Services;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }
    
    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var (success, message, user) = await _authService.RegisterAsync(
            request.Email, request.Password, request.FirstName, request.LastName);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data = new { user.Id, user.Email, user.FirstName } });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, message, user, accessToken, refreshToken) = 
            await _authService.LoginAsync(request.Email, request.Password);

        if (!success)
            return BadRequest(new { success = false, message });

        // Set cookies
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
        };

        Response.Cookies.Append("accessToken", accessToken!, cookieOptions);
        Response.Cookies.Append("refreshToken", refreshToken!, cookieOptions);

        // Log for debugging
        Console.WriteLine($"[LOGIN] User {user!.Email} logged in with RoleName: '{user.RoleName}'");

        return Ok(new
        {
            success = true,
            message,
            data = new
            {
                user = new { user!.Id, user.Email, user.FirstName, user.LastName, user.RoleName },
                accessToken,
                refreshToken
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { success = false, message = "No refresh token" });

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (success, newAccessToken, newRefreshToken) = 
            await _authService.RefreshTokenAsync(refreshToken, ipAddress);

        if (!success)
            return Unauthorized(new { success = false, message = "Invalid or expired refresh token" });

        Response.Cookies.Append("accessToken", newAccessToken!, new CookieOptions { HttpOnly = true, Secure = true });
        Response.Cookies.Append("refreshToken", newRefreshToken!, new CookieOptions { HttpOnly = true, Secure = true });

        return Ok(new { success = true, data = new { accessToken = newAccessToken } });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _authService.LogoutAsync(refreshToken, ipAddress);
        }

        Response.Cookies.Delete("accessToken");
        Response.Cookies.Delete("refreshToken");

        return Ok(new { success = true, message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = GetUserId();
        var user = await _authService.GetUserByIdAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "User not found" });

        return Ok(new
        {
            success = true,
            data = new
            {
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.RoleName
            }
        });
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.UpdateProfileAsync(
            userId, request.FirstName, request.LastName, request.PhoneNumber);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    [HttpPut("password")]
    [Authorize]
    public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordRequest request)
    {
        var userId = GetUserId();
        var (success, message) = await _authService.UpdatePasswordAsync(
            userId, request.CurrentPassword, request.NewPassword);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    public class UpdateProfileRequest
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
    }

    public class UpdatePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
