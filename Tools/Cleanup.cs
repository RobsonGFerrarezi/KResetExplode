using System.ComponentModel;
using System.IO;
using System.Text;

namespace KResetExplode;

/// <summary>Um grupo de arquivos que pode ser apagado (com o tamanho atual).</summary>
public sealed class CleanTarget : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Paths { get; init; } = Array.Empty<string>();
    /// <summary>Só apaga o que não foi alterado nas últimas N horas (0 = tudo).</summary>
    public int OlderThanHours { get; init; }
    /// <summary>Pasta protegida do próprio app: pode ser apagada com administrador. As outras são apagadas como usuário comum.</summary>
    public bool Protected { get; init; }

    private long _size;
    private bool _selected;
    public long SizeBytes { get => _size; set { _size = value; On(nameof(SizeBytes)); On(nameof(SizeText)); } }
    public bool Selected { get => _selected; set { _selected = value; On(nameof(Selected)); } }
    public string SizeText => SizeBytes >= 1L << 30 ? $"{SizeBytes / 1073741824.0:0.0} GB" : $"{SizeBytes / 1048576.0:0} MB";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void On(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>
/// Limpeza para jogos: caches de shaders (NVIDIA, AMD, DirectX), temporários e instaladores de driver antigos.
/// Segurança: as pastas do usuário (%TEMP%, %LocalAppData%, C:\NVIDIA) podem ser alteradas por qualquer programa da conta; apagar
/// nelas como administrador permitiria que um programa trocasse uma pasta por um link para C:\Windows no meio da limpeza.
/// Por isso a limpeza delas roda como usuário comum (PowerShell aberto pelo Explorer) e não segue links nem junções.
/// </summary>
public static class Cleanup
{
    private static string Local(params string[] p) =>
        Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Concat(p).ToArray());

    public static List<CleanTarget> Targets()
    {
        string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var list = new List<CleanTarget>
        {
            new() { Id = "nvidia", Name = "Cache de shaders da NVIDIA", Selected = true,
                    Description = "Shaders compilados pelo driver NVIDIA. Limpar resolve engasgos depois de trocar de driver; o jogo recompila na primeira partida.",
                    Paths = new[] { Local("NVIDIA", "DXCache"), Local("NVIDIA", "GLCache"), Local("NVIDIA Corporation", "NV_Cache") } },
            new() { Id = "amd", Name = "Cache de shaders da AMD", Selected = true,
                    Description = "Shaders compilados pelo driver AMD (DirectX, Vulkan e OpenGL).",
                    Paths = new[] { Local("AMD", "DxCache"), Local("AMD", "DxcCache"), Local("AMD", "VkCache"), Local("AMD", "GLCache") } },
            new() { Id = "dx", Name = "Cache de shaders do DirectX", Selected = true,
                    Description = "Cache do próprio Windows para shaders DirectX (é o mesmo que a Limpeza de Disco apaga).",
                    Paths = new[] { Local("D3DSCache") } },
            new() { Id = "temp", Name = "Arquivos temporários", Selected = true, OlderThanHours = 24,
                    Description = "Pasta TEMP do usuário (só o que não foi usado nas últimas 24 horas; arquivos em uso são pulados).",
                    Paths = new[] { Path.GetTempPath() } },
            new() { Id = "oldinstallers", Name = "Instaladores de driver extraídos", Selected = true,
                    Description = "Pastas que os instaladores da NVIDIA e da AMD deixam no disco depois de instalar (C:\\NVIDIA, C:\\AMD).",
                    Paths = new[] { Path.Combine(sysDrive, "NVIDIA"), Path.Combine(sysDrive, "AMD") } },
            new() { Id = "appdownloads", Name = "Drivers baixados pelo RESET EXPLODE", Selected = true, Protected = true,
                    Description = "Instaladores de driver que o app baixou (o driver já instalado continua no Windows).",
                    Paths = new[] { Path.Combine(SecureFiles.ProtectedRoot, "Downloads", "Drivers") } },
        };
        foreach (var t in list) t.SizeBytes = Measure(t);
        return list;
    }

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Tamanho do que seria apagado (sem seguir links; na TEMP, só o que é mais velho que o limite).</summary>
    public static long Measure(CleanTarget t)
    {
        long total = 0;
        var limit = DateTime.Now.AddHours(-t.OlderThanHours);
        foreach (var dir in t.Paths.Where(Directory.Exists))
        {
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                {
                    if (Skip(t, entry, limit)) continue;
                    if (entry is FileInfo f) total += f.Length;
                    else if (entry is DirectoryInfo d && !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        try { total += d.EnumerateFiles("*", Walk).Sum(x => x.Length); } catch { }
                }
            }
            catch { }
        }
        return total;
    }

    private static bool Skip(CleanTarget t, FileSystemInfo e, DateTime limit) =>
        (t.OlderThanHours > 0 && e.LastWriteTime > limit)
        || (t.Id == "temp" && e.Name.Equals(".net", StringComparison.OrdinalIgnoreCase)); // DLLs extraídas da versão portátil (em uso)

    /// <summary>Apaga os grupos marcados. Devolve quantos bytes deixaram de existir.</summary>
    public static async Task<long> CleanAsync(IReadOnlyList<CleanTarget> targets, Action<string> progress)
    {
        long before = targets.Sum(t => t.SizeBytes);

        foreach (var t in targets.Where(t => t.Protected))
            foreach (var dir in t.Paths.Where(Directory.Exists))
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", Walk))
                    try { f.Delete(); } catch { }

        var user = targets.Where(t => !t.Protected).ToList();
        if (user.Count > 0)
        {
            progress("Apagando como usuário comum (sem administrador)…");
            await RunUnelevatedAsync(user);
        }

        foreach (var t in targets) t.SizeBytes = Measure(t);
        return Math.Max(0, before - targets.Sum(t => t.SizeBytes));
    }

    /// <summary>
    /// Roda a limpeza num PowerShell sem administrador (aberto pelo Explorer) e espera ele terminar (arquivo-sinal no fim).
    /// [IO.Directory]::Delete não entra em junções/links: só remove o link.
    /// </summary>
    private static async Task RunUnelevatedAsync(List<CleanTarget> targets)
    {
        string done = Path.Combine(Path.GetTempPath(), $"KResetExplode-limpeza-{Guid.NewGuid():N}.ok");
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine("""
            function Limpar([string]$dir, [int]$horas) {
              if (-not [IO.Directory]::Exists($dir)) { return }
              $limite = (Get-Date).AddHours(-$horas)
              foreach ($i in [IO.Directory]::GetFileSystemEntries($dir)) {
                try {
                  if ([IO.Path]::GetFileName($i) -eq '.net' -or [IO.Path]::GetFileName($i) -like 'KResetExplode-limpeza-*') { continue }
                  $a = [IO.File]::GetAttributes($i)
                  $isDir = ($a -band [IO.FileAttributes]::Directory) -ne 0
                  if ($horas -gt 0) {
                    $t = if ($isDir) { [IO.Directory]::GetLastWriteTime($i) } else { [IO.File]::GetLastWriteTime($i) }
                    if ($t -gt $limite) { continue }
                  }
                  if (($a -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    if ($isDir) { [IO.Directory]::Delete($i) } else { [IO.File]::Delete($i) }
                  } elseif ($isDir) { [IO.Directory]::Delete($i, $true) } else { [IO.File]::Delete($i) }
                } catch { }
              }
            }
            """);
        foreach (var t in targets)
            foreach (var p in t.Paths)
                sb.AppendLine($"Limpar {Q(p)} {t.OlderThanHours}");
        sb.AppendLine($"[IO.File]::WriteAllText({Q(done)}, 'ok')");

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(sb.ToString()));
        ShellLauncher.Open(SecureFiles.PowerShell, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}",
                           Path.GetTempPath(), hidden: true);

        for (int i = 0; i < 600 && !File.Exists(done); i++) await Task.Delay(500); // até 5 min
        try { File.Delete(done); } catch { }
    }
}
