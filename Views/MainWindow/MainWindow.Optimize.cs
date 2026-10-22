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

/// <summary>RESET FERRAMENTAS › Otimização: limpeza de memória, programas para fechar e ajustes do Windows.</summary>
public partial class MainWindow
{
    // RESET OTIMIZA
    private List<BackgroundApp> _apps = new();
    private readonly List<Tweak> _tweaks; // só as definições; o estado é lido ao abrir a página
    private bool _optInit;

    // ---------------- RESET OTIMIZA ----------------

    /// <summary>Carrega a página RESET OTIMIZA quando ela é aberta (os ajustes do Windows só na primeira vez).</summary>
    private void InitOptimize()
    {
        if (!_optInit)
        {
            _optInit = true;
            foreach (var t in _tweaks)
                try { t.Refresh(); } catch (Exception ex) { t.Status = $"Não foi possível ler: {ex.Message}"; }
            OptTweaks.ItemsSource = _tweaks;
        }
        RefreshApps();
        UpdateRamStatus();
    }

    private void UpdateRamStatus()
    {
        var (total, avail) = Optimizer.MemoryStatus();
        if (total == 0) return;
        double used = total - avail;
        OptRamText.Text = $"{used / 1073741824.0:0.0} de {total / 1073741824.0:0.0} GB · {used / total:P0}";
        OptRamBar.ScaleX = used / total;
    }

    private void RefreshApps()
    {
        _apps = Optimizer.ListApps();
        OptApps.ItemsSource = _apps;
    }

    private void BtnRefreshApps_Click(object sender, RoutedEventArgs e) => RefreshApps();

    private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
    {
        var toClose = _apps.Where(a => a.Selected).ToList();
        string list = toClose.Count == 0 ? "nenhum programa marcado"
                                         : string.Join("\n", toClose.Select(a => $"  • {a.Title}"));
        var r = MessageBox.Show("O RESET OTIMIZA vai:\n\n" +
                                "1. Limpar a memória em espera e enxugar a RAM dos programas em segundo plano\n" +
                                "   (o jogo e os programas abertos não são mexidos).\n" +
                                $"2. Fechar os programas marcados:\n{list}\n\n" +
                                "Salve o que estiver aberto nesses programas antes. Continuar?",
                                "RESET OTIMIZA", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        BtnOptimize.IsEnabled = false;
        BtnOptimize.Content = "OTIMIZANDO…";
        OptResult.Text = "";
        var closeResult = await Task.Run(() => Optimizer.CloseApps(toClose));
        var mem = await Task.Run(Optimizer.CleanMemory); // depois de fechar, para contar a memória liberada por eles também
        BtnOptimize.IsEnabled = true;
        BtnOptimize.Content = "RESET OTIMIZA";

        var parts = new List<string> { $"Memória liberada: {mem.FreedBytes / 1048576.0:N0} MB" };
        parts.Add(mem.StandbyPurged ? "memória em espera limpa" : $"memória em espera não limpa ({mem.StandbyError})");
        if (closeResult.Closed.Count > 0) parts.Add($"fechados: {string.Join(", ", closeResult.Closed)}");
        if (closeResult.NotClosed.Count > 0)
            parts.Add($"não fecharam (podem ter algo não salvo ou pedir confirmação): {string.Join(", ", closeResult.NotClosed)}");
        OptResult.Text = string.Join(" · ", parts) + ".";
        RefreshApps();
        UpdateRamStatus();
    }

    private void Tweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Tweak t } box) return;
        bool turnOn = box.IsChecked == true;
        try
        {
            t.Write(turnOn, _settings);
            _settings.Save();
            t.Refresh();
            // "power" e "ultimate" mexem no mesmo plano de energia: ligar um desliga o outro na tela também.
            if (t.Id is "power" or "ultimate")
                foreach (var o in _tweaks.Where(o => o != t && o.Id is "power" or "ultimate")) { o.Status = ""; o.Refresh(); }
            t.Status = t.IsOn == turnOn
                ? (turnOn ? "Aplicado." : "Desligado.") + (t.NeedsRestart ? " Reinicie o PC para valer." : "")
                : "O Windows não aceitou a mudança (pode exigir administrador).";
        }
        catch (Exception ex)
        {
            t.Refresh();
            t.Status = $"Falhou: {ex.Message}";
        }
        box.IsChecked = t.IsOn; // o binding é só leitura: volta ao estado real
    }

    private void BtnRestoreTweaks_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.TweakBackup.Count == 0)
        {
            MessageBox.Show("Nenhum ajuste foi alterado pelo RESET EXPLODE ainda.", "RESET OTIMIZA",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var r = MessageBox.Show("Voltar todos os ajustes do Windows para como estavam antes do RESET EXPLODE alterá-los?",
                                "RESET OTIMIZA", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        bool restart = _tweaks.Any(t => t.NeedsRestart && _settings.TweakBackup.ContainsKey(t.Id));
        WindowsTweaks.RestoreAll(_tweaks, _settings);
        foreach (var t in _tweaks) { t.Refresh(); t.Status = "Restaurado."; }
        if (restart) MessageBox.Show("Alguns ajustes só voltam ao original depois de reiniciar o PC.", "RESET OTIMIZA");
    }
}
