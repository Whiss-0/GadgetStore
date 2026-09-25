using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Main;
using api.OrderModule;
using api.UserModule;
using api.Security;
using api.ActivityModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IUserRepository _userRepository;
        private readonly IOrderEmailSender _orderEmailSender;
        private readonly IActivityRepository _activityRepository;
        private readonly ILogger<OrderController> _logger;

        public OrderController(
            IOrderRepository orderRepository,
            IUserRepository userRepository,
            IOrderEmailSender orderEmailSender,
            IActivityRepository activityRepository,
            ILogger<OrderController> logger)
        {
            _orderRepository = orderRepository;
            _userRepository = userRepository;
            _orderEmailSender = orderEmailSender;
            _activityRepository = activityRepository;
            _logger = logger;
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

            // Record order placed
            _ = TryLogAsync(new ActivityLog
            {
                User_ID           = userId,
                Actor_User_ID     = userId,
                Actor_Role        = "User",
                Activity_Type     = "OrderPlaced",
                Description       = $"Placed order #{newId}.",
                Related_Order_ID  = newId,
                Created_At        = DateTime.UtcNow,
            });

            // Best-effort confirmation email — doesn't block or fail the order if it errors.
            var user = await _userRepository.GetByIdAsync(userId, ct);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                _ = _orderEmailSender.SendOrderConfirmationAsync(user.Email, newId, dto.TotalAmount, dto.PaymentMethod, ct);
            }

            return CreatedAtAction(nameof(GetById), new { id = newId }, order);
        }

        [HttpPut("{id:int}/cancel")]
        public async Task<IActionResult> CancelMyOrder(int id, CancellationToken ct)
        {
            var order = await _orderRepository.GetByIdAsync(id, ct);
            if (order == null) return NotFound(new { message = $"Order with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdClaim, out int userId);
            if (order.user_id != userId)
                return Forbid();

            if (order.status != "Pending")
                return BadRequest(new { message = $"Can't cancel an order that's already '{order.status}'. Contact support instead." });

            order.status = "Cancelled";
            bool updated = await _orderRepository.UpdateAsync(order, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to cancel order." });

            // Record order cancelled
            _ = TryLogAsync(new ActivityLog
            {
                User_ID          = order.user_id,
                Actor_User_ID    = userId,
                Actor_Role       = "User",
                Activity_Type    = "OrderCancelled",
                Description      = $"Cancelled order #{id}.",
                Related_Order_ID = id,
                Created_At       = DateTime.UtcNow,
            });

            return NoContent();
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

            // Record status change — actor may be staff or admin
            var actorIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(actorIdClaim, out int actorId);
            string actorRole = User.HasClaim("user_role_id", "1") ? "Admin" : "Staff";
            _ = TryLogAsync(new ActivityLog
            {
                User_ID          = existing.user_id,
                Actor_User_ID    = actorId,
                Actor_Role       = actorRole,
                Activity_Type    = "OrderStatusChanged",
                Description      = $"Changed order #{id} status to {existing.status}.",
                Related_Order_ID = id,
                Created_At       = DateTime.UtcNow,
            });

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

        /// <summary>
        /// Atomic checkout: creates the order + validates stock + inserts all line items in
        /// a single SQLite transaction. If any item fails the order is fully rolled back.
        /// </summary>
        [HttpPost("checkout")]
        public async Task<ActionResult<object>> Checkout([FromBody] CheckoutRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.Items == null || dto.Items.Count == 0)
                return BadRequest(new { message = "At least one item is required to place an order." });

            // Always use the authenticated user ID — never trust a client-supplied value.
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();

            // Card/GCash are simulated: mark payment_status as paid immediately.
            // COD stays Unpaid until it's paid on delivery.
            string paymentStatus = dto.PaymentMethod == "COD" ? "Unpaid" : "Paid (Simulated)";

            var order = new Order
            {
                user_id          = userId,
                order_date       = DateTime.UtcNow,
                status           = "Pending",
                total_amount     = dto.TotalAmount,
                shipping_address = dto.ShippingAddress,
                phone_number     = dto.PhoneNumber,
                payment_method   = dto.PaymentMethod,
                payment_status   = paymentStatus
            };

            var details = dto.Items.Select(i => new api.OrderDetailModule.OrderDetail
            {
                product_id = i.ProductId,
                quantity   = i.Quantity,
                price      = i.Price,
            }).ToList();

            int newOrderId;
            try
            {
                newOrderId = await _orderRepository.CreateWithDetailsAsync(order, details, ct);
            }
            catch (InvalidOperationException ex)
            {
                // Stock validation failure — friendly 400 with the product message
                return BadRequest(new { message = ex.Message });
            }

            // Record order placed activity (best-effort, non-blocking)
            _ = TryLogAsync(new ActivityLog
            {
                User_ID          = userId,
                Actor_User_ID    = userId,
                Actor_Role       = "User",
                Activity_Type    = "OrderPlaced",
                Description      = $"Placed order #{newOrderId} via atomic checkout.",
                Related_Order_ID = newOrderId,
                Created_At       = DateTime.UtcNow,
            });

            // Best-effort confirmation email
            var user = await _userRepository.GetByIdAsync(userId, ct);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                _ = _orderEmailSender.SendOrderConfirmationAsync(user.Email, newOrderId, dto.TotalAmount, dto.PaymentMethod, ct);
            }

            return Ok(new { order_id = newOrderId, status = "Pending", total_amount = dto.TotalAmount });
        }

        private async Task TryLogAsync(ActivityLog log)
        {
            try { await _activityRepository.CreateAsync(log); }
            catch (Exception ex) { _logger.LogWarning(ex, "Activity log write failed ({Type})", log.Activity_Type); }
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

    /// <summary>Request body for POST /api/order/checkout (atomic checkout endpoint).</summary>
    public class CheckoutRequest
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
        [System.ComponentModel.DataAnnotations.RegularExpression("^(COD|Card|GCash)$",
            ErrorMessage = "PaymentMethod must be COD, Card, or GCash.")]
        public string PaymentMethod { get; set; } = "COD";

        [System.ComponentModel.DataAnnotations.Required]
        public List<CheckoutItemRequest>? Items { get; set; }
    }

    public class CheckoutItemRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public int ProductId { get; set; }

        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue)]
        public decimal Price { get; set; }
    }
}
