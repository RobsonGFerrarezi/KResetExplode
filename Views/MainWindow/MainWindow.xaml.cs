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

public partial class MainWindow : Window
{
    private readonly SensorMonitor _monitor;
    private readonly DispatcherTimer _timer;
    private readonly ICollectionView _view;
    private string? _category;   // filtro por chip (null = todos)
    private string _search = ""; // filtro por texto

    // Configurações (pasta protegida, ver AppSettings)
    private readonly AppSettings _settings = AppSettings.Load();

    /// <summary>Barra de valor (rótulo, texto, fração 0..1) usada nos históricos e medidores.</summary>
    public sealed record MeterItem(string Label, string Value, double Fraction);

    private Brush Fg => (Brush)FindResource("Fg");
    private Brush Ink => (Brush)FindResource("Ink");
    private Brush Muted => (Brush)FindResource("Muted");
    private Brush Card => (Brush)FindResource("Card");

    public MainWindow()
    {
        _tweaks = WindowsTweaks.All(_settings);
        InitializeComponent();
        ThemeManager.Apply(_settings.LightTheme);
        UpdateThemeButton();
        ShowPage("PageDash");

        // Sensores: a lista começa vazia e é preenchida quando a detecção do hardware termina (LoadHardwareAsync).
        _monitor = new SensorMonitor();
        _view = CollectionViewSource.GetDefaultView(_monitor.Rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
        SensorGrid.ItemsSource = _view;
        SystemSub.Text = "Lendo informações do sistema…";
        StatusText.Text = "Detectando o hardware…";

        InitBench();
        SnapshotUsb();
        RefreshDrives();
        TitlePage.Text = "Visão geral";
        UpdateDashboard();

        // A janela aparece já; as coletas pesadas (WMI, sensores, GPU) rodam em paralelo fora da thread da janela.
        Loaded += async (_, _) => await LoadHardwareAsync();
        Loaded += (_, _) => _ = NotifyGpuDriverUpdateAsync();
        SideVersion.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? ""); // do .csproj

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            // Durante a cena de GPU as leituras de sensores (NVML/driver) ficam pausadas: cada consulta trava o driver
            // por um instante e aparecia como engasgo no 1% low do benchmark.
            if (_gpuBenchActive) return;
            _monitor.Refresh();
            if (_monitor.IsOpen) StatusText.Text = $"{_monitor.Rows.Count} sensores · atualizado {DateTime.Now:HH:mm:ss}";
            UpdateStressPanel();
            UpdateGpuLive();
            UpdateCpuSensorWarning();
            CollectDashSample();
            UpdateBottleneck();
            if (PageBench.IsVisible) UpdateBenchLive();
            if (PageOptimize.IsVisible) UpdateRamStatus();
            AlertsTick();
        };
        _timer.Start();

        StateChanged += (_, _) =>
        {
            // Sem borda nativa, a janela maximizada vaza ~7 px para fora da tela.
            bool max = WindowState == WindowState.Maximized;
            Root.Margin = max ? new Thickness(7) : new Thickness(0);
            BtnMax.Content = max ? "" : "";
        };
        Closed += (_, _) =>
        {
            _timer.Stop(); _overlayTimer?.Stop();
            _stress.Stop(); _burn.Stop(); _benchCts?.Cancel();
            _overlay?.Close();
            _toast?.Close();
            try { WindowsTweaks.UpdateGameQuiet(_settings, gameRunning: false); } catch { } // devolve as notificações
            _frames.Dispose();
            UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, HotkeyId);
            ShutdownInteg();
            _launchCts?.Cancel();
            _monitor.Dispose();
        };
    }

    /// <summary>Coletas lentas da abertura, em paralelo e fora da thread da janela; cada parte preenche a tela quando fica pronta.</summary>
    private async Task LoadHardwareAsync()
    {
        async Task LoadInfo()
        {
            try
            {
                var info = await Task.Run(SystemInfoCollector.Collect);
                InfoCategories.ItemsSource = info;
                InfoCategories.SelectedIndex = 0;
                SystemSub.Text = $"{Environment.MachineName} · {info.Count} categorias";
            }
            catch (Exception ex) { SystemSub.Text = $"Não foi possível ler as informações do sistema: {ex.Message}"; }
        }

        async Task LoadSensors()
        {
            try { await _monitor.OpenAsync(); }
            catch (Exception ex) { StatusText.Text = $"Sensores indisponíveis: {ex.Message}"; return; }
            _monitor.Refresh();
            UpdateCpuSensorWarning();
            AddChip("Todos", null, isChecked: true);
            foreach (var c in _monitor.Rows.Select(r => r.Category).Distinct().OrderBy(x => x))
                AddChip(c, c);
            UpdateDashboard();
        }

        async Task LoadGpu()
        {
            try { _gpus = await Task.Run(GpuInfoCollector.Collect); }
            catch { _gpus = new(); }
            for (int i = 0; i < _gpus.Count && _gpus.Count > 1; i++)
            {
                var g = _gpus[i];
                var chip = new RadioButton { Content = g.Name, GroupName = "Gpu", Style = (Style)FindResource("Chip"), IsChecked = i == 0 };
                chip.Checked += (_, _) => ShowGpu(g);
                GpuChips.Children.Add(chip);
            }
            if (_gpus.Count > 0) ShowGpu(_gpus[0]);

            // Dependem do nome da GPU/CPU
            InitGames();
            InitBottleneckAndOverlay();
            InitOc();
            UpdateDashboard();
        }

        await Task.WhenAll(LoadInfo(), LoadSensors(), LoadGpu());
    }

    // ---------------- Janela / navegação ----------------

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageSystem == null) return; // disparado durante InitializeComponent
        var item = (FrameworkElement)sender;
        string page = (string)item.Tag;
        ShowPage(page);

        // Abre a categoria da página (ex.: quando o painel pula direto para o Benchmark)
        // e mostra "CATEGORIA · página" na barra de título.
        string name = item is ContentControl { Content: Panel panel }
            ? panel.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? "" : "";
        if (item.Parent is FrameworkElement { Name: var items } && items.EndsWith("Items")
            && FindName(items[..^5]) is System.Windows.Controls.Primitives.ToggleButton group)
        {
            group.IsChecked = true;
            name = $"{group.Content} · {name}";
        }
        TitlePage.Text = name;

        // Algumas páginas carregam dados só quando abertas.
        switch (page)
        {
            case "PageDash": UpdateDashboard(); break;
            case "PageDisks": RefreshDiskHealth(); break;
            case "PageEnergy": RefreshEnergy(); break;
            case "PageOc": UpdateOcGpuState(); UpdateRecentErrors(); break;
            case "PageInteg": InitInteg(); break;
            case "PageLauncher": InitLauncher(); break;
            case "PageDriver": InitDriver(); break;
            case "PageStartup": ScanStartup(); break;
            case "PageApps": InitApps(); break;
            case "PageMakerPorts": ScanUsb(); break;
            case "PageMakerDrv": InitMakerDrivers(); break;
            case "PageOptimize": InitOptimize(); InitCleanup(); break;
            case "PageCheckup": InitCheckup(); break;
            case "PageAlerts": InitAlerts(); break;
            case "PageCompare": UpdateCompare(); break;
        }
    }

    private void ShowPage(string name)
    {
        foreach (var p in new FrameworkElement[] { PageDash, PageCheckup, PageRepair, PageSystem, PageSensors, PageGpu, PageGames, PageBottleneck, PageOverlay, PageBench, PageOptimize, PageOc, PageAlerts, PageCompare, PageInteg, PageLauncher, PageDriver, PageStartup, PageApps, PageMakerPorts, PageMakerDrv, PageDisks, PageEnergy, PageNet, PageStress })
            p.Visibility = p.Name == name ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool RequireAdmin()
    {
        if (SensorMonitor.IsElevated) return true;
        MessageBox.Show("Esta ação precisa do RESET EXPLODE aberto como administrador.", "RESET EXPLODE",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
