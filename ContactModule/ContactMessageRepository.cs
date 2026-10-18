using System.Data.Common;
using api.Main;

namespace api.ContactModule;

public sealed class ContactMessageRepository : BaseRepository, IContactMessageRepository
{
    private const int MaxListLimit = 200;

    public ContactMessageRepository(MyCon db) : base(db) { }

    public async Task<long> CreateAsync(ContactMessage message, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO contact_messages
                (user_id, name, email, topic, order_reference, message, status, created_at, updated_at)
            VALUES
                (@user_id, @name, @email, @topic, @order_reference, @message, @status, @created_at, NULL);
            SELECT last_insert_rowid();
            """;

        var parameters = new[]
        {
            CreateParameter("@user_id", message.UserId),
            CreateParameter("@name", message.Name),
            CreateParameter("@email", message.Email),
            CreateParameter("@topic", message.Topic),
            CreateParameter("@order_reference", message.OrderReference),
            CreateParameter("@message", message.Message),
            CreateParameter("@status", "New"),
            CreateParameter("@created_at", message.CreatedAt.ToString("o")),
        };

        return Convert.ToInt64(await ExecuteScalarAsync<long>(sql, parameters, ct: ct));
    }

    public async Task<IReadOnlyList<ContactMessage>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        int safeLimit = Math.Clamp(limit, 1, MaxListLimit);
        const string sql = """
            SELECT contact_message_id, user_id, name, email, topic, order_reference,
                   message, status, created_at, updated_at
            FROM contact_messages
            ORDER BY created_at DESC, contact_message_id DESC
            LIMIT @limit;
            """;
        var rows = await ExecuteReaderToListAsync(sql, MapRow,
            new[] { CreateParameter("@limit", safeLimit) }, ct: ct).ConfigureAwait(false);
        return rows.AsReadOnly();
    }

    public async Task<bool> UpdateStatusAsync(long messageId, string status, DateTime updatedAt, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE contact_messages
            SET status = @status, updated_at = @updated_at
            WHERE contact_message_id = @message_id;
            """;
        int changed = await ExecuteNonQueryAsync(sql, new[]
        {
            CreateParameter("@status", status),
            CreateParameter("@updated_at", updatedAt.ToString("o")),
            CreateParameter("@message_id", messageId),
        }, ct: ct).ConfigureAwait(false);
        return changed > 0;
    }

    private static ContactMessage MapRow(DbDataReader reader)
    {
        int userIdOrdinal = reader.GetOrdinal("user_id");
        int orderReferenceOrdinal = reader.GetOrdinal("order_reference");
        int updatedAtOrdinal = reader.GetOrdinal("updated_at");
        return new ContactMessage
        {
            ContactMessageId = ReadValue(reader, "contact_message_id", 0L),
            UserId = reader.IsDBNull(userIdOrdinal) ? null : ReadValue(reader, "user_id", 0),
            Name = ReadValue(reader, "name", string.Empty),
            Email = ReadValue(reader, "email", string.Empty),
            Topic = ReadValue(reader, "topic", string.Empty),
            OrderReference = reader.IsDBNull(orderReferenceOrdinal) ? null : ReadValue(reader, "order_reference", string.Empty),
            Message = ReadValue(reader, "message", string.Empty),
            Status = ReadValue(reader, "status", "New"),
            CreatedAt = ParseDate(ReadValue(reader, "created_at", DateTime.UtcNow.ToString("o"))),
            UpdatedAt = reader.IsDBNull(updatedAtOrdinal) ? null : ParseDate(ReadValue(reader, "updated_at", DateTime.UtcNow.ToString("o"))),
        };
    }

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var result)
            ? result
            : DateTime.UtcNow;
}
