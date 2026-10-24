using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace HWIDSpoofer.Core;

public enum SpooferResult { Success, RequiresReboot, Failed, RequiresDriver }

public record SmbiosOptions(
    bool    SpoofUuid          = true,
    bool    SpoofSystemSerial  = true,
    bool    SpoofBoardSerial   = true,
    bool    SpoofChassisSerial = true,
    bool    SpoofBiosVersion   = true,
    string? SystemSerial       = null,
    string? BoardSerial        = null,
    string? ChassisSerial      = null,
    string? BiosVersion        = null
);

public static class Spoofer
{
    // ── Generators ────────────────────────────────────────────────────────────

    public static string NewGuid() => Guid.NewGuid().ToString();

    public static string NewMac()
    {
        byte[] ouis = { 0x00, 0x1A, 0x2C, 0x3C, 0x4C, 0x5C, 0x6C, 0x8C, 0x9C };
        var b = new byte[6];
        Random.Shared.NextBytes(b);
        b[0] = ouis[Random.Shared.Next(ouis.Length)];
        return string.Join(":", b.Select(x => x.ToString("X2")));
    }

    public static string NewSerial(int len = 12)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, len)
            .Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    public static string NewComputerName()
    {
        string[] pfx = { "DESKTOP", "LAPTOP", "PC", "WORKSTATION" };
        return $"{pfx[Random.Shared.Next(pfx.Length)]}-{NewSerial(6)}";
    }

    // ── Machine GUID ──────────────────────────────────────────────────────────

    public static SpooferResult SpoofMachineGuid(string? value = null)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography", writable: true);
            if (k == null) return SpooferResult.Failed;
            k.SetValue("MachineGuid", value ?? NewGuid(), RegistryValueKind.String);
            return SpooferResult.Success;
        }
        catch { return SpooferResult.Failed; }
    }

    public static SpooferResult RestoreMachineGuid(string original) =>
        SpoofMachineGuid(original);

    // ── HW Profile GUID ───────────────────────────────────────────────────────

    public static SpooferResult SpoofHwProfileGuid(string? value = null)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles\0001",
                writable: true);
            if (k == null) return SpooferResult.Failed;
            k.SetValue("HwProfileGuid",
                value ?? $"{{{Guid.NewGuid().ToString().ToUpper()}}}",
                RegistryValueKind.String);
            return SpooferResult.Success;
        }
        catch { return SpooferResult.Failed; }
    }

    // ── Computer Name ─────────────────────────────────────────────────────────

    public static SpooferResult SpoofComputerName(string? value = null)
    {
        try
        {
            var name = value ?? NewComputerName();
            string[] paths =
            {
                @"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName",
                @"SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName"
            };
            foreach (var p in paths)
            {
                using var k = Registry.LocalMachine.OpenSubKey(p, writable: true);
                k?.SetValue("ComputerName", name, RegistryValueKind.String);
            }
            return SpooferResult.RequiresReboot;
        }
        catch { return SpooferResult.Failed; }
    }

    // ── Windows Install ID ────────────────────────────────────────────────────

    public static SpooferResult SpoofInstallId(string? value = null)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: true);
            if (k == null) return SpooferResult.Failed;

            k.SetValue("ProductId",
                value ?? $"{NewSerial(5)}-{NewSerial(5)}",
                RegistryValueKind.String);

            var now  = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var date = now - Random.Shared.Next(0, 730 * 86400);
            k.SetValue("InstallDate", (int)date, RegistryValueKind.DWord);

            return SpooferResult.Success;
        }
        catch { return SpooferResult.Failed; }
    }

    // ── MAC Address ───────────────────────────────────────────────────────────

    const string NIC_CLS =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";

    public static SpooferResult SpoofMac(NicInfo nic, string? value = null)
    {
        try
        {
            var mac = (value ?? NewMac()).Replace(":", "").Replace("-", "");
            using var k = Registry.LocalMachine.OpenSubKey(
                $@"{NIC_CLS}\{nic.RegistrySubKey}", writable: true);
            if (k == null) return SpooferResult.Failed;
            k.SetValue("NetworkAddress", mac, RegistryValueKind.String);
            ToggleAdapter(nic.Name);
            return SpooferResult.Success;
        }
        catch { return SpooferResult.Failed; }
    }

    public static SpooferResult RestoreMac(NicInfo nic)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                $@"{NIC_CLS}\{nic.RegistrySubKey}", writable: true);
            k?.DeleteValue("NetworkAddress", throwOnMissingValue: false);
            ToggleAdapter(nic.Name);
            return SpooferResult.Success;
        }
        catch { return SpooferResult.Failed; }
    }

    static void ToggleAdapter(string name)
    {
        RunPS($"Disable-NetAdapter -Name \"{name}\" -Confirm:$false");
        Thread.Sleep(1200);
        RunPS($"Enable-NetAdapter -Name \"{name}\" -Confirm:$false");
    }

    // ── SMBIOS ────────────────────────────────────────────────────────────────

    public static SpooferResult SpoofSmbios(SmbiosOptions opts)
    {
        try
        {
            using var reg = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\mssmbios\Data", writable: true);
            if (reg == null) return SpooferResult.Failed;

            var raw = reg.GetValue("SMBiosData") as byte[];
            if (raw == null || raw.Length < 8) return SpooferResult.Failed;

            var tables = SmbiosParser.Parse(raw);
            var t0 = System.Linq.Enumerable.FirstOrDefault(tables, t => t.Type == 0);
            var t1 = System.Linq.Enumerable.FirstOrDefault(tables, t => t.Type == 1);
            var t2 = System.Linq.Enumerable.FirstOrDefault(tables, t => t.Type == 2);
            var t3 = System.Linq.Enumerable.FirstOrDefault(tables, t => t.Type == 3);

            if (t1 != null)
            {
                if (opts.SpoofUuid && t1.FormattedData.Length >= 20)
                {
                    var uuid = Guid.NewGuid().ToByteArray();
                    Array.Copy(uuid, 0, t1.FormattedData, 4, 16);
                }
                if (opts.SpoofSystemSerial && t1.FormattedData.Length > 3)
                    t1.SetString(t1.FormattedData[3], opts.SystemSerial ?? NewSerial());
            }

            if (t2 != null && opts.SpoofBoardSerial && t2.FormattedData.Length > 3)
                t2.SetString(t2.FormattedData[3], opts.BoardSerial ?? NewSerial());

            if (t3 != null && opts.SpoofChassisSerial && t3.FormattedData.Length > 3)
                t3.SetString(t3.FormattedData[3], opts.ChassisSerial ?? NewSerial());

            if (t0 != null && opts.SpoofBiosVersion && t0.FormattedData.Length > 1)
                t0.SetString(t0.FormattedData[1],
                    opts.BiosVersion ?? $"F.{Random.Shared.Next(10, 99)}");

            reg.SetValue("SMBiosData", SmbiosParser.Rebuild(tables, raw), RegistryValueKind.Binary);
            return SpooferResult.RequiresReboot;
        }
        catch { return SpooferResult.Failed; }
    }

    // ── Volume Serial ─────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string path, uint access, uint share,
        IntPtr sa, uint creation, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(IntPtr h, byte[] buf, int toRead,
        out int read, IntPtr ovl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(IntPtr h, byte[] buf, int toWrite,
        out int written, IntPtr ovl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFilePointerEx(IntPtr h, long dist,
        out long newPos, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint code,
        IntPtr inB, int inSz, IntPtr outB, int outSz,
        out int ret, IntPtr ovl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    const uint GENERIC_RW       = 0xC0000000;
    const uint SHARE_RW         = 3;
    const uint OPEN_EXISTING    = 3;
    const uint NO_BUFFERING     = 0x20000000;
    const uint FSCTL_LOCK       = 0x00090018;
    const uint FSCTL_UNLOCK     = 0x0009001C;
    const int  NTFS_SER_OFFSET  = 0x48;   // offset du serial dans le boot sector NTFS

    public static SpooferResult SpoofVolumeSerial(string drive = "C")
    {
        var rndSerial = GenSerial();

        // Pour C: (volume système actif) — schedule au prochain boot
        if (drive.ToUpper() == "C")
            return ScheduleVolBoot(rndSerial);

        return WriteVolDirect($"\\\\.\\{drive}:", rndSerial);
    }

    static SpooferResult WriteVolDirect(string path, uint serial)
    {
        var h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero,
                           OPEN_EXISTING, NO_BUFFERING, IntPtr.Zero);
        if (h == new IntPtr(-1)) return SpooferResult.Failed;

        try
        {
            DeviceIoControl(h, FSCTL_LOCK, IntPtr.Zero, 0,
                            IntPtr.Zero, 0, out _, IntPtr.Zero);

            var sector = new byte[512];
            SetFilePointerEx(h, 0, out _, 0);
            ReadFile(h, sector, 512, out _, IntPtr.Zero);

            Array.Copy(BitConverter.GetBytes(serial), 0, sector, NTFS_SER_OFFSET, 4);

            SetFilePointerEx(h, 0, out _, 0);
            WriteFile(h, sector, 512, out _, IntPtr.Zero);

            DeviceIoControl(h, FSCTL_UNLOCK, IntPtr.Zero, 0,
                            IntPtr.Zero, 0, out _, IntPtr.Zero);
            return SpooferResult.Success;
        }
        finally { CloseHandle(h); }
    }

    static SpooferResult ScheduleVolBoot(uint serial)
    {
        // Enregistre dans RunOnce — au prochain boot un script patche le boot sector hors-ligne
        try
        {
            var ps = $@"
$h = [System.IO.File]::Open('\\\\.\\ physicaldrive0',
    [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::ReadWrite)
$h.Seek(0x{NTFS_SER_OFFSET:X}, 'Begin') | Out-Null
$h.Write([BitConverter]::GetBytes([uint32]{serial}), 0, 4)
$h.Close()";

            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", writable: true);
            k?.SetValue("HWIDSpooferVol",
                $"powershell -WindowStyle Hidden -EncodedCommand " +
                Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps)));

            return SpooferResult.RequiresReboot;
        }
        catch { return SpooferResult.Failed; }
    }

    static uint GenSerial() { var b = new byte[4]; Random.Shared.NextBytes(b); return BitConverter.ToUInt32(b); }

    // ── Disk Serial (kernel driver requis) ────────────────────────────────────

    public static SpooferResult SpoofDiskSerial() =>
        SpooferResult.RequiresDriver;

    // ── Util ──────────────────────────────────────────────────────────────────

    static void RunPS(string cmd) =>
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NonInteractive -WindowStyle Hidden -Command \"{cmd}\"")
        {
            UseShellExecute = false,
            CreateNoWindow  = true
        })?.WaitForExit(8000);
}
