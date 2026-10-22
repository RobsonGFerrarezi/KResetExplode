using System.Windows;

namespace KResetExplode;

/// <summary>Rede · DNS: testa os DNS públicos e troca com um clique (com volta ao original).</summary>
public partial class MainWindow
{
    private List<DnsCandidate> _dns = new();
    private bool _dnsBusy;

    private void DnsBusy(bool busy)
    {
        _dnsBusy = busy;
        BtnDnsTest.IsEnabled = !busy;
        BtnDnsRestore.IsEnabled = !busy && _settings.DnsBackup.Count > 0;
    }

    private async void BtnDnsTest_Click(object sender, RoutedEventArgs e)
    {
        if (_dnsBusy) return;
        DnsBusy(true);
        try
        {
            _dns = DnsTool.Candidates();
            DnsList.ItemsSource = _dns;
            await DnsTool.TestAsync(_dns, t => Dispatcher.BeginInvoke(new Action(() => DnsStatus.Text = t)), CancellationToken.None);
            var best = _dns.FirstOrDefault(c => c.Best);
            var cur = _dns.FirstOrDefault(c => c.IsCurrent);
            DnsStatus.Text = best == null ? "Nenhum DNS respondeu. Confira a conexão."
                : best.IsCurrent ? $"O seu DNS atual já é o mais rápido daqui ({best.MedianMs:0} ms)."
                : cur?.MedianMs is double cm && best.MedianMs is double bm && cm - bm < 5
                    ? $"{best.Name} foi o mais rápido ({bm:0} ms), mas a diferença para o atual ({cm:0} ms) é pequena: não vale a pena trocar."
                    : $"{best.Name} foi o mais rápido ({best.MedianMs:0} ms). Clique em USAR para trocar; dá para voltar depois.";
        }
        catch (Exception ex) { DnsStatus.Text = "Falhou: " + ex.Message; }
        finally { DnsBusy(false); }
    }

    private async void DnsApply_Click(object sender, RoutedEventArgs e)
    {
        if (_dnsBusy || sender is not FrameworkElement { Tag: DnsCandidate c }) return;
        if (MessageBox.Show(this, $"Usar o DNS {c.Name} ({c.ServersText}) nas conexões ativas?\n\nA configuração atual é guardada; VOLTAR AO ORIGINAL desfaz.",
                "Rede · DNS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DnsBusy(true);
        try { DnsStatus.Text = await DnsTool.ApplyAsync(c.Servers, _settings) + " Clique em TESTAR DNS para conferir."; }
        catch (Exception ex) { DnsStatus.Text = "Falhou: " + ex.Message; }
        finally { DnsBusy(false); }
    }

    private async void BtnDnsRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_dnsBusy) return;
        DnsBusy(true);
        try { DnsStatus.Text = await DnsTool.RestoreAsync(_settings); }
        catch (Exception ex) { DnsStatus.Text = "Falhou: " + ex.Message; }
        finally { DnsBusy(false); }
    }
}
