namespace api.NotificationModule;

public interface INotificationRepository
{
    Task<List<Notification>> GetForUserAsync(int userId, bool unreadOnly, int limit, CancellationToken ct = default);
    Task<int> CountUnreadAsync(int userId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(int notificationId, int userId, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(int userId, CancellationToken ct = default);
    Task<int> CreateForUserAsync(Notification notification, CancellationToken ct = default);
    Task<int> CreateForUsersAsync(IEnumerable<int> recipientIds, NotificationDraft draft, CancellationToken ct = default);
    Task<List<int>> GetUserIdsByRoleAsync(int roleId, CancellationToken ct = default);
    Task<bool> UserExistsAsync(int userId, CancellationToken ct = default);
}
