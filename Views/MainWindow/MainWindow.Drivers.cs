using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KResetExplode;

/// <summary>Aba RESET DRIVER: inventário, NVIDIA (verifica e instala), AMD/placa-mãe (página oficial), Windows Update e backup.</summary>
public partial class MainWindow
{
    private bool _drvInit, _drvBusy;
    private List<GpuDriverStatus> _drvGpus = new();
    private BoardInfo _drvBoardInfo = new();
    private List<WuDriver> _drvWu = new();
    private List<PnpDevice> _drvDevices = new();
    private CancellationTokenSource? _drvCts;

    /// <summary>
    /// 30 s depois de abrir, confere se saiu driver de vídeo novo (NVIDIA/AMD) e avisa uma vez por versão.
    /// Sem internet ou se o site não responder, fica quieto.
    /// </summary>
    private async Task NotifyGpuDriverUpdateAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            foreach (var g in await Task.Run(DriverManager.ReadGpus))
            {
                if (g.Vendor == "NVIDIA") await DriverManager.CheckNvidiaAsync(g, false, CancellationToken.None);
                else if (g.Vendor == "AMD") await DriverManager.CheckAmdAsync(g, CancellationToken.None);
                else continue;
                if (!g.UpdateAvailable || _settings.DriverNotified == g.Latest) continue;
                _settings.DriverNotified = g.Latest;
                try { _settings.Save(); } catch { }
                Toast("Driver de vídeo novo", $"{g.GpuName}: {g.Latest} disponível (instalado: {g.Installed}).\nRESET FERRAMENTAS › Drivers para instalar.", "");
            }
        }
        catch { }
    }

    private void InitDriver()
    {
        if (_drvInit) return;
        _drvInit = true;
        BtnDrvScan_Click(this, new RoutedEventArgs());
    }

    private void DrvSet(string text, double? frac = null)
    {
        DrvStatus.Text = text;
        if (frac.HasValue) DrvProgress.ScaleX = Math.Clamp(frac.Value, 0, 1);
    }

    private void DrvBusy(bool busy)
    {
        _drvBusy = busy;
        BtnDrvScan.IsEnabled = !busy;
        BtnDrvBackup.IsEnabled = !busy;
        BtnDrvWuSearch.IsEnabled = !busy;
        BtnDrvInstall.IsEnabled = !busy && _drvGpus.Any(g => g.CanAutoInstall);
        BtnDrvWuInstall.IsEnabled = !busy && _drvWu.Count > 0;
        BtnDrvRestore.IsEnabled = !busy;
    }

    private async void BtnDrvScan_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        _drvCts = new CancellationTokenSource();
        try
        {
            DrvSet("Lendo drivers instalados…", 0.1);
            var inv = await Task.Run(DriverManager.Inventory);
            DrvList.ItemsSource = inv;

            DrvSet("Lendo placa de vídeo e placa-mãe…", 0.3);
            _drvGpus = await Task.Run(DriverManager.ReadGpus);
            _drvBoardInfo = await Task.Run(DriverManager.ReadBoard);
            ShowBoard();

            bool studio = DrvStudio.IsChecked == true;
            int i = 0;
            foreach (var g in _drvGpus)
            {
                DrvSet($"Consultando {g.Vendor.ToUpperInvariant()} para {g.GpuName}…", 0.4 + 0.5 * i / Math.Max(1, _drvGpus.Count));
                if (g.Vendor == "NVIDIA") await DriverManager.CheckNvidiaAsync(g, studio, _drvCts.Token);
                else if (g.Vendor == "AMD") await DriverManager.CheckAmdAsync(g, _drvCts.Token);
                else if (g.Vendor == "Intel") g.Message = $"{g.Vendor}: use a página oficial para ver a versão mais recente (instalado: {g.Installed}).";
                else g.Message = "Fabricante não reconhecido.";
                i++;
            }
            DrvGpus.ItemsSource = null;
            DrvGpus.ItemsSource = _drvGpus;

            DrvSet("Procurando dispositivos com problema…", 0.95);
            _drvDevices = await Task.Run(DriverUpdates.Devices);
            ShowProblems();
            ShowDownloadsInfo();
            DrvSet($"Pronto às {DateTime.Now:HH:mm:ss}.", 1);
        }
        catch (Exception ex) { DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }

    private void ShowBoard()
    {
        var b = _drvBoardInfo;
        DrvBoard.Text = $"{b.Maker} {b.Model}\nBIOS: {(b.Bios.Length > 0 ? b.Bios : "—")}" +
                        (b.Brand.Length > 0 ? $"\nFabricante reconhecido: {b.Brand}" : "\nFabricante não reconhecido");
        BtnDrvBoardPage.IsEnabled = b.SupportUrl.Length > 0;
    }

    /// <summary>Abre a página no navegador como usuário comum (não como administrador).</summary>
    private static void OpenUrl(string url) => ShellLauncher.TryOpenUrl(url);

    private void BtnDrvGpuPage_Click(object sender, RoutedEventArgs e)
    {
        var g = _drvGpus.FirstOrDefault(x => x.OfficialPage.Length > 0 && x.Vendor != "Intel") ?? _drvGpus.FirstOrDefault(x => x.OfficialPage.Length > 0);
        OpenUrl(g?.OfficialPage ?? "https://www.nvidia.com/Download/index.aspx");
    }

    private void BtnDrvBoardPage_Click(object sender, RoutedEventArgs e) => OpenUrl(_drvBoardInfo.SupportUrl);

    private async void BtnDrvInstall_Click(object sender, RoutedEventArgs e)
    {
        var g = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        if (g == null || _drvBusy) return;
        string msg = g.Vendor == "AMD"
            ? $"Baixar e abrir o instalador AMD Software {g.Latest}?\nO arquivo vem do servidor oficial da AMD."
            : g.UpdateAvailable
            ? $"Instalar o driver NVIDIA {g.Latest} ({g.SizeText})?\nO instalador oficial será baixado e aberto."
            : $"Você já está com a versão {g.Installed}. Baixar e abrir o instalador {g.Latest} mesmo assim (reinstalação)?";
        if (MessageBox.Show(this, msg, "RESET DRIVER", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        DrvBusy(true);
        _drvCts = new CancellationTokenSource();
        try
        {
            string r = await DriverManager.DownloadAndLaunchAsync(g, (t, f) => Dispatcher.BeginInvoke(new Action(() => DrvSet(t, f))), _drvCts.Token);
            DrvSet(r, 1);
        }
        catch (Exception ex) { DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }

    private async void BtnDrvWuSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        DrvSet("Procurando drivers no Windows Update (pode levar 1 minuto)…", 0.3);
        try
        {
            _drvWu = await DriverManager.SearchWindowsUpdateAsync();
            DrvSet("Comparando com os drivers instalados…", 0.8);
            _drvDevices = await Task.Run(DriverUpdates.Devices);
            DriverUpdates.Compare(_drvWu, _drvDevices);
            ShowWuList();
            ShowProblems();
            DrvSet($"{_drvWu.Count} driver(s) no Windows Update · {_drvWu.Count(w => w.Selected)} marcados para instalar.", 1);
        }
        catch (Exception ex) { DrvWuList.Text = "Falhou: " + ex.Message; DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }

    private void ShowWuList()
    {
        int newer = _drvWu.Count(w => w.Verdict is WuVerdict.Newer or WuVerdict.NoDriver);
        int older = _drvWu.Count(w => w.Verdict is WuVerdict.Older or WuVerdict.Same);
        DrvWuList.Text = _drvWu.Count == 0 ? "Nenhum driver novo no Windows Update."
            : $"{newer} atualização(ões) real(is)" + (older > 0 ? $" · {older} igual(is) ou mais antiga(s) que a instalada (desmarcadas)" : "") + ".";
        DrvWuItems.ItemsSource = null;
        DrvWuItems.ItemsSource = _drvWu;
    }

    private async void BtnDrvWuInstall_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _drvWu.Where(w => w.Selected).ToList();
        if (_drvBusy || _drvWu.Count == 0) return;
        if (chosen.Count == 0) { DrvSet("Nenhum driver marcado.", 0); return; }
        var risky = chosen.Where(w => w.Verdict is WuVerdict.Older or WuVerdict.Same).ToList();
        string warn = risky.Count == 0 ? "" :
            $"\n\nAtenção: {risky.Count} deles são iguais ou MAIS ANTIGOS que o instalado:\n" + string.Join("\n", risky.Select(w => "  • " + w.Name));
        if (MessageBox.Show(this, $"Instalar {chosen.Count} driver(s) do Windows Update? Pode ser preciso reiniciar.{warn}", "RESET DRIVER",
                MessageBoxButton.YesNo, risky.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        // Contratos de licença: o usuário lê e decide (antes eram aceitos automaticamente).
        var withEula = chosen.Where(d => d.EulaText != null).ToList();
        bool eulasAccepted = withEula.Count == 0 || ConfirmEulas(withEula);
        DrvBusy(true);
        try
        {
            string r = await DriverManager.InstallWindowsUpdateAsync(chosen, eulasAccepted, t => Dispatcher.BeginInvoke(new Action(() => DrvSet(t, 0.5))));
            DrvSet(r, 1);
            _drvWu = new();
            DrvWuItems.ItemsSource = null;
            DrvWuList.Text = r + " Clique em PROCURAR de novo para conferir.";
        }
        catch (Exception ex) { DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }

    // ---------------- Dispositivos com problema ----------------

    private void ShowProblems()
    {
        var list = DriverUpdates.Problems(_drvDevices, _drvWu);
        DrvProblems.ItemsSource = list;
        DrvProblemsEmpty.Text = list.Count == 0
            ? "Nenhum dispositivo com problema no Gerenciador de Dispositivos."
            : $"{list.Count} dispositivo(s) com problema. Clique em PROCURAR no Windows Update acima para ver se há driver para eles.";
    }

    private async void DrvProblemReinstall_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy || sender is not FrameworkElement { Tag: ProblemDevice d }) return;
        if (MessageBox.Show(this, $"Reinstalar \"{d.Name}\"?\n\nO dispositivo é removido e detectado de novo; o Windows usa o melhor driver que tiver.",
                "RESET DRIVER", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DrvBusy(true);
        try
        {
            DrvSet($"Reinstalando {d.Name}…", 0.5);
            string r = await DriverUpdates.ReinstallAsync(d);
            await Task.Delay(1500); // tempo para o Windows terminar de instalar antes de reler
            _drvDevices = await Task.Run(DriverUpdates.Devices);
            ShowProblems();
            DrvSet(r, 1);
        }
        catch (Exception ex) { DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }

    // ---------------- Backup: restaurar e limpar downloads ----------------

    private async void BtnDrvRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha a pasta do backup (KResetExplode-Drivers\\data-hora)",
            InitialDirectory = Directory.Exists(DriverUpdates.DefaultBackupRoot) ? DriverUpdates.DefaultBackupRoot : "",
        };
        if (dlg.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, $"Instalar todos os drivers de\n{dlg.FolderName}?\n\nSó pacotes com assinatura digital válida são aceitos pelo Windows.",
                "RESET DRIVER", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DrvBusy(true);
        try { DrvSet(await DriverUpdates.RestoreBackupAsync(dlg.FolderName, t => Dispatcher.BeginInvoke(new Action(() => DrvSet(t, 0.5)))), 1); }
        catch (Exception ex) { DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }

    private void ShowDownloadsInfo()
    {
        long b = DriverUpdates.DownloadsSize();
        DrvDownloadsInfo.Text = b > 0 ? $"Instaladores de driver baixados pelo app: {b / 1073741824.0:0.00} GB." : "";
        BtnDrvCleanDownloads.IsEnabled = b > 0;
    }

    private void BtnDrvCleanDownloads_Click(object sender, RoutedEventArgs e)
    {
        var (n, bytes) = DriverUpdates.CleanDownloads();
        DrvSet(n > 0 ? $"{n} instalador(es) apagado(s), {bytes / 1048576.0:0} MB liberados." : "Nada para apagar (ou o instalador ainda está aberto).", 1);
        ShowDownloadsInfo();
    }

    /// <summary>Mostra os contratos de licença completos, com rolagem, e devolve true só se o usuário clicar em ACEITAR.</summary>
    private bool ConfirmEulas(List<WuDriver> drivers)
    {
        string text = string.Join("\n\n", drivers.Select(d => $"=== {d.Title} ===\n\n{(d.EulaText!.Length > 0 ? d.EulaText : "(o Windows Update não forneceu o texto)")}"));
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12),
        };
        var accept = new Button { Content = "ACEITAR", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = false };
        var decline = new Button { Content = "RECUSAR", Padding = new Thickness(16, 6, 16, 6), IsCancel = true, IsDefault = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        buttons.Children.Add(accept);
        buttons.Children.Add(decline);
        var info = new TextBlock
        {
            Text = $"{drivers.Count} driver(s) pedem que você aceite um contrato de licença. Se recusar, eles não são instalados (os outros, sim).",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 12, 12, 0),
        };
        var root = new DockPanel();
        DockPanel.SetDock(info, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(info);
        root.Children.Add(buttons);
        root.Children.Add(box);
        var w = new Window
        {
            Title = "RESET DRIVER · Contratos de licença", Owner = this, Width = 720, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root, ShowInTaskbar = false,
        };
        w.SetResourceReference(BackgroundProperty, "Bg"); // segue o tema claro/escuro do app
        info.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        accept.Click += (_, _) => w.DialogResult = true;
        return w.ShowDialog() == true;
    }

    private async void BtnDrvBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        _drvCts = new CancellationTokenSource();
        try
        {
            string r = await DriverManager.BackupAsync(t => Dispatcher.BeginInvoke(new Action(() => DrvSet(t, 0.5))), _drvCts.Token);
            DrvSet(r, 1);
        }
        catch (Exception ex) { DrvSet("Falhou: " + ex.Message, 0); }
        finally { DrvBusy(false); }
    }
}
