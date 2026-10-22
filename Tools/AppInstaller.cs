using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;

namespace KResetExplode;

/// <summary>App do catálogo "Instalar apps" (pós-formatação), instalado pelo winget.</summary>
public sealed class InstallableApp : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string WingetId { get; init; } = "";
    /// <summary>Começo do nome em "Aplicativos instalados" (Painel de Controle), para saber se já está instalado.</summary>
    public string DisplayPrefix { get; init; } = "";
    public bool PreSelect { get; init; } = true;

    private bool _selected, _installed;
    private string _status = "";

    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public bool Installed
    {
        get => _installed;
        set { Set(ref _installed, value); OnChanged(nameof(CanSelect)); }
    }
    public bool CanSelect => !Installed;
    public string Status { get => _status; set => Set(ref _status, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(p);
    }
}

/// <summary>Programa instalado com atualização disponível no winget.</summary>
public sealed class AppUpdate : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public string Available { get; init; } = "";
    public string Source { get; init; } = "";
    public string Detail => $"{Version}  →  {Available}  ·  {Id}";

    private bool _selected;
    private string _status = "";
    public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Catálogo de apps essenciais e instalação silenciosa pelo winget (Gerenciador de Pacotes do Windows), que baixa
/// sempre a versão oficial mais recente do site do fabricante e confere o hash do instalador.
/// </summary>
public static class AppInstaller
{
    public static List<InstallableApp> Catalog() => new()
    {
        new() { Name = "Steam", Description = "Loja e biblioteca de jogos da Valve.", WingetId = "Valve.Steam", DisplayPrefix = "Steam" },
        new() { Name = "Google Chrome", Description = "Navegador do Google.", WingetId = "Google.Chrome", DisplayPrefix = "Google Chrome" },
        new() { Name = "Discord", Description = "Chat de voz e texto para jogar com os amigos.", WingetId = "Discord.Discord", DisplayPrefix = "Discord" },
        new() { Name = "Lightshot", Description = "Captura de tela rápida com a tecla Print Screen.", WingetId = "Skillbrains.Lightshot", DisplayPrefix = "Lightshot" },
        new() { Name = "7-Zip", Description = "Compactador gratuito (zip, rar, 7z). Abre e cria arquivos compactados.", WingetId = "7zip.7zip", DisplayPrefix = "7-Zip" },
        new() { Name = "WinRAR", Description = "Compactador clássico (pago após o teste). Escolha ele ou o 7-Zip.", WingetId = "RARLab.WinRAR", DisplayPrefix = "WinRAR", PreSelect = false },
        new() { Name = "VLC", Description = "Player de vídeo e música que abre qualquer formato.", WingetId = "VideoLAN.VLC", DisplayPrefix = "VLC media player" },
    };

    /// <summary>Winget presente e apontando para o pacote oficial da Microsoft (ver <see cref="SecureFiles.AcquireWinget"/>).</summary>
    public static bool WingetAvailable => SecureFiles.WingetAvailable;

    /// <summary>Nomes em "Aplicativos instalados" (máquina 64/32 bits e usuário atual).</summary>
    public static HashSet<string> InstalledNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32),
                                             (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (root == null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string n && !string.IsNullOrWhiteSpace(n)) names.Add(n.Trim());
                }
            }
            catch { }
        }
        return names;
    }

    public static void RefreshInstalled(IEnumerable<InstallableApp> apps)
    {
        var names = InstalledNames();
        foreach (var a in apps)
        {
            a.Installed = names.Any(n => n.StartsWith(a.DisplayPrefix, StringComparison.OrdinalIgnoreCase));
            if (a.Installed) a.Selected = false;
        }
    }

    /// <summary>Instala um app em silêncio. Retorna (ok, mensagem curta).</summary>
    public static Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct, bool upgrade = false) =>
        RunAsync(upgrade ? "upgrade" : "install", app.WingetId, null, ct);

    /// <summary>Atualiza um programa da lista do "winget upgrade" (na mesma origem: winget ou msstore).</summary>
    public static Task<(bool Ok, string Message)> UpgradeAsync(AppUpdate u, CancellationToken ct) =>
        RunAsync("upgrade", u.Id, u.Source, ct);

    private static async Task<(bool Ok, string Message)> RunAsync(string verb, string id, string? source, CancellationToken ct)
    {
        // O ID vem da saída do winget: só caracteres de ID de pacote, para não virar outro argumento.
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[\w.+-]+$")) return (false, "ID de pacote inválido.");
        string src = source is "winget" or "msstore" ? $" --source {source}" : "";
        using var winget = SecureFiles.AcquireWinget(out string? problem)
                           ?? throw new InvalidOperationException("winget indisponível: " + problem);
        var psi = new ProcessStartInfo(winget.Path,
            $"{verb} --id {id} -e{src} --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        string output = await outTask + await errTask;

        return p.ExitCode switch
        {
            0 => (true, "Instalado."),
            unchecked((int)0x8A15002B) or unchecked((int)0x8A150061) => (true, "Já estava instalado."),
            unchecked((int)0x8A150014) => (false, "Pacote não encontrado no winget."),
            unchecked((int)0x8A150008) => (false, "Falha no download: confira a internet e tente de novo."),
            _ => (false, $"Falhou (código 0x{p.ExitCode:X8}). {LastLine(output)}".Trim()),
        };
    }

    // ------------------------------------------------------------------ atualizações (winget upgrade)

    /// <summary>Programas instalados com versão mais nova no winget (Nome, ID, Versão, Disponível, Origem).</summary>
    public static async Task<List<AppUpdate>> ListUpdatesAsync(CancellationToken ct)
    {
        using var winget = SecureFiles.AcquireWinget(out string? problem)
                           ?? throw new InvalidOperationException("winget indisponível: " + problem);
        var psi = new ProcessStartInfo(winget.Path, "upgrade --include-unknown --accept-source-agreements --disable-interactivity")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        _ = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        return ParseUpgradeTable(await outTask);
    }

    /// <summary>
    /// Lê a tabela do "winget upgrade". As colunas são alinhadas por espaço: as posições vêm do cabeçalho (a linha antes da
    /// linha de traços), o que funciona em qualquer idioma. Para na primeira linha em branco (depois vem o resumo).
    /// </summary>
    public static List<AppUpdate> ParseUpgradeTable(string output)
    {
        var list = new List<AppUpdate>();
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l) // barras de progresso reescritas com \r
            .ToList();
        int dash = lines.FindIndex(l => l.Trim().Length > 20 && l.Trim().All(c => c == '-'));
        if (dash < 1) return list;
        string header = lines[dash - 1];
        var starts = new List<int>();
        for (int i = 0; i < header.Length; i++)
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        if (starts.Count < 4) return list;

        string Col(string l, int c)
        {
            int a = starts[c], b = c + 1 < starts.Count ? starts[c + 1] : l.Length;
            return a >= l.Length ? "" : l[a..Math.Min(b, l.Length)].Trim();
        }
        for (int i = dash + 1; i < lines.Count; i++)
        {
            string l = lines[i].TrimEnd();
            if (l.Trim().Length == 0) break;
            if (l.Length < starts[3]) continue;
            var u = new AppUpdate
            {
                Name = Col(l, 0), Id = Col(l, 1), Version = Col(l, 2), Available = Col(l, 3),
                Source = starts.Count > 4 ? Col(l, 4) : "",
            };
            if (u.Id.Length == 0 || u.Id.Contains(' ')) continue;
            // Versão "Unknown"/"< x": o winget não sabe a instalada e pode reinstalar sem parar; vem desmarcado.
            u.Selected = !(u.Version.StartsWith('<') || u.Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || u.Version.Equals("Desconhecido", StringComparison.OrdinalIgnoreCase));
            list.Add(u);
        }
        return list;
    }

    /// <summary>Última linha útil da saída do winget (sem as barras de progresso).</summary>
    private static string LastLine(string output) =>
        output.Split('\n', '\r').Select(l => l.Trim())
              .LastOrDefault(l => l.Length > 3 && !l.All(c => "-\\|/█▒ ".Contains(c)) && !l.Contains("KB /") && !l.Contains("MB /")) ?? "";
}
