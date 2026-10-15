using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using api.ActivityModule;
using api.NotificationModule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class NotificationController : ControllerBase
{
    private readonly INotificationRepository _notifications;
    private readonly IActivityRepository _activity;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(INotificationRepository notifications, IActivityRepository activity, ILogger<NotificationController> logger)
    {
        _notifications = notifications;
        _activity = activity;
        _logger = logger;
    }

    [Authorize(Policy = "UserAccess")]
    [HttpGet]
    public async Task<ActionResult<List<Notification>>> List([FromQuery] bool unreadOnly = false, [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        if (!TryGetUserId(out int userId)) return Unauthorized();
        return Ok(await _notifications.GetForUserAsync(userId, unreadOnly, limit, ct));
    }

    [Authorize(Policy = "UserAccess")]
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        if (!TryGetUserId(out int userId)) return Unauthorized();
        return Ok(new { unreadCount = await _notifications.CountUnreadAsync(userId, ct) });
    }

    [Authorize(Policy = "UserAccess")]
    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out int userId)) return Unauthorized();
        bool changed = await _notifications.MarkReadAsync(id, userId, ct);
        return changed ? NoContent() : NotFound(new { message = "Notification not found or already read." });
    }

    [Authorize(Policy = "UserAccess")]
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        if (!TryGetUserId(out int userId)) return Unauthorized();
        return Ok(new { markedRead = await _notifications.MarkAllReadAsync(userId, ct) });
    }

    [Authorize(Policy = "AdminAccess")]
    [HttpPost("admin/send")]
    public async Task<IActionResult> SendAdmin([FromBody] AdminNotificationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryGetUserId(out int senderId)) return Unauthorized();

        string audience = request.Audience.Trim();
        List<int> recipients;
        switch (audience)
        {
            case "AllUsers": recipients = await _notifications.GetUserIdsByRoleAsync(3, ct); break;
            case "AllStaff": recipients = await _notifications.GetUserIdsByRoleAsync(2, ct); break;
            case "Individual":
                if (!request.RecipientUserId.HasValue) return BadRequest(new { message = "recipientUserId is required for an individual notification." });
                if (!await _notifications.UserExistsAsync(request.RecipientUserId.Value, ct)) return NotFound(new { message = "The selected recipient was not found." });
                recipients = new List<int> { request.RecipientUserId.Value };
                break;
            default: return BadRequest(new { message = "Audience must be AllUsers, AllStaff, or Individual." });
        }

        string title = request.Title.Trim();
        string message = request.Message.Trim();
        if (title.Length == 0 || message.Length == 0)
            return BadRequest(new { message = "Title and message cannot be empty." });

        int sent = await _notifications.CreateForUsersAsync(recipients, new NotificationDraft
        {
            Sender_User_ID = senderId,
            Notification_Type = "AdminAnnouncement",
            Title = title,
            Message = message,
        }, ct);

        try
        {
            await _activity.CreateAsync(new ActivityLog
            {
                Actor_User_ID = senderId,
                Actor_Role = "Admin",
                Activity_Type = "NotificationSent",
                Description = $"Sent a notification to {audience} ({sent} recipient{(sent == 1 ? "" : "s")}).",
                Created_At = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Notification activity log failed for admin {AdminId}", senderId); }

        return Ok(new { sentCount = sent });
    }

    private bool TryGetUserId(out int userId) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}

public sealed class AdminNotificationRequest
{
    [Required, StringLength(30)] public string Audience { get; set; } = string.Empty;
    public int? RecipientUserId { get; set; }
    [Required, StringLength(120, MinimumLength = 1)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(1000, MinimumLength = 1)] public string Message { get; set; } = string.Empty;
}
