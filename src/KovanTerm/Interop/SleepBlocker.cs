using System.Runtime.InteropServices;

namespace KovanTerm.Interop;

/// <summary>
/// Keeps Windows from going to sleep on its own while agents work (<c>SetThreadExecutionState</c>). The display may
/// still turn off, and closing a laptop's lid still does what the power settings say. Must be used from one thread:
/// the state belongs to the thread that set it.
/// </summary>
public sealed class SleepBlocker
{
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);

    public bool IsActive { get; private set; }

    /// <summary>Starts or stops keeping the computer awake; <c>true</c> if that changed anything.</summary>
    public bool Set(bool keepAwake)
    {
        if (keepAwake == IsActive) return false;
        SetThreadExecutionState(keepAwake ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
        IsActive = keepAwake;
        return true;
    }
}
