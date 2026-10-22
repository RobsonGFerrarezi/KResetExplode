using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace KResetExplode;

public partial class App : Application
{
    // Uma única instância: abrir o RESET EXPLODE de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        bool first;
        try { _single = new Mutex(true, @"Local\KResetExplode.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outro processo com outro nível de permissão

        // Qualquer programa pode criar um mutex com este nome. Só desistimos de abrir se existir de fato outro
        // RESET EXPLODE (mesmo .exe); senão o nome foi "ocupado" por outro programa e abrimos normalmente.
        if (!first && OtherInstance() is Process other)
        {
            using (other)
            {
                IntPtr h = other.MainWindowHandle;
                if (h != IntPtr.Zero)
                {
                    if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                    SetForegroundWindow(h);
                }
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }

    /// <summary>Outro processo rodando exatamente este mesmo .exe, na mesma sessão do Windows.</summary>
    private static Process? OtherInstance()
    {
        using var self = Process.GetCurrentProcess();
        string? path = Environment.ProcessPath;
        Process? found = null;
        foreach (var p in Process.GetProcessesByName(self.ProcessName))
        {
            try
            {
                if (found == null && p.Id != self.Id && p.SessionId == self.SessionId
                    && string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                { found = p; continue; }
            }
            catch { }
            p.Dispose();
        }
        return found;
    }
}
