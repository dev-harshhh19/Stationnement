using Microsoft.AspNetCore.Mvc;
using Stationnement.Web.Services;
using System.Security.Claims;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    /// <summary>
    /// Get all available subscription tiers
    /// </summary>
    [HttpGet("tiers")]
    public IActionResult GetTiers()
    {
        var tiers = _subscriptionService.GetAllTiers();
        return Ok(new { success = true, data = tiers });
    }

    /// <summary>
    /// Get current user's subscription
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentSubscription()
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Unauthorized(new { success = false, message = "Not authenticated" });

        var subscription = await _subscriptionService.GetUserSubscriptionAsync(userId.Value);
        return Ok(new { success = true, data = subscription });
    }

    /// <summary>
    /// Check if user can access premium slots
    /// </summary>
    [HttpGet("can-access-premium")]
    public async Task<IActionResult> CanAccessPremium()
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Ok(new { success = true, data = new { canAccess = false } });

        var canAccess = await _subscriptionService.CanAccessPremiumSlotsAsync(userId.Value);
        return Ok(new { success = true, data = new { canAccess } });
    }

    /// <summary>
    /// Activate a subscription (for demo - in production, integrate with payment)
    /// </summary>
    [HttpPost("activate")]
    public async Task<IActionResult> ActivateSubscription([FromBody] ActivateSubscriptionRequest request)
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Unauthorized(new { success = false, message = "Not authenticated" });

        try
        {
            var subscription = await _subscriptionService.ActivateSubscriptionAsync(userId.Value, request.Tier);
            return Ok(new { success = true, data = subscription, message = $"Subscription activated: {subscription.TierInfo.Name}" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Cancel current subscription
    /// </summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> CancelSubscription()
    {
        var userId = GetUserIdFromToken();
        if (userId == null)
            return Unauthorized(new { success = false, message = "Not authenticated" });

        try
        {
            var success = await _subscriptionService.CancelSubscriptionAsync(userId.Value);
            if (success)
                return Ok(new { success = true, message = "Subscription cancelled successfully" });
            return BadRequest(new { success = false, message = "No active subscription to cancel" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private Guid? GetUserIdFromToken()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;
        
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    public class ActivateSubscriptionRequest
    {
        public string Tier { get; set; } = "free";
    }
}
