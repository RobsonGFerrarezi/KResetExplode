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

/// <summary>RESET JOGOS › FPS estimado.</summary>
public partial class MainWindow
{
    // Jogos
    private readonly HowManyFpsClient _fpsApi = new();
    private List<GameRow> _gameRows = new();
    private ListCollectionView? _gameView;
    private CancellationTokenSource? _gameFetch;
    private string _cpuName = "", _gpuName = "";
    private double _cpuIdx = 80, _gpuIdx = 50;

    // ---------------- Jogos ----------------

    private void InitGames()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (System.Management.ManagementObject mo in s.Get()) { _cpuName = mo["Name"]?.ToString()?.Trim() ?? ""; break; }
        }
        catch { }
        _gpuName = _gpus.Count > 0 ? _gpus[0].Name : "";
        _gpuIdx = GameCatalog.GpuPerf(_gpuName) ?? 50;
        _cpuIdx = GameCatalog.CpuPerf(_cpuName) ?? 80;
        GamesSub.Text = $"FPS esperado no seu PC · {GameCatalog.ShortGpuName(_gpuName)} · {GameCatalog.ShortCpuName(_cpuName)}";

        _fpsApi.ApiKey = _settings.HowManyFpsKey;
        if (_fpsApi.Configured) ApiKeyBox.Password = _settings.HowManyFpsKey;

        _gameRows = GameCatalog.Games.Select(g => new GameRow(g)).ToList();
        _gameView = new ListCollectionView(_gameRows);
        GameList.ItemsSource = _gameView;
        RefreshGames();
    }

    private void GameOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_gameView != null) RefreshGames(); // também dispara durante InitializeComponent
    }

    private void GameSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_gameView == null) return;
        string q = GameSearch.Text.Trim();
        _gameView.Filter = q.Length == 0 ? null : o => ((GameRow)o).Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void ApiKeyBox_Changed(object sender, RoutedEventArgs e)
        => ApiKeyHint.Visibility = ApiKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void BtnApiKey_Click(object sender, RoutedEventArgs e)
    {
        _settings.HowManyFpsKey = ApiKeyBox.Password;
        _settings.Save();
        _fpsApi.ApiKey = _settings.HowManyFpsKey;
        RefreshGames();
    }

    /// <summary>Recalcula a estimativa local de todos os jogos e, com chave configurada, busca os dados reais na API.</summary>
    private void RefreshGames()
    {
        string res = CheckedTag(GameResPanel) ?? "1920x1080";
        string preset = CheckedTag(GamePresetPanel) ?? "Alto";
        string up = CheckedTag(GameUpscalePanel) ?? "Native";

        foreach (var row in _gameRows)
        {
            var est = GameCatalog.Estimate(row.Game, _gpuIdx, _cpuIdx, res, preset, up);
            string limiter = est.Limiter switch
            {
                Bottleneck.Cpu => "gargalo: CPU",
                Bottleneck.Gpu => "gargalo: GPU",
                Bottleneck.Engine => $"trava do jogo em {row.Game.EngineCap:0} FPS",
                _ => ""
            };
            row.Set(est.Avg, 0, "ESTIMATIVA", limiter);
        }

        _gameFetch?.Cancel();
        if (_fpsApi.Configured)
        {
            GameSourceTitle.Text = "Fonte: HowManyFPS (dados reais)";
            GameSourceText.Text = "FPS médio e 1% low medidos em sessões reais de jogo. Respostas ficam em cache por 7 dias. " +
                                  "A API usa as configurações gráficas dela; \"Gráficos\" vale só para a estimativa.";
            BtnApiKey.Content = "ATUALIZAR CHAVE";
            _gameFetch = new CancellationTokenSource();
            _ = FetchGamesAsync(res, up, _gameFetch.Token);
        }
        else
        {
            GameSourceTitle.Text = "Fonte: estimativa local (aproximada)";
            GameSourceText.Text = "Calculada a partir do desempenho relativo da sua GPU e CPU. Para FPS medido em jogo, " +
                                  "peça uma chave da API em info@howmanyfps.com e cole ao lado.";
            BtnApiKey.Content = "SALVAR CHAVE";
        }
    }

    private async Task FetchGamesAsync(string res, string up, CancellationToken ct)
    {
        string cpu = GameCatalog.ShortCpuName(_cpuName), gpu = GameCatalog.ShortGpuName(_gpuName);
        int ok = 0;
        foreach (var row in _gameRows)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var r = await _fpsApi.GetAsync(cpu, gpu, row.Name, res, up, ct);
                row.Set(r.AvgFps, r.Min1Fps, "HOWMANYFPS", "");
                ok++;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                row.Set(row.Fps, 0, "ESTIMATIVA", $"API: {ex.Message}");
                if (ex.Message.Contains("chave") || ex.Message.Contains("cota"))
                {
                    GameSourceText.Text = $"A API recusou a consulta ({ex.Message}). Mostrando a estimativa local.";
                    return; // não adianta insistir nos outros jogos
                }
            }
        }
        if (!ct.IsCancellationRequested)
            GameSourceText.Text = $"{ok} de {_gameRows.Count} jogos com dados da HowManyFPS; os demais usam a estimativa local.";
    }
}
