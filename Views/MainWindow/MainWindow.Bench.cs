using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace KResetExplode;

/// <summary>RESET DESEMPENHO › Benchmark: render de CPU e benchmarks de jogo (GPU, simulação, memória).</summary>
public partial class MainWindow
{
    // RESET BENCH
    private readonly BenchRenderer _bench = new();
    private System.Windows.Media.Imaging.WriteableBitmap? _benchBitmap;
    private CancellationTokenSource? _benchCts;
    private DispatcherTimer? _benchTimer;
    private DateTime _benchStart;
    private string _benchPhase = "";
    private DateTime _benchPhaseStart;

    // ---------------- RESET BENCH ----------------

    private void InitBench()
    {
        _benchBitmap = new System.Windows.Media.Imaging.WriteableBitmap(BenchRenderer.Width, BenchRenderer.Height, 96, 96,
                                                                       PixelFormats.Bgra32, null);
        BenchImage.Source = _benchBitmap;
        UpdateBenchHistory();
        UpdateBenchLive();
    }

    private bool BenchRunning => _benchCts != null;

    private async void BtnBench_Click(object sender, RoutedEventArgs e)
    {
        if (BenchRunning) { _benchCts!.Cancel(); return; }
        if (TestRunning || _gameBenchRunning) { BenchHint.Text = "Pare o outro teste antes de rodar o benchmark."; return; }

        string mode = CheckedTag(BenchModePanel) ?? "both";
        bool sustained = CheckedTag(BenchDurationPanel) == "10";
        var minDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        int threads = Environment.ProcessorCount;

        _benchCts = new CancellationTokenSource();
        var ct = _benchCts.Token;
        BtnBench.Content = "PARAR";
        BtnBench.Style = (Style)FindResource("OutlineButton");
        BenchModePanel.IsEnabled = BenchDurationPanel.IsEnabled = BenchTestPanel.IsEnabled = false;
        BenchHint.Text = "";
        BenchIdle.Visibility = Visibility.Collapsed;
        BenchMt.Text = BenchSt.Text = "—";
        BenchRatio.Text = "";
        _benchStart = DateTime.Now;
        _benchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateBenchView(), Dispatcher);
        _benchTimer.Start();

        double? mt = null, st = null;
        try
        {
            if (mode is "mt" or "both")
            {
                _benchPhase = $"Multi-core · {threads} threads";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(threads, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { mt = r.Score; BenchMt.Text = $"{r.Score:N0}"; }
            }
            if (!ct.IsCancellationRequested && mode is "st" or "both")
            {
                _benchPhase = "Single-core · 1 thread";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(1, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { st = r.Score; BenchSt.Text = $"{r.Score:N0}"; }
            }
        }
        catch (OperationCanceledException) { }

        _benchTimer.Stop();
        UpdateBenchView();
        bool cancelled = ct.IsCancellationRequested;
        _benchCts = null;
        BtnBench.Content = "INICIAR";
        BtnBench.Style = (Style)FindResource("PrimaryButton");
        BenchModePanel.IsEnabled = BenchDurationPanel.IsEnabled = BenchTestPanel.IsEnabled = true;
        BenchBuckets.Children.Clear();

        var total = DateTime.Now - _benchStart;
        if (cancelled)
        {
            BenchStatus.Text = "Cancelado";
            return;
        }
        BenchStatus.Text = $"Concluído em {total:m\\:ss}";
        if (mt is double m && st is double s)
            BenchRatio.Text = $"Multi ÷ single: {m / s:0.0}x com {threads} threads " +
                              $"({Environment.ProcessorCount / 2} núcleos com SMT costumam render 8–12x).";

        _settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, GameCatalog.ShortCpuName(_cpuName)));
        AddEvent("", "RESET BENCH", "Render de CPU", mt is double mte ? $"{mte:N0} pts" : st is double ste ? $"{ste:N0} pts" : "");
        if (_settings.BenchRuns.Count > 10) _settings.BenchRuns.RemoveRange(10, _settings.BenchRuns.Count - 10);
        _settings.Save();
        UpdateBenchHistory();
    }

    /// <summary>Copia a imagem parcial e desenha o contorno dos blocos que cada thread está renderizando.</summary>
    private void UpdateBenchView()
    {
        _benchBitmap!.WritePixels(new Int32Rect(0, 0, BenchRenderer.Width, BenchRenderer.Height),
                                  _bench.Pixels, BenchRenderer.Width * 4, 0);

        BenchBuckets.Children.Clear();
        var active = _bench.Active;
        foreach (int b in active)
        {
            if (b < 0) continue;
            var (x, y, w, h) = _bench.Buckets[b];
            var rect = new Rectangle { Width = w, Height = h, Stroke = Brushes.White, StrokeThickness = 1.5 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            BenchBuckets.Children.Add(rect);
        }

        if (BenchRunning)
        {
            double frac = _bench.BucketsTotal > 0 ? (double)_bench.BucketsDone / _bench.BucketsTotal : 0;
            BenchProgress.ScaleX = frac;
            // Tempo restante da passada atual, pela velocidade até agora (só depois de 3% para não oscilar).
            var phaseElapsed = DateTime.Now - _benchPhaseStart;
            string eta = frac >= 0.03 ? $" · faltam ~{TimeSpan.FromSeconds(phaseElapsed.TotalSeconds * (1 - frac) / frac):m\\:ss}" : "";
            BenchStatus.Text = $"{_benchPhase} · {frac:P0}{eta}";
            BenchElapsed.Text = (DateTime.Now - _benchStart).ToString(@"mm\:ss");
        }
        else BenchProgress.ScaleX = 0;
    }

    /// <summary>Temperatura, clock médio dos núcleos e consumo da CPU (atualizado junto com os sensores).</summary>
    private void UpdateBenchLive()
    {
        var clocks = _monitor.CpuCoreClocks();
        BenchLive.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("CPU", GameCatalog.ShortCpuName(_cpuName)),
            new KeyValuePair<string, string>("Threads", $"{Environment.ProcessorCount}"),
            new KeyValuePair<string, string>("Temperatura", _monitor.CpuTemperature is float t ? $"{t:0.#} °C" : "—"),
            new KeyValuePair<string, string>("Clock médio", clocks.Count > 0 ? $"{clocks.Average():0} MHz" : "—"),
            new KeyValuePair<string, string>("Consumo", _monitor.CpuPower is float p ? $"{p:0.#} W" : "—"),
            new KeyValuePair<string, string>("Carga", _monitor.CpuLoad is float l ? $"{l:0} %" : "—"),
        };
    }

    private void UpdateBenchHistory()
    {
        var runs = _settings.BenchRuns;
        BenchHistoryEmpty.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        double bestMt = runs.Max(r => r.Multi) ?? 1, bestSt = runs.Max(r => r.Single) ?? 1;
        BenchHistory.ItemsSource = runs.Select(r =>
        {
            string label = $"{r.Date:dd/MM HH:mm}{(r.Sustained ? " · 10 min" : "")}";
            string value = $"{(r.Multi is double m ? $"{m:N0}" : "—")} / {(r.Single is double s ? $"{s:N0}" : "—")}";
            double frac = r.Multi is double mm ? mm / bestMt : r.Single is double ss ? ss / bestSt : 0;
            return new MeterItem(label, value, frac);
        }).ToList();
    }

    // ---------------- RESET BENCH: benchmarks de jogo ----------------

    public sealed record MemRow(string Label, string Value, string Hint);
    private bool _gameBenchRunning;
    private GpuGameBench? _gpuBench;
    private bool _gpuBenchActive;
    private CancellationTokenSource? _gameBenchCts;
    private System.Windows.Media.Imaging.WriteableBitmap? _simBitmap;
    private int[] _simPixels = Array.Empty<int>();
    private const int SimView = 440;

    private bool AnyBenchRunning => BenchRunning || _gameBenchRunning;

    private void BenchTest_Checked(object sender, RoutedEventArgs e)
    {
        if (BenchPanelRender == null) return; // durante InitializeComponent
        string test = (string)((FrameworkElement)sender).Tag;
        BenchPanelRender.Visibility = test == "render" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelGpu.Visibility = test == "gpu" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelRt.Visibility = test == "rt" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelSim.Visibility = test == "sim" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelMem.Visibility = test == "mem" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelDisk.Visibility = test == "disk" ? Visibility.Visible : Visibility.Collapsed;
        if (test == "disk") InitDiskBench();
        BenchSub.Text = test switch
        {
            "gpu" => "Cena de jogo em Direct3D 11 para medir a placa de vídeo, no estilo 3DMark / Unigine",
            "rt" => "Ray tracing por hardware (DXR 1.1, Direct3D 12): usa os núcleos de RT da placa, com 3 passadas e nota pela mediana",
            "sim" => "Quanto FPS a CPU aguenta numa simulação de jogo com milhares de unidades",
            "mem" => "Latência e banda da RAM e velocidade de carregamento do disco",
            "disk" => "Velocidade de leitura e escrita do disco: sequencial e acessos aleatórios de 4 KB, no estilo CrystalDiskMark",
            _ => "Benchmark de CPU por renderização 3D (path tracing), no mesmo princípio do Cinebench",
        };
        UpdateGameBenchHistory();
    }

    private void SaveBenchRecord(string test, double score, string summary)
    {
        AddEvent("", "Benchmark concluído", summary, "");
        _settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, test, score, summary));
        // Mantém os 10 mais recentes de cada teste.
        _settings.BenchRecords = _settings.BenchRecords.GroupBy(r => r.Test).SelectMany(g => g.Take(10))
                                          .OrderByDescending(r => r.Date).ToList();
        _settings.Save();
        UpdateGameBenchHistory();
    }

    private void UpdateGameBenchHistory()
    {
        List<MeterItem> For(string test)
        {
            var list = _settings.BenchRecords.Where(r => r.Test == test).ToList();
            double best = list.Count > 0 ? list.Max(r => r.Score) : 1;
            return list.Select(r => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, best > 0 ? r.Score / best : 0)).ToList();
        }
        GbHistory.ItemsSource = For("gpu");
        RtHistory.ItemsSource = RtHistoryItems();
        SimHistory.ItemsSource = For("sim");
        MemHistory.ItemsSource = For("mem");
        DiskHistory.ItemsSource = For("disk");
    }

    private void SetGameBenchRunning(Button btn, bool running)
    {
        _gameBenchRunning = running;
        btn.Content = running ? "CANCELAR" : "INICIAR";
        btn.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
        BenchTestPanel.IsEnabled = !running;
    }

    // ----- Cena de jogo (GPU) -----

    private async void BtnGpuBench_Click(object sender, RoutedEventArgs e)
    {
        if (_gameBenchRunning) { _gpuBench?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { GbStatus.Text = "Termine o outro teste antes."; return; }

        var size = (CheckedTag(GbResPanel) ?? "1920x1080").Split('x').Select(int.Parse).ToArray();
        int secs = int.Parse(CheckedTag(GbDurPanel) ?? "60");
        _gpuBench = new GpuGameBench
        {
            Width = size[0], Height = size[1], Duration = TimeSpan.FromSeconds(secs),
            AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
        };
        SetGameBenchRunning(BtnGpuBench, true);
        GbResult.ItemsSource = null;
        var bench = _gpuBench;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            GbFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            GbProgress.ScaleX = bench.Progress;
            GbStatus.Text = $"Rodando · {size[0]}x{size[1]} · {bench.Progress:P0}";
        }, Dispatcher);
        timer.Start();
        _gpuBenchActive = true;
        GbGpuNow.Text = "sensores pausados";
        bool overlayWasOn = ChkOverlay.IsChecked == true;
        if (overlayWasOn) ChkOverlay.IsChecked = false; // o overlay também consulta a GPU

        // Thread dedicada: a cena roda num loop próprio com prioridade alta.
        var r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        timer.Stop();
        _gpuBenchActive = false;
        if (overlayWasOn) ChkOverlay.IsChecked = true;
        GbGpuNow.Text = "—";
        SetGameBenchRunning(BtnGpuBench, false);
        GbProgress.ScaleX = 0;
        GbFpsNow.Text = "—";

        if (r == null)
        {
            GbStatus.Text = bench.Cancelled ? "Cancelado" : $"Falhou: {bench.Error}";
            return;
        }
        GbStatus.Text = $"Concluído · {r.Adapter}";
        GbResult.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("Nota", $"{r.Score:N0} pts"),
            new KeyValuePair<string, string>("FPS médio", $"{r.AvgFps:0.0}"),
            new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0.0}"),
            new KeyValuePair<string, string>("Resolução", $"{r.Width}x{r.Height}"),
            new KeyValuePair<string, string>("Frames", $"{r.Frames:N0}"),
        };
        SaveBenchRecord("gpu", r.Score, $"{r.Score:N0} pts · {r.AvgFps:0} FPS · {r.Height}p");
    }

    // ----- Simulação de jogo (CPU) -----

    private async void BtnSimBench_Click(object sender, RoutedEventArgs e)
    {
        if (_gameBenchRunning) { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { SimStatus.Text = "Termine o outro teste antes."; return; }

        int secs = int.Parse(CheckedTag(SimDurPanel) ?? "20");
        _simBitmap ??= new System.Windows.Media.Imaging.WriteableBitmap(SimView, SimView, 96, 96, PixelFormats.Bgra32, null);
        if (_simPixels.Length == 0) _simPixels = new int[SimView * SimView];
        SimImage.Source = _simBitmap;
        SimIdle.Visibility = Visibility.Collapsed;

        var sim = new GameSimBench();
        _gameBenchCts = new CancellationTokenSource();
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnSimBench, true);
        SimResult.ItemsSource = null;
        SimStatus.Text = "Aquecendo (2 s)…";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            DrawSim(sim);
            SimProgress.ScaleX = sim.Progress;
            if (sim.FramesDone > 0) SimStatus.Text = $"Simulando · {sim.Progress:P0}";
            SimFpsNow.Text = sim.CurrentFps > 0 ? $"{sim.CurrentFps:0} FPS" : "";
        }, Dispatcher);
        timer.Start();

        var r = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(secs), ct));
        timer.Stop();
        bool cancelled = ct.IsCancellationRequested;
        SetGameBenchRunning(BtnSimBench, false);
        SimProgress.ScaleX = 0;
        SimFpsNow.Text = "";
        if (cancelled) { SimStatus.Text = "Cancelado"; return; }

        SimStatus.Text = $"Concluído · {r.Frames:N0} frames em {r.Seconds:0} s";
        SimResult.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("FPS médio (nota)", $"{r.AvgFps:0}"),
            new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0}"),
            new KeyValuePair<string, string>("Thread principal", $"{r.MainThreadMs:0.00} ms/frame"),
            new KeyValuePair<string, string>("Física + IA (paralelo)", $"{r.ParallelMs:0.00} ms/frame"),
            new KeyValuePair<string, string>("Unidades", $"{GameSimBench.Entities:N0}"),
        };
        SaveBenchRecord("sim", r.Score, $"{r.AvgFps:0} FPS · 1% low {r.Low1Fps:0}");
    }

    /// <summary>Vista de cima da batalha: time A em branco, time B em cinza.</summary>
    private void DrawSim(GameSimBench sim)
    {
        Array.Fill(_simPixels, unchecked((int)0xFF050505));
        float[] xs = sim.ViewX, ys = sim.ViewY; byte[] team = sim.ViewTeam;
        float k = SimView / GameSimBench.World;
        for (int i = 0; i < GameSimBench.Entities; i++)
        {
            int px = (int)(xs[i] * k), py = (int)(ys[i] * k);
            if ((uint)px < SimView && (uint)py < SimView)
                _simPixels[py * SimView + px] = team[i] == 0 ? unchecked((int)0xFFF5F5F5) : unchecked((int)0xFF5A5A5A);
        }
        _simBitmap!.WritePixels(new Int32Rect(0, 0, SimView, SimView), _simPixels, SimView * 4, 0);
    }

    // ----- Memória e carregamento -----

    private async void BtnMemBench_Click(object sender, RoutedEventArgs e)
    {
        if (_gameBenchRunning) { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { MemStatus.Text = "Termine o outro teste antes."; return; }

        _gameBenchCts = new CancellationTokenSource();
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnMemBench, true);
        var rows = new List<MemRow>();
        void Show() => MemResult.ItemsSource = rows.ToList();
        (string Phase, double Frac) progress = ("", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            MemStatus.Text = progress.Phase;
            MemProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            progress = ("Medindo latência da RAM…", 0.05);
            double lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct));
            ct.ThrowIfCancellationRequested();
            rows.Add(new MemRow("Latência da RAM", $"{lat:0.0} ns", "Acesso aleatório. Menor é melhor; pesa muito em jogos competitivos."));
            Show();

            progress = ("Medindo banda da RAM…", 0.2);
            var bw = await Task.Run(() => MemoryBench.MeasureBandwidth(ct));
            ct.ThrowIfCancellationRequested();
            rows.Add(new MemRow("Banda de leitura da RAM", $"{bw.Read:0.0} GB/s", "Todas as threads lendo em sequência."));
            rows.Add(new MemRow("Banda de cópia da RAM", $"{bw.Copy:0.0} GB/s", "Leitura + escrita."));
            Show();

            var load = await Task.Run(() => MemoryBench.MeasureLoading((phase, f) => progress = (phase, 0.35 + f * 0.6), ct));
            rows.Add(new MemRow($"SSD {load.Drive} · leitura sequencial", $"{load.SeqReadMBs:N0} MB/s", "Pacote de 512 MB, sem cache do Windows."));
            rows.Add(new MemRow("Assets pequenos", $"{load.FilesPerSec:N0} arq/s",
                                $"Mil arquivos de 64 KB com 8 threads ({load.SmallFilesMBs:N0} MB/s), como engines modernas."));
            rows.Add(new MemRow("Carregamento do mapa", $"{load.MapLoadSeconds:0.00} s", "Pacote + assets: quanto menor, mais rápido o jogo carrega."));
            Show();

            MemStatus.Text = "Concluído · arquivos de teste apagados";
            // Nota para a barra do histórico: mais rápido = maior (inverso da latência × velocidade de carga).
            SaveBenchRecord("mem", 1000 / lat + 10 / load.MapLoadSeconds,
                            $"{lat:0} ns · {bw.Read:0} GB/s · mapa {load.MapLoadSeconds:0.00} s");
        }
        catch (OperationCanceledException) { MemStatus.Text = "Cancelado (arquivos de teste apagados)"; }
        catch (Exception ex) { MemStatus.Text = $"Falhou: {ex.Message}"; }
        finally
        {
            timer.Stop();
            MemProgress.ScaleX = 0;
            SetGameBenchRunning(BtnMemBench, false);
        }
    }
}
