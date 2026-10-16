using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ImageMango.Core;

/// <summary>
/// The hardware the recommended settings are chosen for. Saved to machine.json; set
/// "vramOverrideGb" there to pick another tier without re-detecting.
/// </summary>
public sealed class MachineProfile
{
    public string GpuName { get; set; } = "Unknown GPU";
    public string GpuVendor { get; set; } = "unknown";
    public double VramGb { get; set; }
    public double RamGb { get; set; }
    public int LogicalCores { get; set; }
    public string DetectedBy { get; set; } = "";
    public DateTime DetectedAt { get; set; }
    public double? VramOverrideGb { get; set; }

    [JsonIgnore] public double EffectiveVramGb => VramOverrideGb ?? VramGb;

    [JsonIgnore]
    public string Summary =>
        $"{GpuName} · {EffectiveVramGb:0.#} GB VRAM{(VramOverrideGb is null ? "" : " (override)")} · {RamGb:0} GB RAM · {LogicalCores} threads";

    public static MachineProfile LoadOrDetect()
    {
        try
        {
            var saved = Json.Load<MachineProfile>(AppPaths.MachineFile);
            if (saved is not null) return saved;
        }
        catch { /* corrupt file: re-detect */ }
        var p = Detect();
        p.Save();
        return p;
    }

    public void Save() => Json.Save(AppPaths.MachineFile, this);

    public static MachineProfile Detect()
    {
        var p = new MachineProfile
        {
            RamGb = Math.Round(TotalRamBytes() / 1073741824.0, 1),
            LogicalCores = Environment.ProcessorCount,
            DetectedAt = DateTime.Now,
        };
        if (!TryNvidiaSmi(p)) TryRegistry(p);
        return p;
    }

    private static bool TryNvidiaSmi(MachineProfile p)
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000) || proc.ExitCode != 0) return false;

            // One line per GPU: "NVIDIA GeForce RTX 4090, 24564". Use the largest.
            var best = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split(','))
                .Where(a => a.Length >= 2 && double.TryParse(a[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                .Select(a => (Name: a[0].Trim(), MiB: double.Parse(a[1].Trim(), CultureInfo.InvariantCulture)))
                .OrderByDescending(g => g.MiB)
                .FirstOrDefault();
            if (best.Name is null) return false;

            p.GpuName = best.Name;
            p.GpuVendor = "nvidia";
            p.VramGb = Math.Round(best.MiB / 1024.0, 1);
            p.DetectedBy = "nvidia-smi";
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Display adapters record a 64-bit memory size in the registry (WMI's AdapterRAM caps at 4 GB).</summary>
    private static void TryRegistry(MachineProfile p)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("HardwareInformation.qwMemorySize") is not { } raw) continue;
                double bytes = raw switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                    _ => 0,
                };
                double gb = Math.Round(bytes / 1073741824.0, 1);
                if (gb <= p.VramGb) continue;
                p.VramGb = gb;
                p.GpuName = k.GetValue("DriverDesc") as string ?? "GPU";
                var lower = p.GpuName.ToLowerInvariant();
                p.GpuVendor = lower.Contains("nvidia") ? "nvidia" : lower.Contains("amd") || lower.Contains("radeon") ? "amd" : lower.Contains("intel") ? "intel" : "unknown";
                p.DetectedBy = "registry";
            }
        }
        catch { /* leave defaults */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private static double TotalRamBytes()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m.ullTotalPhys : 0;
    }
}
