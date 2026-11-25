using System.Diagnostics;
using KovanTerm.Interop;

namespace KovanTerm.Tests;

public sealed class ProcessMemoryTests
{
    [Fact]
    public void Own_working_set_matches_what_Process_reports()
    {
        var own = ProcessMemory.OwnWorkingSet();
        using var self = Process.GetCurrentProcess();

        Assert.InRange(own, self.WorkingSet64 * 0.8, self.WorkingSet64 * 1.2);
    }
}
