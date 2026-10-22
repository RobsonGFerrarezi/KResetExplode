using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KResetExplode;

/// <summary>Resultado de uma etapa do reparo.</summary>
public sealed record RepairStep(string Name, int ExitCode, string Summary);

/// <summary>
/// Reparo do Windows com as ferramentas oficiais: DISM (/RestoreHealth, conserta a imagem do sistema baixando arquivos do
/// Windows Update) e depois SFC (/scannow, confere e repara os arquivos do sistema a partir dessa imagem). É a ordem
/// recomendada pela Microsoft. Leva de 10 a 40 minutos. Requer administrador.
/// </summary>
public static class WindowsRepair
{
    static WindowsRepair() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static async Task<List<RepairStep>> RunAsync(Action<string> log, Action<string, double> progress, CancellationToken ct)
    {
        var steps = new List<RepairStep>();

        progress("1/2 · DISM: verificando e reparando a imagem do Windows…", 0);
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var (dismCode, dismOut) = await RunToolAsync("Dism.exe", "/Online /Cleanup-Image /RestoreHealth", oem, log,
            pct => progress($"1/2 · DISM: {pct:0}%", pct / 200), ct);
        steps.Add(new RepairStep("DISM", dismCode, dismCode switch
        {
            0 => "Imagem do Windows verificada: sem problemas ou reparada.",
            _ => $"O DISM terminou com erro (código {dismCode}). {LastLines(dismOut, 2)} Sem internet o DISM não consegue baixar os arquivos de reparo.",
        }));

        progress("2/2 · SFC: verificando os arquivos do sistema…", 0.5);
        // O SFC escreve em UTF-16 quando a saída é redirecionada.
        var (sfcCode, sfcOut) = await RunToolAsync("sfc.exe", "/scannow", Encoding.Unicode, log,
            pct => progress($"2/2 · SFC: {pct:0}%", 0.5 + pct / 200), ct);
        steps.Add(new RepairStep("SFC", sfcCode, LastLines(sfcOut, 3)));

        progress("Concluído.", 1);
        return steps;
    }

    /// <summary>Roda a ferramenta lendo a saída em pedaços: as duas reescrevem a linha de progresso com \r, sem quebrar a linha.</summary>
    private static async Task<(int Code, string Output)> RunToolAsync(string exe, string args, Encoding enc, Action<string> log,
                                                                       Action<double> percent, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(SecureFiles.SystemTool(exe), args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = enc, StandardErrorEncoding = enc,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {exe}.");
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var all = new StringBuilder();
        var line = new StringBuilder();
        var buf = new char[512];
        _ = p.StandardError.ReadToEndAsync(CancellationToken.None);
        int n;
        while ((n = await p.StandardOutput.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                char c = buf[i];
                if (c is '\r' or '\n')
                {
                    string l = line.ToString().Trim('\0', ' ');
                    line.Clear();
                    if (l.Length == 0) continue;
                    var m = Regex.Match(l, @"(\d{1,3}(?:[.,]\d)?)\s*%");
                    if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                        percent(Math.Clamp(pct, 0, 100));
                    else { log(l); all.AppendLine(l); }
                }
                else if (c != '\0') line.Append(c);
            }
        }
        if (line.Length > 0) { log(line.ToString()); all.AppendLine(line.ToString()); }
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return (p.ExitCode, all.ToString());
    }

    private static string LastLines(string s, int n) =>
        string.Join(" ", s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(n));

    public static string CbsLog => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Logs\CBS\CBS.log");
}
