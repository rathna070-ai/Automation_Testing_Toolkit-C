using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace WebTestToolkit.Inspector;

// The Job Object mitigation from ARCHITECTURE.md's P16 item 3: pure .NET Dispose()/timeout
// logic only ever runs on the API process's graceful shutdown path, so a hard crash or
// `kill -9` of the API leaves chromedriver.exe and its Chrome children running forever —
// each one a leaked browser window that has to be found and killed by hand. Assigning both
// processes to a Windows Job Object created with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE fixes
// this at the OS level: Windows itself terminates every process still in the job the moment
// the job's last handle closes, which happens automatically when this process dies for any
// reason, graceful or not.
//
// One job object for the whole API process lifetime (not one per session) — job membership
// is one-way and cumulative, so every Chrome this process ever launches just keeps
// accumulating in the same job; there is no per-session cleanup to do, and no reason for one.
[SupportedOSPlatform("windows")]
public static class ChromeProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int jobObjectInfoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint ProcessAllAccess = 0x1F0FFF;

    private static readonly Lazy<IntPtr> Job = new(CreateKillOnCloseJob);

    private static IntPtr CreateKillOnCloseJob()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            return IntPtr.Zero;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            CloseHandle(handle);
            return IntPtr.Zero;
        }

        return handle;
    }

    // Best-effort, and deliberately silent on failure: this is defense-in-depth cleanup for
    // an already-abnormal shutdown, not something an inspect session should ever fail to
    // start over. AssignProcessToJobObject can fail for reasons that are nobody's bug here —
    // the process already belongs to another job (Windows Sandbox, another automation tool,
    // Windows versions before nested-job support), or it exited in the gap between us finding
    // its PID and opening a handle to it.
    public static void TryAssign(int processId, ILogger logger)
    {
        var job = Job.Value;
        if (job == IntPtr.Zero)
        {
            logger.LogDebug("Chrome process Job Object unavailable — orphaned-process cleanup on a crash falls back to none.");
            return;
        }

        TryAssignOne(job, processId, logger);

        foreach (var childPid in ChildProcessFinder.FindChildren(processId))
            TryAssignOne(job, childPid, logger);
    }

    private static void TryAssignOne(IntPtr job, int processId, ILogger logger)
    {
        var handle = OpenProcess(ProcessAllAccess, false, processId);
        if (handle == IntPtr.Zero)
            return; // Already exited, or access denied — nothing to assign.

        try
        {
            if (!AssignProcessToJobObject(job, handle))
                logger.LogDebug("Could not assign process {Pid} to the Chrome cleanup Job Object.", processId);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
