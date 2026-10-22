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

/// <summary>RESET JOGOS › Gargalo: equilíbrio teórico, leitura ao vivo e análise de sessão.</summary>
public partial class MainWindow
{
    // Gargalo + overlay
    private readonly FrameMonitor _frames = new();
    private BottleneckSession? _bnSession;
    private double _balanceRatio = 1;

    // ---------------- Gargalo ----------------

    private void InitBottleneckAndOverlay()
    {
        _frames.Start();

        var (ratio, text) = BottleneckAnalyzer.Balance(_gpuIdx, _cpuIdx);
        _balanceRatio = ratio;
        BnCpuName.Text = $"CPU · {GameCatalog.ShortCpuName(_cpuName)}";
        BnGpuName.Text = $"GPU · {GameCatalog.ShortGpuName(_gpuName)}";
        BnBalanceText.Text = text;

        // Preferências do overlay
        foreach (var rb in OvCornerPanel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == _settings.OverlayCorner;
        foreach (var rb in OvScalePanel.Children.OfType<RadioButton>())
            rb.IsChecked = Math.Abs(double.Parse((string)rb.Tag, CultureInfo.InvariantCulture) - _settings.OverlayScale) < 0.01;
        BuildOverlayPicker();
        _overlayReady = true;
        UpdateOverlayStatus(null);
    }

    /// <summary>Posição do marcador: escala logarítmica da razão GPU/CPU (0,4× … 2,5×), centro = equilibrado.</summary>
    private void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = BnBalanceCanvas.ActualWidth;
        double t = Math.Clamp((Math.Log(_balanceRatio) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0, 1);
        Canvas.SetLeft(BnBalanceDot, t * (w - BnBalanceDot.Width));
    }

    private static MeterItem Meter(string label, double? pct, string? suffix = null)
        => new(label, pct == null ? "—" : $"{pct:0} %{suffix}", Math.Clamp((pct ?? 0) / 100, 0, 1));

    private void UpdateBottleneck()
    {
        var frames = _frames.GetForegroundStats();
        var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), frames);
        var lim = s.Classify();

        if (PageBottleneck.IsVisible || _bnSession != null)
        {
            BnLiveTitle.Text = BottleneckAnalyzer.Title(lim);
            BnLiveAdvice.Text = BottleneckAnalyzer.Advice(lim);
            BnLiveGame.Text = frames != null
                ? $"{frames.ProcessName} · {frames.Fps:0} FPS · 1% low {frames.Low1Fps:0}"
                : "Nenhum jogo em primeiro plano (o FPS aparece quando o jogo estiver na tela)";
            bool alert = lim is Limiter.Cpu or Limiter.Vram or Limiter.Ram or Limiter.Thermal;
            BnLiveCard.Background = alert ? Fg : Card;
            BnLiveTitle.Foreground = alert ? Ink : Fg;
            BnLiveLabel.Foreground = BnLiveGame.Foreground = BnLiveAdvice.Foreground = alert ? Ink : Muted;

            BnMeters.ItemsSource = new[]
            {
                Meter("Carga da GPU", s.GpuLoad),
                Meter("Carga total da CPU", s.CpuTotal),
                Meter(s.BusiestCore != null ? $"Núcleo mais ocupado ({s.BusiestCore})" : "Núcleo mais ocupado", s.CpuMaxCore),
                Meter("VRAM em uso", s.VramPct),
                Meter("RAM em uso", s.RamPct),
                new MeterItem("Temperaturas (GPU / CPU)",
                              $"{(s.GpuTemp is float g ? $"{g:0} °C" : "—")} / {(s.CpuTemp is float c ? $"{c:0} °C" : "—")}",
                              Math.Clamp(Math.Max(s.GpuTemp ?? 0, s.CpuTemp ?? 0) / 100.0, 0, 1)),
            };
        }

        if (_bnSession != null)
        {
            _bnSession.Add(s);
            var el = DateTime.Now - _bnSession.Start;
            BnSessionStatus.Text = $"Coletando… {el:m\\:ss} de {_bnSession.Duration:m\\:ss} · {_bnSession.Count} amostras · " +
                                   (frames != null ? $"jogo detectado: {frames.ProcessName}" : "abra o jogo agora");
            if (_bnSession.Done) FinishBottleneckSession();
        }
    }

    private void BtnBnSession_Click(object sender, RoutedEventArgs e)
    {
        if (_bnSession != null)
        {
            _bnSession = null;
            BtnBnSession.Content = "INICIAR ANÁLISE";
            BtnBnSession.Style = (Style)FindResource("PrimaryButton");
            BnSessionStatus.Text = "Análise cancelada.";
            return;
        }
        int minutes = int.Parse(CheckedTag(BnDurationPanel) ?? "1");
        _bnSession = new BottleneckSession { Duration = TimeSpan.FromMinutes(minutes) };
        BnResult.Visibility = Visibility.Collapsed;
        BtnBnSession.Content = "CANCELAR";
        BtnBnSession.Style = (Style)FindResource("OutlineButton");
        BnSessionStatus.Text = "Coletando… abra o jogo agora.";
    }

    private void FinishBottleneckSession()
    {
        var sess = _bnSession!;
        _bnSession = null;
        BtnBnSession.Content = "INICIAR ANÁLISE";
        BtnBnSession.Style = (Style)FindResource("PrimaryButton");
        BnSessionStatus.Text = $"Análise concluída às {DateTime.Now:HH:mm} ({sess.Count} amostras).";

        var main = sess.Main ?? Limiter.Capped;
        BnResultTitle.Text = $"Resultado: {BottleneckAnalyzer.Title(main)}";
        var parts = new List<string>();
        if (sess.AvgFps is double fps) parts.Add($"FPS médio {fps:0}");
        if (sess.Avg(x => x.GpuLoad) is double g) parts.Add($"GPU média {g:0} %");
        if (sess.Avg(x => x.CpuTotal) is double c) parts.Add($"CPU média {c:0} %");
        if (sess.Avg(x => x.CpuMaxCore) is double k) parts.Add($"núcleo mais ocupado {k:0} %");
        if (sess.Max(x => x.VramPct) is double v) parts.Add($"VRAM máx. {v:0} %");
        if (sess.Max(x => x.GpuTemp) is double gt) parts.Add($"GPU máx. {gt:0} °C");
        BnResultSummary.Text = string.Join(" · ", parts);
        BnDistribution.ItemsSource = sess.Distribution()
            .Select(d => new MeterItem($"{BottleneckAnalyzer.Title(d.Limiter)} — % do tempo", $"{d.Pct:0} %", d.Pct / 100)).ToList();
        BnResultAdvice.Text = BottleneckAnalyzer.Advice(main);
        BnResult.Visibility = Visibility.Visible;
    }
}
