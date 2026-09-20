using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.WishlistModule;
using api.ActivityModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class WishlistController : ControllerBase
    {
        private readonly IWishlistRepository _wishlistRepository;
        private readonly IActivityRepository _activityRepository;
        private readonly ILogger<WishlistController> _logger;

        public WishlistController(
            IWishlistRepository wishlistRepository,
            IActivityRepository activityRepository,
            ILogger<WishlistController> logger)
        {
            _wishlistRepository = wishlistRepository;
            _activityRepository = activityRepository;
            _logger = logger;
        }

        /// <summary>Get the current user's wishlist.</summary>
        [HttpGet("my")]
        public async Task<ActionResult<List<Wishlist>>> GetMyWishlist(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            return Ok(await _wishlistRepository.GetByUserAsync(userId, ct));
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpGet("user/{userId:int}")]
        public async Task<ActionResult<List<Wishlist>>> GetByUser(int userId, CancellationToken ct)
        {
            return Ok(await _wishlistRepository.GetByUserAsync(userId, ct));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Wishlist>> GetById(int id, CancellationToken ct)
        {
            var item = await _wishlistRepository.GetByIdAsync(id, ct);
            if (item == null) return NotFound(new { message = $"Wishlist item with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (item.user_id != userId && !User.HasClaim("user_role_id", "1"))
                return Forbid();

            return Ok(item);
        }

        /// <summary>Add a product to wishlist. Ignores duplicates.</summary>
        [HttpPost]
        public async Task<ActionResult<Wishlist>> AddToWishlist([FromBody] WishlistRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            int effectiveUserId = userId;   // never trust dto.User_ID

            // Prevent duplicates
            var existing = await _wishlistRepository.GetByUserAndProductAsync(effectiveUserId, dto.Product_ID, ct);
            if (existing != null) return Ok(existing);

            var wishlist = new Wishlist { user_id = effectiveUserId, product_id = dto.Product_ID };
            int newId = await _wishlistRepository.CreateAsync(wishlist, ct);

            _ = TryLogAsync(new ActivityLog
            {
                User_ID            = effectiveUserId,
                Actor_User_ID      = effectiveUserId,
                Actor_Role         = "User",
                Activity_Type      = "WishlistItemAdded",
                Description        = $"Saved product #{dto.Product_ID} to the wishlist.",
                Related_Product_ID = dto.Product_ID,
                Created_At         = DateTime.UtcNow,
            });

            return CreatedAtAction(nameof(GetById), new { id = newId }, wishlist);
        }

        /// <summary>Remove an item from wishlist.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var item = await _wishlistRepository.GetByIdAsync(id, ct);
            if (item == null) return NotFound(new { message = $"Wishlist item with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (item.user_id != userId && !User.HasClaim("user_role_id", "1"))
                return Forbid();

            bool deleted = await _wishlistRepository.DeleteAsync(id, ct);
            if (!deleted) return NotFound(new { message = $"Wishlist item with ID {id} not found." });

            _ = TryLogAsync(new ActivityLog
            {
                User_ID            = item.user_id,
                Actor_User_ID      = userId,
                Actor_Role         = "User",
                Activity_Type      = "WishlistItemRemoved",
                Description        = $"Removed product #{item.product_id} from the wishlist.",
                Related_Product_ID = item.product_id,
                Created_At         = DateTime.UtcNow,
            });

            return NoContent();
        }

        /// <summary>Clear the current user's wishlist.</summary>
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearWishlist(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            await _wishlistRepository.ClearWishlistAsync(userId, ct);
            return NoContent();
        }

        private async Task TryLogAsync(ActivityLog log)
        {
            try { await _activityRepository.CreateAsync(log); }
            catch (Exception ex) { _logger.LogWarning(ex, "Activity log write failed ({Type})", log.Activity_Type); }
        }
    }

    public class WishlistRequest
    {
        public int User_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int Product_ID { get; set; }
    }
}
