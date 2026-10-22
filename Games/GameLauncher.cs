using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KResetExplode;

/// <summary>
/// Aplica o perfil de um jogo e o inicia. Tudo é reversível: o plano de energia volta ao anterior, os programas auxiliares fecham e
/// as preferências por jogo (GPU, tela cheia) ficam no registro do usuário só para aquele .exe.
/// </summary>
public static class GameLauncher
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    // ---------------- Argumentos de gráficos ----------------

    /// <summary>Monta a linha de argumentos do perfil (modo de janela, resolução, taxa e extras) no estilo da engine escolhida.</summary>
    public static string BuildArgs(GameProfile p)
    {
        var a = new List<string>();
        int w = 0, h = 0;
        var m = Regex.Match(p.Res ?? "", @"^(\d+)x(\d+)$");
        if (m.Success) { w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value); }

        switch (p.Style)
        {
            case "unity":
                if (p.WinMode == "full") a.Add("-screen-fullscreen 1");
                else if (p.WinMode == "window") a.Add("-screen-fullscreen 0");
                else if (p.WinMode == "borderless") a.Add("-popupwindow");
                if (w > 0) a.Add($"-screen-width {w} -screen-height {h}");
                break;
            case "unreal":
                if (p.WinMode == "full") a.Add("-fullscreen");
                else if (p.WinMode is "window" or "borderless") a.Add("-windowed");
                if (w > 0) a.Add($"-ResX={w} -ResY={h}");
                break;
            default: // source / CS2
                if (p.WinMode == "full") a.Add("-fullscreen");
                else if (p.WinMode == "window") a.Add("-windowed");
                else if (p.WinMode == "borderless") a.Add("-windowed -noborder");
                if (w > 0) a.Add($"-w {w} -h {h}");
                if (p.Hz > 0) a.Add($"-freq {p.Hz}");
                break;
        }
        if (!string.IsNullOrWhiteSpace(p.Extra)) a.Add(p.Extra.Trim());
        return string.Join(" ", a);
    }

    // ---------------- Executável do jogo ----------------

    private static readonly string[] IgnoredExe =
        { "unins", "crash", "redist", "vc_", "vcredist", "dxsetup", "dotnet", "easyanticheat", "beservice", "cef", "setup", "uninstall", "launcher_helper", "notification" };

    /// <summary>Procura o .exe principal na pasta do jogo (nome parecido com o do jogo; senão, o maior arquivo). Pode errar: o usuário pode escolher outro.</summary>
    public static string? FindMainExe(string dir, string gameName)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            var exes = new DirectoryInfo(dir).EnumerateFiles("*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                .Where(f => !IgnoredExe.Any(i => f.Name.Contains(i, StringComparison.OrdinalIgnoreCase))).ToList();
            if (exes.Count == 0) return null;
            string norm = Regex.Replace(gameName.ToLowerInvariant(), "[^a-z0-9]", "");
            var match = exes.Where(f => { string n = Regex.Replace(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant(), "[^a-z0-9]", ""); return n.Length >= 3 && (norm.Contains(n) || n.Contains(norm)); })
                            .OrderByDescending(f => f.Length).FirstOrDefault();
            return (match ?? exes.OrderByDescending(f => f.Length).First()).FullName;
        }
        catch { return null; }
    }

    // ---------------- Preferências por jogo no Windows ----------------

    /// <summary>"Configurações gráficas" do Windows: alto desempenho (GPU dedicada) para este .exe. Remove a preferência quando desligado.</summary>
    public static void SetGpuPreference(string exe, bool high)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        if (high) k.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
        else if (k.GetValue(exe) is string v && v.StartsWith("GpuPreference=2", StringComparison.Ordinal)) k.DeleteValue(exe, false);
    }

    /// <summary>Desativa as "otimizações de tela cheia" do Windows para este .exe (aba Compatibilidade).</summary>
    public static void SetDisableFso(string exe, bool disable)
    {
        const string disableFso = "DISABLEDXMAXIMIZEDWINDOWEDMODE";
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
        var tokens = ((k.GetValue(exe) as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t != "~").ToList();
        if (disable && !tokens.Contains(disableFso)) tokens.Add(disableFso);
        if (!disable) tokens.Remove(disableFso);
        if (tokens.Count == 0) k.DeleteValue(exe, false);
        else k.SetValue(exe, "~ " + string.Join(" ", tokens), RegistryValueKind.String);
    }

    // ---------------- Plano de energia ----------------

    private static string PowerCfg(string args)
    {
        var psi = new ProcessStartInfo(SecureFiles.SystemTool("powercfg.exe"), args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return o;
    }

    private static string? ActiveScheme()
    {
        var m = Regex.Match(PowerCfg("/getactivescheme"), @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value : null;
    }

    // ---------------- Iniciar ----------------

    private static Process? FindGameProcess(string dir, string? exe)
    {
        Process? found = null;
        foreach (var pr in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                string? path = pr.MainModule?.FileName;
                if (found == null && path != null &&
                    ((exe != null && path.Equals(exe, StringComparison.OrdinalIgnoreCase)) || path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                { found = pr; keep = true; }
            }
            catch { }
            if (!keep) pr.Dispose();
        }
        return found;
    }

    /// <summary>Processos iniciados depois de <paramref name="since"/> a partir deste .exe (para fechar os auxiliares junto com o jogo).</summary>
    private static List<Process> ProcessesFrom(string exe, DateTime since)
    {
        var list = new List<Process>();
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return list; // .bat/.lnk: não dá para saber qual processo é
        foreach (var pr in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                if (string.Equals(pr.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase) && pr.StartTime >= since)
                { list.Add(pr); continue; }
            }
            catch { }
            pr.Dispose();
        }
        return list;
    }

    /// <summary>
    /// Aplica o perfil, inicia o jogo e acompanha o processo até ele fechar. Ao terminar (ou cancelar) desfaz o que for temporário.
    /// <paramref name="learnedExe"/> é chamado quando o .exe real do jogo é descoberto.
    /// </summary>
    public static async Task RunAsync(LibGame g, GameProfile p, Action<string> status, Action<string> learnedExe, CancellationToken ct)
    {
        string? oldPlan = null;
        var helpers = new List<(Process Proc, bool Close)>();
        try
        {
            status("Aplicando o perfil…");
            if (p.HighPerfPlan)
            {
                oldPlan = ActiveScheme();
                if (!WindowsTweaks.IsFastScheme(oldPlan)) // Desempenho Máximo também conta: não rebaixa para Alto desempenho
                {
                    if (!PowerCfg("/list").Contains(HighPerformance, StringComparison.OrdinalIgnoreCase))
                        PowerCfg($"-duplicatescheme {HighPerformance} {HighPerformance}");
                    PowerCfg($"/setactive {HighPerformance}");
                }
                else oldPlan = null; // já estava em Alto desempenho ou Desempenho Máximo: nada a restaurar
            }

            string? exe = p.ExePath ?? g.ExePath ?? FindMainExe(g.InstallDir, g.Name);
            if (exe != null && File.Exists(exe))
            {
                try { SetGpuPreference(exe, p.GpuHigh); } catch { }
                try { SetDisableFso(exe, p.DisableFso); } catch { }
            }

            // Jogos e programas auxiliares abrem SEM administrador (pelo Explorer): o RESET EXPLODE roda elevado e
            // tudo que ele inicia com Process.Start herdaria o administrador.
            var started = DateTime.Now.AddSeconds(-1);
            var helperPaths = new List<(string Path, bool Close)>();
            foreach (var h in p.Helpers.Where(h => File.Exists(h.Path)))
            {
                status($"Abrindo {Path.GetFileName(h.Path)}…");
                try
                {
                    ShellLauncher.Open(h.Path, h.Args, Path.GetDirectoryName(h.Path));
                    helperPaths.Add((h.Path, h.CloseWithGame));
                }
                catch (Exception ex) { status($"{Path.GetFileName(h.Path)}: {ex.Message}"); }
            }
            if (helperPaths.Count > 0)
            {
                await Task.Delay(1500, ct);
                foreach (var (path, close) in helperPaths)
                    helpers.AddRange(ProcessesFrom(path, started).Select(pr => (pr, close)));
            }

            status($"Iniciando {g.Name}…");
            string args = (g.Args + " " + BuildArgs(p)).Trim();
            if (g.IsSteam) ShellLauncher.Open($"steam://rungameid/{g.Id}");
            else if (g.LaunchUri != null) ShellLauncher.Open(g.LaunchUri);
            else if (exe != null) ShellLauncher.Open(exe, args, Path.GetDirectoryName(exe));
            else throw new InvalidOperationException("Não achei o executável deste jogo. Escolha o .exe na seção Desempenho.");

            // Espera o processo do jogo aparecer (até 3 min) e acompanha até fechar
            Process? game = null;
            for (int i = 0; i < 120 && game == null; i++)
            {
                await Task.Delay(1500, ct);
                game = FindGameProcess(g.InstallDir, p.ExePath ?? g.ExePath);
            }
            if (game == null) { status("Não detectei o processo do jogo (ele pode ter demorado a abrir). Os ajustes continuam aplicados."); return; }

            while (game != null)
            {
                try
                {
                    string? real = game.MainModule?.FileName;
                    if (real != null) { learnedExe(real); }
                    game.PriorityClass = p.Priority switch { "high" => ProcessPriorityClass.High, "above" => ProcessPriorityClass.AboveNormal, _ => ProcessPriorityClass.Normal };
                }
                catch { }
                status($"{g.Name} em execução" + (p.Priority != "normal" ? $" · prioridade {(p.Priority == "high" ? "alta" : "acima do normal")}" : "") + ".");
                try { await game.WaitForExitAsync(ct); } catch (OperationCanceledException) { throw; } catch { }
                game.Dispose();
                game = null;
                // alguns jogos fecham um launcher e abrem o jogo de verdade: espera mais um pouco
                for (int i = 0; i < 6 && game == null; i++)
                {
                    await Task.Delay(1000, ct);
                    game = FindGameProcess(g.InstallDir, p.ExePath ?? g.ExePath);
                }
            }
            status($"{g.Name} encerrado. Ajustes temporários desfeitos.");
        }
        catch (OperationCanceledException) { status("Acompanhamento cancelado."); }
        catch (Exception ex) { status("Falhou: " + ex.Message); }
        finally
        {
            if (oldPlan != null) { try { PowerCfg($"/setactive {oldPlan}"); } catch { } }
            foreach (var (proc, close) in helpers)
            {
                if (!close) continue;
                try { if (!proc.HasExited) { proc.CloseMainWindow(); if (!proc.WaitForExit(1500)) proc.Kill(); } } catch { }
            }
        }
    }
}
