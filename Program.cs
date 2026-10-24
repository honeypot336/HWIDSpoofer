using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace HWIDSpoofer;

static class Program
{
    [STAThread]
    static void Main()
    {
        if (!IsAdmin())
        {
            var psi = new ProcessStartInfo(
                Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try { Process.Start(psi); } catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Forms.MainForm());
    }

    static bool IsAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
}
