using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HWIDSpoofer.Core;

public class SmbiosTable
{
    public byte   Type;
    public byte   Length;
    public ushort Handle;
    public byte[] FormattedData = Array.Empty<byte>();
    public List<string> Strings = new();
    public int    OriginalOffset;
}

public static class SmbiosParser
{
    // Windows mssmbios\Data\SMBiosData layout:
    // [0-3] DWORD  size of SMBIOS data
    // [4]   BYTE   major version
    // [5]   BYTE   minor version
    // [6-7] WORD   DMI revision
    // [8+]  raw SMBIOS tables

    public static List<SmbiosTable> Parse(byte[] raw)
    {
        var out_ = new List<SmbiosTable>();
        int pos  = 8;

        while (pos + 4 <= raw.Length)
        {
            byte type = raw[pos];
            if (type == 127) break;           // End-of-Table marker

            byte   len    = raw[pos + 1];
            if (len < 4) break;

            ushort handle = BitConverter.ToUInt16(raw, pos + 2);
            int    start  = pos;

            int fmtLen = len - 4;
            var fmt    = new byte[Math.Max(0, fmtLen)];
            if (fmtLen > 0 && pos + len <= raw.Length)
                Array.Copy(raw, pos + 4, fmt, 0, fmtLen);

            pos += len;

            // String area: null-terminated strings, terminated by double-null
            var strings = new List<string> { "" };  // index 0 reserved (empty)
            var sb      = new StringBuilder();

            while (pos < raw.Length)
            {
                if (raw[pos] == 0)
                {
                    strings.Add(sb.ToString());
                    sb.Clear();
                    pos++;
                    if (pos < raw.Length && raw[pos] == 0) { pos++; break; }
                }
                else { sb.Append((char)raw[pos++]); }
            }

            out_.Add(new SmbiosTable
            {
                Type           = type,
                Length         = len,
                Handle         = handle,
                FormattedData  = fmt,
                Strings        = strings,
                OriginalOffset = start
            });
        }

        return out_;
    }

    public static byte[] Rebuild(List<SmbiosTable> tables, byte[] originalRaw)
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[8], 0, 8);  // header placeholder

        foreach (var t in tables)
        {
            ms.WriteByte(t.Type);
            ms.WriteByte(t.Length);
            ms.Write(BitConverter.GetBytes(t.Handle), 0, 2);
            ms.Write(t.FormattedData, 0, t.FormattedData.Length);

            bool hasStr = t.Strings.Count > 1;

            for (int i = 1; i < t.Strings.Count; i++)
            {
                var b = Encoding.ASCII.GetBytes(t.Strings[i]);
                ms.Write(b, 0, b.Length);
                ms.WriteByte(0);
            }

            // Terminator: single \0 for structs with strings (loop already wrote individual \0s)
            // Double \0 for structs with no strings
            ms.WriteByte(0);
            if (!hasStr) ms.WriteByte(0);
        }

        // End-of-Table
        ms.WriteByte(127);
        ms.WriteByte(4);
        ms.Write(BitConverter.GetBytes((ushort)0xFFFF), 0, 2);
        ms.WriteByte(0);
        ms.WriteByte(0);

        var buf = ms.ToArray();

        // Patch header with real data length
        Array.Copy(BitConverter.GetBytes((uint)(buf.Length - 8)), 0, buf, 0, 4);

        // Copy version bytes from original
        if (originalRaw.Length >= 8)
        {
            buf[4] = originalRaw[4];
            buf[5] = originalRaw[5];
            buf[6] = originalRaw[6];
            buf[7] = originalRaw[7];
        }

        return buf;
    }
}

public static class SmbiosTableExt
{
    public static string GetString(this SmbiosTable t, int idx)
    {
        if (idx <= 0 || idx >= t.Strings.Count) return "";
        return t.Strings[idx];
    }

    public static void SetString(this SmbiosTable t, int idx, string value)
    {
        while (t.Strings.Count <= idx) t.Strings.Add("");
        if (idx > 0) t.Strings[idx] = value;
    }
}
