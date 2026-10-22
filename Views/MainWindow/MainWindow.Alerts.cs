using System.Globalization;
using System.Windows;

namespace KResetExplode;

/// <summary>
/// RESET EXPLODE › Alertas: avisos (ToastWindow) de temperatura alta de CPU/GPU, disco com saúde crítica e bateria desgastada.
/// Funcionam só enquanto o app está aberto (inclusive minimizado), pelo timer de 1 s dos sensores: nada de serviço do Windows
/// nem tarefa agendada. Também é aqui que o ajuste "Silenciar notificações durante o jogo" é acompanhado.
/// </summary>
public partial class MainWindow
{
    public sealed record AlertEntry(string Time, string Title, string Text);

    private readonly List<AlertEntry> _alertLog = new();
    private int _alertTicks;
    private readonly Dictionary<string, int> _hotSeconds = new();      // "cpu"/"gpu" → segundos seguidos acima do limite
    private readonly Dictionary<string, DateTime> _hotLastAlert = new();
    private readonly HashSet<string> _hotAlerted = new();              // já avisou; rearma quando esfria 5 °C abaixo do limite
    private readonly HashSet<string> _healthNotified = new();          // discos e bateria já avisados nesta sessão
    private bool _healthBusy;
    private DateTime? _healthLastCheck;
    private bool _alertsInit;

    private const int HotSeconds = 10;                                 // ignora picos curtos
    private static readonly TimeSpan HotRepeat = TimeSpan.FromMinutes(10);
    private const string AlertIcon = "";                          // Segoe MDL2: aviso

    /// <summary>Chamado a cada segundo pelo timer dos sensores (que continua rodando com a janela minimizada).</summary>
    private void AlertsTick()
    {
        _alertTicks++;
        if (_alertTicks % 5 == 0)
            try { WindowsTweaks.UpdateGameQuiet(_settings, _launchCts != null || WindowsTweaks.FullScreenAppRunning()); } catch { }

        var a = _settings.Alerts;
        if (PageAlerts.IsVisible) UpdateAlertsNow();
        if (!a.Enabled) return;
        // Durante o stress test a temperatura é acompanhada pelo próprio teste (que para sozinho no limite).
        if (!TestRunning)
        {
            CheckHot("cpu", "CPU", a.Cpu, a.CpuLimit, _monitor.CpuTemperature);
            CheckHot("gpu", "Placa de vídeo", a.Gpu, a.GpuLimit, MainGpuTemp());
        }
        // Disco e bateria: 2 min depois de abrir (para não disputar com a detecção do hardware) e depois a cada 30 min.
        if (_alertTicks == 120 || _alertTicks % 1800 == 0) CheckHealthAsync(manual: false);
    }

    private void CheckHot(string id, string name, bool on, double limit, float? temp)
    {
        if (!on || temp is not float t) { _hotSeconds[id] = 0; return; }
        if (t < limit - 5) _hotAlerted.Remove(id);
        _hotSeconds[id] = t >= limit ? _hotSeconds.GetValueOrDefault(id) + 1 : 0;
        if (_hotSeconds[id] < HotSeconds || _hotAlerted.Contains(id)) return;
        if (_hotLastAlert.TryGetValue(id, out var last) && DateTime.Now - last < HotRepeat) return;
        _hotAlerted.Add(id);
        _hotLastAlert[id] = DateTime.Now;
        AddAlert($"{name} quente: {t:0} °C",
                 $"Passou de {limit:0} °C por mais de {HotSeconds} s. Confira se as ventoinhas giram e se há poeira nos dissipadores.");
    }

    /// <summary>Discos (S.M.A.R.T.) e bateria. Leitura do WMI fora da thread da janela; cada problema é avisado uma vez por sessão.</summary>
    private async void CheckHealthAsync(bool manual)
    {
        if (_healthBusy) return;
        _healthBusy = true;
        if (manual) AlertStatus.Text = "Verificando discos e bateria…";
        try
        {
            var a = _settings.Alerts;
            var sensors = _monitor.Rows.ToList();
            var disks = a.Disk ? await Task.Run(() => DiskHealth.Read(sensors)) : new List<DiskHealthCard>();
            double? wear = a.Battery ? await Task.Run(BatteryInfo.MaxWear) : null;
            _healthLastCheck = DateTime.Now;

            var bad = disks.Where(d => d.Verdict == DiskVerdict.Critical).ToList();
            foreach (var d in bad)
                if (_healthNotified.Add("disk:" + (d.Serial.Length > 0 ? d.Serial : d.Title)))
                    AddAlert("Disco com saúde crítica",
                             $"{d.Title}: {FirstSentence(d.Reasons)} Faça backup do que for importante. Detalhes em RESET EXPLODE › Discos.");
            bool wornOut = wear is double w && w >= a.BatteryWear;
            if (wornOut && _healthNotified.Add("battery"))
                AddAlert("Bateria desgastada",
                         $"Perdeu {wear:0}% da capacidade original: a autonomia caiu na mesma proporção. Detalhes em RESET EXPLODE › Energia.");

            if (manual)
            {
                var parts = new List<string>();
                if (a.Disk) parts.Add(bad.Count == 0 ? $"{disks.Count(d => d.Verdict != DiskVerdict.Unknown)} disco(s) sem problema crítico" : $"{bad.Count} disco(s) com saúde crítica");
                if (a.Battery) parts.Add(wear is double ww ? $"bateria com {ww:0}% de desgaste" + (wornOut ? " (acima do limite)" : "") : "sem bateria");
                AlertStatus.Text = parts.Count == 0 ? "Disco e bateria estão desligados nos alertas." : $"Verificado às {DateTime.Now:HH:mm}: {string.Join(" · ", parts)}.";
            }
        }
        catch (Exception ex) { if (manual) AlertStatus.Text = $"Não foi possível verificar: {ex.Message}"; }
        finally { _healthBusy = false; }
    }

    private static string FirstSentence(string s)
    {
        int i = s.IndexOf(". ", StringComparison.Ordinal);
        return i > 0 ? s[..(i + 1)] : s;
    }

    private void AddAlert(string title, string text)
    {
        _alertLog.Insert(0, new AlertEntry(DateTime.Now.ToString("dd/MM HH:mm"), title, text));
        if (_alertLog.Count > 30) _alertLog.RemoveAt(_alertLog.Count - 1);
        Toast(title, text, AlertIcon);
        if (PageAlerts.IsVisible) ShowAlertLog();
    }

    // ---------------- Página ----------------

    private void InitAlerts()
    {
        if (!_alertsInit)
        {
            _alertsInit = true;
            var a = _settings.Alerts;
            AlertOn.IsChecked = a.Enabled;
            AlertCpu.IsChecked = a.Cpu;
            AlertGpu.IsChecked = a.Gpu;
            AlertDisk.IsChecked = a.Disk;
            AlertBattery.IsChecked = a.Battery;
            AlertCpuLimit.Text = a.CpuLimit.ToString("0", CultureInfo.CurrentCulture);
            AlertGpuLimit.Text = a.GpuLimit.ToString("0", CultureInfo.CurrentCulture);
            AlertBatteryWear.Text = a.BatteryWear.ToString("0", CultureInfo.CurrentCulture);
            UpdateAlertOptionsEnabled();
            AlertBatteryNote.Text = GameCheckup.HasBattery() ? "" : "Este PC não tem bateria: o aviso de bateria não se aplica.";
        }
        if (string.IsNullOrEmpty(AlertStatus.Text))
            AlertStatus.Text = _healthLastCheck is DateTime t ? $"Discos e bateria verificados pela última vez às {t:HH:mm}."
                                                             : "Discos e bateria são verificados 2 minutos depois de abrir o app e a cada 30 minutos.";
        UpdateAlertsNow();
        ShowAlertLog();
    }

    private void ShowAlertLog()
    {
        AlertLog.ItemsSource = _alertLog.ToList();
        AlertLogEmpty.Visibility = _alertLog.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAlertsNow()
    {
        AlertNowCpu.Text = _monitor.CpuTemperature is float c ? $"{c:0} °C" : "—";
        AlertNowGpu.Text = MainGpuTemp() is float g ? $"{g:0} °C" : "—";
    }

    private void UpdateAlertOptionsEnabled()
    {
        bool on = AlertOn.IsChecked == true;
        AlertOptions.IsEnabled = on;
        AlertOptions.Opacity = on ? 1 : 0.5;
        AlertCpuLimit.IsEnabled = AlertCpu.IsChecked == true;
        AlertGpuLimit.IsEnabled = AlertGpu.IsChecked == true;
        AlertBatteryWear.IsEnabled = AlertBattery.IsChecked == true;
    }

    private void AlertOption_Click(object sender, RoutedEventArgs e)
    {
        UpdateAlertOptionsEnabled();
        SaveAlerts(quiet: true);
    }

    private void BtnAlertSave_Click(object sender, RoutedEventArgs e) => SaveAlerts(quiet: false);

    /// <summary>Grava as opções; um limite inválido mantém o valor anterior e avisa.</summary>
    private void SaveAlerts(bool quiet)
    {
        var a = _settings.Alerts;
        var errors = new List<string>();
        double Parse(System.Windows.Controls.TextBox box, double current, double min, double max, string what)
        {
            if (double.TryParse(box.Text.Replace("°", "").Replace("%", "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double v)
                && v >= min && v <= max) return v;
            errors.Add($"{what} deve ficar entre {min:0} e {max:0}");
            box.Text = current.ToString("0", CultureInfo.CurrentCulture);
            return current;
        }
        a.Enabled = AlertOn.IsChecked == true;
        a.Cpu = AlertCpu.IsChecked == true;
        a.Gpu = AlertGpu.IsChecked == true;
        a.Disk = AlertDisk.IsChecked == true;
        a.Battery = AlertBattery.IsChecked == true;
        a.CpuLimit = Parse(AlertCpuLimit, a.CpuLimit, 50, 110, "O limite da CPU");
        a.GpuLimit = Parse(AlertGpuLimit, a.GpuLimit, 50, 110, "O limite da GPU");
        a.BatteryWear = Parse(AlertBatteryWear, a.BatteryWear, 10, 90, "O desgaste da bateria");
        _hotAlerted.Clear(); // limite novo vale na hora
        try { _settings.Save(); }
        catch (Exception ex) { AlertStatus.Text = $"Não foi possível salvar: {ex.Message}"; return; }
        if (errors.Count > 0) AlertStatus.Text = string.Join("; ", errors) + ". Os outros valores foram salvos.";
        else if (!quiet) AlertStatus.Text = a.Enabled ? "Salvo. Os alertas valem enquanto o RESET EXPLODE estiver aberto (pode minimizar)." : "Salvo. Alertas desligados.";
    }

    private void BtnAlertTest_Click(object sender, RoutedEventArgs e) =>
        Toast("Teste de alerta", "Os avisos aparecem assim, neste canto da tela, mesmo com o RESET EXPLODE minimizado.", AlertIcon);

    private void BtnAlertCheck_Click(object sender, RoutedEventArgs e)
    {
        SaveAlerts(quiet: true);
        CheckHealthAsync(manual: true);
    }
}
