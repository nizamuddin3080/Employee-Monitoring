using LocalActivityTracker.Core.Models;

namespace LocalActivityTracker.Core.Repositories;

public interface ISettingsRepository
{
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default);
}
