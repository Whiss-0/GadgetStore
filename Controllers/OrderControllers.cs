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
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<Order>> Create([FromBody] OrderRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);

            var order = new Order
            {
                user_id = dto.User_ID > 0 ? dto.User_ID : userId,
                order_date = DateTime.UtcNow,
                status = "Pending",
                total_amount = dto.TotalAmount
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
        public int User_ID { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue)]
        public decimal TotalAmount { get; set; }
    }

    public class OrderUpdateRequest
    {
        public string? Status { get; set; }
    }
}
