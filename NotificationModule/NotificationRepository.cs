using System.Data.Common;
using api.Main;

namespace api.NotificationModule;

public sealed class NotificationRepository : BaseRepository, INotificationRepository
{
    private const int MaxLimit = 100;

    public NotificationRepository(MyCon db) : base(db) { }

    public Task<List<Notification>> GetForUserAsync(int userId, bool unreadOnly, int limit, CancellationToken ct = default)
    {
        int safeLimit = Math.Clamp(limit, 1, MaxLimit);
        string filter = unreadOnly ? "AND is_read = 0" : string.Empty;
        string sql = $"""
            SELECT notification_id, recipient_user_id, sender_user_id, notification_type,
                   title, message, related_order_id, is_read, created_at, read_at
            FROM notifications
            WHERE recipient_user_id = @user_id {filter}
            ORDER BY created_at DESC, notification_id DESC
            LIMIT @limit;
            """;
        var parameters = new[] { CreateParameter("@user_id", userId), CreateParameter("@limit", safeLimit) };
        return ExecuteReaderToListAsync(sql, MapRow, parameters, ct: ct);
    }

    public async Task<int> CountUnreadAsync(int userId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) FROM notifications WHERE recipient_user_id = @user_id AND is_read = 0;";
        return Convert.ToInt32(await ExecuteScalarAsync<long>(sql, new[] { CreateParameter("@user_id", userId) }, ct: ct));
    }

    public async Task<bool> MarkReadAsync(int notificationId, int userId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notifications
            SET is_read = 1, read_at = @read_at
            WHERE notification_id = @notification_id
              AND recipient_user_id = @user_id
              AND is_read = 0;
            """;
        int changed = await ExecuteNonQueryAsync(sql, new[]
        {
            CreateParameter("@read_at", DateTime.UtcNow.ToString("o")),
            CreateParameter("@notification_id", notificationId),
            CreateParameter("@user_id", userId),
        }, ct: ct);
        return changed > 0;
    }

    public Task<int> MarkAllReadAsync(int userId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notifications
            SET is_read = 1, read_at = @read_at
            WHERE recipient_user_id = @user_id AND is_read = 0;
            """;
        return ExecuteNonQueryAsync(sql, new[]
        {
            CreateParameter("@read_at", DateTime.UtcNow.ToString("o")),
            CreateParameter("@user_id", userId),
        }, ct: ct);
    }

    public async Task<int> CreateForUserAsync(Notification notification, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notifications
                (recipient_user_id, sender_user_id, notification_type, title, message,
                 related_order_id, is_read, created_at, read_at)
            VALUES
                (@recipient_user_id, @sender_user_id, @notification_type, @title, @message,
                 @related_order_id, 0, @created_at, NULL);
            SELECT last_insert_rowid();
            """;
        var parameters = ParametersFor(notification);
        return Convert.ToInt32(await ExecuteScalarAsync<long>(sql, parameters, ct: ct));
    }

    public Task<int> CreateForUsersAsync(IEnumerable<int> recipientIds, NotificationDraft draft, CancellationToken ct = default)
    {
        var ids = recipientIds.Distinct().ToList();
        if (ids.Count == 0) return Task.FromResult(0);
        return WithTransactionAsync(async (connection, transaction) =>
        {
            const string sql = """
                INSERT INTO notifications
                    (recipient_user_id, sender_user_id, notification_type, title, message,
                     related_order_id, is_read, created_at, read_at)
                VALUES
                    (@recipient_user_id, @sender_user_id, @notification_type, @title, @message,
                     @related_order_id, 0, @created_at, NULL);
                """;
            int count = 0;
            string createdAt = DateTime.UtcNow.ToString("o");
            foreach (int recipientId in ids)
            {
                var parameters = new[]
                {
                    CreateParameter("@recipient_user_id", recipientId),
                    CreateParameter("@sender_user_id", draft.Sender_User_ID),
                    CreateParameter("@notification_type", draft.Notification_Type),
                    CreateParameter("@title", draft.Title),
                    CreateParameter("@message", draft.Message),
                    CreateParameter("@related_order_id", draft.Related_Order_ID),
                    CreateParameter("@created_at", createdAt),
                };
                count += await ExecuteNonQueryAsync(connection, transaction, sql, parameters, ct: ct);
            }
            return count;
        }, ct: ct);
    }

    public async Task<List<int>> GetUserIdsByRoleAsync(int roleId, CancellationToken ct = default)
    {
        const string sql = "SELECT user_id FROM users WHERE role_id = @role_id ORDER BY user_id;";
        var rows = await ExecuteReaderToListAsync(sql, reader => ReadValue(reader, "user_id", 0), new[] { CreateParameter("@role_id", roleId) }, ct: ct);
        return rows;
    }

    public async Task<bool> UserExistsAsync(int userId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) FROM users WHERE user_id = @user_id;";
        return Convert.ToInt32(await ExecuteScalarAsync<long>(sql, new[] { CreateParameter("@user_id", userId) }, ct: ct)) > 0;
    }

    private static DbParameter[] ParametersFor(Notification notification) => new[]
    {
        CreateParameter("@recipient_user_id", notification.recipient_user_id),
        CreateParameter("@sender_user_id", notification.sender_user_id),
        CreateParameter("@notification_type", notification.notification_type),
        CreateParameter("@title", notification.title),
        CreateParameter("@message", notification.message),
        CreateParameter("@related_order_id", notification.related_order_id),
        CreateParameter("@created_at", notification.created_at.ToString("o")),
    };

    private static Notification MapRow(DbDataReader reader) => new()
    {
        notification_id = ReadValue(reader, "notification_id", 0),
        recipient_user_id = ReadValue(reader, "recipient_user_id", 0),
        sender_user_id = reader.IsDBNull(reader.GetOrdinal("sender_user_id")) ? null : ReadValue(reader, "sender_user_id", 0),
        notification_type = ReadValue(reader, "notification_type", string.Empty),
        title = ReadValue(reader, "title", string.Empty),
        message = ReadValue(reader, "message", string.Empty),
        related_order_id = reader.IsDBNull(reader.GetOrdinal("related_order_id")) ? null : ReadValue(reader, "related_order_id", 0),
        is_read = ReadValue(reader, "is_read", 0) != 0,
        created_at = ParseDate(ReadValue(reader, "created_at", DateTime.UtcNow.ToString("o"))),
        read_at = reader.IsDBNull(reader.GetOrdinal("read_at")) ? null : ParseDate(ReadValue(reader, "read_at", DateTime.UtcNow.ToString("o"))),
    };

    private static DateTime ParseDate(string value) => DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var result) ? result : DateTime.UtcNow;
}
