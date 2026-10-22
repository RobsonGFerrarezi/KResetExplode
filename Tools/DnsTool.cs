using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace KResetExplode;

/// <summary>Um provedor de DNS testado.</summary>
public sealed class DnsCandidate : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string[] Servers { get; init; } = Array.Empty<string>();
    public bool IsCurrent { get; init; }
    public string ServersText => string.Join(" · ", Servers);

    private double? _ms;
    private int _fails, _total;
    private bool _best;
    public double? MedianMs { get => _ms; set { _ms = value; On(nameof(Result)); } }
    public int Fails { get => _fails; set { _fails = value; On(nameof(Result)); } }
    public int Total { get => _total; set { _total = value; On(nameof(Result)); } }
    public bool Best { get => _best; set { _best = value; On(nameof(Badge)); } }
    public string Result => MedianMs is double m ? $"{m:0} ms" + (Fails > 0 ? $" · {Fails} de {Total} sem resposta" : "") : Total > 0 ? "sem resposta" : "";
    public string Badge => IsCurrent && Best ? "ATUAL · MAIS RÁPIDO" : IsCurrent ? "ATUAL" : Best ? "MAIS RÁPIDO" : "";
    public bool CanApply => !IsCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void On(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>
/// Teste e troca de DNS. O DNS é quem traduz "steamcommunity.com" em endereço: um DNS lento atrasa o início de cada conexão
/// (abrir páginas, entrar em partidas, baixar atualizações). Não muda o ping dentro do jogo.
/// A troca vale para os adaptadores conectados (IPv4) e guarda a configuração anterior para VOLTAR AO ORIGINAL.
/// </summary>
public static class DnsTool
{
    private static readonly string[] Domains =
        { "www.google.com", "www.youtube.com", "steamcommunity.com", "discord.com", "www.epicgames.com", "www.twitch.tv", "www.microsoft.com", "www.riotgames.com" };

    public static List<DnsCandidate> Candidates()
    {
        var current = ActiveAdapters().SelectMany(a => a.GetIPProperties().DnsAddresses)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).Select(ip => ip.ToString()).Distinct().ToArray();
        var known = new (string Name, string[] Servers)[]
        {
            ("Cloudflare", new[] { "1.1.1.1", "1.0.0.1" }),
            ("Google", new[] { "8.8.8.8", "8.8.4.4" }),
            ("Quad9 (bloqueia sites maliciosos)", new[] { "9.9.9.9", "149.112.112.112" }),
            ("OpenDNS", new[] { "208.67.222.222", "208.67.220.220" }),
        };
        var list = new List<DnsCandidate>();
        if (current.Length > 0)
        {
            var match = known.FirstOrDefault(k => k.Servers.Contains(current[0]));
            list.Add(new DnsCandidate { Name = match.Name ?? "Atual (provedor ou roteador)", Servers = current, IsCurrent = true });
        }
        list.AddRange(known.Where(k => !k.Servers.Contains(current.FirstOrDefault() ?? "")).Select(k => new DnsCandidate { Name = k.Name, Servers = k.Servers }));
        return list;
    }

    /// <summary>Mede o tempo de resposta de cada DNS (mediana de 2 rodadas pelos sites da lista, só o primeiro servidor de cada).</summary>
    public static async Task TestAsync(IReadOnlyList<DnsCandidate> list, Action<string> progress, CancellationToken ct)
    {
        foreach (var c in list)
        {
            progress($"Testando {c.Name}…");
            var times = new List<double>();
            int fails = 0, total = 0;
            if (IPAddress.TryParse(c.Servers.FirstOrDefault(), out var ip))
                for (int round = 0; round < 2; round++)
                    foreach (var d in Domains)
                    {
                        ct.ThrowIfCancellationRequested();
                        total++;
                        if (await QueryAsync(ip, d, 1500) is double ms) times.Add(ms); else fails++;
                    }
            times.Sort();
            c.Total = total;
            c.Fails = fails;
            c.MedianMs = times.Count > 0 ? times[times.Count / 2] : null;
        }
        var best = list.Where(c => c.MedianMs != null && c.Fails <= c.Total / 4).OrderBy(c => c.MedianMs).FirstOrDefault();
        foreach (var c in list) c.Best = c == best;
    }

    /// <summary>Consulta DNS mínima (registro A, UDP 53). Devolve o tempo em ms ou null sem resposta válida.</summary>
    private static async Task<double?> QueryAsync(IPAddress server, string name, int timeoutMs)
    {
        using var udp = new UdpClient(server.AddressFamily);
        var id = (ushort)Random.Shared.Next(1, 65535);
        var q = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 }; // RD=1, 1 pergunta
        foreach (var label in name.Split('.')) { q.Add((byte)label.Length); q.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
        q.AddRange(new byte[] { 0, 0, 1, 0, 1 }); // fim, tipo A, classe IN
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(q.ToArray(), q.Count, new IPEndPoint(server, 53));
            using var cts = new CancellationTokenSource(timeoutMs);
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token);
                var b = r.Buffer;
                if (b.Length >= 12 && b[0] == (byte)(id >> 8) && b[1] == (byte)id && (b[2] & 0x80) != 0)
                    return (b[3] & 0x0F) is 0 or 3 ? sw.Elapsed.TotalMilliseconds : null; // 0 = ok, 3 = não existe (também é resposta)
            }
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ aplicar / voltar

    /// <summary>VPNs e adaptadores virtuais: o DNS deles é do serviço (Radmin, Tailscale, Hyper-V...) e não deve ser trocado.</summary>
    private static readonly string[] VirtualHints =
        { "vpn", "virtual", "tap-", "tap ", "tun", "wireguard", "tailscale", "zerotier", "radmin", "hamachi", "hyper-v", "vmware", "virtualbox", "vethernet", "loopback", "bluetooth" };

    /// <summary>Placas de rede físicas conectadas com gateway IPv4 (as que levam à internet).</summary>
    public static List<NetworkInterface> ActiveAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet
                        && !VirtualHints.Any(h => (n.Name + " " + n.Description).Contains(h, StringComparison.OrdinalIgnoreCase))
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ToList();

    /// <summary>Configuração atual do adaptador: "dhcp" ou a lista estática ("1.1.1.1,8.8.8.8").</summary>
    private static string CurrentSetting(NetworkInterface n)
    {
        using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{n.Id}");
        string servers = (k?.GetValue("NameServer") as string ?? "").Trim();
        return servers.Length == 0 ? "dhcp" : servers.Replace(' ', ',');
    }

    public static async Task<string> ApplyAsync(string[] servers, AppSettings settings)
    {
        if (servers.Length == 0 || !servers.All(s => IPAddress.TryParse(s, out var a) && a.AddressFamily == AddressFamily.InterNetwork))
            return "Endereço de DNS inválido.";
        var adapters = ActiveAdapters();
        if (adapters.Count == 0) return "Nenhum adaptador de rede conectado.";
        var done = new List<string>();
        foreach (var n in adapters)
        {
            settings.DnsBackup.TryAdd(n.Id, CurrentSetting(n)); // guarda o original só na primeira troca
            if (await SetAsync(n, servers)) done.Add(n.Name);
        }
        settings.Save();
        await RunAsync("ipconfig.exe", "/flushdns");
        return done.Count > 0 ? $"DNS trocado para {string.Join(" / ", servers)} em: {string.Join(", ", done)}." : "O Windows não aceitou a troca de DNS.";
    }

    public static async Task<string> RestoreAsync(AppSettings settings)
    {
        if (settings.DnsBackup.Count == 0) return "O DNS não foi alterado pelo RESET EXPLODE.";
        var all = NetworkInterface.GetAllNetworkInterfaces().ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var done = new List<string>();
        foreach (var (id, value) in settings.DnsBackup.ToList())
        {
            if (!all.TryGetValue(id, out var n)) continue;
            string[] servers = value == "dhcp" ? Array.Empty<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(s => IPAddress.TryParse(s, out _)).ToArray();
            if (await SetAsync(n, servers)) { done.Add(n.Name); settings.DnsBackup.Remove(id); }
        }
        settings.Save();
        await RunAsync("ipconfig.exe", "/flushdns");
        return done.Count > 0 ? $"DNS original restaurado em: {string.Join(", ", done)}." : "Nada restaurado (adaptador desconectado?).";
    }

    /// <summary>Servidores vazios = automático (DHCP). Usa o índice da interface (evita problemas com nomes com espaço/acento).</summary>
    private static async Task<bool> SetAsync(NetworkInterface n, string[] servers)
    {
        int index = n.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
        if (index < 0) return false;
        if (servers.Length == 0)
            return await RunAsync("netsh.exe", "interface", "ipv4", "set", "dnsservers", $"name={index}", "source=dhcp") == 0;
        if (await RunAsync("netsh.exe", "interface", "ipv4", "set", "dnsservers", $"name={index}", "source=static",
                           $"address={servers[0]}", "register=primary", "validate=no") != 0) return false;
        for (int i = 1; i < servers.Length; i++)
            await RunAsync("netsh.exe", "interface", "ipv4", "add", "dnsservers", $"name={index}", $"address={servers[i]}", $"index={i + 1}", "validate=no");
        return true;
    }

    private static async Task<int> RunAsync(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(SecureFiles.SystemTool(exe)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }
}
