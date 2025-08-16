using System.Runtime.InteropServices;
using System.Security;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

[SuppressUnmanagedCodeSecurity]
static class PInvoke
{
    internal const uint MB_ICONERROR = 0x00000010;

    [DllImport("Shlwapi", EntryPoint = "ShellMessageBoxW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int ShellMessageBox(nint hAppInst, nint hWnd, string lpcText, string lpcTitle, uint fuStyle);

    [DllImport("Kernel32", EntryPoint = "GetPackagesByPackageFamily", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetPackagesByPackageFamily(string packageFamilyName, out uint count, nint packageFullNames, out uint bufferLength, nint buffer);
}