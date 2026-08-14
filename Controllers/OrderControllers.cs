using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Main;
using api.OrderModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderRepository _orderRepository;

        public OrderController(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpGet]
        public async Task<ActionResult<object>> GetAll([FromQuery] int? pageNumber, [FromQuery] int? pageSize, CancellationToken ct)
        {
            if (pageNumber.HasValue && pageSize.HasValue)
                return Ok(await _orderRepository.GetPagedAsync(pageNumber.Value, pageSize.Value, ct));
            return Ok(await _orderRepository.GetAllAsync(ct));
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpGet("user/{userId:int}")]
        public async Task<ActionResult<List<Order>>> GetByUser(int userId, CancellationToken ct)
        {
            return Ok(await _orderRepository.GetByUserAsync(userId, ct));
        }

        [HttpGet("my")]
        public async Task<ActionResult<List<Order>>> GetMyOrders(CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            return Ok(await _orderRepository.GetByUserAsync(userId, ct));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Order>> GetById(int id, CancellationToken ct)
        {
            var order = await _orderRepository.GetByIdAsync(id, ct);
            if (order == null) return NotFound(new { message = $"Order with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (order.user_id != userId && !User.HasClaim("user_role_id", "1") && !User.HasClaim("user_role_id", "2"))
                return Forbid();

            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<Order>> Create([FromBody] OrderRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);

            // Card/GCash are simulated: mark payment_status as paid immediately since
            // there's no real gateway behind this. COD stays Unpaid until it's paid
            // on delivery. `status` (shipping progress) always starts at 'Pending' —
            // never write "Paid (Simulated)" into status, it violates that column's
            // CHECK constraint.
            string paymentStatus = dto.PaymentMethod == "COD" ? "Unpaid" : "Paid (Simulated)";

            Console.WriteLine($"[ORDER CREATE] Received TotalAmount: '{dto.TotalAmount}', PaymentMethod: '{dto.PaymentMethod}'");

            var order = new Order
            {
                user_id           = userId,   // never trust a client-supplied user id
                order_date        = DateTime.UtcNow,
                status            = "Pending",
                total_amount      = dto.TotalAmount,
                shipping_address  = dto.ShippingAddress,
                phone_number      = dto.PhoneNumber,
                payment_method    = dto.PaymentMethod,
                payment_status    = paymentStatus
            };
            int newId = await _orderRepository.CreateAsync(order, ct);
            return CreatedAtAction(nameof(GetById), new { id = newId }, order);
        }

        [Authorize(Policy = "ModAccess")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] OrderUpdateRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _orderRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Order with ID {id} not found." });
            existing.status = dto.Status ?? existing.status;
            bool updated = await _orderRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update order." });
            return NoContent();
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var existing = await _orderRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Order with ID {id} not found." });
            bool deleted = await _orderRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete order." });
            return NoContent();
        }
    }

    public class OrderRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public decimal TotalAmount { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(300)]
        public string ShippingAddress { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.Phone]
        public string PhoneNumber { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.RegularExpression("^(COD|Card|GCash)$")]
        public string PaymentMethod { get; set; } = "COD";
    }

    public class OrderUpdateRequest
    {
        public string? Status { get; set; }
    }
}
