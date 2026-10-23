using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Profiles;
using LocalActivityTracker.Core.Repositories;
using Xunit;

namespace LocalActivityTracker.Tests;

public class ChromeProfileScannerTests
{
    [Fact]
    public async Task DiscoverLocalProfiles_ParsesLocalStateAndIdentifiesEnrolledStatus()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), "LAT_UserDataTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserData);

        try
        {
            var localStateContent = @"
            {
                ""profile"": {
                    ""info_cache"": {
                        ""Default"": {
                            ""name"": ""Personal"",
                            ""user_name"": ""user@gmail.com""
                        },
                        ""Profile 1"": {
                            ""name"": ""Company Work"",
                            ""user_name"": ""dev@company.com""
                        }
                    }
                }
            }";

            File.WriteAllText(Path.Combine(tempUserData, "Local State"), localStateContent);

            var tempDb = Path.Combine(Path.GetTempPath(), "LAT_ScannerDb_" + Guid.NewGuid().ToString("N") + ".db");
            var factory = new SqliteConnectionFactory(tempDb);
            var runner = new MigrationRunner(factory);
            await runner.ApplyMigrationsAsync();

            var profRepo = new ProfileRepository(factory);
            await profRepo.UpsertProfileAsync(new ChromeProfile
            {
                ProfileId = "uuid-work-123",
                ProfileName = "Company Work",
                ChromeProfileDir = "Profile 1",
                IsEnrolled = true
            });

            var scanner = new ChromeProfileScanner(profRepo);
            var discovered = scanner.DiscoverLocalProfiles(tempUserData);

            Assert.Equal(2, discovered.Count);

            var personal = discovered.FirstOrDefault(p => p.ProfileDirectoryName == "Default");
            Assert.NotNull(personal);
            Assert.Equal("Personal", personal.DisplayName);
            Assert.False(personal.IsEnrolled);

            var work = discovered.FirstOrDefault(p => p.ProfileDirectoryName == "Profile 1");
            Assert.NotNull(work);
            Assert.Equal("Company Work", work.DisplayName);
            Assert.True(work.IsEnrolled);
        }
        finally
        {
            try { Directory.Delete(tempUserData, true); } catch { }
        }
    }
}
