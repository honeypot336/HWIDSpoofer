using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HWIDSpoofer.Core;

namespace HWIDSpoofer.Forms;

public class MainForm : Form
{
    static readonly Color BG      = Color.FromArgb(13,  13,  18);
    static readonly Color SURFACE = Color.FromArgb(20,  20,  28);
    static readonly Color BORDER  = Color.FromArgb(40,  40,  58);
    static readonly Color ACCENT  = Color.FromArgb(0,   255, 136);
    static readonly Color DANGER  = Color.FromArgb(233, 69,  96);
    static readonly Color WARNING = Color.FromArgb(255, 200, 50);
    static readonly Color TEXT    = Color.FromArgb(220, 220, 230);
    static readonly Color SUBTEXT = Color.FromArgb(110, 110, 130);

    Panel           headerPanel = null!;
    Panel           footerPanel = null!;
    FlowLayoutPanel rowContainer = null!;
    RichTextBox     logBox      = null!;
    Button          btnAll      = null!;
    Button          btnSel      = null!;
    Button          btnRestore  = null!;
    Button          btnRefresh  = null!;
    CheckBox        chkAll      = null!;
    Label           lblStatus   = null!;

    HardwareSnapshot?      snap;
    readonly List<ComponentRow> rows = new();

    public MainForm()
    {
        BuildUI();
        _ = LoadSnap();
    }

    void BuildUI()
    {
        Text          = "HWID Spoofer";
        Size          = new Size(1000, 780);
        MinimumSize   = new Size(900, 640);
        BackColor     = BG;
        ForeColor     = TEXT;
        StartPosition = FormStartPosition.CenterScreen;
        Font          = new Font("Segoe UI", 9f);

        BuildHeader();
        BuildColHeaders();
        BuildRows();
        BuildLog();
        BuildFooter();
    }

    void BuildHeader()
    {
        headerPanel = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = SURFACE };

        headerPanel.Controls.Add(new Label
        {
            Text      = "HWID SPOOFER",
            Font      = new Font("Consolas", 15f, FontStyle.Bold),
            ForeColor = ACCENT,
            AutoSize  = true,
            Location  = new Point(16, 12)
        });
        headerPanel.Controls.Add(new Label
        {
            Text      = "Anti-Cheat Bypass  ·  Requires Administrator",
            Font      = new Font("Consolas", 7.5f),
            ForeColor = SUBTEXT,
            AutoSize  = true,
            Location  = new Point(18, 42)
        });

        chkAll = new CheckBox
        {
            Text      = "SELECT ALL",
            Font      = new Font("Consolas", 8f),
            ForeColor = Color.FromArgb(170, 170, 190),
            Location  = new Point(720, 22),
            AutoSize  = true,
            BackColor = Color.Transparent
        };
        chkAll.CheckedChanged += (_, _) =>
        {
            foreach (var r in rows) r.IsChecked = chkAll.Checked;
        };

        lblStatus = new Label
        {
            Text      = "● READY",
            Font      = new Font("Consolas", 9f, FontStyle.Bold),
            ForeColor = ACCENT,
            AutoSize  = true,
            Location  = new Point(840, 24)
        };

        headerPanel.Controls.Add(chkAll);
        headerPanel.Controls.Add(lblStatus);
        Controls.Add(headerPanel);
    }

    void BuildColHeaders()
    {
        var bar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 26,
            BackColor = Color.FromArgb(15, 15, 22)
        };

        void H(string t, int x, int w) => bar.Controls.Add(new Label
        {
            Text = t, Font = new Font("Consolas", 7.5f, FontStyle.Bold),
            ForeColor = SUBTEXT, Location = new Point(x, 6), Width = w
        });

        H("",              10, 20);
        H("COMPONENT",     38, 185);
        H("CURRENT VALUE", 230, 262);
        H("NEW VALUE",     500, 240);
        H("STATUS",        752, 130);

        Controls.Add(bar);
    }

    void BuildRows()
    {
        rowContainer = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            AutoScroll    = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            BackColor     = BG
        };
        Controls.Add(rowContainer);
    }

    void BuildLog()
    {
        Controls.Add(new Splitter
            { Dock = DockStyle.Bottom, Height = 3, BackColor = BORDER });

        logBox = new RichTextBox
        {
            Dock        = DockStyle.Bottom,
            Height      = 120,
            BackColor   = Color.FromArgb(10, 10, 14),
            ForeColor   = ACCENT,
            Font        = new Font("Consolas", 8.5f),
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            ScrollBars  = RichTextBoxScrollBars.Vertical
        };
        Controls.Add(logBox);
    }

    void BuildFooter()
    {
        footerPanel = new Panel
            { Dock = DockStyle.Bottom, Height = 50, BackColor = SURFACE };

        btnRefresh = Btn("↺  REFRESH",        new Point(14,  11), 118, SUBTEXT);
        btnAll     = Btn("⚡  SPOOF ALL",      new Point(148, 11), 138, ACCENT);
        btnSel     = Btn("✓  SPOOF SELECTED",  new Point(302, 11), 172, WARNING);
        btnRestore = Btn("⊘  RESTORE ALL",     new Point(490, 11), 142, DANGER);

        btnRefresh.Click += async (_, _) => await LoadSnap();
        btnAll    .Click += async (_, _) => await RunSpoof(false);
        btnSel    .Click += async (_, _) => await RunSpoof(true);
        btnRestore.Click += async (_, _) => await RunRestore();

        footerPanel.Controls.AddRange(
            new Control[] { btnRefresh, btnAll, btnSel, btnRestore });
        Controls.Add(footerPanel);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    async Task LoadSnap()
    {
        SetStatus("⟳ READING...", WARNING);
        snap = await Task.Run(HardwareReader.Read);
        PopulateRows(snap);
        SetStatus("● READY", ACCENT);
        Log("Hardware snapshot loaded.", SUBTEXT);
    }

    void PopulateRows(HardwareSnapshot hw)
    {
        rowContainer.SuspendLayout();
        rowContainer.Controls.Clear();
        rows.Clear();

        int  i  = 0;
        Color B() => (i++ % 2 == 0)
            ? Color.FromArgb(20, 20, 29)
            : Color.FromArgb(16, 16, 24);

        Add("Machine GUID",        hw.MachineGuid,           ComponentTag.MachineGuid,      B());
        Add("HW Profile GUID",     hw.HwProfileGuid,         ComponentTag.HwProfileGuid,    B());
        Add("Computer Name",       hw.ComputerName,          ComponentTag.ComputerName,     B());
        Add("Windows Install ID",  hw.WindowsInstallId,      ComponentTag.WindowsId,        B());

        foreach (var nic in hw.NetworkAdapters)
            Add($"MAC  [{Clip(nic.Name, 24)}]", nic.Mac, ComponentTag.MacAddress, B(), nic);

        Add("SMBIOS UUID",          hw.Smbios.SystemUuid,    ComponentTag.SmbiosUuid,      B());
        Add("SMBIOS Sys Serial",    hw.Smbios.SystemSerial,  ComponentTag.SmbiosSysSer,    B());
        Add("SMBIOS Board Serial",  hw.Smbios.BoardSerial,   ComponentTag.SmbiosBoardSer,  B());
        Add("SMBIOS Chassis Ser.",  hw.Smbios.ChassisSerial, ComponentTag.SmbiosChassisSer,B());
        Add("BIOS Version",         hw.Smbios.BiosVersion,   ComponentTag.BiosVer,         B());
        Add("Volume Serial (C:)",   hw.VolumeSerial,         ComponentTag.VolumeSerial,    B());
        Add("Disk Serial",          hw.DiskSerial,           ComponentTag.DiskSerial,      B());

        rowContainer.ResumeLayout();
    }

    void Add(string name, string cur, ComponentTag tag, Color bg, NicInfo? nic = null)
    {
        var r = new ComponentRow(name, cur, tag, bg, rowContainer.Width - 4, nic);
        rows.Add(r);
        rowContainer.Controls.Add(r.Panel);
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    async Task RunSpoof(bool selectedOnly)
    {
        if (snap == null) return;
        SetButtons(false);
        SetStatus("⚡ SPOOFING...", WARNING);

        bool needReboot = false;

        foreach (var row in rows)
        {
            if (selectedOnly && !row.IsChecked) continue;

            var res = await Task.Run(() => Apply(row, snap));
            row.SetResult(res);

            switch (res)
            {
                case SpooferResult.Success:
                    Log($"✓  {row.Name}", ACCENT); break;
                case SpooferResult.RequiresReboot:
                    Log($"↺  {row.Name}  (reboot required)", WARNING);
                    needReboot = true; break;
                case SpooferResult.RequiresDriver:
                    Log($"⚠  {row.Name}  — install DiskSpoofer.sys (Driver/)", DANGER); break;
                case SpooferResult.Failed:
                    Log($"✗  {row.Name}  — failed", DANGER); break;
            }
        }

        SetStatus(needReboot ? "↺ DONE — REBOOT" : "✓ DONE", ACCENT);
        SetButtons(true);

        if (needReboot &&
            MessageBox.Show("Certains changements nécessitent un redémarrage.\nReboot maintenant ?",
                "Reboot Required", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            == DialogResult.Yes)
        {
            Process.Start("shutdown.exe", "/r /t 5 /c \"HWID Spoofer — applying changes\"");
        }
    }

    static SpooferResult Apply(ComponentRow row, HardwareSnapshot snap) =>
        row.Tag switch
        {
            ComponentTag.MachineGuid    => Spoofer.SpoofMachineGuid(row.Custom),
            ComponentTag.HwProfileGuid  => Spoofer.SpoofHwProfileGuid(row.Custom),
            ComponentTag.ComputerName   => Spoofer.SpoofComputerName(row.Custom),
            ComponentTag.WindowsId      => Spoofer.SpoofInstallId(row.Custom),
            ComponentTag.MacAddress     => row.Nic != null
                                           ? Spoofer.SpoofMac(row.Nic, row.Custom)
                                           : SpooferResult.Failed,
            ComponentTag.SmbiosUuid     => Spoofer.SpoofSmbios(new(
                                           SpoofUuid: true, SpoofSystemSerial: false,
                                           SpoofBoardSerial: false, SpoofChassisSerial: false,
                                           SpoofBiosVersion: false)),
            ComponentTag.SmbiosSysSer   => Spoofer.SpoofSmbios(new(
                                           SpoofUuid: false, SpoofSystemSerial: true,
                                           SpoofBoardSerial: false, SpoofChassisSerial: false,
                                           SpoofBiosVersion: false, SystemSerial: row.Custom)),
            ComponentTag.SmbiosBoardSer => Spoofer.SpoofSmbios(new(
                                           SpoofUuid: false, SpoofSystemSerial: false,
                                           SpoofBoardSerial: true, SpoofChassisSerial: false,
                                           SpoofBiosVersion: false, BoardSerial: row.Custom)),
            ComponentTag.SmbiosChassisSer => Spoofer.SpoofSmbios(new(
                                           SpoofUuid: false, SpoofSystemSerial: false,
                                           SpoofBoardSerial: false, SpoofChassisSerial: true,
                                           SpoofBiosVersion: false, ChassisSerial: row.Custom)),
            ComponentTag.BiosVer        => Spoofer.SpoofSmbios(new(
                                           SpoofUuid: false, SpoofSystemSerial: false,
                                           SpoofBoardSerial: false, SpoofChassisSerial: false,
                                           SpoofBiosVersion: true, BiosVersion: row.Custom)),
            ComponentTag.VolumeSerial   => Spoofer.SpoofVolumeSerial(),
            ComponentTag.DiskSerial     => Spoofer.SpoofDiskSerial(),
            _                           => SpooferResult.Failed
        };

    async Task RunRestore()
    {
        if (snap == null) return;
        SetButtons(false);
        SetStatus("⊘ RESTORING...", DANGER);

        await Task.Run(() =>
        {
            Spoofer.RestoreMachineGuid(snap.MachineGuid);
            foreach (var nic in snap.NetworkAdapters)
                Spoofer.RestoreMac(nic);
        });

        Log("Restore complete. Reboot to revert SMBIOS + ComputerName.", WARNING);
        SetStatus("● RESTORED", ACCENT);
        SetButtons(true);
        await LoadSnap();
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    void Log(string msg, Color c)
    {
        if (InvokeRequired) { Invoke(() => Log(msg, c)); return; }
        logBox.SelectionColor = c;
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}]  {msg}\n");
        logBox.ScrollToCaret();
    }

    void SetStatus(string t, Color c)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(t, c)); return; }
        lblStatus.Text      = t;
        lblStatus.ForeColor = c;
    }

    void SetButtons(bool on)
    {
        if (InvokeRequired) { Invoke(() => SetButtons(on)); return; }
        foreach (Control ctrl in footerPanel.Controls)
            if (ctrl is Button b) b.Enabled = on;
    }

    static Button Btn(string text, Point loc, int w, Color fore)
    {
        var b = new Button
        {
            Text      = text,
            Location  = loc,
            Size      = new Size(w, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(26, 26, 36),
            ForeColor = fore,
            Font      = new Font("Consolas", 8.5f, FontStyle.Bold),
            Cursor    = Cursors.Hand
        };
        b.FlatAppearance.BorderColor         = fore;
        b.FlatAppearance.BorderSize          = 1;
        b.FlatAppearance.MouseOverBackColor  = Color.FromArgb(36, 36, 50);
        return b;
    }

    static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(max - 2)] + "..";
}

// ── Enum ──────────────────────────────────────────────────────────────────────

public enum ComponentTag
{
    MachineGuid, HwProfileGuid, ComputerName, WindowsId,
    MacAddress,
    SmbiosUuid, SmbiosSysSer, SmbiosBoardSer, SmbiosChassisSer, BiosVer,
    VolumeSerial, DiskSerial
}

// ── ComponentRow ──────────────────────────────────────────────────────────────

public class ComponentRow
{
    static readonly Color ACCENT  = Color.FromArgb(0,   255, 136);
    static readonly Color DANGER  = Color.FromArgb(233, 69,  96);
    static readonly Color WARNING = Color.FromArgb(255, 200, 50);
    static readonly Color TEXT    = Color.FromArgb(210, 210, 225);
    static readonly Color SUBTEXT = Color.FromArgb(100, 100, 120);

    public Panel        Panel  { get; }
    public ComponentTag Tag    { get; }
    public NicInfo?     Nic    { get; }
    public string       Name   { get; }

    public bool IsChecked
    {
        get => chk.Checked;
        set
        {
            if (chk.InvokeRequired) chk.Invoke(() => chk.Checked = value);
            else chk.Checked = value;
        }
    }

    public string? Custom =>
        string.IsNullOrWhiteSpace(customBox.Text) ? null : customBox.Text.Trim();

    readonly CheckBox chk;
    readonly TextBox  customBox;
    readonly Label    statusLbl;

    public ComponentRow(string name, string cur, ComponentTag tag,
                        Color bg, int width, NicInfo? nic = null)
    {
        Name = name;
        Tag  = tag;
        Nic  = nic;

        Panel = new Panel { Width = width, Height = 34, BackColor = bg };

        chk = new CheckBox
        {
            Location  = new Point(10, 9),
            Size      = new Size(18, 18),
            BackColor = Color.Transparent
        };

        var nameLbl = new Label
        {
            Text      = name,
            Location  = new Point(36, 9),
            Width     = 188,
            ForeColor = TEXT,
            Font      = new Font("Consolas", 8.5f)
        };

        var curLbl = new Label
        {
            Text      = Clip(cur, 34),
            Location  = new Point(230, 9),
            Width     = 262,
            ForeColor = SUBTEXT,
            Font      = new Font("Consolas", 8f)
        };

        // Tooltip pour voir la valeur complète au survol
        var tip = new ToolTip();
        tip.SetToolTip(curLbl, cur);

        customBox = new TextBox
        {
            Location        = new Point(500, 6),
            Width           = 240,
            BackColor       = Color.FromArgb(18, 18, 27),
            ForeColor       = Color.FromArgb(180, 180, 200),
            Font            = new Font("Consolas", 8f),
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "(random)"
        };

        statusLbl = new Label
        {
            Text      = "—",
            Location  = new Point(752, 9),
            Width     = 180,
            ForeColor = SUBTEXT,
            Font      = new Font("Consolas", 8f, FontStyle.Bold)
        };

        Panel.Controls.Add(chk);
        Panel.Controls.Add(nameLbl);
        Panel.Controls.Add(curLbl);
        Panel.Controls.Add(customBox);
        Panel.Controls.Add(statusLbl);
    }

    public void SetResult(SpooferResult r)
    {
        if (Panel.InvokeRequired) { Panel.Invoke(() => SetResult(r)); return; }

        (statusLbl.Text, statusLbl.ForeColor) = r switch
        {
            SpooferResult.Success        => ("✓ SPOOFED",     ACCENT),
            SpooferResult.RequiresReboot => ("↺ ON REBOOT",   WARNING),
            SpooferResult.RequiresDriver => ("⚠ NEED DRIVER", DANGER),
            SpooferResult.Failed         => ("✗ FAILED",      DANGER),
            _                            => ("—",             SUBTEXT)
        };
    }

    static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(max - 2)] + "..";
}
