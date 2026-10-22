using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KResetExplode;

/// <summary>
/// Carregamento seguro de DLLs nativas. Por padrão o Windows e o .NET procuram uma DLL primeiro na pasta do .exe
/// (que costuma ser Downloads) e na pasta atual: um "nvml.dll" falso colocado ali rodaria como administrador.
/// Todas as DLLs usadas por [DllImport] neste programa são do Windows ou do driver, então são carregadas só pelo caminho completo.
/// </summary>
internal static class NativeLoading
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Tira a pasta atual da busca de DLLs do processo inteiro (vale também para as bibliotecas de terceiros).
        SetDllDirectoryW("");
        NativeLibrary.SetDllImportResolver(typeof(NativeLoading).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (Path.IsPathRooted(name)) return IntPtr.Zero;
        string file = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";

        var candidates = new List<string> { Path.Combine(Environment.SystemDirectory, file) };
        if (file.Equals("nvml.dll", StringComparison.OrdinalIgnoreCase)) // drivers NVIDIA antigos (não-DCH)
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll"));

        foreach (var c in candidates)
            if (File.Exists(c) && NativeLibrary.TryLoad(c, out IntPtr h)) return h;

        // Não achou numa pasta protegida: falha em vez de deixar o Windows procurar na pasta do app.
        throw new DllNotFoundException($"{file} não encontrada em {Environment.SystemDirectory}.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string? path);
}
