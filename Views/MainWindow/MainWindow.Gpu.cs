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

/// <summary>RESET EXPLODE › Placa de vídeo, e leituras da GPU principal usadas pelas outras páginas.</summary>
public partial class MainWindow
{
    // Placa de vídeo
    private List<GpuInfo> _gpus = new();
    private GpuInfo? _gpu;

    // ---------------- Placa de vídeo ----------------

    private void ShowGpu(GpuInfo g)
    {
        _gpu = g;
        GpuName.Text = g.Name;
        GpuSubtitle.Text = g.Subtitle;
        GpuVendor.Text = g.Vendor;
        GpuSections.ItemsSource = g.Sections;

        // Sensores do LibreHardwareMonitor deste hardware, em ordem fixa (clock, temperatura, carga...).
        var live = new ListCollectionView(_monitor.Rows)
        {
            Filter = o => ((SensorRow)o).Hardware == g.Name
        };
        live.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Order), ListSortDirection.Ascending));
        live.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Name), ListSortDirection.Ascending));
        GpuLive.ItemsSource = live;
        GpuExtras.ItemsSource = g.LiveExtras();
    }

    private void UpdateGpuLive()
    {
        // Barra lateral: GPU principal (dedicada = primeira da lista).
        if (_gpus.Count > 0)
        {
            var load = _monitor.Rows.FirstOrDefault(r => r.Hardware == _gpus[0].Name && r.Type == SensorType.Load
                                                         && r.Name == "GPU Core");
            SideGpuTemp.Text = MainGpuTemp() is float t ? $"{t:0.#} °C" : "—";
            SideGpuLoad.Text = load?.RawValue is float l ? $"{l:0} %" : "—";
        }
        if (_gpu != null && PageGpu.IsVisible) GpuExtras.ItemsSource = _gpu.LiveExtras();
    }

    /// <summary>Temperatura do núcleo da GPU principal (dedicada).</summary>
    private float? MainGpuTemp()
    {
        if (_gpus.Count == 0) return null;
        var temps = _monitor.Rows.Where(r => r.Hardware == _gpus[0].Name && r.Type == SensorType.Temperature).ToList();
        return (temps.FirstOrDefault(r => r.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;
    }

    /// <summary>Consumo da GPU principal: NVML quando disponível, senão LibreHardwareMonitor.</summary>
    private float? MainGpuPower()
    {
        if (_gpus.Count == 0) return null;
        var g = _gpus[0];
        if (g.NvmlHandle != IntPtr.Zero && Nvml.PowerUsageMw(g.NvmlHandle) is uint mw) return mw / 1000f;
        return _monitor.Rows.FirstOrDefault(r => r.Hardware == g.Name && r.Type == SensorType.Power)?.RawValue;
    }

    private string MainGpuPerfCap()
    {
        if (_gpus.Count == 0) return "";
        return _gpus[0].LiveExtras().FirstOrDefault(kv => kv.Key.StartsWith("PerfCap")).Value ?? "";
    }
}
