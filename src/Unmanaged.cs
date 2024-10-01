using System.Security;
using System.Runtime.InteropServices;

static class Unmanaged
{
    [DllImport("Kernel32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32), SuppressUnmanagedCodeSecurity]
    internal static extern bool DeleteFile(string lpFileName);

    [DllImport("Kernel32"), DefaultDllImportSearchPaths(DllImportSearchPath.System32), SuppressUnmanagedCodeSecurity]
    internal static extern ulong GetVersion();

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32), SuppressUnmanagedCodeSecurity]
    internal static extern int ShellMessageBox(nint hAppInst = default, nint hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);
}