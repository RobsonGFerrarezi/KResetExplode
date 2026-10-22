using System.Windows;
using System.Windows.Controls;

namespace KResetExplode;

/// <summary>Checkup para jogos (RESET JOGOS) e Limpeza + ponto de restauração (Otimização).</summary>
public partial class MainWindow
{
    private bool _checkupInit, _checkupBusy, _cleanBusy;
    private List<CleanTarget> _cleanTargets = new();

    // ---------------- Checkup ----------------

    private void InitCheckup()
    {
        if (_checkupInit) return;
        _checkupInit = true;
        BtnCheckup_Click(this, new RoutedEventArgs());
    }

    private async void BtnCheckup_Click(object sender, RoutedEventArgs e)
    {
        if (_checkupBusy) return;
        _checkupBusy = true;
        BtnCheckup.IsEnabled = false;
        CheckupStatus.Text = "Verificando monitor, memória, ajustes do Windows, disco e driver de vídeo…";
        try
        {
            var items = await GameCheckup.RunAsync(_tweaks, _settings, CancellationToken.None);
            CheckupList.ItemsSource = items;
            int bad = items.Count(i => i.State == CheckState.Bad), warn = items.Count(i => i.State == CheckState.Warn);
            CheckupStatus.Text = bad + warn == 0
                ? $"Nada de errado encontrado ({items.Count} itens verificados às {DateTime.Now:HH:mm})."
                : $"{bad} problema(s) e {warn} ponto(s) de atenção em {items.Count} itens verificados às {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex) { CheckupStatus.Text = "Falhou: " + ex.Message; }
        finally { _checkupBusy = false; BtnCheckup.IsEnabled = true; }
    }

    private async void CheckupAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CheckItem item } btn) return;
        if (item.GoToPage != null) { GoToPage(item.GoToPage); return; }
        if (item.Fix == null) return;
        btn.IsEnabled = false;
        try
        {
            item.Result = await item.Fix();
            if (!item.Result.StartsWith("Não", StringComparison.Ordinal) && !item.Result.StartsWith("O Windows não", StringComparison.Ordinal))
                item.State = CheckState.Ok;
        }
        catch (Exception ex) { item.Result = "Falhou: " + ex.Message; btn.IsEnabled = true; }
    }

    /// <summary>Abre outra página do app (marca o item do menu correspondente).</summary>
    private void GoToPage(string page)
    {
        var nav = FindNav(this, page);
        if (nav != null) nav.IsChecked = true;

        // Árvore lógica (do XAML): inclui os itens de grupos recolhidos, que não estão na árvore visual.
        static RadioButton? FindNav(DependencyObject root, string tag)
        {
            foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (c is RadioButton r && r.Tag as string == tag) return r;
                if (FindNav(c, tag) is RadioButton found) return found;
            }
            return null;
        }
    }

    // ---------------- Limpeza ----------------

    private async void InitCleanup()
    {
        if (_cleanTargets.Count > 0 || _cleanBusy) return;
        await MeasureCleanupAsync();
    }

    private async Task MeasureCleanupAsync()
    {
        _cleanBusy = true;
        BtnClean.IsEnabled = false;
        CleanTitle.Text = "LIMPEZA · MEDINDO…";
        try
        {
            _cleanTargets = await Task.Run(Cleanup.Targets);
            CleanList.ItemsSource = _cleanTargets;
            UpdateCleanTitle();
        }
        catch (Exception ex) { CleanResult.Text = "Falhou: " + ex.Message; CleanTitle.Text = "LIMPEZA"; }
        finally { _cleanBusy = false; BtnClean.IsEnabled = true; }
    }

    private void UpdateCleanTitle()
    {
        long total = _cleanTargets.Sum(t => t.SizeBytes);
        CleanTitle.Text = $"LIMPEZA · {(total >= 1L << 30 ? $"{total / 1073741824.0:0.0} GB" : $"{total / 1048576.0:0} MB")} PARA LIBERAR";
    }

    private async void BtnCleanScan_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanBusy) return;
        CleanResult.Text = "";
        await MeasureCleanupAsync();
    }

    private async void BtnClean_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanBusy) return;
        var chosen = _cleanTargets.Where(t => t.Selected && t.SizeBytes > 0).ToList();
        if (chosen.Count == 0) { CleanResult.Text = "Nada marcado para limpar (ou tudo já está vazio)."; return; }
        var r = MessageBox.Show(this, "Apagar:\n\n" + string.Join("\n", chosen.Select(t => $"  • {t.Name} ({t.SizeText})")) +
                                "\n\nFeche os jogos antes. Depois de limpar o cache de shaders, a primeira partida de cada jogo pode travar um pouco enquanto ele recompila.",
                                "RESET OTIMIZA · Limpeza", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _cleanBusy = true;
        BtnClean.IsEnabled = false;
        BtnClean.Content = "LIMPANDO…";
        try
        {
            long freed = await Cleanup.CleanAsync(chosen, t => Dispatcher.BeginInvoke(new Action(() => CleanResult.Text = t)));
            long left = chosen.Sum(t => t.SizeBytes);
            CleanResult.Text = $"{freed / 1048576.0:N0} MB liberados." + (left > 1048576 ? $" {left / 1048576.0:N0} MB ficaram (arquivos em uso ou recentes)." : "");
            UpdateCleanTitle();
        }
        catch (Exception ex) { CleanResult.Text = "Falhou: " + ex.Message; }
        finally { _cleanBusy = false; BtnClean.IsEnabled = true; BtnClean.Content = "LIMPAR MARCADOS"; }
    }

    // ---------------- Ponto de restauração ----------------

    private async void BtnRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        BtnRestorePoint.IsEnabled = false;
        OptResult.Text = "Criando ponto de restauração (pode levar 1 minuto)…";
        try { OptResult.Text = await DriverManager.TryRestorePointAsync("RESET EXPLODE - antes de otimizar"); }
        finally { BtnRestorePoint.IsEnabled = true; }
    }
}
