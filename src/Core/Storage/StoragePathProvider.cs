using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LocalActivityTracker.Core.Storage;

public class StoragePathProvider
{
    public string BaseDirectory { get; }
    public string DataDirectory => Path.Combine(BaseDirectory, "data");
    public string ScreenshotsDirectory => Path.Combine(BaseDirectory, "screenshots");
    public string LogsDirectory => Path.Combine(BaseDirectory, "logs");
    public string ExportsDirectory => Path.Combine(BaseDirectory, "exports");
    public string ConfigDirectory => Path.Combine(BaseDirectory, "config");
    public string BackupsDirectory => Path.Combine(BaseDirectory, "backups");

    public string DatabasePath => Path.Combine(DataDirectory, "activity.db");
    public string AuthTokenPath => Path.Combine(ConfigDirectory, "auth.token");

    public StoragePathProvider(string? customBaseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customBaseDirectory))
        {
            BaseDirectory = customBaseDirectory;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            BaseDirectory = Path.Combine(localAppData, "LocalActivityTracker");
        }
    }

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(BaseDirectory);
        EnforceStrictUserAcl(BaseDirectory);

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    /// <summary>
    /// Enforces Windows NTFS ACLs restricting access exclusively to the current user and SYSTEM.
    /// Disables inheritance to prevent world-writable or cross-user permissions.
    /// </summary>
    public void EnforceStrictUserAcl(string directoryPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            var currentUser = WindowsIdentity.GetCurrent().User;
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            var dirSecurity = new DirectorySecurity();
            dirSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            if (currentUser != null)
            {
                dirSecurity.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            dirSecurity.AddAccessRule(new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            dirInfo.SetAccessControl(dirSecurity);
        }
        catch
        {
            // Suppress filesystem security exceptions if running on non-NTFS or virtual volumes
        }
    }
}
