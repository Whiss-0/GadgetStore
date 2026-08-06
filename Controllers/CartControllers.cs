using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.CartModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class CartController : ControllerBase
    {
        private readonly ICartRespository _cartRepository;

        public CartController(ICartRespository cartRepository)
        {
            _cartRepository = cartRepository;
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
            return Ok(cart);
        }

        /// <summary>Add a product to cart. If already in cart, updates the quantity.</summary>
        [HttpPost]
        public async Task<ActionResult<Cart>> AddToCart([FromBody] CartRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            int effectiveUserId = dto.User_ID > 0 ? dto.User_ID : userId;

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
            return CreatedAtAction(nameof(GetById), new { id = newId }, cart);
        }

        /// <summary>Update quantity of a cart item.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateQuantity(int id, [FromBody] CartQuantityRequest dto, CancellationToken ct)
        {
            if (dto.Quantity < 1) return BadRequest(new { message = "Quantity must be at least 1." });
            bool updated = await _cartRepository.UpdateQuantityAsync(id, dto.Quantity, ct);
            if (!updated) return NotFound(new { message = $"Cart item with ID {id} not found." });
            return NoContent();
        }

        /// <summary>Remove a specific item from cart.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            bool deleted = await _cartRepository.DeleteAsync(id, ct);
            if (!deleted) return NotFound(new { message = $"Cart item with ID {id} not found." });
            return NoContent();
        }

        /// <summary>Clear the current user's cart.</summary>
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCart(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            await _cartRepository.ClearCartAsync(userId, ct);
            return NoContent();
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
