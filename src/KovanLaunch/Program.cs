// Kovan starts a terminal through this program when its profile sets environment variables: the terminal control
// can't give a process its own environment, and changing Kovan's own would race with other terminals starting.
//
//   KovanLaunch.exe [--set NAME=VALUE]... [--secret NAME=KEY]... [--wsl] --run "<command line>"
//
// --secret reads KEY from Windows Credential Manager (KovanTerm/KEY); the first time, it asks for it in the terminal
// and saves it there, so API keys never appear in a file or on a command line. --wsl forwards the variables into WSL
// (WSLENV). The program then runs in this console, and this one waits for it and returns its exit code.

using System.Runtime.InteropServices;
using System.Text;
using KovanLaunch;

string? run = null;
var wsl = false;
var names = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--set" or "--secret" when i + 1 < args.Length:
            var secret = args[i] == "--secret";
            var pair = args[++i];
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var name = pair[..eq];
            var value = pair[(eq + 1)..];
            if (secret)
            {
                if ((Secrets.Read(value) ?? Secrets.Ask(value, name)) is not { } key) return 1;
                value = key;
            }
            else value = Environment.ExpandEnvironmentVariables(value);
            Environment.SetEnvironmentVariable(name, value);
            names.Add(name);
            break;
        case "--wsl":
            wsl = true;
            break;
        case "--run" when i + 1 < args.Length:
            run = args[++i];
            break;
    }
}
if (run == null)
{
    Console.Error.WriteLine("Usage: KovanLaunch [--set NAME=VALUE]... [--secret NAME=KEY]... [--wsl] --run \"<command line>\"");
    return 2;
}
if (wsl && names.Count > 0)
{
    var existing = Environment.GetEnvironmentVariable("WSLENV");
    Environment.SetEnvironmentVariable("WSLENV", string.Join(':', new[] { existing }.Concat(names).Where(s => !string.IsNullOrEmpty(s))));
}

// Ctrl+C reaches every process in the console: it's the program's to handle, this one keeps waiting for it
Console.CancelKeyPress += (_, e) => e.Cancel = true;
return Child.Run(run);

namespace KovanLaunch
{
    internal static class Child
    {
        /// <summary>Runs the command line in this console with this process's environment; its exit code.</summary>
        public static int Run(string commandLine)
        {
            var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcessW(null, new StringBuilder(commandLine), 0, 0, false, 0, 0, null, ref si, out var pi))
            {
                // Read before touching the console, which resets it
                var error = Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
                Console.Error.WriteLine($"Kovan: couldn't start {commandLine}: {error}");
                return 1;
            }
            CloseHandle(pi.Thread);
            WaitForSingleObject(pi.Process, uint.MaxValue);
            GetExitCodeProcess(pi.Process, out var code);
            CloseHandle(pi.Process);
            return (int)code;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string? lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public nint lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public nint Process, Thread;
            public int ProcessId, ThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string? application, StringBuilder commandLine, nint processAttributes, nint threadAttributes,
            bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfo startupInfo, out ProcessInformation info);

        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
        [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint process, out uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    }

    /// <summary>The clipboard's text, read with the Win32 API (no UI framework in this program).</summary>
    internal static class Clipboard
    {
        private const uint UnicodeText = 13;

        public static string? Text()
        {
            // Another program may hold it for a moment
            for (var i = 0; i < 10 && !OpenClipboard(0); i++) Thread.Sleep(20);
            try
            {
                var handle = GetClipboardData(UnicodeText);
                if (handle == 0) return null;
                var p = GlobalLock(handle);
                try { return p == 0 ? null : Marshal.PtrToStringUni(p); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
        [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    }

    /// <summary>API keys in Windows Credential Manager, as generic credentials named <c>KovanTerm/&lt;key&gt;</c>.</summary>
    internal static class Secrets
    {
        public static string Target(string key) => "KovanTerm/" + key;

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

        /// <summary>Asks for the key in the terminal (not echoed) and saves it; <c>null</c> if none was given.</summary>
        public static string? Ask(string key, string variable)
        {
            Console.WriteLine($"Kovan: this profile needs the key \"{key}\" (for {variable}), and none is saved yet.");
            Console.Write("Paste it (Ctrl+V) and press Enter: ");
            var text = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace)
                {
                    if (text.Length == 0) continue;
                    text.Length--;
                    Console.Write("\b \b");
                    continue;
                }
                // The terminal passes Ctrl+V on to the program (agents read the clipboard themselves): read it here too
                var typed = k.KeyChar == '\u0016' || (k.Key == ConsoleKey.V && k.Modifiers.HasFlag(ConsoleModifiers.Control))
                    ? Clipboard.Text() ?? ""
                    : k.KeyChar.ToString();
                foreach (var c in typed.Where(c => !char.IsControl(c)))
                {
                    text.Append(c);
                    Console.Write('*');
                }
            }
            Console.WriteLine();
            var value = text.ToString().Trim();
            if (value.Length == 0)
            {
                Console.WriteLine("No key given, so the program wasn't started. Close this terminal and open a new one to try again.");
                return null;
            }
            if (!Write(key, value))
            {
                var error = Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
                Console.WriteLine($"Couldn't save it in Credential Manager: {error}");
                return null;
            }
            Console.WriteLine($"Saved in Windows Credential Manager as {Target(key)}. To change or remove it, open Credential Manager > Windows Credentials.");
            return value;
        }

        public static bool Write(string key, string value)
        {
            var blob = Marshal.StringToCoTaskMemUni(value);
            try
            {
                var c = new Credential
                {
                    Type = 1, // generic
                    TargetName = Target(key),
                    CredentialBlobSize = (uint)(value.Length * 2),
                    CredentialBlob = blob,
                    Persist = 2, // this computer, kept across sign-ins
                    UserName = "KovanTerm",
                };
                return CredWriteW(ref c, 0);
            }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
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

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(ref Credential credential, uint flags);

        [DllImport("advapi32.dll")] private static extern void CredFree(nint buffer);
    }
}
