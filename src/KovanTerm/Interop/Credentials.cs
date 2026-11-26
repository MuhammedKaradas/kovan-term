using System.Runtime.InteropServices;

namespace KovanTerm.Interop;

/// <summary>
/// Reads the API keys KovanLaunch saves in Windows Credential Manager (generic credentials named
/// <c>KovanTerm/&lt;key&gt;</c>). Kovan only reads them; asking for and saving a key stays in the launcher's terminal.
/// </summary>
public static class Credentials
{
    public static string Target(string key) => "KovanTerm/" + key;

    /// <summary>The saved key, or <c>null</c> when there's none.</summary>
    public static string? Read(string key)
    {
        if (!CredReadW(Target(key), 1, 0, out var cred)) return null;
        try
        {
            var c = Marshal.PtrToStructure<Credential>(cred);
            return c.CredentialBlobSize == 0 ? null : Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
        }
        finally { CredFree(cred); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint flags, out nint credential);

    [DllImport("advapi32.dll")] private static extern void CredFree(nint buffer);
}
