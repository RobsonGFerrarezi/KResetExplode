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

/// <summary>RESET EXPLODE › Sensores: lista com filtros e o aviso de sensores da CPU sem resposta (driver PawnIO).</summary>
public partial class MainWindow
{
    /// <summary>Mostra o aviso no menu quando temperatura/consumo da CPU não respondem, com o motivo provável.</summary>
    private void UpdateCpuSensorWarning()
    {
        if (!_monitor.IsOpen) return; // ainda detectando: não acusar falta de sensor antes da hora
        var p = _monitor.DiagnoseCpuSensors();
        CpuSensorWarn.Visibility = p == SensorMonitor.CpuSensorProblem.None ? Visibility.Collapsed : Visibility.Visible;
        CpuSensorWarnText.Text = p switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => "Sem permissão de administrador.",
            SensorMonitor.CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO.",
            SensorMonitor.CpuSensorProblem.ReadFailed => "O driver não respondeu.",
            _ => ""
        };
    }

    private async void BtnFixCpuSensors_Click(object sender, RoutedEventArgs e)
    {
        var p = _monitor.DiagnoseCpuSensors();
        string why = SensorMonitor.Explain(p);
        if (p != SensorMonitor.CpuSensorProblem.PawnIOMissing)
        {
            MessageBox.Show(why, "RESET EXPLODE", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // winget vem com o Windows 11 e com o Windows 10 atualizado (App Installer). Ele é conferido antes de rodar como administrador.
        using (var check = SecureFiles.AcquireWinget(out string? problem))
            if (check == null)
            {
                MessageBox.Show(why + "\n\nO instalador de pacotes do Windows (winget) não está disponível neste PC (" + problem + "). " +
                                "Atualize o \"Instalador de Aplicativo\" pela Microsoft Store e use o botão de novo, " +
                                "ou instale o PawnIO (autor: namazso) manualmente.",
                                "RESET EXPLODE", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

        var r = MessageBox.Show(why + "\n\nInstalar o PawnIO agora? Vai abrir uma janela com o instalador do Windows (winget). " +
                                "Quando terminar, feche o RESET EXPLODE e abra de novo.",
                                "RESET EXPLODE", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            System.Diagnostics.Process proc;
            using (var winget = SecureFiles.AcquireWinget(out string? problem) ?? throw new InvalidOperationException(problem))
                proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(winget.Path,
                           "install --id namazso.PawnIO -e --accept-package-agreements --accept-source-agreements")
                       { UseShellExecute = false, CreateNoWindow = false })!;
            using (proc)
            {
                await proc.WaitForExitAsync();
                MessageBox.Show(proc.ExitCode == 0 ? "PawnIO instalado. Feche e abra o RESET EXPLODE de novo."
                                                   : $"O winget terminou com o código 0x{proc.ExitCode:X8}. Tente de novo ou instale o PawnIO manualmente.",
                                "RESET EXPLODE", MessageBoxButton.OK, proc.ExitCode == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível iniciar o winget: " + ex.Message, "RESET EXPLODE", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Sensores ----------------

    private void AddChip(string label, string? category, bool isChecked = false)
    {
        var chip = new RadioButton
        {
            Content = label,
            GroupName = "Category",
            Style = (Style)FindResource("Chip"),
            IsChecked = isChecked
        };
        chip.Checked += (_, _) => { _category = category; ApplySensorFilter(); };
        CategoryChips.Children.Add(chip);
    }

    private void SensorSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _search = SensorSearch.Text.Trim();
        ApplySensorFilter();
    }

    private void ApplySensorFilter()
    {
        if (_view == null) return;
        if (_category == null && _search.Length == 0) { _view.Filter = null; return; }
        _view.Filter = o =>
        {
            var r = (SensorRow)o;
            return (_category == null || r.Category == _category)
                && (_search.Length == 0
                    || r.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
                    || r.Hardware.Contains(_search, StringComparison.OrdinalIgnoreCase));
        };
    }
}
