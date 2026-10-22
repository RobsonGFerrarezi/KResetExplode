using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KResetExplode;

/// <summary>RESET DESEMPENHO › Overclock: ajustes da GPU (NVML), verificador de estabilidade e guia de overclock na BIOS.</summary>
public partial class MainWindow
{
    public sealed record GuideStep(string Number, string Title, string Where, string Text);

    private uint? _ocClockCap;      // teto de clock aplicado agora (null = sem teto)
    private uint? _ocPendingCap;    // teto escolhido nos chips, aplicado ao clicar em APLICAR
    private CancellationTokenSource? _ocCts;
    private bool _ocRunning;

    // Limites de segurança do verificador de estabilidade.
    private const float OcCpuTempLimit = 95f;
    private const float OcGpuTempLimit = 88f;
    private const int NvmlErrorNotSupported = 3;

    /// <summary>Handle NVML da GPU principal (IntPtr.Zero quando não é NVIDIA).</summary>
    private nint OcGpu => _gpus.Count > 0 ? _gpus[0].NvmlHandle : IntPtr.Zero;

    private static string Watts(uint? milliwatts) => milliwatts is uint mw ? $"{mw / 1000.0:0} W" : "—";

    private void InitOc()
    {
        nint gpu = OcGpu;
        OcGpuTitle.Text = _gpus.Count > 0 ? "GPU · " + GameCatalog.ShortGpuName(_gpus[0].Name).ToUpperInvariant() : "GPU";
        if (gpu == IntPtr.Zero)
        {
            OcGpuState.Text = "Ajustes disponíveis só para placas NVIDIA (via driver oficial).";
            OcPower.IsEnabled = BtnOcApply.IsEnabled = false;
        }
        else
        {
            if (Nvml.PowerConstraintsMw(gpu) is (uint minMw, uint maxMw))
            {
                OcPower.Minimum = minMw / 1000.0;
                OcPower.Maximum = maxMw / 1000.0;
            }
            if (Nvml.PowerLimitMw(gpu) is uint limitMw) OcPower.Value = limitMw / 1000.0;

            uint maxClock = Nvml.MaxClock(gpu, 0).GetValueOrDefault(3000u);
            AddClockChip("Sem teto", null, isChecked: true);
            AddClockChip($"Leve · {maxClock - 300} MHz", maxClock - 300, isChecked: false);
            AddClockChip($"Equilibrado · {maxClock - 500} MHz", maxClock - 500, isChecked: false);
            AddClockChip($"Econômico · {maxClock - 800} MHz", maxClock - 800, isChecked: false);
            UpdateOcGpuState();
        }
        BuildOcGuide();
        UpdateOcHistory();
        UpdateRecentErrors();

        void AddClockChip(string label, uint? cap, bool isChecked)
        {
            var chip = new RadioButton { Content = label, GroupName = "OcClock", Style = (Style)FindResource("Chip"), IsChecked = isChecked };
            chip.Checked += (_, _) => _ocPendingCap = cap;
            OcClockPanel.Children.Add(chip);
        }
    }

    private void UpdateOcGpuState()
    {
        nint gpu = OcGpu;
        if (gpu == IntPtr.Zero) return;
        string cap = _ocClockCap is uint c ? $"{c} MHz" : "nenhum";
        string clock = Nvml.Clock(gpu, 0) is uint mhz ? $"{mhz} MHz" : "—";
        OcGpuState.Text = $"Limite de energia atual {Watts(Nvml.PowerLimitMw(gpu))} (padrão {Watts(Nvml.PowerDefaultMw(gpu))}) · " +
                          $"teto de clock: {cap} · clock máximo atual: {clock}";
    }

    private void OcPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OcPowerValue != null) OcPowerValue.Text = $"{OcPower.Value:0} W";
    }

    private void BtnOcApply_Click(object sender, RoutedEventArgs e)
    {
        nint gpu = OcGpu;
        if (gpu == IntPtr.Zero || !RequireAdmin()) return;

        var messages = new List<string>();
        string? powerError = Nvml.Explain(Nvml.SetPowerLimitMw(gpu, (uint)(OcPower.Value * 1000.0)));
        messages.Add(powerError != null ? $"Energia: não aplicado ({powerError})." : $"Energia: {OcPower.Value:0} W aplicado.");

        int clockCode = _ocPendingCap is uint pending ? Nvml.LockGpuClocks(gpu, 210u, pending) : Nvml.ResetGpuClocks(gpu);
        string? clockError = Nvml.Explain(clockCode);
        if (clockError != null) messages.Add($"Teto de clock: não aplicado ({clockError}).");
        else
        {
            _ocClockCap = _ocPendingCap;
            messages.Add(_ocClockCap is uint applied ? $"Teto de clock: {applied} MHz aplicado." : "Teto de clock: removido.");
        }
        OcGpuResult.Text = string.Join(" ", messages) + " Rode o verificador para confirmar a estabilidade.";
        UpdateOcGpuState();
    }

    private void BtnOcReset_Click(object sender, RoutedEventArgs e)
    {
        nint gpu = OcGpu;
        if (gpu == IntPtr.Zero || !RequireAdmin()) return;

        int powerCode = Nvml.PowerDefaultMw(gpu) is uint defaultMw ? Nvml.SetPowerLimitMw(gpu, defaultMw) : NvmlErrorNotSupported;
        int clockCode = Nvml.ResetGpuClocks(gpu);
        if (Nvml.PowerDefaultMw(gpu) is uint newDefaultMw) OcPower.Value = newDefaultMw / 1000.0;
        foreach (var chip in OcClockPanel.Children.OfType<RadioButton>())
            chip.IsChecked = chip.Content as string == "Sem teto";
        if (clockCode == 0) _ocClockCap = null;

        string? error = Nvml.Explain(powerCode) ?? Nvml.Explain(clockCode);
        OcGpuResult.Text = error != null ? $"Restauração parcial: {error}." : "GPU de volta ao padrão de fábrica.";
        UpdateOcGpuState();
    }

    private void UpdateRecentErrors()
    {
        var errors = OcCheck.ErrorsSince(DateTime.Now.AddDays(-7.0));
        OcRecentErrors.Text = errors.Error != null ? $"Não foi possível ler o registro: {errors.Error}"
            : errors.Total == 0 ? "Nenhum erro de hardware nem reinício do driver de vídeo nos últimos 7 dias."
            : $"Últimos 7 dias: {errors.Whea} erros WHEA · {errors.GpuResets} reinícios do driver de vídeo.\nÚltimo: {errors.LastDetail}";
    }

    private void UpdateOcHistory() =>
        OcHistory.ItemsSource = _settings.BenchRecords.Where(r => r.Test == "oc")
                                         .Select(r => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, r.Score)).ToList();

    /// <summary>Verificador de estabilidade: mede a nota, roda o stress pelo tempo escolhido e procura erros de hardware.</summary>
    private async void BtnOcCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_ocRunning) { _ocCts?.Cancel(); return; }
        if (TestRunning || AnyBenchRunning) { OcStatus.Text = "Termine o outro teste antes."; return; }

        string target = CheckedTag(OcTargetPanel) ?? "cpu";
        bool testCpu = target is "cpu" or "both";
        bool testGpu = target is "gpu" or "both";
        var duration = TimeSpan.FromMinutes(int.Parse(CheckedTag(OcDurPanel) ?? "15"));
        string label = OcLabel.Text.Trim().Length > 0 ? OcLabel.Text.Trim() : "sem descrição";
        string adapterName = _gpus.Count > 0 ? _gpus[0].Name : "";

        _ocCts = new CancellationTokenSource();
        var ct = _ocCts.Token;
        SetOcCheckRunning(true);
        OcVerdictCard.Visibility = Visibility.Collapsed;

        var start = DateTime.Now;
        double? cpuScore = null, gpuScore = null;
        float maxCpuTemp = 0f, maxGpuTemp = 0f;
        double cpuWattsSum = 0, gpuWattsSum = 0;
        int cpuWattsSamples = 0, gpuWattsSamples = 0;
        string? failure = null;
        var cpuStress = new StressTester();
        var gpuBurn = new GpuBurn();
        try
        {
            if (testCpu)
            {
                OcStatus.Text = "Medindo a nota da CPU (render multi-core)…";
                var bench = await Task.Run(() => _bench.Run(Environment.ProcessorCount, TimeSpan.Zero, ct), ct);
                if (!ct.IsCancellationRequested) cpuScore = bench.Score;
            }
            if (testGpu && !ct.IsCancellationRequested)
            {
                OcStatus.Text = "Medindo a nota da GPU (cena de jogo, 30 s)…";
                var scene = new GpuGameBench { Duration = TimeSpan.FromSeconds(30.0), AdapterName = adapterName };
                _gpuBenchActive = true;
                using (ct.Register(scene.Cancel))
                {
                    var result = await Task.Factory.StartNew(scene.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    if (result != null) gpuScore = result.Score;
                    else if (!scene.Cancelled) failure = $"a cena de GPU falhou ({scene.Error})";
                }
                _gpuBenchActive = false;
            }

            if (failure == null && !ct.IsCancellationRequested)
            {
                if (testCpu) cpuStress.Start(cpu: true, ram: true);
                if (testGpu)
                    gpuBurn.Start(new GpuBurnSettings { Width = 1920, Height = 1080, Msaa = 4, Shells = 128, Iterations = 48, AdapterName = adapterName });

                var elapsed = Stopwatch.StartNew();
                while (elapsed.Elapsed < duration && !ct.IsCancellationRequested)
                {
                    await Task.Delay(1000);
                    float? cpuTemp = _monitor.CpuTemperature;
                    float? gpuTemp = MainGpuTemp();
                    if (cpuTemp is float cpuNow) maxCpuTemp = Math.Max(maxCpuTemp, cpuNow);
                    if (gpuTemp is float gpuNow) maxGpuTemp = Math.Max(maxGpuTemp, gpuNow);
                    if (testCpu && _monitor.CpuPower is float cpuWatts) { cpuWattsSum += cpuWatts; cpuWattsSamples++; }
                    if (testGpu && MainGpuPower() is float gpuWatts) { gpuWattsSum += gpuWatts; gpuWattsSamples++; }

                    OcProgress.ScaleX = elapsed.Elapsed / duration;
                    string what = testCpu && testGpu ? "CPU + RAM + GPU" : testCpu ? "CPU + RAM" : "GPU";
                    OcStatus.Text = $"Stress {what} · {elapsed.Elapsed:mm\\:ss} de {duration:mm\\:ss} · " +
                                    $"máx. CPU {(maxCpuTemp > 0f ? $"{maxCpuTemp:0} °C" : "—")} · máx. GPU {(maxGpuTemp > 0f ? $"{maxGpuTemp:0} °C" : "—")}";

                    if (testCpu && cpuTemp >= OcCpuTempLimit) { failure = $"CPU chegou a {cpuTemp:0} °C (limite de segurança {OcCpuTempLimit:0} °C)"; break; }
                    if (testGpu && gpuTemp >= OcGpuTempLimit) { failure = $"GPU chegou a {gpuTemp:0} °C (limite de segurança {OcGpuTempLimit:0} °C)"; break; }
                    if (testCpu && cpuStress.MemoryErrors > 0) { failure = $"{cpuStress.MemoryErrors} erros de memória (RAM/EXPO instável)"; break; }
                    if (testGpu && !gpuBurn.Running)
                    {
                        failure = gpuBurn.Error != null ? $"a GPU falhou ({gpuBurn.Error})" : "a janela do teste de GPU foi fechada";
                        break;
                    }
                    if ((int)elapsed.Elapsed.TotalSeconds % 5 == 0 && OcCheck.ErrorsSince(start) is { Total: > 0 } during)
                    {
                        failure = HardwareErrorText(during);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            cpuStress.Stop();
            gpuBurn.Stop();
            _gpuBenchActive = false;
        }

        SetOcCheckRunning(false);
        if (ct.IsCancellationRequested && failure == null) { OcStatus.Text = "Verificação cancelada."; return; }

        var errors = OcCheck.ErrorsSince(start);
        if (failure == null && errors.Total > 0) failure = HardwareErrorText(errors);
        bool stable = failure == null;
        ShowOcVerdict(stable, start, duration, failure);

        var rows = new List<KeyValuePair<string, string>> { new("Configuração", label) };
        if (cpuScore is double cs) rows.Add(new("Nota CPU (render)", $"{cs:N0} pts"));
        if (gpuScore is double gs) rows.Add(new("Nota GPU (cena)", $"{gs:N0} pts"));
        if (maxCpuTemp > 0f) rows.Add(new("Temp. máx. CPU", $"{maxCpuTemp:0} °C"));
        if (maxGpuTemp > 0f) rows.Add(new("Temp. máx. GPU", $"{maxGpuTemp:0} °C"));
        if (cpuWattsSamples > 0) rows.Add(new("Consumo médio CPU", $"{cpuWattsSum / cpuWattsSamples:0} W"));
        if (gpuWattsSamples > 0) rows.Add(new("Consumo médio GPU", $"{gpuWattsSum / gpuWattsSamples:0} W"));
        rows.Add(new("Erros WHEA / resets de vídeo", $"{errors.Whea} / {errors.GpuResets}"));
        OcResult.ItemsSource = rows;
        OcResult.Foreground = stable ? Fg : Ink;
        OcVerdictCard.Visibility = Visibility.Visible;

        string summary = (stable ? "estável" : "INSTÁVEL") + " · " + label
                       + (cpuScore is double c2 ? $" · CPU {c2:N0}" : "")
                       + (gpuScore is double g2 ? $" · GPU {g2:N0}" : "");
        // Score no histórico: barra cheia = estável, curta = instável.
        _settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, "oc", stable ? 1.0 : 0.15, summary));
        _settings.Save();
        UpdateOcHistory();
        UpdateRecentErrors();
    }

    private static string HardwareErrorText(HardwareErrors e) =>
        $"o Windows registrou {e.Whea} erros WHEA e {e.GpuResets} reinícios do driver de vídeo";

    private void SetOcCheckRunning(bool running)
    {
        _ocRunning = running;
        BtnOcCheck.Content = running ? "CANCELAR" : "INICIAR VERIFICAÇÃO";
        BtnOcCheck.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
        OcTargetPanel.IsEnabled = OcDurPanel.IsEnabled = !running;
        if (!running) OcProgress.ScaleX = 0.0;
    }

    /// <summary>Cartão do veredito: estável = cartão normal; instável = invertido (fundo claro, texto escuro).</summary>
    private void ShowOcVerdict(bool stable, DateTime start, TimeSpan duration, string? failure)
    {
        OcStatus.Text = $"Concluído em {DateTime.Now - start:mm\\:ss}.";
        OcVerdict.Text = stable ? "ESTÁVEL" : "INSTÁVEL";
        OcVerdictWhy.Text = stable
            ? $"Nenhum erro em {duration.TotalMinutes:0} min de stress. Para Curve Optimizer, use o PC normalmente por um dia e confira os erros recentes."
            : $"Motivo: {failure}. Volte um passo no ajuste e teste de novo.";
        OcVerdictCard.Background = stable ? Card : Fg;
        OcVerdict.Foreground = OcVerdictWhy.Foreground = stable ? Fg : Ink;
    }

    private void BuildOcGuide()
    {
        var (maker, model) = OcCheck.Motherboard();
        bool gigabyte = maker.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase);
        bool asus = maker.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
        bool msi = maker.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || maker.Contains("MSI", StringComparison.OrdinalIgnoreCase);
        bool ryzen = _cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        string cpu = GameCatalog.ShortCpuName(_cpuName);
        string board = ((gigabyte ? "Gigabyte" : maker.Split(' ').FirstOrDefault() ?? "") + " " + model).Trim();

        OcGuideTitle.Text = $"GUIA: OVERCLOCK NA BIOS · {board.ToUpperInvariant()} · {cpu.ToUpperInvariant()}";
        OcGuideIntro.Text = "Faça uma mudança por vez e rode o verificador depois de cada uma. Os menus podem mudar um pouco com a versão da BIOS. " +
                            "Valores são pontos de partida seguros, não garantia: cada chip é diferente.";

        // Caminho dos menus na BIOS de cada fabricante.
        string pboMenu = gigabyte ? "Tweaker → Advanced CPU Settings → Precision Boost Overdrive  (ou Settings → AMD Overclocking)"
                       : asus ? "Advanced → AMD Overclocking → Precision Boost Overdrive  (ou AI Tweaker → PBO)"
                       : msi ? "OC → Advanced CPU Configuration → AMD Overclocking → Precision Boost Overdrive"
                       : "Advanced → AMD Overclocking → Precision Boost Overdrive";
        string expoMenu = gigabyte ? "Tweaker → Extreme Memory Profile (X.M.P.) → EXPO1"
                        : asus ? "AI Tweaker → Ai Overclock Tuner → EXPO I"
                        : msi ? "OC → A-XMP/EXPO → Profile 1"
                        : "procure EXPO / XMP no menu de memória";

        var steps = new List<GuideStep>
        {
            new("0", "Antes de começar", gigabyte ? "Save & Exit → Save Profiles" : "Save & Exit → Save Profile",
                "Salve o perfil atual da BIOS. Se o PC não ligar depois de um ajuste, espere 3 tentativas (muitas placas voltam sozinhas) ou limpe a CMOS (jumper CLR_CMOS na placa ou tirando a bateria por 1 minuto)."),
            new("1", "Ative o perfil da memória (EXPO)", expoMenu,
                "É o maior ganho em jogos para a maioria dos PCs: a RAM sai do padrão lento e vai para a velocidade da etiqueta. Teste: verificador \"CPU + RAM\" por 15 min (o teste de RAM acusa erro se o perfil não for estável)."),
        };
        if (ryzen)
        {
            steps.Add(new("2", "Precision Boost Overdrive (PBO)", pboMenu,
                "Precision Boost Overdrive → Advanced. PBO Limits → Motherboard. Isso libera os limites de energia para o boost durar mais. Teste com o verificador."));
            steps.Add(new("3", "Curve Optimizer (undervolt da CPU)", pboMenu + " → Curve Optimizer",
                "All Cores → Negative → 10. Teste. Se passar, tente 15, 20, 25… até falhar; volte 5 a partir de onde falhou. No " + cpu + " costuma ficar entre -15 e -30. Instabilidade de Curve Optimizer aparece muito em uso leve: use o PC normalmente por um dia e veja \"Erros de hardware recentes\" aqui — erro WHEA = volte um passo."));
            steps.Add(new("4", "Max CPU Boost Clock Override (opcional)", pboMenu,
                "Depois do Curve Optimizer estável: +100 MHz, teste; depois +200 MHz. Ganho pequeno; pode exigir reduzir o Curve Optimizer."));
            steps.Add(new("5", "Alternativa fria: Eco Mode", pboMenu + " → Eco Mode",
                "Em vez de mais desempenho: limita a CPU a 65 W (ou 105 W). Perde ~5–10% em tarefas pesadas multi-core, quase nada em jogos, e roda bem mais frio e silencioso."));
        }
        else
        {
            steps.Add(new("2", "Processador Intel", "Tweaker / Ai Tweaker / OC → CPU",
                "Para Intel, prefira undervolt (offset negativo de tensão) e limites de energia (PL1/PL2) em vez de aumentar multiplicadores. Teste cada passo com o verificador."));
        }
        OcGuide.ItemsSource = steps;
    }
}
