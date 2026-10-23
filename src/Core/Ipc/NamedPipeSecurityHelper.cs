using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using LocalActivityTracker.Core.Win32;
using Microsoft.Win32.SafeHandles;

namespace LocalActivityTracker.Core.Ipc;

/// <summary>
/// Security helper for Windows Named Pipes: enforces DACLs restricted to the current user and SYSTEM,
/// and verifies client process ownership against the current user SID via kernel process tokens.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NamedPipeSecurityHelper
{
    /// <summary>
    /// Constructs a PipeSecurity DACL granting Read/Write access exclusively to the current user and NT AUTHORITY\SYSTEM.
    /// All other users and remote callers are denied.
    /// </summary>
    public static PipeSecurity CreateCurrentUserPipeSecurity()
    {
        var security = new PipeSecurity();

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve current Windows user SID.");

        // Grant full control to current interactive user
        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        // Grant read/write to LocalSystem
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
    }

    /// <summary>
    /// Obtains the client PID connected to the given Named Pipe and verifies that the client process
    /// token owner matches the current user's SID.
    /// </summary>
    public static bool VerifyClientProcessOwner(SafePipeHandle pipeHandle, out uint clientPid)
    {
        clientPid = 0;

        if (pipeHandle == null || pipeHandle.IsInvalid || pipeHandle.IsClosed)
        {
            return false;
        }

        if (!Win32Native.GetNamedPipeClientProcessId(pipeHandle, out clientPid) || clientPid == 0)
        {
            return false;
        }

        // Verify that the process owner SID matches current user SID
        var currentUserSid = WindowsIdentity.GetCurrent().User;
        if (currentUserSid == null)
        {
            return false;
        }

        var hProcess = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_LIMITED_INFORMATION, false, clientPid);
        if (hProcess == IntPtr.Zero)
        {
            // Access denied opening client process implies elevated / different credentials
            return false;
        }

        try
        {
            if (!Win32Native.OpenProcessToken(hProcess, Win32Native.TOKEN_QUERY, out var hToken))
            {
                return false;
            }

            try
            {
                var clientSid = GetProcessTokenUserSid(hToken);
                return clientSid != null && clientSid.Equals(currentUserSid);
            }
            finally
            {
                Win32Native.CloseHandle(hToken);
            }
        }
        finally
        {
            Win32Native.CloseHandle(hProcess);
        }
    }

    private static SecurityIdentifier? GetProcessTokenUserSid(IntPtr hToken)
    {
        Win32Native.GetTokenInformation(hToken, Win32Native.TokenUser, IntPtr.Zero, 0, out var returnLength);
        if (returnLength == 0)
        {
            return null;
        }

        var pTokenUser = Marshal.AllocHGlobal((int)returnLength);
        try
        {
            if (Win32Native.GetTokenInformation(hToken, Win32Native.TokenUser, pTokenUser, returnLength, out _))
            {
                var tokenUser = Marshal.PtrToStructure<Win32Native.TOKEN_USER>(pTokenUser);
                if (tokenUser.User.Sid != IntPtr.Zero)
                {
                    return new SecurityIdentifier(tokenUser.User.Sid);
                }
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(pTokenUser);
        }
    }
}
