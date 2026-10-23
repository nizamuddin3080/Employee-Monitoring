using LocalActivityTracker.Core.Database;
using Microsoft.Data.Sqlite;

namespace LocalActivityTracker.Core.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SettingsRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT setting_value FROM application_settings WHERE setting_key = @key;";
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result?.ToString();
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO application_settings (setting_key, setting_value, updated_at_utc)
            VALUES (@key, @value, @now)
            ON CONFLICT(setting_key) DO UPDATE SET
                setting_value = excluded.setting_value,
                updated_at_utc = excluded.updated_at_utc;
        ";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        var dict = new Dictionary<string, string>();
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "SELECT setting_key, setting_value FROM application_settings;";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        return dict;
    }
}
