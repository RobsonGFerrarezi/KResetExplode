using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KResetExplode;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks de jogo (Test = "gpu", "sim" ou "mem"). Score é usado para a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>Resultado guardado como "antes de otimizar" (Key = métrica da página Comparar, ex.: "render.mt", "gpu:1080").</summary>
public sealed record BenchBaseline(string Key, DateTime Date, double Score, string Summary);

/// <summary>Alertas em segundo plano (enquanto o app está aberto, inclusive minimizado).</summary>
public sealed class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public bool Cpu { get; set; } = true;
    public double CpuLimit { get; set; } = 95;
    public bool Gpu { get; set; } = true;
    public double GpuLimit { get; set; } = 87;
    public bool Disk { get; set; } = true;
    public bool Battery { get; set; } = true;
    /// <summary>Desgaste da bateria (% da capacidade original perdida) a partir do qual avisa.</summary>
    public double BatteryWear { get; set; } = 40;
}

/// <summary>Programa extra que abre antes do jogo (ex.: ferramenta de comando/monitoramento) e pode fechar junto com ele.</summary>
public sealed class HelperProgram
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseWithGame { get; set; } = true;
    public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : " " + Args);
}

/// <summary>Perfil de um jogo na aba RESET JOGOS: desempenho, argumentos de gráficos e programas auxiliares.</summary>
public sealed class GameProfile
{
    public string Priority { get; set; } = "normal";   // normal | above | high
    public bool HighPerfPlan { get; set; }
    public bool GpuHigh { get; set; }
    public bool DisableFso { get; set; }
    public string? ExePath { get; set; }
    public string WinMode { get; set; } = "default";   // default | full | borderless | window
    public string Res { get; set; } = "default";       // default | 1280x720 | 1920x1080 | 2560x1440 | 3840x2160
    public int Hz { get; set; }                        // 0 = não definir
    public string Style { get; set; } = "source";      // source | unity | unreal
    public string Extra { get; set; } = "";
    public List<HelperProgram> Helpers { get; set; } = new();
}

/// <summary>Jogo adicionado à mão (qualquer .exe).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Args { get; set; } = "";
}

/// <summary>
/// Configurações em %ProgramFiles%\KResetExplode\Usuarios\&lt;SID&gt; (só administradores gravam). Ficavam em %AppData%, mas o app roda
/// como administrador e usa estes dados para restaurar itens de inicialização, valores do registro e abrir programas: em %AppData%
/// qualquer programa da conta poderia editá-los e usar o RESET EXPLODE para ganhar administrador.
/// A chave da API é cifrada com DPAPI (só este usuário do Windows a lê).
/// </summary>
public sealed class AppSettings
{
    public static string Folder => SecureFiles.UserFolder;
    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Local antigo (até a v0.16), gravável por qualquer programa do usuário.</summary>
    private static string LegacyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResetExplode", "settings.json");

    public string? HowManyFpsKeyProtected { get; set; }

    // RESET BENCH: últimos resultados (mais recente primeiro)
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();
    /// <summary>Resultados marcados como "antes de otimizar" (cópia: o histórico só guarda os 10 últimos de cada teste).</summary>
    public List<BenchBaseline> BenchBaseline { get; set; } = new();
    public DateTime? BenchBaselineDate { get; set; }

    // RESET OTIMIZA: valores originais dos ajustes do Windows antes da primeira alteração ("<ausente>" = não existia)
    public Dictionary<string, string?> TweakBackup { get; set; } = new();
    /// <summary>ComDB original (reservas de números COM) antes da primeira "liberação".</summary>
    public byte[]? ComDbBackup { get; set; }
    /// <summary>Programas de inicialização removidos, para "Restaurar removidos".</summary>
    public List<StartupBackup> StartupRemoved { get; set; } = new();
    /// <summary>Última versão de driver de vídeo já avisada ao abrir o app (para não repetir o aviso).</summary>
    public string? DriverNotified { get; set; }
    /// <summary>DNS de cada adaptador antes da primeira troca (id do adaptador → "dhcp" ou "ip,ip").</summary>
    public Dictionary<string, string> DnsBackup { get; set; } = new();
    /// <summary>Silenciar as notificações do Windows enquanto um jogo está aberto (ajuste "quiet" do RESET OTIMIZA).</summary>
    public bool MuteNotificationsInGame { get; set; }

    // Alertas
    public AlertSettings Alerts { get; set; } = new();

    // RESET JOGOS
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();

    // Aparência
    public bool LightTheme { get; set; }

    // Overlay
    public string OverlayCorner { get; set; } = "TL";
    public double OverlayScale { get; set; } = 1.0;
    /// <summary>Itens escolhidos para o overlay (chaves de OverlayMetrics); null = padrão.</summary>
    public List<string>? OverlayItems { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] // nunca gravar a chave em texto puro
    public string? HowManyFpsKey
    {
        get
        {
            if (string.IsNullOrEmpty(HowManyFpsKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(HowManyFpsKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
        set
        {
            HowManyFpsKeyProtected = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            if (File.Exists(LegacyFilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(LegacyFilePath)) ?? new AppSettings();
                s.DropUntrustedLegacyData();
                s.Save();
                return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    /// <summary>
    /// Ao importar do local antigo (que qualquer programa podia editar), descarta os itens de "Restaurar removidos" da
    /// inicialização que seriam gravados com administrador (registro da máquina e atalhos movidos de pasta). Os valores de
    /// ajustes e do ComDB são validados na hora de restaurar; histórico, perfis e preferências ficam.
    /// </summary>
    private void DropUntrustedLegacyData() => StartupRemoved.RemoveAll(b => b.Machine || b.Kind != "Run");

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        SecureFiles.EnsureNoReparsePoints(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
