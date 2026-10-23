using LocalActivityTracker.Core.Storage;
using Xunit;

namespace LocalActivityTracker.Tests;

public class StoragePathProviderTests
{
    [Fact]
    public void StoragePathProvider_WithCustomDirectory_ComputesExpectedSubdirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_Test_" + Guid.NewGuid().ToString("N"));
        var provider = new StoragePathProvider(tempDir);

        Assert.Equal(tempDir, provider.BaseDirectory);
        Assert.Equal(Path.Combine(tempDir, "data"), provider.DataDirectory);
        Assert.Equal(Path.Combine(tempDir, "screenshots"), provider.ScreenshotsDirectory);
        Assert.Equal(Path.Combine(tempDir, "logs"), provider.LogsDirectory);
        Assert.Equal(Path.Combine(tempDir, "exports"), provider.ExportsDirectory);
        Assert.Equal(Path.Combine(tempDir, "config"), provider.ConfigDirectory);
        Assert.Equal(Path.Combine(tempDir, "backups"), provider.BackupsDirectory);
        Assert.Equal(Path.Combine(tempDir, "data", "activity.db"), provider.DatabasePath);
        Assert.Equal(Path.Combine(tempDir, "config", "auth.token"), provider.AuthTokenPath);
    }

    [Fact]
    public void EnsureDirectoriesExist_CreatesAllRequiredDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_DirTest_" + Guid.NewGuid().ToString("N"));
        var provider = new StoragePathProvider(tempDir);

        try
        {
            provider.EnsureDirectoriesExist();

            Assert.True(Directory.Exists(provider.BaseDirectory));
            Assert.True(Directory.Exists(provider.DataDirectory));
            Assert.True(Directory.Exists(provider.ScreenshotsDirectory));
            Assert.True(Directory.Exists(provider.LogsDirectory));
            Assert.True(Directory.Exists(provider.ExportsDirectory));
            Assert.True(Directory.Exists(provider.ConfigDirectory));
            Assert.True(Directory.Exists(provider.BackupsDirectory));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
