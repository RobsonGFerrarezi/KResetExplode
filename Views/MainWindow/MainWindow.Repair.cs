using System.Windows;

namespace KResetExplode;

/// <summary>Aba RESET FERRAMENTAS · Reparar Windows (DISM + SFC).</summary>
public partial class MainWindow
{
    private CancellationTokenSource? _repairCts;

    private async void BtnRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_repairCts != null) { _repairCts.Cancel(); BtnRepair.IsEnabled = false; RepairStatus.Text = "Cancelando…"; return; }
        if (MessageBox.Show(this, "Verificar e reparar os arquivos do Windows (DISM e SFC)?\n\nLeva de 10 a 40 minutos. Dá para usar o PC enquanto isso, " +
                                  "mas evite desligar. Precisa de internet para o DISM baixar arquivos de reparo.",
                "Reparar Windows", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _repairCts = new CancellationTokenSource();
        BtnRepair.Content = "CANCELAR";
        RepairLog.Clear();
        RepairResult.Text = "Em andamento…";
        try
        {
            var steps = await WindowsRepair.RunAsync(
                l => Dispatcher.BeginInvoke(new Action(() => { RepairLog.AppendText(l + Environment.NewLine); RepairLog.ScrollToEnd(); })),
                (t, f) => Dispatcher.BeginInvoke(new Action(() => { RepairStatus.Text = t; RepairProgress.ScaleX = Math.Clamp(f, 0, 1); })),
                _repairCts.Token);
            RepairResult.Text = string.Join("\n\n", steps.Select(s => $"{s.Name}: {s.Summary}"));
            RepairStatus.Text = $"Concluído às {DateTime.Now:HH:mm}. Se algo foi reparado, reinicie o PC.";
        }
        catch (OperationCanceledException) { RepairStatus.Text = "Cancelado. Nada fica pela metade: rode de novo quando quiser."; RepairResult.Text = "Cancelado."; }
        catch (Exception ex) { RepairStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _repairCts.Dispose();
            _repairCts = null;
            BtnRepair.Content = "VERIFICAR E REPARAR";
            BtnRepair.IsEnabled = true;
        }
    }
}
