using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace KResetExplode;

/// <summary>
/// Proteção da versão portátil (.exe de arquivo único). O .NET extrai as DLLs nativas (WPF, compilador de shaders...) para
/// %TEMP%\.net\KResetExplode\&lt;id&gt; e as reaproveita nas próximas execuções sem conferir o conteúdo. Como %TEMP% é gravável por
/// qualquer programa da conta, uma DLL trocada ali rodaria como administrador.
/// Antes de qualquer DLL nativa ser carregada (este código roda antes do Main), aqui:
///  1. a pasta passa a pertencer aos Administradores, sem permissão de escrita para programas comuns;
///  2. cada arquivo extraído é comparado byte a byte (SHA-256) com o original guardado dentro do próprio .exe, e arquivos
///     a mais são recusados;
///  3. os arquivos ficam abertos até o programa fechar, sem permitir gravar, renomear ou apagar.
/// Se algo não bater, a pasta é apagada (o .NET extrai de novo na próxima vez) e o programa não abre.
/// Na versão instalada não há pacote de arquivo único e nada disso é necessário.
/// </summary>
internal static class BundleGuard
{
    private static readonly List<FileStream> Locks = new();

    // Assinatura que marca um .exe de arquivo único do .NET (SHA-256 de ".net core bundle"), precedida do endereço do cabeçalho.
    private static readonly byte[] Signature =
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
    };

    private sealed record Entry(long Offset, long Size, long CompressedSize, string Path);

    [ModuleInitializer]
    internal static void Init()
    {
        string? problem;
        try { problem = Check(); }
        catch (Exception ex) { problem = ex.Message; }
        if (problem == null) return;

        MessageBoxW(IntPtr.Zero,
            "O RESET EXPLODE não vai abrir por segurança.\n\n" + problem +
            "\n\nOs arquivos temporários foram apagados. Abra o programa de novo; se a mensagem voltar, " +
            "pode haver um programa malicioso neste PC.",
            "RESET EXPLODE", 0x10 /* MB_ICONERROR */);
        Environment.Exit(1);
    }

    /// <summary>Devolve null se está tudo certo (ou não é a versão portátil), ou a descrição do problema.</summary>
    private static string? Check()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null) return null;
        var (bundleId, entries) = ReadManifest(exe);
        if (bundleId == null) return null; // versão instalada (não é arquivo único)

        string? dir = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.TrimEnd('\\', '/'))
            .FirstOrDefault(d => Path.GetFileName(d).Equals(bundleId, StringComparison.OrdinalIgnoreCase) && Directory.Exists(d));
        if (dir == null) return null; // nada foi extraído

        try
        {
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                return Fail(dir, $"A pasta {dir} é um link para outro lugar.");

            // Sem administrador não há privilégio a ganhar trocando as DLLs (e não seria possível mudar o dono).
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) ProtectTree(dir);

            var expected = entries.ToDictionary(e => Path.GetFullPath(Path.Combine(dir, e.Path)), StringComparer.OrdinalIgnoreCase);
            using var bundle = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (!expected.TryGetValue(Path.GetFullPath(file), out var e))
                    return Fail(dir, $"Arquivo estranho na pasta temporária: {file}");
                var lockStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                Locks.Add(lockStream);
                if (!SHA256.HashData(lockStream).AsSpan().SequenceEqual(HashOf(bundle, e)))
                    return Fail(dir, $"Arquivo alterado: {file}");
            }
            return null;
        }
        catch (Exception ex) { return Fail(dir, "Não foi possível conferir os arquivos temporários: " + ex.Message); }
    }

    private static string Fail(string dir, string why)
    {
        foreach (var l in Locks) l.Dispose();
        Locks.Clear();
        try { Directory.Delete(dir, true); } catch { }
        return why;
    }

    /// <summary>Dono = Administradores; Administradores e SYSTEM com controle total; usuários só leem. Sem herdar da pasta %TEMP%.</summary>
    private static void ProtectTree(string dir)
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var ds = new DirectorySecurity();
        ds.SetAccessRuleProtection(true, false);
        ds.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        var root = new DirectoryInfo(dir);
        root.SetAccessControl(ds);  // primeiro as permissões (garante que podemos trocar o dono)...
        ds.SetOwner(admins);
        root.SetAccessControl(ds);  // ...depois o dono: o usuário perde o direito implícito de dono de mudar as permissões

        foreach (var sub in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"{sub.FullName} é um link.");
            if (sub is DirectoryInfo d)
            {
                var s = new DirectorySecurity();
                s.SetAccessRuleProtection(false, false); // herda da pasta protegida acima
                d.SetAccessControl(s);
                s.SetOwner(admins);
                d.SetAccessControl(s);
            }
            else if (sub is FileInfo f)
            {
                var s = new FileSecurity();
                s.SetAccessRuleProtection(false, false);
                f.SetAccessControl(s);
                s.SetOwner(admins);
                f.SetAccessControl(s);
            }
        }
    }

    private static byte[] HashOf(FileStream bundle, Entry e)
    {
        bundle.Position = e.Offset;
        if (e.CompressedSize > 0)
        {
            using var part = new MemoryStream(new BinaryReader(bundle).ReadBytes(checked((int)e.CompressedSize)));
            using var z = new DeflateStream(part, CompressionMode.Decompress);
            return SHA256.HashData(z);
        }
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[1 << 20];
        for (long left = e.Size; left > 0;)
        {
            int n = bundle.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) throw new EndOfStreamException();
            sha.AppendData(buf, 0, n);
            left -= n;
        }
        return sha.GetHashAndReset();
    }

    /// <summary>Lê o manifesto do pacote de arquivo único (formato do Microsoft.NET.HostModel). Devolve id null se não for um pacote.</summary>
    private static (string? Id, List<Entry> Entries) ReadManifest(string exe)
    {
        var entries = new List<Entry>();
        using var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
        long headerOffset = FindHeaderOffset(fs);
        if (headerOffset <= 0 || headerOffset >= fs.Length) return (null, entries);

        fs.Position = headerOffset;
        using var r = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
        uint major = r.ReadUInt32();
        r.ReadUInt32(); // versão menor
        int count = r.ReadInt32();
        string id = r.ReadString();
        if (major >= 2) { r.ReadInt64(); r.ReadInt64(); r.ReadInt64(); r.ReadInt64(); r.ReadUInt64(); } // deps.json, runtimeconfig, flags
        for (int i = 0; i < count; i++)
        {
            long offset = r.ReadInt64(), size = r.ReadInt64();
            long compressed = major >= 6 ? r.ReadInt64() : 0;
            r.ReadByte(); // tipo
            string path = r.ReadString().Replace('/', '\\');
            entries.Add(new Entry(offset, size, compressed, path));
        }
        return (id, entries);
    }

    /// <summary>Procura a assinatura no início do .exe (fica no executável do host, antes dos arquivos empacotados).</summary>
    private static long FindHeaderOffset(FileStream fs)
    {
        const int Window = 1 << 20;
        long limit = Math.Min(fs.Length, 64L << 20);
        var buf = new byte[8 + Window + Signature.Length]; // 8 bytes antes e a assinatura inteira, mesmo na divisa entre blocos
        for (long pos = 0; pos < limit; pos += Window)
        {
            long start = Math.Max(0, pos - 8);
            fs.Position = start;
            int n = fs.Read(buf, 0, buf.Length);
            int at = buf.AsSpan(0, n).IndexOf(Signature);
            if (at >= 8) return BitConverter.ToInt64(buf, at - 8);
            if (start + n >= fs.Length) break;
        }
        return 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
