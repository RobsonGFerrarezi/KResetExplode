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

/// <summary>RESET DESEMPENHO › Stress test de CPU, RAM e GPU, com limite de temperatura e gráfico.</summary>
public partial class MainWindow
{
    // Stress test
    private readonly StressTester _stress = new();
    private readonly List<double> _tempHistory = new();
    private const int HistoryLength = 300; // 5 min a 1 amostra/s
    private DateTime _stressStart;
    private TimeSpan _stressDuration;
    private double _tempLimit = 90;
    private float _stressMaxTemp;

    // Stress de GPU (estilo FurMark)
    private readonly GpuBurn _burn = new();
    private bool _gpuBurnOn;
    private double _gpuTempLimit = 85;
    private float _gpuMaxTemp;
    private readonly List<double> _gpuTempHistory = new();

    // ---------------- Stress test ----------------

    private bool TestRunning => _stress.Running || _gpuBurnOn;

    private void BtnStress_Click(object sender, RoutedEventArgs e)
    {
        if (TestRunning) StopStress("Parado pelo usuário");
        else StartStress();
    }

    private void ChkRam_Click(object sender, RoutedEventArgs e)
    {
        if (RamFractionPanel != null) RamFractionPanel.IsEnabled = ChkRam.IsChecked == true;
    }

    private void ChkGpu_Click(object sender, RoutedEventArgs e)
    {
        if (GpuOptions != null) GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = ChkGpu.IsChecked == true;
        if (PageStress != null) DrawChart(); // mostra/oculta a linha de limite da GPU
    }

    private static string? CheckedTag(Panel panel)
        => panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void StartStress()
    {
        StressHint.Text = "";
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { StressHint.Text = "Ative CPU, RAM ou GPU."; return; }

        if (!double.TryParse(TempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _tempLimit)
            || _tempLimit < 40 || _tempLimit > 110)
        {
            StressHint.Text = "Limite da CPU deve estar entre 40 e 110 °C.";
            return;
        }
        if (gpu && (!double.TryParse(GpuTempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _gpuTempLimit)
                    || _gpuTempLimit < 40 || _gpuTempLimit > 100))
        {
            StressHint.Text = "Limite da GPU deve estar entre 40 e 100 °C.";
            return;
        }
        if (!double.TryParse(TxtDuration.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double minutes) || minutes < 0)
        {
            StressHint.Text = "Duração inválida.";
            return;
        }
        if ((cpu || ram) && _monitor.CpuTemperature == null)
        {
            string why = SensorMonitor.IsPawnIOInstalled
                ? ""
                : "\n\nCausa provável: o driver PawnIO não está instalado. Instale com:\n" +
                  "winget install namazso.PawnIO\ne reabra o RESET EXPLODE.";
            var r = MessageBox.Show("Nenhum sensor de temperatura da CPU foi encontrado, então o corte automático " +
                                    "por temperatura da CPU NÃO funcionará. Continuar mesmo assim?" + why,
                                    "RESET EXPLODE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        if (gpu && MainGpuTemp() == null)
        {
            var r = MessageBox.Show("Nenhum sensor de temperatura da GPU foi encontrado, então o corte automático " +
                                    "por temperatura da GPU NÃO funcionará. Continuar mesmo assim?",
                                    "RESET EXPLODE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        double fraction = double.Parse(CheckedTag(RamFractionPanel) ?? "0.6", CultureInfo.InvariantCulture);

        _stressDuration = TimeSpan.FromMinutes(minutes);
        _stressStart = DateTime.Now;
        _stressMaxTemp = 0;
        _gpuMaxTemp = 0;
        _tempHistory.Clear();
        _gpuTempHistory.Clear();

        if (cpu || ram) _stress.Start(cpu, ram, fraction);

        string gpuDesc = "não";
        if (gpu)
        {
            string res = CheckedTag(GpuResPanel) ?? "1920x1080";
            int msaa = int.Parse(CheckedTag(GpuMsaaPanel) ?? "4");
            var intensity = (CheckedTag(GpuIntensityPanel) ?? "128,48").Split(',').Select(int.Parse).ToArray();
            bool full = res == "full";
            var size = full ? new[] { 0, 0 } : res.Split('x').Select(int.Parse).ToArray();
            _burn.Start(new GpuBurnSettings
            {
                Fullscreen = full, Width = size[0], Height = size[1], Msaa = msaa,
                Shells = intensity[0], Iterations = intensity[1],
                AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
            });
            _gpuBurnOn = true;
            gpuDesc = $"sim ({(full ? "tela cheia" : res)}, {(msaa > 1 ? $"MSAA {msaa}x" : "sem MSAA")}, " +
                      $"{GpuIntensityPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Content}, limite {_gpuTempLimit:0} °C)";
        }

        SetStressControls(running: true);
        SetStatus("Rodando", alert: false);
        Log($"Iniciado · CPU {(cpu ? $"sim (limite {_tempLimit:0} °C)" : "não")} · RAM {(ram ? $"sim ({fraction:P0} da livre)" : "não")} · " +
            $"GPU {gpuDesc} · {(minutes > 0 ? $"{minutes:0.##} min" : "sem limite de tempo")}");
    }

    private void StopStress(string reason)
    {
        if (TestRunning) AddEvent("", "Stress test", reason, reason.StartsWith("ABORTADO") ? "abortado" : "ok");
        if (!TestRunning) return;
        var errors = _stress.MemoryErrors;
        var passes = _stress.MemoryPasses;
        bool hadRam = _stress.MemoryAllocatedBytes > 0;
        bool hadCpu = _stress.Running;
        _stress.Stop();

        string gpuSummary = "";
        if (_gpuBurnOn)
        {
            _burn.Stop();
            _gpuBurnOn = false;
            // "Pontuação" no estilo FurMark: frames renderizados e FPS médio.
            gpuSummary = $" · GPU: {_burn.Frames:N0} frames, média {_burn.AverageFps:0} FPS, temp. máx. {_gpuMaxTemp:0.#} °C";
        }
        SetStressControls(running: false);

        bool aborted = reason.StartsWith("ABORTADO");
        SetStatus(aborted ? "Abortado" : "Parado", alert: aborted);
        Log($"{reason} · duração {DateTime.Now - _stressStart:hh\\:mm\\:ss}" +
            (hadCpu ? $" · CPU temp. máx. {_stressMaxTemp:0.#} °C" : "") +
            (hadRam ? $" · RAM {passes} passes, {errors} erros" : "") + gpuSummary);
    }

    private void SetStressControls(bool running)
    {
        BtnStress.Content = running ? "PARAR" : "INICIAR";
        BtnStress.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
        ChkCpu.IsEnabled = ChkRam.IsEnabled = ChkGpu.IsEnabled = !running;
        RamFractionPanel.IsEnabled = !running && ChkRam.IsChecked == true;
        GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = !running && ChkGpu.IsChecked == true;
        TempLimit.IsEnabled = TxtDuration.IsEnabled = !running;
        SideStress.Text = running ? "rodando" : "parado";
    }

    /// <summary>Alerta = cartão invertido (branco com texto preto).</summary>
    private void SetStatus(string text, bool alert)
    {
        StStatus.Text = text;
        StatusCard.Background = alert ? Fg : Card;
        StStatus.Foreground = alert ? Ink : Fg;
        StStatusLabel.Foreground = alert ? Ink : Muted;
    }

    private void UpdateStressPanel()
    {
        float? temp = _monitor.CpuTemperature;
        float? load = _monitor.CpuLoad;
        float? power = _monitor.CpuPower;
        float? gTemp = MainGpuTemp();
        float? gPower = MainGpuPower();

        if (temp is float t)
        {
            _tempHistory.Add(t);
            if (_tempHistory.Count > HistoryLength) _tempHistory.RemoveAt(0);
            if (_stress.Running && t > _stressMaxTemp) _stressMaxTemp = t;
        }
        if (gTemp is float gt)
        {
            _gpuTempHistory.Add(gt);
            if (_gpuTempHistory.Count > HistoryLength) _gpuTempHistory.RemoveAt(0);
            if (_gpuBurnOn && gt > _gpuMaxTemp) _gpuMaxTemp = gt;
        }

        StTemp.Text = SideTemp.Text = temp == null ? "—" : $"{temp:0.#} °C";
        StTempMax.Text = _stressMaxTemp > 0 ? $"máx. {_stressMaxTemp:0.#} °C" : "máx. —";
        SideLoad.Text = load == null ? "—" : $"{load:0} %";
        StLoad.Text = load == null ? "carga —" : $"carga {load:0} %";
        StPower.Text = power == null ? "—" : $"{power:0.#} W";

        StGpuTemp.Text = gTemp == null ? "—" : $"{gTemp:0.#} °C";
        StGpuTempMax.Text = _gpuMaxTemp > 0 ? $"máx. {_gpuMaxTemp:0.#} °C" : "máx. —";
        StGpuPower.Text = gPower == null ? "—" : $"{gPower:0.#} W";
        string cap = MainGpuPerfCap();
        StGpuCap.Text = cap.Length > 0 ? $"limitador: {cap}" : "—";
        StFps.Text = _gpuBurnOn && _burn.Fps > 0 ? $"{_burn.Fps:0}" : "—";
        StFpsAvg.Text = _gpuBurnOn && _burn.AverageFps > 0 ? $"média {_burn.AverageFps:0}" : "média —";

        if (TestRunning)
        {
            var elapsed = DateTime.Now - _stressStart;
            StElapsed.Text = elapsed.ToString(@"hh\:mm\:ss");
            StRam.Text = _stress.MemoryAllocatedBytes > 0
                ? $"{_stress.MemoryPasses} / {_stress.MemoryErrors}"
                : "—";

            if (_stress.Running && temp >= _tempLimit)
                StopStress($"ABORTADO: CPU a {temp:0.#} °C atingiu o limite de {_tempLimit:0} °C");
            else if (_gpuBurnOn && gTemp >= _gpuTempLimit)
                StopStress($"ABORTADO: GPU a {gTemp:0.#} °C atingiu o limite de {_gpuTempLimit:0} °C");
            else if (_stress.MemoryErrors > 0)
                StopStress($"ABORTADO: {_stress.MemoryErrors} erros de memória detectados");
            else if (_gpuBurnOn && !_burn.Running)
                StopStress(_burn.Error != null ? $"ABORTADO: falha na GPU ({_burn.Error})"
                                               : "Janela da GPU fechada pelo usuário");
            else if (_stressDuration > TimeSpan.Zero && elapsed >= _stressDuration)
                StopStress("Concluído (tempo esgotado)");
        }

        if (PageStress.IsVisible) DrawChart();
    }

    private void Log(string msg)
    {
        StressLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
        StressLog.ScrollIntoView(StressLog.Items[^1]);
    }

    private void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Desenha o histórico de temperatura da CPU (branco) e da GPU (cinza) com as linhas de limite.</summary>
    private void DrawChart()
    {
        TempChart.Children.Clear();
        double w = TempChart.ActualWidth, h = TempChart.ActualHeight;
        if (w < 50 || h < 40) return;

        const double left = 36, top = 6, bottom = 6;
        double plotW = w - left, plotH = h - top - bottom;
        double yMin = 20, yMax = Math.Max(110, Math.Max(_tempLimit, _gpuTempLimit) + 10);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));

        var gridBrush = (Brush)FindResource("Line");
        for (double v = yMin; v <= yMax; v += 20)
        {
            TempChart.Children.Add(new Line { X1 = left, X2 = w, Y1 = Y(v), Y2 = Y(v), Stroke = gridBrush });
            var lbl = new TextBlock { Text = $"{v:0}°", FontSize = 10, Foreground = (Brush)FindResource("Faint"),
                                      FontFamily = (FontFamily)FindResource("Mono") };
            Canvas.SetLeft(lbl, 0); Canvas.SetTop(lbl, Y(v) - 7);
            TempChart.Children.Add(lbl);
        }

        void LimitLine(double v, Brush b) => TempChart.Children.Add(new Line
        {
            X1 = left, X2 = w, Y1 = Y(v), Y2 = Y(v),
            Stroke = b, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
        });
        LimitLine(_tempLimit, Muted);
        if (ChkGpu.IsChecked == true) LimitLine(_gpuTempLimit, (Brush)FindResource("Faint"));

        double step = plotW / (HistoryLength - 1);
        void Series(List<double> data, Brush stroke, bool fill)
        {
            if (data.Count < 2) return;
            int offset = HistoryLength - data.Count; // alinha à direita
            var line = new Polyline { Stroke = stroke, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < data.Count; i++)
                line.Points.Add(new Point(left + (offset + i) * step, Y(data[i])));
            if (fill)
            {
                var area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x16, ((SolidColorBrush)FindResource("Fg")).Color.R, ((SolidColorBrush)FindResource("Fg")).Color.G, ((SolidColorBrush)FindResource("Fg")).Color.B)) };
                foreach (var p in line.Points) area.Points.Add(p);
                area.Points.Add(new Point(line.Points[^1].X, top + plotH));
                area.Points.Add(new Point(line.Points[0].X, top + plotH));
                TempChart.Children.Add(area);
            }
            TempChart.Children.Add(line);
        }
        Series(_gpuTempHistory, Muted, fill: false);
        Series(_tempHistory, Fg, fill: true);
    }
}
