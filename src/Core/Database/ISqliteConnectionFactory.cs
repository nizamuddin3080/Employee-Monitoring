using Microsoft.Data.Sqlite;

namespace LocalActivityTracker.Core.Database;

public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }
    SqliteConnection CreateConnection();
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
