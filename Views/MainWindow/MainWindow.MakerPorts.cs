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

/// <summary>RESET MAKER › Portas COM: registros de dispositivos ausentes e números COM presos.</summary>
public partial class MainWindow
{
    // ----- USB e portas COM -----

    private List<UsbDevice> _usb = new();

    private void ScanUsb()
    {
        try
        {
            _usb = UsbCleaner.List();
        }
        catch (Exception ex) { UsbResult.Text = $"Não foi possível listar os dispositivos: {ex.Message}"; return; }

        static bool Relevant(UsbDevice d) => d.Port != null || d.Maker != null;
        var connected = _usb.Where(d => d.Present && Relevant(d)).ToList();
        UsbConnected.Text = connected.Count == 0 ? "Nenhuma placa ou porta serial conectada."
                          : string.Join("\n", connected.Select(d => $"{d.Port ?? "—"} · {d.Name}"));

        var shown = _usb.Where(d => !d.Present && (UsbShowAll.IsChecked == true || Relevant(d))).ToList();
        UsbList.ItemsSource = shown;
        int ghosts = _usb.Count(d => !d.Present);
        UsbSummary.Text = shown.Count == 0
            ? "Nenhum registro antigo encontrado."
            : $"{shown.Count} registros antigos na lista ({ghosts} dispositivos ausentes no total).";

        try
        {
            var (reserved, inUse) = UsbCleaner.ComStatus();
            string R(IEnumerable<int> l) => l.Any() ? string.Join(", ", l.Select(n => $"COM{n}")) : "nenhum";
            UsbComDb.Text = $"{reserved.Count} reservados: {R(reserved)}\nEm uso agora: {R(inUse)}";
        }
        catch (Exception ex) { UsbComDb.Text = ex.Message; }
        BtnComDbRestore.IsEnabled = _settings.ComDbBackup != null;
    }

    private void BtnUsbScan_Click(object sender, RoutedEventArgs e) => ScanUsb();
    private void UsbShowAll_Changed(object sender, RoutedEventArgs e) { if (UsbList != null) ScanUsb(); }

    private async void BtnUsbRemove_Click(object sender, RoutedEventArgs e)
    {
        var sel = _usb.Where(d => d.Selected && !d.Present).ToList();
        if (sel.Count == 0) { UsbResult.Text = "Nenhum registro selecionado."; return; }
        if (!RequireAdmin()) return;
        string names = string.Join("\n", sel.Take(12).Select(d => $"  • {d.Name}{(d.Port != null ? "" : $" ({d.Class})")}"))
                       + (sel.Count > 12 ? $"\n  … e mais {sel.Count - 12}" : "");
        var r = MessageBox.Show($"Remover {sel.Count} registros de dispositivos que não estão conectados?\n\n{names}\n\n" +
                                "Se plugar algum deles de novo, o Windows reinstala automaticamente.",
                                "RESET MAKER", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        BtnUsbRemove.IsEnabled = false;
        UsbResult.Text = "Removendo…";
        var (removed, failed) = await Task.Run(() => UsbCleaner.Remove(sel.Select(d => d.InstanceId)));
        BtnUsbRemove.IsEnabled = true;
        UsbResult.Text = $"{removed} registros removidos." +
                         (failed.Count > 0 ? $" Não removidos: {string.Join("; ", failed.Take(5))}" : "") +
                         " Para liberar os números COM que eles prendiam, use \"Liberar números COM presos\".";
        ScanUsb();
    }

    private void BtnComDb_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        var (reserved, inUse) = UsbCleaner.ComStatus();
        var freed = reserved.Except(inUse).ToList();
        if (freed.Count == 0) { UsbResult.Text = "Nenhum número COM preso: todos os reservados estão em uso."; return; }
        var r = MessageBox.Show($"Liberar {freed.Count} números COM presos por dispositivos que não estão conectados?\n\n" +
                                $"Liberados: {string.Join(", ", freed.Select(n => $"COM{n}"))}\n" +
                                $"Mantidos (em uso): {(inUse.Count > 0 ? string.Join(", ", inUse.Select(n => $"COM{n}")) : "nenhum")}\n\n" +
                                "A próxima placa plugada vai receber um número baixo (COM3, COM4…). " +
                                "O valor atual fica guardado para \"Desfazer liberação\".",
                                "RESET MAKER", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            var old = UsbCleaner.RebuildComDb();
            _settings.ComDbBackup ??= old; // guarda o original da primeira vez
            _settings.Save();
            UsbResult.Text = $"Liberados: {string.Join(", ", freed.Select(n => $"COM{n}"))}. Plugue a placa de novo para ela pegar um número baixo.";
        }
        catch (Exception ex) { UsbResult.Text = $"Falhou: {ex.Message}"; }
        ScanUsb();
    }

    private void BtnComDbRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ComDbBackup == null || !RequireAdmin()) return;
        try
        {
            UsbCleaner.RestoreComDb(_settings.ComDbBackup);
            _settings.ComDbBackup = null;
            _settings.Save();
            UsbResult.Text = "Reservas de números COM restauradas para como estavam.";
        }
        catch (Exception ex) { UsbResult.Text = $"Falhou: {ex.Message}"; }
        ScanUsb();
    }
}
