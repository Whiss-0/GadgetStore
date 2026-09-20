using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.CartModule;
using api.ActivityModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class CartController : ControllerBase
    {
        private readonly ICartRespository _cartRepository;
        private readonly IActivityRepository _activityRepository;
        private readonly ILogger<CartController> _logger;

        public CartController(
            ICartRespository cartRepository,
            IActivityRepository activityRepository,
            ILogger<CartController> logger)
        {
            _cartRepository = cartRepository;
            _activityRepository = activityRepository;
            _logger = logger;
        }

        /// <summary>Get the current user's cart items.</summary>
        [HttpGet("my")]
        public async Task<ActionResult<List<Cart>>> GetMyCart(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            return Ok(await _cartRepository.GetByUserAsync(userId, ct));
        }

        /// <summary>Get cart items for any user (admin only).</summary>
        [Authorize(Policy = "AdminAccess")]
        [HttpGet("user/{userId:int}")]
        public async Task<ActionResult<List<Cart>>> GetByUser(int userId, CancellationToken ct)
        {
            return Ok(await _cartRepository.GetByUserAsync(userId, ct));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Cart>> GetById(int id, CancellationToken ct)
        {
            var cart = await _cartRepository.GetByIdAsync(id, ct);
            if (cart == null) return NotFound(new { message = $"Cart item with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (cart.user_id != userId && !User.HasClaim("user_role_id", "1"))
                return Forbid();

            return Ok(cart);
        }

        /// <summary>Add a product to cart. If already in cart, updates the quantity.</summary>
        [HttpPost]
        public async Task<ActionResult<Cart>> AddToCart([FromBody] CartRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            // Always use the authenticated user's own id — never trust dto.User_ID
            int effectiveUserId = userId;

            // Check if already in cart
            var existing = await _cartRepository.GetByUserAndProductAsync(effectiveUserId, dto.Product_ID, ct);
            if (existing != null)
            {
                existing.quantity += dto.Quantity;
                await _cartRepository.UpdateQuantityAsync(existing.cart_id, existing.quantity, ct);
                return Ok(existing);
            }

            var cart = new Cart
            {
                user_id = effectiveUserId,
                product_id = dto.Product_ID,
                quantity = dto.Quantity
            };
            int newId = await _cartRepository.CreateAsync(cart, ct);

            _ = TryLogAsync(new ActivityLog
            {
                User_ID            = effectiveUserId,
                Actor_User_ID      = effectiveUserId,
                Actor_Role         = "User",
                Activity_Type      = "CartItemAdded",
                Description        = $"Added product #{dto.Product_ID} to the cart.",
                Related_Product_ID = dto.Product_ID,
                Created_At         = DateTime.UtcNow,
            });

            return CreatedAtAction(nameof(GetById), new { id = newId }, cart);
        }

        /// <summary>Update quantity of a cart item.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateQuantity(int id, [FromBody] CartQuantityRequest dto, CancellationToken ct)
        {
            if (dto.Quantity < 1) return BadRequest(new { message = "Quantity must be at least 1." });

            var cart = await _cartRepository.GetByIdAsync(id, ct);
            if (cart == null) return NotFound(new { message = $"Cart item with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (cart.user_id != userId && !User.HasClaim("user_role_id", "1"))
                return Forbid();

            bool updated = await _cartRepository.UpdateQuantityAsync(id, dto.Quantity, ct);
            if (!updated) return NotFound(new { message = $"Cart item with ID {id} not found." });

            _ = TryLogAsync(new ActivityLog
            {
                User_ID       = userId,
                Actor_User_ID = userId,
                Actor_Role    = "User",
                Activity_Type = "CartUpdated",
                Description   = $"Updated cart item #{id} quantity to {dto.Quantity}.",
                Created_At    = DateTime.UtcNow,
            });

            return NoContent();
        }

        /// <summary>Remove a specific item from cart.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var cart = await _cartRepository.GetByIdAsync(id, ct);
            if (cart == null) return NotFound(new { message = $"Cart item with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (cart.user_id != userId && !User.HasClaim("user_role_id", "1"))
                return Forbid();

            bool deleted = await _cartRepository.DeleteAsync(id, ct);
            if (!deleted) return NotFound(new { message = $"Cart item with ID {id} not found." });

            _ = TryLogAsync(new ActivityLog
            {
                User_ID            = cart.user_id,
                Actor_User_ID      = userId,
                Actor_Role         = "User",
                Activity_Type      = "CartItemRemoved",
                Description        = $"Removed product #{cart.product_id} from the cart.",
                Related_Product_ID = cart.product_id,
                Created_At         = DateTime.UtcNow,
            });

            return NoContent();
        }

        /// <summary>Clear the current user's cart.</summary>
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCart(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            await _cartRepository.ClearCartAsync(userId, ct);

            _ = TryLogAsync(new ActivityLog
            {
                User_ID       = userId,
                Actor_User_ID = userId,
                Actor_Role    = "User",
                Activity_Type = "CartCleared",
                Description   = "Cleared the cart.",
                Created_At    = DateTime.UtcNow,
            });

            return NoContent();
        }

        private async Task TryLogAsync(ActivityLog log)
        {
            try { await _activityRepository.CreateAsync(log); }
            catch (Exception ex) { _logger.LogWarning(ex, "Activity log write failed ({Type})", log.Activity_Type); }
        }
    }

    public class CartRequest
    {
        public int User_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int Product_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Quantity { get; set; } = 1;
    }

    public class CartQuantityRequest
    {
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Quantity { get; set; }
    }
}
