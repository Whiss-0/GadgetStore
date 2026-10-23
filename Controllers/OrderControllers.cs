using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.Main;
using api.OrderModule;
using api.UserModule;
using api.Security;
using api.ActivityModule;
using api.Services;

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
        private readonly INotificationService _notificationService;

        public OrderController(
            IOrderRepository orderRepository,
            IUserRepository userRepository,
            IOrderEmailSender orderEmailSender,
            IActivityRepository activityRepository,
            ILogger<OrderController> logger,
            INotificationService notificationService)
        {
            _orderRepository = orderRepository;
            _userRepository = userRepository;
            _orderEmailSender = orderEmailSender;
            _activityRepository = activityRepository;
            _logger = logger;
            _notificationService = notificationService;
        }

        [Authorize(Policy = "ModAccess")]
        [HttpGet]
        public async Task<ActionResult<object>> GetAll([FromQuery] int? pageNumber, [FromQuery] int? pageSize, CancellationToken ct)
        {
            if (pageNumber.HasValue && pageSize.HasValue)
                return Ok(await _orderRepository.GetPagedAsync(pageNumber.Value, pageSize.Value, ct));
            return Ok(await _orderRepository.GetAllAsync(ct));
        }

        [Authorize(Policy = "ModAccess")]
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

        [HttpGet("{id:int}/delivery-events")]
        public async Task<ActionResult<List<OrderDeliveryEvent>>> GetDeliveryEvents(int id, CancellationToken ct)
        {
            var order = await _orderRepository.GetByIdAsync(id, ct);
            if (order == null) return NotFound(new { message = $"Order with ID {id} not found." });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId)) return Unauthorized();
            if (order.user_id != userId && !User.HasClaim("user_role_id", "1") && !User.HasClaim("user_role_id", "2"))
                return Forbid();

            return Ok(await _orderRepository.GetDeliveryEventsAsync(id, ct));
        }

        [Authorize(Policy = "ModAccess")]
        [HttpPut("{id:int}/fulfillment")]
        public async Task<IActionResult> UpdateFulfillment(int id, [FromBody] FulfillmentUpdateRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var order = await _orderRepository.GetByIdAsync(id, ct);
            if (order == null) return NotFound(new { message = $"Order with ID {id} not found." });

            string? courierName = NormalizeOptional(dto.CourierName);
            string? trackingNumber = NormalizeOptional(dto.TrackingNumber);
            string? eta = NormalizeOptional(dto.EstimatedDeliveryDate);
            if (eta != null && !DateTime.TryParseExact(eta, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
                return BadRequest(new { message = "EstimatedDeliveryDate must be YYYY-MM-DD, null, or empty." });

            bool detailsChanged = !string.Equals(order.courier_name, courierName, StringComparison.Ordinal)
                || !string.Equals(order.tracking_number, trackingNumber, StringComparison.Ordinal)
                || !string.Equals(order.estimated_delivery_date, eta, StringComparison.Ordinal);
            order.courier_name = courierName;
            order.tracking_number = trackingNumber;
            order.estimated_delivery_date = eta;

            string actorRole = GetActorRole();
            bool updated = await _orderRepository.UpdateFulfillmentWithEventAsync(order, actorRole, NormalizeOptional(dto.Note), ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update fulfillment details." });
            if (detailsChanged) await _notificationService.CreateTrackingUpdateNotificationAsync(order, ct);

            await TryLogAsync(new ActivityLog
            {
                Actor_User_ID = GetActorId(),
                Actor_Role = actorRole,
                Activity_Type = "OrderFulfillmentUpdated",
                Description = $"Updated delivery fulfillment details for order #{id}.",
                Related_Order_ID = id,
                Created_At = DateTime.UtcNow,
            });

            return NoContent();
        }

        [HttpPost]
        public ActionResult<Order> Create([FromBody] OrderRequest dto)
        {
            // This older endpoint has no order lines and accepts a client total, so it
            // cannot safely apply current DB prices, delivery settings, or VAT.
            return StatusCode(StatusCodes.Status410Gone,
                new { message = "This order endpoint is retired. Submit all items through POST /api/order/checkout." });
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

            string previousStatus = order.status;
            bool updated = await _orderRepository.UpdateStatusWithEventAsync(id, previousStatus, "Cancelled", "User", "Order cancelled by customer.", ct);
            if (!updated) return Conflict(new { message = "Order status changed before cancellation could be saved." });
            order.status = "Cancelled";

            _ = _notificationService.CreateOrderStatusNotificationAsync(order, previousStatus, ct);

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

        private static readonly HashSet<string> AllowedStatuses =
            new(StringComparer.Ordinal) { "Pending", "Processing", "Shipped", "Delivered", "Cancelled" };

        private static readonly Dictionary<string, HashSet<string>> AllowedTransitions =
            new(StringComparer.Ordinal)
            {
                ["Pending"]    = new(StringComparer.Ordinal) { "Processing", "Cancelled" },
                ["Processing"] = new(StringComparer.Ordinal) { "Shipped",    "Cancelled" },
                ["Shipped"]    = new(StringComparer.Ordinal) { "Delivered" },
                ["Delivered"]  = new(StringComparer.Ordinal),
                ["Cancelled"]  = new(StringComparer.Ordinal),
            };

        [Authorize(Policy = "ModAccess")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] OrderUpdateRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.Status != null)
            {
                if (!AllowedStatuses.Contains(dto.Status))
                    return BadRequest(new { message = $"'{dto.Status}' is not a valid order status. Allowed values: Pending, Processing, Shipped, Delivered, Cancelled." });
            }

            var existing = await _orderRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"Order with ID {id} not found." });

            if (dto.Status != null && dto.Status != existing.status)
            {
                if (!AllowedTransitions.TryGetValue(existing.status, out var allowed) || !allowed.Contains(dto.Status))
                    return BadRequest(new { message = $"Cannot transition order from '{existing.status}' to '{dto.Status}'." });
            }

            string previousStatus = existing.status;
            string targetStatus = dto.Status ?? existing.status;
            string actorRole = GetActorRole();
            bool updated;
            if (targetStatus != previousStatus)
            {
                string note = $"Status changed from {previousStatus} to {targetStatus}.";
                updated = await _orderRepository.UpdateStatusWithEventAsync(id, previousStatus, targetStatus, actorRole, note, ct);
                if (!updated) return Conflict(new { message = "Order status changed before the update could be saved." });
                existing.status = targetStatus;
            }
            else
            {
                updated = await _orderRepository.UpdateAsync(existing, ct);
                if (!updated) return StatusCode(500, new { message = "Failed to update order." });
            }

            _ = _notificationService.CreateOrderStatusNotificationAsync(existing, previousStatus, ct);

            // Record successful status changes — actor may be staff or admin.
            if (targetStatus != previousStatus)
            {
                await TryLogAsync(new ActivityLog
                {
                    User_ID          = existing.user_id,
                    Actor_User_ID    = GetActorId(),
                    Actor_Role       = actorRole,
                    Activity_Type    = "OrderStatusChanged",
                    Description      = $"Changed order #{id} status from {previousStatus} to {existing.status}.",
                    Related_Order_ID = id,
                    Created_At       = DateTime.UtcNow,
                });
            }

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

            await TryLogAsync(new ActivityLog
            {
                Actor_User_ID = GetActorId(),
                Actor_Role = GetActorRole(),
                Activity_Type = "OrderDeleted",
                Description = $"Deleted order #{id}.",
                Related_Order_ID = id,
                Created_At = DateTime.UtcNow,
            });
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
                shipping_city_code = dto.CityCode,
                shipping_province_code = dto.ProvinceCode,
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
                _ = _orderEmailSender.SendOrderConfirmationAsync(user.Email, newOrderId, order.total_amount, dto.PaymentMethod, ct);
            }

            // Return the complete authoritative order-time pricing snapshot, not client totals.
            return Ok(new
            {
                order_id = newOrderId,
                status = "Pending",
                subtotal_amount = order.subtotal_amount,
                delivery_fee_amount = order.delivery_fee_amount,
                vat_amount = order.vat_amount,
                vat_rate_percent = order.vat_rate_percent,
                vat_price_mode = order.vat_price_mode,
                shipping_city_code = order.shipping_city_code,
                shipping_province_code = order.shipping_province_code,
                total_amount = order.total_amount
            });
        }

        private async Task TryLogAsync(ActivityLog log)
        {
            try { await _activityRepository.CreateAsync(log); }
            catch (Exception ex) { _logger.LogWarning(ex, "Activity log write failed ({Type})", log.Activity_Type); }
        }

        private string GetActorRole() => User.HasClaim("user_role_id", "1") ? "Admin" : "Staff";
        private int? GetActorId()
        {
            var actorIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(actorIdClaim, out int actorId) ? actorId : null;
        }
        private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public class FulfillmentUpdateRequest
    {
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string? CourierName { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string? TrackingNumber { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(10)]
        public string? EstimatedDeliveryDate { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string? Note { get; set; }
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

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "provinceCode is required.")]
        [System.ComponentModel.DataAnnotations.StringLength(4, MinimumLength = 4)]
        [System.Text.Json.Serialization.JsonPropertyName("provinceCode")]
        public string ProvinceCode { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "cityCode is required.")]
        [System.ComponentModel.DataAnnotations.StringLength(6, MinimumLength = 6)]
        [System.Text.Json.Serialization.JsonPropertyName("cityCode")]
        public string CityCode { get; set; } = string.Empty;
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
