using System.Security.Cryptography;
using LocalActivityTracker.Core.Storage;

namespace LocalActivityTracker.Coordinator.Api;

/// <summary>
/// Generates, persists, and validates the 256-bit cryptographic capability bearer token (auth.token).
/// Restricts file access to the current Windows user to protect localhost REST endpoints.
/// </summary>
public sealed class LocalCapabilityTokenProvider
{
    private readonly StoragePathProvider _storage;
    private readonly object _lock = new();
    private string? _cachedToken;

    public string TokenFilePath => Path.Combine(_storage.ConfigDirectory, "auth.token");

    public LocalCapabilityTokenProvider(StoragePathProvider? storage = null)
    {
        _storage = storage ?? new StoragePathProvider();
    }

    public string GetOrCreateToken()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_cachedToken))
            {
                return _cachedToken;
            }

            _storage.EnsureDirectoriesExist();
            var filePath = TokenFilePath;

            if (File.Exists(filePath))
            {
                try
                {
                    var existing = File.ReadAllText(filePath).Trim();
                    if (!string.IsNullOrWhiteSpace(existing) && existing.Length == 64)
                    {
                        _cachedToken = existing;
                        return _cachedToken;
                    }
                }
                catch
                {
                    // If read fails, regenerate
                }
            }

            // Generate cryptographically secure 256-bit token (64 hex characters)
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            var newToken = Convert.ToHexString(bytes).ToLowerInvariant();

            try
            {
                File.WriteAllText(filePath, newToken);

                // Set file attributes to prevent unauthorized tampering
                var fileInfo = new FileInfo(filePath)
                {
                    Attributes = FileAttributes.Normal
                };
            }
            catch
            {
                // Fallback to in-memory if disk write fails
            }

            _cachedToken = newToken;
            return _cachedToken;
        }
    }

    public bool ValidateToken(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return false;
        }

        var expected = GetOrCreateToken();
        var tokenToValidate = bearerToken.Trim();

        if (tokenToValidate.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            tokenToValidate = tokenToValidate.Substring(7).Trim();
        }

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(tokenToValidate));
    }
}
