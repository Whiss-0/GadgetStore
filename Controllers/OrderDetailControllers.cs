using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.OrderDetailModule;
using api.OrderModule;
using api.ProductsModule;
using api.ActivityModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class OrderDetailController : ControllerBase
    {
        private readonly IOrderDetailRepository _orderDetailRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly IActivityRepository _activityRepository;
        private readonly ILogger<OrderDetailController> _logger;

        public OrderDetailController(
            IOrderDetailRepository orderDetailRepository,
            IOrderRepository orderRepository,
            IProductRepository productRepository,
            IActivityRepository activityRepository,
            ILogger<OrderDetailController> logger)
        {
            _orderDetailRepository = orderDetailRepository;
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _activityRepository = activityRepository;
            _logger = logger;
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpGet]
        public async Task<ActionResult<List<OrderDetail>>> GetAll(CancellationToken ct)
        {
            return Ok(await _orderDetailRepository.GetAllAsync(ct));
        }

        /// <summary>
        /// Returns all line items for an order, with each line enriched with its product.
        /// Customers may only view their own orders; staff/admin may view any order.
        /// </summary>
        [HttpGet("order/{orderId:int}")]
        public async Task<ActionResult<List<OrderDetail>>> GetByOrder(int orderId, CancellationToken ct)
        {
            // Ownership / authorization check
            var order = await _orderRepository.GetByIdAsync(orderId, ct);
            if (order == null)
                return NotFound(new { message = $"Order {orderId} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);

            bool isStaffOrAdmin = User.HasClaim("user_role_id", "1") || User.HasClaim("user_role_id", "2");
            if (!isStaffOrAdmin && order.user_id != userId)
                return Forbid();

            var details = await _orderDetailRepository.GetByOrderAsync(orderId, ct);

            // Enrich each line with its product (best-effort — deleted products get null)
            foreach (var detail in details)
            {
                try
                {
                    detail.product = await _productRepository.GetByIdAsync(detail.product_id, ct);
                }
                catch
                {
                    detail.product = null;
                }
            }

            return Ok(details);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<OrderDetail>> GetById(int id, CancellationToken ct)
        {
            var detail = await _orderDetailRepository.GetByIdAsync(id, ct);
            if (detail == null) return NotFound(new { message = $"OrderDetail with ID {id} not found." });
            return Ok(detail);
        }

        [HttpPost]
        public async Task<ActionResult<OrderDetail>> Create([FromBody] OrderDetailRequest dto, CancellationToken ct)
        {
            Console.WriteLine($"[ORDER DETAIL CREATE] Received OrderId: '{dto.OrderId}', ProductId: '{dto.ProductId}', Quantity: '{dto.Quantity}', Price: '{dto.Price}'");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var product = await _productRepository.GetByIdAsync(dto.ProductId, ct);
            if (product == null)
                return NotFound(new { message = $"Product {dto.ProductId} not found." });

            if (product.stock < dto.Quantity)
                return BadRequest(new { message = $"Only {product.stock} of '{product.product_name}' left in stock." });

            bool decremented = await _productRepository.DecrementStockAsync(dto.ProductId, dto.Quantity, ct);
            if (!decremented)
                return BadRequest(new { message = $"'{product.product_name}' went out of stock while you were checking out." });

            var detail = new OrderDetail
            {
                order_id = dto.OrderId,
                product_id = dto.ProductId,
                quantity = dto.Quantity,
                price = dto.Price
            };
            int newId = await _orderDetailRepository.CreateAsync(detail, ct);
            string? actorRole = GetAuditedActorRole();
            if (actorRole != null)
            {
                await TryLogAuditAsync(new ActivityLog
                {
                    Actor_User_ID = GetActorId(),
                    Actor_Role = actorRole,
                    Activity_Type = "OrderDetailCreated",
                    Description = $"Added a product line to order #{dto.OrderId} (product #{dto.ProductId}).",
                    Related_Order_ID = dto.OrderId,
                    Related_Product_ID = dto.ProductId,
                    Created_At = DateTime.UtcNow,
                });
            }
            return CreatedAtAction(nameof(GetById), new { id = newId }, detail);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] OrderDetailRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _orderDetailRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"OrderDetail with ID {id} not found." });
            int existingOrderId = existing.order_id;
            int existingProductId = existing.product_id;
            existing.order_id = dto.OrderId;
            existing.product_id = dto.ProductId;
            existing.quantity = dto.Quantity;
            existing.price = dto.Price;
            bool updated = await _orderDetailRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update order detail." });
            string? actorRole = GetAuditedActorRole();
            if (actorRole != null)
            {
                await TryLogAuditAsync(new ActivityLog
                {
                    Actor_User_ID = GetActorId(),
                    Actor_Role = actorRole,
                    Activity_Type = "OrderDetailUpdated",
                    Description = $"Updated a product line on order #{existingOrderId}.",
                    Related_Order_ID = existingOrderId,
                    Related_Product_ID = existingProductId,
                    Created_At = DateTime.UtcNow,
                });
            }
            return NoContent();
        }

        [Authorize(Policy = "ModAccess")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var existing = await _orderDetailRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"OrderDetail with ID {id} not found." });
            bool deleted = await _orderDetailRepository.DeleteAsync(id, ct);
            if (!deleted) return StatusCode(500, new { message = "Failed to delete order detail." });
            string? actorRole = GetAuditedActorRole();
            if (actorRole != null)
            {
                await TryLogAuditAsync(new ActivityLog
                {
                    Actor_User_ID = GetActorId(),
                    Actor_Role = actorRole,
                    Activity_Type = "OrderDetailDeleted",
                    Description = $"Removed a product line from order #{existing.order_id}.",
                    Related_Order_ID = existing.order_id,
                    Related_Product_ID = existing.product_id,
                    Created_At = DateTime.UtcNow,
                });
            }
            return NoContent();
        }

        private int? GetActorId()
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out int id) ? id : null;
        }

        private string? GetAuditedActorRole() => User.HasClaim("user_role_id", "1") ? "Admin" :
            User.HasClaim("user_role_id", "2") ? "Staff" : null;

        private async Task TryLogAuditAsync(ActivityLog log)
        {
            try { await _activityRepository.CreateAsync(log); }
            catch (Exception ex) { _logger.LogWarning(ex, "Audit log write failed ({Type})", log.Activity_Type); }
        }
    }

    public class OrderDetailRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public int OrderId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int ProductId { get; set; }
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Quantity { get; set; }
        [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue)]
        public decimal Price { get; set; }
    }
}
