using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace HWIDSpoofer.Core;

public record HardwareSnapshot(
    string MachineGuid,
    string HwProfileGuid,
    string ComputerName,
    string WindowsInstallId,
    List<NicInfo> NetworkAdapters,
    string VolumeSerial,
    SmbiosData Smbios,
    string DiskSerial
);

public record NicInfo(string Name, string InstanceId, string Mac, string RegistrySubKey);

public record SmbiosData(
    string SystemUuid,
    string SystemSerial,
    string BoardSerial,
    string ChassisSerial,
    string BiosVersion,
    byte[] RawBlob
);

public static class HardwareReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetVolumeInformation(
        string root, StringBuilder volName, int volNameSz,
        out uint serial, out uint maxComp, out uint flags,
        StringBuilder fsName, int fsNameSz);

    public static HardwareSnapshot Read()
    {
        return new HardwareSnapshot(
            MachineGuid:      ReadReg(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid"),
            HwProfileGuid:    ReadReg(@"SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles\0001", "HwProfileGuid"),
            ComputerName:     Environment.MachineName,
            WindowsInstallId: ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId"),
            NetworkAdapters:  EnumerateNics(),
            VolumeSerial:     ReadVolSerial("C:\\"),
            Smbios:           ReadSmbios(),
            DiskSerial:       ReadDiskSerial()
        );
    }

    static string ReadReg(string sub, string val)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(sub);
            return k?.GetValue(val)?.ToString() ?? "N/A";
        }
        catch { return "N/A"; }
    }

    static string ReadVolSerial(string root)
    {
        var vn = new StringBuilder(256);
        var fs = new StringBuilder(256);
        return GetVolumeInformation(root, vn, 256, out uint s, out _, out _, fs, 256)
            ? s.ToString("X8") : "N/A";
    }

    static List<NicInfo> EnumerateNics()
    {
        var result = new List<NicInfo>();
        const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";

        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(cls);
            if (root == null) return result;

            foreach (var sub in root.GetSubKeyNames())
            {
                if (sub == "Properties") continue;
                using var k = root.OpenSubKey(sub);
                if (k == null) continue;

                var desc = k.GetValue("DriverDesc")?.ToString();
                if (desc == null) continue;

                var guid = k.GetValue("NetCfgInstanceId")?.ToString() ?? "";

                var iface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n =>
                        n.Id.Equals(guid, StringComparison.OrdinalIgnoreCase) ||
                        n.Description.Equals(desc, StringComparison.OrdinalIgnoreCase));

                var mac = iface != null
                    ? BitConverter.ToString(iface.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":")
                    : "N/A";

                result.Add(new NicInfo(desc, guid, mac, sub));
            }
        }
        catch { }

        return result;
    }

    static SmbiosData ReadSmbios()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\mssmbios\Data");
            var raw = k?.GetValue("SMBiosData") as byte[];
            if (raw == null || raw.Length < 8) return Empty(raw);

            var tables = SmbiosParser.Parse(raw);
            var t0 = tables.FirstOrDefault(t => t.Type == 0);
            var t1 = tables.FirstOrDefault(t => t.Type == 1);
            var t2 = tables.FirstOrDefault(t => t.Type == 2);
            var t3 = tables.FirstOrDefault(t => t.Type == 3);

            return new SmbiosData(
                SystemUuid:    t1 != null ? FormatUuid(t1.FormattedData, 4) : "N/A",
                SystemSerial:  t1 != null && t1.FormattedData.Length > 3 ? t1.GetString(t1.FormattedData[3]) : "N/A",
                BoardSerial:   t2 != null && t2.FormattedData.Length > 3 ? t2.GetString(t2.FormattedData[3]) : "N/A",
                ChassisSerial: t3 != null && t3.FormattedData.Length > 3 ? t3.GetString(t3.FormattedData[3]) : "N/A",
                BiosVersion:   t0 != null && t0.FormattedData.Length > 1 ? t0.GetString(t0.FormattedData[1]) : "N/A",
                RawBlob:       raw
            );
        }
        catch { return Empty(null); }
    }

    static SmbiosData Empty(byte[]? raw) =>
        new("N/A", "N/A", "N/A", "N/A", "N/A", raw ?? Array.Empty<byte>());

    static string FormatUuid(byte[] d, int off)
    {
        if (d.Length < off + 16) return "N/A";
        var b = d.AsSpan(off, 16);
        return $"{BitConverter.ToUInt32(b[0..4]):X8}-" +
               $"{BitConverter.ToUInt16(b[4..6]):X4}-" +
               $"{BitConverter.ToUInt16(b[6..8]):X4}-" +
               $"{b[8]:X2}{b[9]:X2}-" +
               $"{b[10]:X2}{b[11]:X2}{b[12]:X2}{b[13]:X2}{b[14]:X2}{b[15]:X2}";
    }

    static string ReadDiskSerial()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
            foreach (ManagementObject d in s.Get())
                return d["SerialNumber"]?.ToString()?.Trim() ?? "N/A";
        }
        catch { }
        return "N/A";
    }
}
