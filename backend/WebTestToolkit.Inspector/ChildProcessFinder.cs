using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WebTestToolkit.Inspector;

// .NET's Process class exposes no parent-PID — the Toolhelp32 snapshot is the standard way
// to walk the process tree on Windows without a WMI dependency. Used by ChromeProcessJob to
// find chrome.exe right after chromedriver.exe spawns it, since Job Object membership has to
// be assigned to that specific process, not inferred from its name alone (a user could have
// an unrelated Chrome window open already).
[SupportedOSPlatform("windows")]
internal static class ChildProcessFinder
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    // Direct children only — deliberately not recursive. Chrome's own further children
    // (renderer/GPU processes it spawns continuously during operation) join the Job Object
    // automatically once chrome.exe itself is a member, the same cascade-to-future-children
    // behavior a Job Object always gives for free; only chrome.exe's own creation had to be
    // caught explicitly, since it already existed by the time this process could look for it.
    public static IEnumerable<int> FindChildren(int parentProcessId)
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            yield break;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry))
                yield break;

            do
            {
                if (entry.th32ParentProcessID == (uint)parentProcessId)
                    yield return (int)entry.th32ProcessID;
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }
}
