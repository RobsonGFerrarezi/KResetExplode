using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KResetExplode;

/// <summary>
/// Caminhos seguros para um programa que roda como administrador. Tudo que o app executa, carrega ou restaura com privilégio
/// fica em %ProgramFiles%\KResetExplode, onde um processo sem administrador não consegue criar nem trocar arquivos.
/// Pastas do usuário (%AppData%, %TEMP%, Downloads) podem ser alteradas por qualquer programa da conta e não são confiáveis.
/// </summary>
public static class SecureFiles
{
    /// <summary>%ProgramFiles%\KResetExplode (só administradores gravam).</summary>
    public static string ProtectedRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KResetExplode");

    /// <summary>Configurações e backups do usuário atual, dentro da pasta protegida (separados por SID).</summary>
    public static string UserFolder { get; } = Path.Combine(ProtectedRoot, "Usuarios", CurrentSid());

    /// <summary>Cria (se preciso) e devolve uma subpasta da pasta protegida, recusando links e junções no caminho.</summary>
    public static string ProtectedFolder(params string[] parts)
    {
        string dir = Path.Combine(new[] { ProtectedRoot }.Concat(parts).ToArray());
        Directory.CreateDirectory(dir);
        EnsureNoReparsePoints(dir);
        return dir;
    }

    /// <summary>Pasta temporária nova e exclusiva dentro da pasta protegida (apague ao terminar).</summary>
    public static string NewProtectedTemp(string prefix) => ProtectedFolder("Temp", $"{prefix}-{Guid.NewGuid():N}");

    /// <summary>Pasta nova com nome imprevisível dentro de uma pasta qualquer (para testes que gravam no disco escolhido).</summary>
    public static string NewUniqueFolder(string parent, string prefix)
    {
        string dir = Path.Combine(parent, $"{prefix}-{Guid.NewGuid():N}");
        if (Directory.Exists(dir) || File.Exists(dir)) throw new IOException("Pasta temporária já existe: " + dir);
        Directory.CreateDirectory(dir);
        EnsureNoReparsePoints(dir);
        return dir;
    }

    /// <summary>Lança exceção se a pasta (ou alguma pasta acima dela) for um link simbólico ou junção.</summary>
    public static void EnsureNoReparsePoints(string dir)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(dir)); d != null; d = d.Parent)
            if (d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint) && d.Parent != null)
                throw new IOException($"A pasta {d.FullName} é um link/junção; operação cancelada por segurança.");
    }

    /// <summary>Verdadeiro se <paramref name="path"/> está dentro de <paramref name="folder"/> (comparando caminhos completos).</summary>
    public static bool IsInside(string path, string folder)
    {
        string p = Path.GetFullPath(path), f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Caminho completo de um programa do System32 (evita rodar um pnputil.exe/powercfg.exe falso da pasta do app).</summary>
    public static string SystemTool(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    public static string PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    private static string CurrentSid()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.User?.Value ?? "desconhecido";
    }

    // ------------------------------------------------------------------ winget

    /// <summary>
    /// O winget.exe em %LocalAppData%\Microsoft\WindowsApps é um "alias" (reparse point AppExecLink) numa pasta do usuário.
    /// Antes de executá-lo como administrador conferimos que ele aponta para o pacote oficial da Microsoft (Program Files\WindowsApps),
    /// e mantemos o arquivo aberto sem permitir troca nem exclusão enquanto o processo é criado.
    /// </summary>
    public sealed class WingetLease : IDisposable
    {
        private readonly SafeFileHandle _handle;
        public string Path { get; }
        internal WingetLease(string path, SafeFileHandle h) { Path = path; _handle = h; }
        public void Dispose() => _handle.Dispose();
    }

    public static string WingetAliasPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

    /// <summary>Abre e valida o winget. Devolve null (com o motivo) se não existir ou não for o oficial.</summary>
    public static WingetLease? AcquireWinget(out string? problem)
    {
        problem = null;
        string alias = WingetAliasPath;
        var h = CreateFileW(alias, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING,
                            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h.IsInvalid) { problem = "winget não encontrado."; h.Dispose(); return null; }
        try
        {
            string? target = AppExecLinkTarget(h);
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + "\\Microsoft.DesktopAppInstaller_";
            if (target == null || !target.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
                || !target.EndsWith("\\winget.exe", StringComparison.OrdinalIgnoreCase) || target.Contains("..") || !File.Exists(target))
            {
                problem = "o winget.exe desta conta não aponta para o pacote oficial da Microsoft (pode ter sido trocado).";
                h.Dispose();
                return null;
            }
            return new WingetLease(alias, h);
        }
        catch (Exception ex) { problem = ex.Message; h.Dispose(); return null; }
    }

    public static bool WingetAvailable
    {
        get { using var l = AcquireWinget(out _); return l != null; }
    }

    /// <summary>Lê o destino de um reparse point AppExecLink (versão 3: id do pacote, id do app, caminho do .exe...).</summary>
    private static string? AppExecLinkTarget(SafeFileHandle h)
    {
        var buf = new byte[16 * 1024];
        if (!DeviceIoControl(h, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, buf, buf.Length, out int got, IntPtr.Zero))
            return null;
        if (got < 12 || BitConverter.ToUInt32(buf, 0) != IO_REPARSE_TAG_APPEXECLINK) return null;
        int len = BitConverter.ToUInt16(buf, 4);
        if (BitConverter.ToUInt32(buf, 8) != 3) return null;
        var strings = Encoding.Unicode.GetString(buf, 12, Math.Min(len - 4, got - 12)).Split('\0');
        return strings.Length >= 3 ? strings[2] : null;
    }

    private const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8, IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
}
