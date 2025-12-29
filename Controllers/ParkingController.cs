using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stationnement.Web.Repositories;
using Stationnement.Web.Services;

namespace Stationnement.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ParkingController : ControllerBase
{
    private readonly IParkingRepository _parkingRepository;
    private readonly IReservationService _reservationService;

    public ParkingController(IParkingRepository parkingRepository, IReservationService reservationService)
    {
        _parkingRepository = parkingRepository;
        _reservationService = reservationService;
    }

    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations()
    {
        var locations = await _parkingRepository.GetAllLocationsAsync();
        return Ok(new { success = true, data = locations });
    }

    [HttpGet("locations/{id}")]
    public async Task<IActionResult> GetLocation(Guid id)
    {
        var location = await _parkingRepository.GetLocationByIdAsync(id);
        if (location == null)
            return NotFound(new { success = false, message = "Location not found" });
        return Ok(new { success = true, data = location });
    }

    [HttpGet("locations/{id}/slots")]
    public async Task<IActionResult> GetSlots(Guid id)
    {
        var slots = await _parkingRepository.GetSlotsByLocationAsync(id);
        return Ok(new { success = true, data = slots });
    }

    [HttpGet("available-slots")]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] Guid locationId, 
        [FromQuery] DateTime startTime, 
        [FromQuery] DateTime endTime)
    {
        // Return all slots with availability status (including locked/booked ones)
        var slots = await _parkingRepository.GetSlotsWithAvailabilityAsync(locationId, startTime, endTime);
        return Ok(new { success = true, data = slots });
    }

    [HttpGet("calculate-price")]
    [Authorize]
    public async Task<IActionResult> CalculatePrice(
        [FromQuery] Guid slotId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var userId = GetUserId();
        var (baseAmount, discountAmount, surchargeAmount, totalAmount, discountSource) = 
            await _reservationService.CalculatePriceAsync(slotId, startTime, endTime, userId);

        return Ok(new
        {
            success = true,
            data = new { baseAmount, discountAmount, surchargeAmount, totalAmount, discountSource }
        });
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : null;
    }
}
