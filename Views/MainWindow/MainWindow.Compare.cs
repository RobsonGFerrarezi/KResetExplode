using System.Text.RegularExpressions;
using System.Windows;

namespace KResetExplode;

/// <summary>
/// RESET DESEMPENHO › Comparar: o último resultado de cada teste contra o anterior e contra o "antes de otimizar"
/// marcado pelo usuário (ex.: antes e depois do RESET OTIMIZA ou de um ajuste), com a diferença em %.
/// Só compara resultados do mesmo teste com as mesmas opções (resolução, preset, disco, duração).
/// </summary>
public partial class MainWindow
{
    public sealed record CompareRow(string Name, string Current, string Date, string VsPrev, string PrevText, string VsBase, string BaseText);

    private sealed record BenchMetric(string Key, string Name, DateTime Date, double Score, string Summary);

    /// <summary>Todos os resultados do histórico como métricas comparáveis (mais recente primeiro).</summary>
    private List<BenchMetric> BenchMetrics()
    {
        var list = new List<BenchMetric>();
        foreach (var r in _settings.BenchRuns)
        {
            string key = r.Sustained ? ".10" : "", name = r.Sustained ? " (10 min)" : "";
            if (r.Multi is double m) list.Add(new($"render.mt{key}", $"Render (CPU) · multi-core{name}", r.Date, m, $"{m:N0} pts"));
            if (r.Single is double s) list.Add(new($"render.st{key}", $"Render (CPU) · single-core{name}", r.Date, s, $"{s:N0} pts"));
        }
        foreach (var r in _settings.BenchRecords)
        {
            if (r.Test == "oc") continue; // verificação de estabilidade (passou/falhou), não é nota
            var (key, name) = MetricOf(r);
            list.Add(new(key, name, r.Date, r.Score, r.Summary));
        }
        return list.OrderByDescending(x => x.Date).ToList();
    }

    private static (string Key, string Name) MetricOf(BenchRecord r)
    {
        if (r.Test.StartsWith("rt:"))
        {
            var p = r.Test.Split(':');
            return (r.Test, p.Length == 3 ? $"Ray tracing (GPU) · {PresetName(p[1])} {p[2]}p" : "Ray tracing (GPU)");
        }
        switch (r.Test)
        {
            case "gpu":
                // A nota depende da resolução, que está no resumo ("… · 1080p").
                var h = Regex.Match(r.Summary, @"(\d{3,4})p\b");
                return h.Success ? ($"gpu:{h.Groups[1].Value}", $"Cena de jogo (GPU) · {h.Groups[1].Value}p") : ("gpu", "Cena de jogo (GPU)");
            case "disk":
                string drive = r.Summary.Split('·')[0].Trim();
                return ($"disk:{drive}", $"Disco {drive}");
            case "sim": return ("sim", "Simulação de jogo (CPU)");
            case "mem": return ("mem", "Memória e carregamento");
            default: return (r.Test, r.Test);
        }
    }

    private static int MetricRank(string key) =>
        new[] { "render.", "gpu", "rt:", "sim", "mem", "disk" }.Select((p, i) => (p, i)).Where(x => key.StartsWith(x.p))
                                                                .Select(x => x.i).DefaultIfEmpty(99).First();

    private static string Pct(double now, double before, out double diff)
    {
        diff = before > 0 ? (now - before) / before * 100 : 0;
        if (Math.Abs(diff) < 1) return $"≈ igual ({diff:+0.0;-0.0;0.0}%)";
        return diff > 0 ? $"▲ {diff:+0.0}%" : $"▼ {diff:0.0}%";
    }

    private void UpdateCompare()
    {
        var metrics = BenchMetrics();
        var baseline = _settings.BenchBaseline.ToDictionary(b => b.Key);
        var rows = new List<CompareRow>();
        var vsBase = new List<double>();

        foreach (var g in metrics.GroupBy(m => m.Key).OrderBy(g => MetricRank(g.Key)).ThenBy(g => g.First().Name))
        {
            var cur = g.First();
            var prev = g.Skip(1).FirstOrDefault();
            string vsPrev = "—", prevText = "primeiro resultado deste teste";
            if (prev != null)
            {
                vsPrev = Pct(cur.Score, prev.Score, out _);
                prevText = $"{prev.Summary} · {prev.Date:dd/MM HH:mm}";
            }
            string vsB = "—", baseText = baseline.Count == 0 ? "nenhum marcado" : "este teste não foi marcado";
            if (baseline.TryGetValue(g.Key, out var b))
            {
                if (b.Date == cur.Date)
                {
                    vsB = "é o próprio";
                    baseText = "rode o teste de novo depois de otimizar";
                }
                else
                {
                    vsB = Pct(cur.Score, b.Score, out double d);
                    vsBase.Add(d);
                    baseText = $"{b.Summary} · {b.Date:dd/MM HH:mm}";
                }
            }
            rows.Add(new CompareRow(cur.Name, cur.Summary, $"{cur.Date:dd/MM HH:mm} · {g.Count()} resultado(s)", vsPrev, prevText, vsB, baseText));
        }

        CompareList.ItemsSource = rows;
        CompareEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnBaselineClear.IsEnabled = _settings.BenchBaseline.Count > 0;
        CompareBaselineText.Text = _settings.BenchBaseline.Count == 0
            ? "Nenhum resultado marcado. Rode os benchmarks, toque em MARCAR e depois aplique o RESET OTIMIZA, um ajuste ou um driver novo. " +
              "Rode os mesmos testes de novo e a diferença aparece aqui."
            : $"Marcado em {_settings.BenchBaselineDate:dd/MM/yyyy HH:mm} com {_settings.BenchBaseline.Count} teste(s). " +
              "Para valer a comparação, rode de novo com as mesmas opções (resolução, preset, disco).";
        CompareSummary.Text = vsBase.Count == 0 ? ""
            : $"Desde o \"antes de otimizar\": média de {vsBase.Average():+0.0;-0.0;0.0}% em {vsBase.Count} teste(s)" +
              (vsBase.All(d => Math.Abs(d) < 3) ? " (diferenças abaixo de 3% costumam ser variação normal entre execuções)." : ".");
    }

    private void BtnBaselineMark_Click(object sender, RoutedEventArgs e)
    {
        var latest = BenchMetrics().GroupBy(m => m.Key).Select(g => g.First()).ToList();
        if (latest.Count == 0)
        {
            MessageBox.Show("Rode pelo menos um benchmark (RESET DESEMPENHO › Benchmark) antes de marcar.", "Comparar",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_settings.BenchBaseline.Count > 0 &&
            MessageBox.Show($"Substituir o \"antes de otimizar\" de {_settings.BenchBaselineDate:dd/MM HH:mm} pelos resultados mais recentes?",
                            "Comparar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.BenchBaseline = latest.Select(m => new BenchBaseline(m.Key, m.Date, m.Score, m.Summary)).ToList();
        _settings.BenchBaselineDate = DateTime.Now;
        _settings.Save();
        AddEvent("", "Comparar", $"\"Antes de otimizar\" marcado com {latest.Count} teste(s)", "");
        UpdateCompare();
    }

    private void BtnBaselineClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Apagar a marcação \"antes de otimizar\"? O histórico dos benchmarks continua.", "Comparar",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.BenchBaseline.Clear();
        _settings.BenchBaselineDate = null;
        _settings.Save();
        UpdateCompare();
    }

    private void BtnCompareOpenBench_Click(object sender, RoutedEventArgs e) => NavBench.IsChecked = true;
}
