using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KResetExplode;

/// <summary>Dispositivo do Gerenciador de Dispositivos com o driver em uso (se houver).</summary>
public sealed class PnpDevice
{
    public string DeviceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string[] HardwareIds { get; init; } = Array.Empty<string>();
    public string[] CompatibleIds { get; init; } = Array.Empty<string>();
    public uint ErrorCode { get; init; }
    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
}

/// <summary>Dispositivo com problema (código do Gerenciador de Dispositivos), com orientação e o driver do Windows Update, se houver.</summary>
public sealed class ProblemDevice
{
    public string DeviceId { get; init; } = "";
    public string Name { get; init; } = "";
    public uint Code { get; init; }
    public string Problem { get; init; } = "";
    public string Advice { get; set; } = "";
    public bool CanReinstall { get; init; } = true;
    public string Detail => $"Código {Code} · {Problem}";
}

/// <summary>
/// Atualizador de drivers: compara o que o Windows Update oferece com o driver instalado em cada dispositivo (o Windows Update
/// também lista versões MAIS ANTIGAS que a instalada; instalar tudo poderia rebaixar drivers), lista dispositivos com problema,
/// restaura backups e limpa instaladores baixados.
/// </summary>
public static class DriverUpdates
{
    // ------------------------------------------------------------------ dispositivos

    private static string? Str(ManagementBaseObject o, string p) => o[p]?.ToString()?.Trim();

    /// <summary>Todos os dispositivos presentes, com IDs de hardware e o driver em uso.</summary>
    public static List<PnpDevice> Devices()
    {
        var byId = new Dictionary<string, PnpDevice>(StringComparer.OrdinalIgnoreCase);
        using (var s = new ManagementObjectSearcher("SELECT DeviceID, Name, HardwareID, CompatibleID, ConfigManagerErrorCode FROM Win32_PnPEntity"))
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    string id = Str(o, "DeviceID") ?? "";
                    if (id.Length == 0) continue;
                    byId[id] = new PnpDevice
                    {
                        DeviceId = id,
                        Name = Str(o, "Name") ?? "",
                        HardwareIds = o["HardwareID"] as string[] ?? Array.Empty<string>(),
                        CompatibleIds = o["CompatibleID"] as string[] ?? Array.Empty<string>(),
                        ErrorCode = o["ConfigManagerErrorCode"] is uint c ? c : 0,
                    };
                }
        using (var s = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion, DriverDate FROM Win32_PnPSignedDriver"))
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    if (!byId.TryGetValue(Str(o, "DeviceID") ?? "", out var d)) continue;
                    d.DriverVersion = Str(o, "DriverVersion") ?? "";
                    try { if (Str(o, "DriverDate") is string dd) d.DriverDate = ManagementDateTimeConverter.ToDateTime(dd); } catch { }
                }
        return byId.Values.ToList();
    }

    /// <summary>"Intel Corporation - System - 10/8/2018 12:00:00 AM - 30.100.1841.2" → "30.100.1841.2".</summary>
    public static string VersionFromTitle(string title)
    {
        var m = Regex.Match(title, @"(\d+(?:\.\d+){1,3})\s*$");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static Version? ParseVersion(string s) =>
        Version.TryParse(Regex.Match(s ?? "", @"\d+(?:\.\d+){1,3}").Value, out var v) ? v : null;

    /// <summary>Acha o dispositivo de cada driver oferecido e classifica: atualização, mesma versão, mais antigo ou dispositivo sem driver.</summary>
    public static void Compare(List<WuDriver> offers, List<PnpDevice> devices)
    {
        foreach (var w in offers)
        {
            string hw = w.HardwareId;
            var dev = hw.Length == 0 ? null
                : devices.FirstOrDefault(d => d.HardwareIds.Any(h => h.Equals(hw, StringComparison.OrdinalIgnoreCase)))
                  ?? devices.FirstOrDefault(d => d.CompatibleIds.Any(h => h.Equals(hw, StringComparison.OrdinalIgnoreCase)));
            if (dev == null) { w.Verdict = WuVerdict.Unknown; w.Selected = false; continue; }

            w.Device = dev.Name;
            w.InstalledVersion = dev.DriverVersion;
            w.InstalledDate = dev.DriverDate;
            if (dev.DriverVersion.Length == 0 || dev.ErrorCode == 28) w.Verdict = WuVerdict.NoDriver;
            else if (ParseVersion(w.OfferedVersion) is Version ov && ParseVersion(dev.DriverVersion) is Version iv)
                w.Verdict = ov > iv ? WuVerdict.Newer : ov == iv ? WuVerdict.Same : WuVerdict.Older;
            // Sem versão para comparar, a data só vale se for real (a Intel grava 1968 nos .inf de chipset de propósito).
            else if (w.OfferedDate is DateTime od && dev.DriverDate is DateTime idt && idt.Year > 1990 && od.Year > 1990)
                w.Verdict = od.Date > idt.Date ? WuVerdict.Newer : od.Date == idt.Date ? WuVerdict.Same : WuVerdict.Older;
            else w.Verdict = WuVerdict.Unknown;
            // Só vem marcado o que é claramente melhor: versão nova ou dispositivo que hoje está sem driver.
            w.Selected = w.Verdict is WuVerdict.Newer or WuVerdict.NoDriver;
        }
        // Ordem: atualizações reais, sem driver, a comparar, iguais, mais antigas.
        static int Rank(WuVerdict v) => v switch { WuVerdict.Newer => 0, WuVerdict.NoDriver => 1, WuVerdict.Unknown => 2, WuVerdict.Same => 3, _ => 4 };
        offers.Sort((a, b) => Rank(a.Verdict).CompareTo(Rank(b.Verdict)) is int c && c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    // ------------------------------------------------------------------ dispositivos com problema

    private static (string Problem, string Advice, bool Reinstall) Explain(uint code) => code switch
    {
        1 or 18 or 19 or 21 or 31 or 39 => ("driver com defeito ou instalado pela metade",
            "Clique em REINSTALAR. Se voltar, procure no Windows Update (acima) ou no site do fabricante.", true),
        3 => ("o driver falhou ou falta memória", "Reinicie o PC. Se continuar, REINSTALAR ou atualize o driver.", true),
        10 => ("o dispositivo não inicia", "REINSTALAR costuma resolver. Se não, atualize o driver (Windows Update ou fabricante).", true),
        12 => ("conflito de recursos", "Desconecte outros dispositivos ou atualize a BIOS; REINSTALAR pode ajudar.", true),
        14 => ("precisa reiniciar o PC", "Reinicie o PC para o dispositivo funcionar.", false),
        22 => ("desativado", "Foi desativado no Gerenciador de Dispositivos. REINSTALAR ativa de novo.", true),
        24 => ("não encontrado ou com driver incompleto",
            "Se você não usa esse dispositivo (ex.: mouse/teclado PS/2 que o Windows lista mesmo sem nada ligado), é normal e pode ignorar. " +
            "Se usa, confira a conexão e clique em REINSTALAR.", true),
        28 => ("sem driver", "Procure no Windows Update (acima). Se não aparecer, use o site do fabricante do PC ou da placa-mãe.", true),
        32 => ("serviço do driver desativado", "Reinstale o driver do fabricante.", true),
        37 or 52 => ("o Windows recusou o driver (assinatura ou falha ao carregar)", "Instale o driver oficial mais recente do fabricante.", true),
        43 => ("o Windows parou o dispositivo após um erro", "Desconecte e conecte de novo, troque de porta/cabo e clique em REINSTALAR.", true),
        48 => ("driver bloqueado por incompatibilidade", "Instale uma versão mais nova do driver no site do fabricante.", false),
        _ => ($"problema no dispositivo", "Clique em REINSTALAR; se não resolver, atualize o driver.", true),
    };

    /// <summary>Dispositivos com problema agora (código 45, "desconectado", fica de fora), com o driver do Windows Update quando houver.</summary>
    public static List<ProblemDevice> Problems(List<PnpDevice> devices, IReadOnlyList<WuDriver>? offers)
    {
        var list = new List<ProblemDevice>();
        foreach (var d in devices.Where(d => d.ErrorCode != 0 && d.ErrorCode != 45))
        {
            var (problem, advice, reinstall) = Explain(d.ErrorCode);
            var offer = offers?.FirstOrDefault(w => w.HardwareId.Length > 0 &&
                d.HardwareIds.Concat(d.CompatibleIds).Any(h => h.Equals(w.HardwareId, StringComparison.OrdinalIgnoreCase)));
            if (offer != null) advice = $"O Windows Update tem um driver para ele ({offer.OfferedVersion}): marque na lista acima e instale.";
            list.Add(new ProblemDevice
            {
                DeviceId = d.DeviceId, Code = d.ErrorCode, Problem = problem, Advice = advice, CanReinstall = reinstall,
                Name = d.Name.Length > 0 ? d.Name : d.HardwareIds.FirstOrDefault() ?? d.DeviceId,
            });
        }
        return list.OrderBy(p => p.Name).ToList();
    }

    /// <summary>Remove o dispositivo e pede ao Windows para detectar de novo (ele reinstala com o melhor driver que tiver).</summary>
    public static async Task<string> ReinstallAsync(ProblemDevice d)
    {
        var (c, o) = await RunAsync("pnputil.exe", $"/remove-device \"{d.DeviceId}\"");
        await Task.Delay(800);
        await RunAsync("pnputil.exe", "/scan-devices");
        return c == 0 ? $"{d.Name}: reinstalado, o Windows detectou o dispositivo de novo." : $"{d.Name}: o Windows não removeu ({LastLine(o)}).";
    }

    // ------------------------------------------------------------------ backup

    public static string DefaultBackupRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KResetExplode-Drivers");

    /// <summary>Instala todos os drivers de uma pasta de backup (pnputil só aceita pacotes com assinatura válida).</summary>
    public static async Task<string> RestoreBackupAsync(string folder, Action<string> progress)
    {
        if (!Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Any())
            return "Nenhum driver (.inf) nesta pasta. Escolha a pasta criada pelo FAZER BACKUP (KResetExplode-Drivers\\data-hora).";
        progress("Instalando os drivers do backup… (pode levar alguns minutos)");
        var (c, o) = await RunAsync("pnputil.exe", $"/add-driver \"{Path.Combine(folder, "*.inf")}\" /subdirs /install");
        var added = Regex.Matches(o, @"(?im)^\s*(Driver package added successfully|Pacote de driver adicionado com êxito)").Count;
        string tail = LastLine(o);
        return c switch
        {
            0 => $"Backup restaurado. {tail}",
            3010 => $"Backup restaurado. Reinicie o PC para finalizar. {tail}",
            _ => $"Terminou com avisos (código {c}): {tail}" + (added > 0 ? $" · {added} pacote(s) adicionados." : ""),
        };
    }

    // ------------------------------------------------------------------ instaladores baixados

    public static long DownloadsSize()
    {
        try
        {
            string dir = Path.Combine(SecureFiles.ProtectedRoot, "Downloads", "Drivers");
            return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch { return 0; }
    }

    /// <summary>Apaga os instaladores de driver baixados (fica tudo na pasta protegida; os que estiverem em uso são pulados).</summary>
    public static (int Deleted, long Bytes) CleanDownloads()
    {
        string dir = Path.Combine(SecureFiles.ProtectedRoot, "Downloads", "Drivers");
        int n = 0; long bytes = 0;
        if (!Directory.Exists(dir)) return (0, 0);
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            try { long len = f.Length; f.Delete(); n++; bytes += len; } catch { }
        }
        return (n, bytes);
    }

    // ------------------------------------------------------------------ AMD

    /// <summary>
    /// Versão do AMD Software (Adrenalin, ex.: "24.3.1"), gravada pelo instalador na chave do driver de vídeo. O Windows só mostra a
    /// versão interna (32.0.x), que não dá para comparar com a do site da AMD.
    /// </summary>
    public static string? AmdSoftwareVersion()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var sub in cls?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                try
                {
                    using var k = cls!.OpenSubKey(sub);
                    if (k?.GetValue("RadeonSoftwareVersion") is string v && v.Length > 0) return v.Trim();
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------------ utilidades

    private static async Task<(int Code, string Output)> RunAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(SecureFiles.SystemTool(exe), args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {exe}.");
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await o + await e);
    }

    private static string LastLine(string s) =>
        s.Split('\n', '\r').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
}
