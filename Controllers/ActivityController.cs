using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.ActivityModule;
using System.Security.Claims;

namespace api.Controllers
{
    [ApiController]
    [Route("api/activity")]
    public class ActivityController : ControllerBase
    {
        private readonly IActivityRepository _activityRepository;

        public ActivityController(IActivityRepository activityRepository)
        {
            _activityRepository = activityRepository;
        }

        /// <summary>Returns the authenticated user's own activity.</summary>
        [Authorize(Policy = "UserAccess")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMy(
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized();

            var logs = await _activityRepository.GetForUserAsync(userId, limit, ct);
            return Ok(logs);
        }

        /// <summary>Returns customer-only activity (actor_role = "User"). Staff and admin only.</summary>
        [Authorize(Policy = "ModAccess")]
        [HttpGet("customers")]
        public async Task<IActionResult> GetCustomers(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? type,
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            var logs = await _activityRepository.GetAllAsync(from, to, type, null, "User", limit, ct);
            return Ok(logs);
        }

        /// <summary>Returns activity for a specific user. Staff and admin only.</summary>
        [Authorize(Policy = "ModAccess")]
        [HttpGet("user/{userId:int}")]
        public async Task<IActionResult> GetForUser(int userId, [FromQuery] int? limit, CancellationToken ct)
        {
            // Moderators should only see customer (User-role) activity for this endpoint.
            // Admins can see all activity for the user.
            string? roleFilter = User.HasClaim("user_role_id", "1") ? null : "User";
            var logs = await _activityRepository.GetAllAsync(null, null, null, userId, roleFilter, limit, ct);
            return Ok(logs);
        }

        /// <summary>Returns the full activity log. Admin only.</summary>
        [Authorize(Policy = "AdminAccess")]
        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? type,
            [FromQuery] int? userId,
            [FromQuery] string? actorRole,
            [FromQuery] int? limit,
            CancellationToken ct)
        {
            var logs = await _activityRepository.GetAllAsync(from, to, type, userId, actorRole, limit, ct);
            return Ok(logs);
        }
    }
}
