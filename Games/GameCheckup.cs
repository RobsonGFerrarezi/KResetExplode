using System.ComponentModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace KResetExplode;

public enum CheckState { Ok, Info, Warn, Bad }

/// <summary>Um item do checkup: o que foi verificado, o resultado e (quando dá para resolver por aqui) a ação.</summary>
public sealed class CheckItem : INotifyPropertyChanged
{
    public string Title { get; init; } = "";
    private CheckState _state;
    private string _detail = "", _result = "";
    public CheckState State { get => _state; set { _state = value; On(nameof(State)); On(nameof(Badge)); } }
    public string Detail { get => _detail; set { _detail = value; On(nameof(Detail)); } }
    public string Result { get => _result; set { _result = value; On(nameof(Result)); } }
    public string Badge => State switch { CheckState.Ok => "OK", CheckState.Info => "INFO", CheckState.Warn => "ATENÇÃO", _ => "PROBLEMA" };

    /// <summary>Texto do botão; null = sem ação.</summary>
    public string? ActionText { get; init; }
    /// <summary>Ação que resolve aqui mesmo (devolve a mensagem para mostrar).</summary>
    public Func<Task<string>>? Fix { get; init; }
    /// <summary>Página do app para onde o botão leva (quando a solução está em outra aba).</summary>
    public string? GoToPage { get; init; }
    public bool HasAction => ActionText != null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void On(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>
/// Checkup para jogos: procura os problemas de configuração mais comuns que tiram FPS ou deixam o jogo travando,
/// explica cada um e resolve com um clique o que é seguro e reversível. Não mexe em segurança (VBS só é informado).
/// </summary>
public static class GameCheckup
{
    public static async Task<List<CheckItem>> RunAsync(IReadOnlyList<Tweak> tweaks, AppSettings settings, CancellationToken ct)
    {
        var items = new List<CheckItem>();
        void Safe(Func<IEnumerable<CheckItem>> f) { try { items.AddRange(f()); } catch { } }

        await Task.Run(() =>
        {
            Safe(Monitors);
            Safe(Ram);
            Safe(() => Tweaks(tweaks, settings));
            Safe(Vbs);
            Safe(DiskSpace);
            Safe(ShaderCache);
            Safe(StartupApps);
        }, ct);
        try { items.AddRange(await GpuDriverAsync(ct)); } catch (OperationCanceledException) { throw; } catch { }

        return items.OrderByDescending(i => i.State).ToList();
    }

    // ------------------------------------------------------------------ monitor

    private static IEnumerable<CheckItem> Monitors()
    {
        var mons = DisplayModes.ActiveMonitors();
        var gpus = GpuNames();
        bool hasDiscrete = gpus.Any(IsDiscrete);
        foreach (var m in mons)
        {
            string name = mons.Count > 1 ? $"Monitor {m.Device.Replace(@"\\.\DISPLAY", "")}" : "Monitor";
            if (m.Hz < m.MaxHz)
            {
                var mode = m;
                yield return new CheckItem
                {
                    Title = $"{name}: taxa de atualização abaixo do máximo",
                    State = CheckState.Bad,
                    Detail = $"Está em {m.Hz} Hz, mas aceita {m.MaxHz} Hz em {m.Width}×{m.Height}. Muito comum em monitores de 144 Hz que " +
                             "ficaram em 60 Hz: o jogo pode até rodar a 150 FPS, mas você só vê 60.",
                    ActionText = $"USAR {m.MaxHz} Hz",
                    Fix = () => Task.FromResult(DisplayModes.SetRefreshRate(mode, mode.MaxHz) is string err
                        ? "Não foi possível: " + err + "." : $"Pronto: {mode.MaxHz} Hz. Se a tela piscar ou ficar preta, espere 15 s ou volte em Configurações > Vídeo."),
                };
            }
            else yield return new CheckItem
            {
                Title = $"{name}: {m.Hz} Hz", State = CheckState.Ok,
                Detail = $"{m.Width}×{m.Height} na taxa máxima disponível ({m.MaxHz} Hz).",
            };

            if (m.Adapter.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                yield return new CheckItem
                {
                    Title = $"{name}: placa de vídeo sem driver", State = CheckState.Bad,
                    Detail = "O Windows está usando o vídeo básico da Microsoft: jogos ficam lentos ou nem abrem. Instale o driver da placa de vídeo.",
                    ActionText = "ABRIR DRIVERS", GoToPage = "PageDriver",
                };
            else if (hasDiscrete && !IsDiscrete(m.Adapter) && !HasBattery())
                yield return new CheckItem
                {
                    Title = $"{name}: ligado na placa-mãe, não na placa de vídeo", State = CheckState.Bad,
                    Detail = $"Ele está saindo por \"{m.Adapter}\" (vídeo integrado), mas o PC tem placa de vídeo dedicada. Ligue o cabo " +
                             "do monitor na saída da placa de vídeo (as entradas mais baixas, na horizontal, atrás do gabinete).",
                };
        }
    }

    private static List<string> GpuNames()
    {
        var list = new List<string>();
        using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
        foreach (ManagementObject o in s.Get()) using (o) if (o["Name"] is string n) list.Add(n);
        return list;
    }

    private static bool IsDiscrete(string gpu) =>
        gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || gpu.Contains("Arc", StringComparison.OrdinalIgnoreCase)
        || (gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(gpu, @"\b(RX|R9|R7|Pro W)\b"));

    // ------------------------------------------------------------------ RAM

    private static IEnumerable<CheckItem> Ram()
    {
        var mods = new List<(int Speed, int Configured, int Type)>();
        using (var s = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
            foreach (ManagementObject o in s.Get())
                using (o)
                    mods.Add((Convert.ToInt32(o["Speed"] ?? 0), Convert.ToInt32(o["ConfiguredClockSpeed"] ?? 0), Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0)));
        if (mods.Count == 0) yield break;

        int cfg = mods.Where(m => m.Configured > 0).Select(m => m.Configured).DefaultIfEmpty(0).Min();
        int rated = mods.Select(m => m.Speed).DefaultIfEmpty(0).Max();
        string type = mods[0].Type switch { 26 => "DDR4", 34 or 35 => "DDR5", 24 => "DDR3", _ => "RAM" };

        if (mods.Count == 1 && !HasBattery())
            yield return new CheckItem
            {
                Title = "RAM: um pente só (canal único)", State = CheckState.Warn,
                Detail = "Com um pente, a memória trabalha em canal único: jogos que dependem do processador podem perder 10–30% de FPS. " +
                         "Dois pentes iguais (nos slots indicados no manual da placa-mãe, geralmente o 2º e o 4º) ativam o canal duplo.",
            };

        if (cfg > 0 && rated > 0 && cfg < rated)
            yield return new CheckItem
            {
                Title = $"RAM abaixo da velocidade: {cfg} de {rated} MHz", State = CheckState.Warn,
                Detail = $"A memória ({type}) está rodando abaixo do que ela informa suportar. Ative o perfil XMP (Intel) ou EXPO/DOCP (AMD) " +
                         "na BIOS. Atenção: algumas placas-mãe (chipsets H310/B360/H410 e notebooks) limitam a velocidade; nesse caso é normal.",
            };
        else if ((type == "DDR4" && cfg is > 0 and <= 2400) || (type == "DDR5" && cfg is > 0 and <= 4800))
            yield return new CheckItem
            {
                Title = $"RAM a {cfg} MHz: confira o XMP/EXPO", State = CheckState.Info,
                Detail = $"{cfg} MHz é a velocidade padrão de {type}. Se o seu kit é mais rápido (veja a etiqueta do pente, ex.: 3200 ou 6000), " +
                         "ative o XMP/EXPO na BIOS para ganhar desempenho.",
            };
        else
            yield return new CheckItem
            {
                Title = $"RAM: {mods.Count} pente(s) a {cfg} MHz", State = CheckState.Ok,
                Detail = $"{type} na velocidade informada pelos pentes" + (mods.Count >= 2 ? ", com mais de um pente (canal duplo provável)." : "."),
            };
    }

    // ------------------------------------------------------------------ ajustes do Windows

    private static IEnumerable<CheckItem> Tweaks(IReadOnlyList<Tweak> tweaks, AppSettings settings)
    {
        Tweak? T(string id) => tweaks.FirstOrDefault(t => t.Id == id);

        CheckItem Toggle(Tweak t, string title, CheckState bad, string why, string action, bool wantOn = true) => new()
        {
            Title = title, State = bad, Detail = why, ActionText = action,
            Fix = () =>
            {
                t.Write(wantOn, settings);
                settings.Save();
                t.Refresh();
                return Task.FromResult(t.IsOn == wantOn
                    ? "Aplicado (dá para desfazer em Otimização > Restaurar originais)." + (t.NeedsRestart ? " Reinicie o PC para valer." : "")
                    : "O Windows não aceitou a mudança.");
            },
        };

        if (T("gamemode") is Tweak gm)
        {
            gm.Refresh();
            yield return gm.IsOn ? new CheckItem { Title = "Modo de Jogo ligado", State = CheckState.Ok, Detail = "O Windows dá prioridade ao jogo e não instala atualizações durante a partida." }
                : Toggle(gm, "Modo de Jogo desligado", CheckState.Warn, "Ligado, o Windows dá prioridade ao jogo e evita que o Windows Update reinicie no meio da partida.", "LIGAR");
        }
        if (T("gamedvr") is Tweak dvr)
        {
            dvr.Refresh();
            yield return dvr.IsOn ? new CheckItem { Title = "Gravação em segundo plano desligada", State = CheckState.Ok, Detail = "O Xbox Game Bar não está gravando o tempo todo." }
                : Toggle(dvr, "Gravação em segundo plano (Game DVR) ligada", CheckState.Warn,
                         "O Xbox Game Bar grava os últimos minutos de jogo o tempo todo, usando GPU e disco. Desligar recupera alguns FPS.", "DESLIGAR GRAVAÇÃO");
        }
        if (T("hags") is Tweak hags)
        {
            hags.Refresh();
            yield return hags.IsOn ? new CheckItem { Title = "Agendamento de GPU acelerado ligado", State = CheckState.Ok, Detail = "Necessário para o Frame Generation do DLSS 3/4." }
                : Toggle(hags, "Agendamento de GPU acelerado desligado", CheckState.Info,
                         "Necessário para o Frame Generation (DLSS 3/4) e pode reduzir a latência. Requer placa e driver compatíveis (GTX 1000 / RX 5000 ou mais nova) e reiniciar o PC.", "LIGAR");
        }
        if (T("power") is Tweak pw && !HasBattery())
        {
            pw.Refresh();
            var ult = T("ultimate");
            ult?.Refresh();
            if (ult?.IsOn == true)
                yield return new CheckItem { Title = "Plano de energia: Desempenho Máximo", State = CheckState.Ok, Detail = "O processador fica sempre pronto, sem economia de energia." };
            else yield return pw.IsOn ? new CheckItem { Title = "Plano de energia: Alto desempenho", State = CheckState.Ok, Detail = "O processador fica pronto para subir o clock na hora." }
                : Toggle(pw, "Plano de energia não é Alto desempenho", CheckState.Info,
                         "No PC de mesa, o Alto desempenho evita que o processador demore a subir o clock (melhora o 1% low em alguns jogos).", "LIGAR ALTO DESEMPENHO");
        }
    }

    // ------------------------------------------------------------------ segurança (só informa)

    private static IEnumerable<CheckItem> Vbs()
    {
        using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard");
        foreach (ManagementObject o in s.Get())
            using (o)
            {
                bool vbs = Convert.ToInt32(o["VirtualizationBasedSecurityStatus"] ?? 0) == 2;
                if (!vbs) yield break;
                bool hvci = o["SecurityServicesRunning"] is uint[] svc && svc.Contains(2u);
                yield return new CheckItem
                {
                    Title = "Segurança baseada em virtualização (VBS) ativa", State = CheckState.Info,
                    Detail = (hvci ? "A Integridade de Memória (HVCI) está ligada. " : "") +
                             "Ela protege o Windows contra malware no kernel e custa alguns FPS (geralmente 0–10%). O RESET EXPLODE não desliga " +
                             "por ser uma proteção de segurança; se quiser, o caminho é Segurança do Windows > Segurança do dispositivo > Isolamento de núcleo.",
                };
                yield break;
            }
    }

    // ------------------------------------------------------------------ disco, cache, inicialização

    private static IEnumerable<CheckItem> DiskSpace()
    {
        var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        double freeGb = c.AvailableFreeSpace / 1073741824.0, pct = (double)c.AvailableFreeSpace / c.TotalSize;
        bool low = freeGb < 15 || pct < 0.10;
        yield return new CheckItem
        {
            Title = low ? $"Pouco espaço livre no disco {c.Name.TrimEnd('\\')} ({freeGb:0} GB)" : $"Disco {c.Name.TrimEnd('\\')} com {freeGb:0} GB livres",
            State = low ? CheckState.Warn : CheckState.Ok,
            Detail = low ? "Com o disco do Windows quase cheio, jogos engasgam (arquivo de paginação e cache de shaders sem espaço) e atualizações falham. Use a Limpeza em Otimização."
                         : $"{pct:P0} do disco livre.",
            ActionText = low ? "ABRIR LIMPEZA" : null, GoToPage = low ? "PageOptimize" : null,
        };
    }

    private static IEnumerable<CheckItem> ShaderCache()
    {
        long bytes = Cleanup.Targets().Where(t => t.Id is "nvidia" or "amd" or "dx").Sum(t => t.SizeBytes);
        if (bytes < 5L << 30) yield break;
        yield return new CheckItem
        {
            Title = $"Cache de shaders grande: {bytes / 1073741824.0:0.0} GB", State = CheckState.Info,
            Detail = "O cache guarda shaders já compilados para os jogos abrirem sem travar. Ele cresce com o tempo e fica com restos de drivers antigos. " +
                     "Limpar libera espaço e resolve engasgos depois de atualizar o driver; na primeira partida o jogo recompila (pode travar um pouco).",
            ActionText = "ABRIR LIMPEZA", GoToPage = "PageOptimize",
        };
    }

    private static IEnumerable<CheckItem> StartupApps()
    {
        int n = StartupManager.List().Count(e => e.Enabled && !e.FileMissing);
        if (n < 12) yield break;
        yield return new CheckItem
        {
            Title = $"{n} programas abrem com o Windows", State = CheckState.Info,
            Detail = "Muitos programas na inicialização deixam o PC lento para ligar e ficam ocupando memória e processador em segundo plano.",
            ActionText = "VER INICIALIZAÇÃO", GoToPage = "PageStartup",
        };
    }

    // ------------------------------------------------------------------ driver de vídeo

    private static async Task<IEnumerable<CheckItem>> GpuDriverAsync(CancellationToken ct)
    {
        var list = new List<CheckItem>();
        foreach (var g in DriverManager.ReadGpus().Where(g => g.Vendor is "NVIDIA" or "AMD"))
        {
            if (g.Vendor == "NVIDIA") await DriverManager.CheckNvidiaAsync(g, false, ct);
            else await DriverManager.CheckAmdAsync(g, ct);
            if (g.Latest.Length == 0) continue;
            list.Add(g.UpdateAvailable
                ? new CheckItem
                {
                    Title = $"Driver de vídeo desatualizado ({g.GpuName})", State = CheckState.Warn,
                    Detail = $"Instalado: {g.Installed} · mais recente: {g.Latest}. Drivers novos trazem otimizações para jogos lançados recentemente.",
                    ActionText = "ABRIR DRIVERS", GoToPage = "PageDriver",
                }
                : new CheckItem { Title = $"Driver de vídeo em dia ({g.GpuName})", State = CheckState.Ok, Detail = g.Message });
        }
        return list;
    }

    // ------------------------------------------------------------------ utilidades

    /// <summary>Notebook (tem bateria): alguns itens não se aplicam (canal único soldado, monitor no vídeo integrado é o normal).</summary>
    internal static bool HasBattery() => GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
}
