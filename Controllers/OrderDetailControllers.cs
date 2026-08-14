using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using api.OrderDetailModule;
using api.ProductsModule;

namespace api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "UserAccess")]
    public class OrderDetailController : ControllerBase
    {
        private readonly IOrderDetailRepository _orderDetailRepository;
        private readonly IProductRepository _productRepository;

        public OrderDetailController(IOrderDetailRepository orderDetailRepository, IProductRepository productRepository)
        {
            _orderDetailRepository = orderDetailRepository;
            _productRepository = productRepository;
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpGet]
        public async Task<ActionResult<List<OrderDetail>>> GetAll(CancellationToken ct)
        {
            return Ok(await _orderDetailRepository.GetAllAsync(ct));
        }

        [HttpGet("order/{orderId:int}")]
        public async Task<ActionResult<List<OrderDetail>>> GetByOrder(int orderId, CancellationToken ct)
        {
            return Ok(await _orderDetailRepository.GetByOrderAsync(orderId, ct));
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
            return CreatedAtAction(nameof(GetById), new { id = newId }, detail);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] OrderDetailRequest dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existing = await _orderDetailRepository.GetByIdAsync(id, ct);
            if (existing == null) return NotFound(new { message = $"OrderDetail with ID {id} not found." });
            existing.order_id = dto.OrderId;
            existing.product_id = dto.ProductId;
            existing.quantity = dto.Quantity;
            existing.price = dto.Price;
            bool updated = await _orderDetailRepository.UpdateAsync(existing, ct);
            if (!updated) return StatusCode(500, new { message = "Failed to update order detail." });
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
            return NoContent();
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
