using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KResetExplode;

/// <summary>
/// Ajuste do Windows para jogos: lê o estado atual, aplica e guarda o valor original (em AppSettings.TweakBackup)
/// na primeira vez que é alterado, para "Restaurar originais" desfazer tudo.
/// Só ajustes documentados e reversíveis; nada que reduza a segurança (Defender, VBS/HVCI, UAC, Windows Update).
/// </summary>
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public bool NeedsRestart { get; init; }
    internal Func<bool?> Read { get; init; } = () => null;
    internal Action<bool, AppSettings> Write { get; init; } = (_, _) => { };
    internal Action<AppSettings> Restore { get; init; } = _ => { };
    /// <summary>Motivo para o ajuste não se aplicar a este PC (ex.: só para PC de mesa); null = disponível.</summary>
    internal Func<string?> Unavailable { get; init; } = () => null;

    private bool _isOn;
    public bool IsOn { get => _isOn; set { _isOn = value; Changed(nameof(IsOn)); } }
    private string _status = "";
    public string Status { get => _status; set { _status = value; Changed(nameof(Status)); } }
    public string Badge => NeedsRestart ? "REQUER REINICIAR" : "";
    private bool _canChange = true;
    public bool CanChange { get => _canChange; private set { _canChange = value; Changed(nameof(CanChange)); } }

    public void Refresh()
    {
        IsOn = Read() ?? false;
        string? why = Unavailable();
        CanChange = why == null || IsOn; // se já estiver ligado, deixa desligar
        if (why != null && !IsOn) Status = why;
    }

    private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class WindowsTweaks
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    /// <summary>Modelo oculto do plano "Desempenho Máximo" (Ultimate Performance) do Windows 10 1803+ e 11.</summary>
    private const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    /// <summary>Cópia do modelo criada pelo RESET EXPLODE, com GUID fixo para reconhecer e apagar ao desfazer.</summary>
    private const string UltimateCopy = "5e7e0e1a-4d3b-4c1e-9a7e-2e5e7e0e1a01";

    /// <summary>Planos que já deixam a CPU no máximo (o perfil de jogo não troca para Alto desempenho se um deles estiver ativo).</summary>
    public static bool IsFastScheme(string? guid) =>
        guid != null && (guid.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase) || IsUltimate(guid));

    private static bool IsUltimate(string? guid) =>
        guid != null && (guid.Equals(UltimateCopy, StringComparison.OrdinalIgnoreCase) || guid.Equals(UltimateTemplate, StringComparison.OrdinalIgnoreCase));

    /// <param name="settings">Configurações do app (o ajuste "quiet" é um comportamento do app, guardado nelas).</param>
    public static List<Tweak> All(AppSettings settings) => new()
    {
        new Tweak
        {
            Id = "power", Title = "Plano de energia: Alto desempenho",
            Description = "Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia.",
            Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria.",
            Read = () => ActiveScheme()?.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase),
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("power")) s.TweakBackup["power"] = ActiveScheme();
                if (on)
                {
                    // Alguns PCs vêm sem o plano listado: duplicar o modelo padrão o recria com o mesmo GUID.
                    if (!PowerCfg("/list").Contains(HighPerformance, StringComparison.OrdinalIgnoreCase))
                        PowerCfg($"-duplicatescheme {HighPerformance} {HighPerformance}");
                    PowerCfg($"/setactive {HighPerformance}");
                }
                else if (s.TweakBackup["power"] is string prev && IsGuid(prev) && !prev.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase))
                    PowerCfg($"/setactive {prev}");
                else PowerCfg($"/setactive {Balanced}"); // Equilibrado
            },
            Restore = s => { if (s.TweakBackup.GetValueOrDefault("power") is string prev && IsGuid(prev)) PowerCfg($"/setactive {prev}"); },
        },
        // Mexe no mesmo item do Windows que o "power" (o plano ativo): os dois dividem o backup "power", para "Restaurar originais"
        // voltar ao plano de antes de qualquer um deles. Ligar um desliga o outro.
        new Tweak
        {
            Id = "ultimate", Title = "Plano de energia: Desempenho Máximo",
            Description = "Plano oculto do Windows que tira também as pequenas economias que o Alto desempenho mantém (núcleos estacionados, latência para sair do repouso).",
            Effect = "Só para PC de mesa: ganho pequeno sobre o Alto desempenho, com mais consumo parado. Substitui o Alto desempenho enquanto estiver ligado.",
            Unavailable = () => GameCheckup.HasBattery() ? "Só para PC de mesa: em notebook gasta bateria e esquenta sem ganho real." : null,
            Read = () => IsUltimate(ActiveScheme()),
            Write = (on, s) =>
            {
                if (on && GameCheckup.HasBattery()) throw new InvalidOperationException("este ajuste é só para PC de mesa.");
                if (!s.TweakBackup.ContainsKey("power")) s.TweakBackup["power"] = ActiveScheme();
                if (on)
                {
                    if (!PowerCfg("/list").Contains(UltimateCopy, StringComparison.OrdinalIgnoreCase))
                        PowerCfg($"-duplicatescheme {UltimateTemplate} {UltimateCopy}");
                    PowerCfg($"/setactive {UltimateCopy}");
                }
                else
                {
                    PowerCfg(s.TweakBackup["power"] is string prev && IsGuid(prev) && !IsUltimate(prev) ? $"/setactive {prev}" : $"/setactive {Balanced}");
                    DeleteUltimateCopy();
                }
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("power") is string prev && IsGuid(prev)) PowerCfg($"/setactive {prev}");
                DeleteUltimateCopy();
            },
        },
        RegTweak("gamemode", "Modo de Jogo",
            "Dá prioridade ao jogo e evita que o Windows Update instale ou reinicie durante a partida.",
            "Recomendado ligado (é o padrão do Windows 11).",
            Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on: 1, off: 0, absentMeansOn: true),
        new Tweak
        {
            Id = "gamedvr", Title = "Desativar gravação em segundo plano (Game DVR)",
            Description = "Desliga a captura contínua do Xbox Game Bar, que grava os últimos minutos de jogo o tempo todo.",
            Effect = "Recupera alguns FPS e reduz uso de GPU/disco. Você perde o \"gravar o que aconteceu\" do Game Bar.",
            Read = () => (ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") ?? 1) == 0 &&
                         (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") ?? 1) == 0,
            Write = (on, s) =>
            {
                SetDword(s, "gamedvr.a", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", on ? 0 : 1);
                SetDword(s, "gamedvr.b", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
            },
            Restore = s =>
            {
                RestoreDword(s, "gamedvr.a", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
                RestoreDword(s, "gamedvr.b", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            },
        },
        RegTweak("hags", "Agendamento de GPU acelerado por hardware",
            "A própria placa de vídeo gerencia sua fila de trabalho, em vez da CPU.",
            "Necessário para o Frame Generation do DLSS 3/4. Pode reduzir latência. Requer GPU e driver compatíveis.",
            Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on: 2, off: 1,
            absentMeansOn: false, needsRestart: true),
        new Tweak
        {
            Id = "windowed", Title = "Otimizações para jogos em janela",
            Description = "Faz jogos DirectX 10/11 em janela ou \"sem bordas\" usarem o modelo de apresentação moderno (flip).",
            Effect = "Menor latência e suporte a VRR/G-Sync em janela. Recurso do Windows 11.",
            Read = () => DxSettings().GetValueOrDefault("SwapEffectUpgradeEnable") == "1",
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("windowed"))
                    s.TweakBackup["windowed"] = Registry.CurrentUser.OpenSubKey(DxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "<ausente>";
                var d = DxSettings();
                d["SwapEffectUpgradeEnable"] = on ? "1" : "0";
                using var k = Registry.CurrentUser.CreateSubKey(DxPath);
                k.SetValue("DirectXUserGlobalSettings", string.Concat(d.Select(kv => $"{kv.Key}={kv.Value};")));
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("windowed") is not string prev) return;
                using var k = Registry.CurrentUser.CreateSubKey(DxPath);
                if (prev == "<ausente>") k.DeleteValue("DirectXUserGlobalSettings", false);
                else k.SetValue("DirectXUserGlobalSettings", prev);
            },
        },
        new Tweak
        {
            Id = "mouse", Title = "Desligar aceleração do mouse",
            Description = "Desmarca \"Aprimorar precisão do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.",
            Effect = "Não muda FPS, mas deixa a mira consistente em jogos de tiro.",
            Read = () => ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") == "0",
            Write = (on, s) =>
            {
                foreach (var n in new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" })
                    if (!s.TweakBackup.ContainsKey($"mouse.{n}")) s.TweakBackup[$"mouse.{n}"] = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", n);
                var v = on ? new[] { "0", "0", "0" } : new[] { "1", "6", "10" }; // padrão do Windows
                ApplyMouse(v);
            },
            Restore = s =>
            {
                if (!s.TweakBackup.ContainsKey("mouse.MouseSpeed")) return;
                var prev = new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }
                    .Select(n => s.TweakBackup.GetValueOrDefault($"mouse.{n}") ?? "0").ToArray();
                if (prev.All(v => int.TryParse(v, out _))) ApplyMouse(prev);
            },
        },
        new Tweak
        {
            Id = "visual", Title = "Efeitos visuais: modo desempenho",
            Description = "Desliga animações de janelas, menus e barra de tarefas, esmaecimentos, sombras e a Espiada da área de trabalho. Mantém o ClearType e as miniaturas.",
            Effect = "O Windows fica mais ágil em PCs modestos e sobra um pouco de GPU para o jogo. A barra de tarefas pode só mudar depois de sair e entrar na conta.",
            Read = () => SpiRead() is { } v && v.All(x => x == 0) && ReadDword(Registry.CurrentUser, ExplorerAdvanced, "TaskbarAnimations") == 0,
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("visual.spi") && SpiRead() is { } cur) s.TweakBackup["visual.spi"] = string.Join(",", cur);
                int v = on ? 0 : 1; // desligar = padrão do Windows (tudo ligado)
                SpiWrite(Enumerable.Repeat(v, VisualSpi.Length + 1).ToArray());
                foreach (var (key, path, name) in VisualReg)
                    SetDword(s, key, Registry.CurrentUser, path, name, v);
                // 3 = "Personalizado" em Propriedades do Sistema › Efeitos visuais; 0 = "Deixar o Windows escolher".
                SetDword(s, "visual.fx", Registry.CurrentUser, VisualFxPath, "VisualFXSetting", on ? 3 : 0);
                BroadcastSettingChange();
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("visual.spi") is string raw)
                {
                    var parts = raw.Split(',');
                    if (parts.Length == VisualSpi.Length + 1 && parts.All(p => p is "0" or "1")) SpiWrite(parts.Select(int.Parse).ToArray());
                }
                foreach (var (key, path, name) in VisualReg) RestoreDword(s, key, Registry.CurrentUser, path, name);
                RestoreDword(s, "visual.fx", Registry.CurrentUser, VisualFxPath, "VisualFXSetting");
                BroadcastSettingChange();
            },
        },
        new Tweak
        {
            Id = "quiet", Title = "Silenciar notificações durante o jogo",
            Description = "Enquanto um jogo (ou outro programa) está em tela cheia, ou um jogo aberto pela Biblioteca está rodando, os avisos do Windows e dos apps não aparecem.",
            Effect = "Nada de aviso do Discord ou do Windows por cima do jogo. Funciona com o RESET EXPLODE aberto (pode ficar minimizado); ao fechar o jogo tudo volta.",
            Read = () => settings.MuteNotificationsInGame,
            Write = (on, s) =>
            {
                s.TweakBackup.TryAdd("quiet.flag", s.MuteNotificationsInGame ? "1" : "0");
                s.MuteNotificationsInGame = on;
                if (!on) UpdateGameQuiet(s, gameRunning: false);
            },
            Restore = s =>
            {
                s.MuteNotificationsInGame = s.TweakBackup.GetValueOrDefault("quiet.flag") == "1";
                UpdateGameQuiet(s, gameRunning: false);
            },
        },
    };

    /// <summary>Desfaz tudo que foi alterado pelo app (valores guardados antes da primeira alteração).</summary>
    public static void RestoreAll(IEnumerable<Tweak> tweaks, AppSettings s)
    {
        foreach (var t in tweaks)
            try { t.Restore(s); } catch { }
        s.TweakBackup.Clear();
        s.Save();
    }

    // ------------------------------------------------------------------ notificações durante o jogo

    private const string QuietKey = "quiet.active"; // presente = notificações silenciadas agora pelo app (valor = backup da política)
    private const string QuietPath = @"Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications";

    /// <summary>
    /// Chamado periodicamente: silencia as notificações quando há jogo rodando e devolve como estava quando ele fecha.
    /// Usa a política documentada "Desativar notificações do sistema" (NoToastApplicationNotification), que vale sem reiniciar.
    /// Se o app fechar no meio do jogo, a próxima abertura desfaz, porque o backup fica salvo junto com as configurações.
    /// </summary>
    public static void UpdateGameQuiet(AppSettings s, bool gameRunning)
    {
        bool want = s.MuteNotificationsInGame && gameRunning;
        bool muted = s.TweakBackup.ContainsKey(QuietKey);
        if (want == muted) return;
        if (want) SetDword(s, QuietKey, Registry.CurrentUser, QuietPath, "NoToastApplicationNotification", 1);
        else
        {
            RestoreDword(s, QuietKey, Registry.CurrentUser, QuietPath, "NoToastApplicationNotification");
            s.TweakBackup.Remove(QuietKey);
        }
        s.Save();
    }

    /// <summary>Jogo ou outro programa em tela cheia (inclusive "sem bordas"), pelo mesmo estado que o Windows usa para segurar avisos.</summary>
    public static bool FullScreenAppRunning()
    {
        try { return SHQueryUserNotificationState(out int st) == 0 && st is 2 or 3; } // QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN
        catch { return false; }
    }

    // ------------------------------------------------------------------ efeitos visuais

    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string VisualFxPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

    /// <summary>Pares SPI_GET/SPI_SET de efeitos liga/desliga: menus, caixas e listas, esmaecimentos, dicas, sombras e animação de controles.</summary>
    private static readonly (uint Get, uint Set)[] VisualSpi =
    {
        (0x1002, 0x1003), (0x1004, 0x1005), (0x1006, 0x1007), (0x1012, 0x1013), (0x1014, 0x1015),
        (0x1016, 0x1017), (0x1018, 0x1019), (0x101A, 0x101B), (0x1024, 0x1025), (0x1042, 0x1043),
    };

    private static readonly (string Key, string Path, string Name)[] VisualReg =
    {
        ("visual.taskbar", ExplorerAdvanced, "TaskbarAnimations"),
        ("visual.lvalpha", ExplorerAdvanced, "ListviewAlphaSelect"),
        ("visual.lvshadow", ExplorerAdvanced, "ListviewShadow"),
        ("visual.peek", @"Software\Microsoft\Windows\DWM", "EnableAeroPeek"),
    };

    /// <summary>[animação ao minimizar/maximizar, ...VisualSpi] como 0/1; null se o Windows não responder.</summary>
    private static int[]? SpiRead()
    {
        var anim = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
        if (!SpiAnimation(0x0048 /* SPI_GETANIMATION */, anim.cbSize, ref anim, 0)) return null;
        var list = new List<int> { anim.iMinAnimate != 0 ? 1 : 0 };
        foreach (var (get, _) in VisualSpi)
        {
            if (!SpiGetBool(get, 0, out int v, 0)) return null;
            list.Add(v != 0 ? 1 : 0);
        }
        return list.ToArray();
    }

    /// <summary>Aplica na hora pelo SystemParametersInfo, que também grava no perfil do usuário.</summary>
    private static void SpiWrite(int[] v)
    {
        const uint flags = 0x01 | 0x02; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
        var anim = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = v[0] };
        SpiAnimation(0x0049 /* SPI_SETANIMATION */, anim.cbSize, ref anim, flags);
        for (int i = 0; i < VisualSpi.Length; i++)
            SpiSetBool(VisualSpi[i].Set, 0, (IntPtr)v[i + 1], flags);
    }

    private static void BroadcastSettingChange()
    {
        foreach (var area in new[] { "TraySettings", "WindowMetrics" })
            SendMessageTimeout((IntPtr)0xFFFF /* HWND_BROADCAST */, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, area, 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

    // ------------------------------------------------------------------ registro

    private static Tweak RegTweak(string id, string title, string desc, string effect, RegistryKey hive, string path, string name,
                                  int on, int off, bool absentMeansOn, bool needsRestart = false) => new()
    {
        Id = id, Title = title, Description = desc, Effect = effect, NeedsRestart = needsRestart,
        Read = () => (ReadDword(hive, path, name) is int v) ? v == on : absentMeansOn,
        Write = (enable, s) => SetDword(s, id, hive, path, name, enable ? on : off),
        Restore = s => RestoreDword(s, id, hive, path, name),
    };

    private static int? ReadDword(RegistryKey hive, string path, string name)
    {
        using var k = hive.OpenSubKey(path);
        return k?.GetValue(name) is int v ? v : null;
    }

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        using var k = hive.OpenSubKey(path);
        return k?.GetValue(name)?.ToString();
    }

    private static void SetDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name, int value)
    {
        if (!s.TweakBackup.ContainsKey(backupKey))
            s.TweakBackup[backupKey] = ReadDword(hive, path, name)?.ToString() ?? "<ausente>";
        using var k = hive.CreateSubKey(path);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void RestoreDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
    {
        if (s.TweakBackup.GetValueOrDefault(backupKey) is not string prev) return;
        using var k = hive.CreateSubKey(path);
        if (prev == "<ausente>") k.DeleteValue(name, false);
        else if (int.TryParse(prev, out int v)) k.SetValue(name, v, RegistryValueKind.DWord);
    }

    private const string DxPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>"Chave=Valor;Chave=Valor;" → dicionário (o valor pode ter outras opções do usuário, que preservamos).</summary>
    private static Dictionary<string, string> DxSettings()
    {
        var d = new Dictionary<string, string>();
        string raw = Registry.CurrentUser.OpenSubKey(DxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "";
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) d[kv[0].Trim()] = kv[1].Trim();
        }
        return d;
    }

    private static void ApplyMouse(string[] v)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
        {
            k.SetValue("MouseSpeed", v[0]);
            k.SetValue("MouseThreshold1", v[1]);
            k.SetValue("MouseThreshold2", v[2]);
        }
        // Aplica na hora, sem precisar sair da conta.
        var p = v.Select(int.Parse).ToArray();
        SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, p, 0x01 | 0x02);
    }

    // ------------------------------------------------------------------ plano de energia

    /// <summary>GUID de plano de energia (o valor vem do arquivo de configurações e vira argumento do powercfg).</summary>
    private static bool IsGuid(string s) => Guid.TryParseExact(s, "D", out _);

    private static string? ActiveScheme()
    {
        var m = System.Text.RegularExpressions.Regex.Match(PowerCfg("/getactivescheme"), @"[0-9a-fA-F\-]{36}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>Apaga a cópia do Desempenho Máximo criada pelo app (o Windows não apaga o plano ativo, então só depois de trocar).</summary>
    private static void DeleteUltimateCopy()
    {
        if (!IsUltimate(ActiveScheme()) && PowerCfg("/list").Contains(UltimateCopy, StringComparison.OrdinalIgnoreCase))
            PowerCfg($"/delete {UltimateCopy}");
    }

    private static string PowerCfg(string args)
    {
        var psi = new ProcessStartInfo(SecureFiles.SystemTool("powercfg.exe"), args)
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return output;
    }

    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, int[] data, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiGetBool(uint action, uint param, out int value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiSetBool(uint action, uint param, IntPtr value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiAnimation(uint action, uint param, ref ANIMATIONINFO value, uint winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
