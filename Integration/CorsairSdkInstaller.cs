using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace KResetExplode;

/// <summary>
/// Baixa o SDK oficial da Corsair (github.com/CorsairOfficial/cue-sdk, última release) e extrai SOMENTE o iCUESDK.x64_2019.dll
/// para %ProgramFiles%\KResetExplode (pasta que só administradores alteram). Só roda quando o usuário confirma.
/// A DLL é extraída e verificada já dentro da pasta protegida: sem assinatura, assinatura inválida, revogada ou de outro
/// fabricante cancela tudo (ela seria carregada num processo de administrador).
/// </summary>
public static class CorsairSdkInstaller
{
    private const string Repo = "CorsairOfficial/cue-sdk";
    private const string DllName = "iCUESDK.x64_2019.dll";

    /// <summary>Organizações aceitas no certificado da DLL.</summary>
    public static readonly string[] Publishers = { "Corsair" };

    public static string TargetPath => Path.Combine(SecureFiles.ProtectedRoot, DllName);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KResetExplode");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Baixa, extrai e confere a DLL. Devolve um resumo para mostrar na tela; lança exceção com a explicação se falhar.</summary>
    public static async Task<string> InstallAsync(Action<string> progress, CancellationToken ct = default)
    {
        progress("Consultando a última versão do SDK no GitHub…");
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "?" : "?";

        // Candidatos: anexos .zip da release (preferindo os que citam o SDK) e, por último, o código-fonte da versão.
        var urls = new List<(string Name, string Url)>();
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && url.StartsWith("https://")) urls.Add((name, url));
            }
        urls = urls.OrderByDescending(x => x.Name.Contains("sdk", StringComparison.OrdinalIgnoreCase)).ToList();
        if (root.TryGetProperty("zipball_url", out var z) && z.GetString() is string zip && zip.StartsWith("https://"))
            urls.Add(("código-fonte " + tag, zip));
        if (urls.Count == 0) throw new InvalidOperationException("A release não tem nenhum arquivo .zip para baixar.");

        // O .zip baixado só é lido; a DLL é extraída e conferida na pasta protegida, onde nenhum outro programa consegue trocá-la.
        string tmpDir = SecureFiles.NewProtectedTemp("cue");
        try
        {
            foreach (var (name, url) in urls)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Host is not ("api.github.com" or "github.com" or "codeload.github.com"))
                    continue; // só baixa do próprio GitHub
                string zipPath = Path.Combine(tmpDir, "sdk.zip");
                progress($"Baixando {name}…");
                using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? 0;
                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = File.Create(zipPath);
                    var buf = new byte[81920];
                    long done = 0; int n; long lastReport = 0;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        if (done - lastReport > 1_000_000)
                        {
                            lastReport = done;
                            progress(total > 0 ? $"Baixando {name}… {done / 1_048_576.0:0.0} de {total / 1_048_576.0:0.0} MB" : $"Baixando {name}… {done / 1_048_576.0:0.0} MB");
                        }
                    }
                }

                string? extracted = ExtractDll(zipPath, tmpDir);
                if (extracted == null) continue; // não está neste pacote: tenta o próximo

                progress("Verificando a assinatura digital…");
                string verdict = Verify(extracted);
                File.Copy(extracted, TargetPath, true);
                return $"SDK {tag} instalado em {TargetPath}. {verdict}";
            }
            throw new InvalidOperationException($"Não achei o {DllName} dentro da última release ({tag}). Baixe o SDK manualmente em github.com/{Repo}/releases.");
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    private static string? ExtractDll(string zipPath, string tmpDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries
            .Where(e => string.Equals(e.Name, DllName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.FullName.Contains("x64", StringComparison.OrdinalIgnoreCase) && e.FullName.Contains("redist", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (entry == null) return null;
        string outPath = Path.Combine(tmpDir, DllName);
        entry.ExtractToFile(outPath, true); // só esta DLL é extraída; nenhum outro arquivo do pacote toca o disco
        return outPath;
    }

    /// <summary>Confere tamanho, formato e a assinatura digital da Corsair (com consulta de revogação). Lança exceção se falhar.</summary>
    private static string Verify(string path)
    {
        var fi = new FileInfo(path);
        if (fi.Length < 100_000) throw new InvalidOperationException("O arquivo extraído é pequeno demais para ser a DLL do SDK.");
        using (var fs = File.OpenRead(path))
            if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') throw new InvalidOperationException("O arquivo extraído não é uma DLL do Windows.");
        string org = Authenticode.Verify(path, Publishers, checkRevocation: true);
        return $"Assinatura digital válida ({org}).";
    }
}
