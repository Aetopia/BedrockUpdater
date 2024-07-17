using System;
using System.Runtime.InteropServices;

static class NativeMethods
{
    [DllImport("Kernel32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool DeleteFile(string lpFileName);

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr ShellExecute(IntPtr hwnd = default, string lpOperation = null, string lpFile = null, string lpParameters = null, string lpDirectory = null, int nShowCmd = 0);

    [DllImport("Shlwapi"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool IsOS(int dwOS);

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ShellMessageBox(IntPtr hAppInst = default, IntPtr hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);
}