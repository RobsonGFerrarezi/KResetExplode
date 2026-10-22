using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace KResetExplode;

/// <summary>
/// Verificação de assinatura digital (Authenticode) feita pelo próprio Windows, mais a conferência do fabricante pelo campo
/// "O=" (organização) do certificado — e não por um trecho qualquer do nome, que aceitaria "Fulano Fã da NVIDIA Ltda".
/// </summary>
public static class Authenticode
{
    /// <summary>
    /// Confere a assinatura do arquivo e se a organização do certificado começa com um dos nomes aceitos
    /// (comparação sem maiúsculas/pontuação, ex.: "nvidia corporation"). Devolve a organização; lança exceção se falhar.
    /// </summary>
    public static string Verify(string path, IReadOnlyList<string> allowedOrgs, bool checkRevocation)
    {
        int trust = WinVerifyTrustFile(path, checkRevocation);
        if (trust != 0) throw new InvalidOperationException(Explain(trust));

        string org;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            org = Organization(cert.Subject);
        }
        catch (Exception ex) { throw new InvalidOperationException("Não foi possível ler o certificado: " + ex.Message); }

        string n = Normalize(org);
        if (n.Length == 0 || !allowedOrgs.Any(a => n.StartsWith(Normalize(a), StringComparison.Ordinal)))
            throw new InvalidOperationException($"O arquivo está assinado por outro fabricante ({(org.Length > 0 ? org : "sem organização")}).");
        return org;
    }

    private static string Explain(int code) => unchecked((uint)code) switch
    {
        0x800B0100 => "O arquivo não tem assinatura digital.",
        0x80096010 => "A assinatura digital não confere com o conteúdo (arquivo alterado).",
        0x800B010C => "O certificado da assinatura foi revogado.",
        0x80092013 or 0x800B010E => "Não foi possível consultar a revogação do certificado (sem internet?).",
        0x800B0101 => "O certificado da assinatura expirou.",
        _ => $"Assinatura digital inválida (0x{code:X8}).",
    };

    /// <summary>Valor de O= no nome do certificado (aceita valores entre aspas, como O="Advanced Micro Devices, Inc.").</summary>
    private static string Organization(string subject)
    {
        int i = 0;
        while (i < subject.Length)
        {
            while (i < subject.Length && (subject[i] == ' ' || subject[i] == ',')) i++;
            int eq = subject.IndexOf('=', i);
            if (eq < 0) break;
            string key = subject[i..eq].Trim();
            i = eq + 1;
            var val = new StringBuilder();
            if (i < subject.Length && subject[i] == '"')
            {
                for (i++; i < subject.Length; i++)
                {
                    if (subject[i] == '"' && i + 1 < subject.Length && subject[i + 1] == '"') { val.Append('"'); i++; }
                    else if (subject[i] == '"') { i++; break; }
                    else val.Append(subject[i]);
                }
            }
            else
                for (; i < subject.Length && subject[i] != ','; i++) val.Append(subject[i]);
            if (key.Equals("O", StringComparison.OrdinalIgnoreCase)) return val.ToString().Trim();
        }
        return "";
    }

    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();

    // ---- WinVerifyTrust ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

    private static int WinVerifyTrustFile(string path, bool checkRevocation)
    {
        var file = new WINTRUST_FILE_INFO { pcwszFilePath = Path.GetFullPath(path) };
        file.cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>();
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new WINTRUST_DATA
            {
                dwUIChoice = 2,                                    // WTD_UI_NONE
                fdwRevocationChecks = checkRevocation ? 1u : 0u,   // WTD_REVOKE_WHOLECHAIN / WTD_REVOKE_NONE
                dwUnionChoice = 1,                                 // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,                                 // WTD_STATEACTION_IGNORE
                dwProvFlags = checkRevocation ? 0x80u : 0x10u,     // CHAIN_EXCLUDE_ROOT / REVOCATION_CHECK_NONE
            };
            data.cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>();
            return WinVerifyTrust(new IntPtr(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), ref data);
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
