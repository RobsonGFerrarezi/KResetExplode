using System.Runtime.InteropServices;

namespace KResetExplode;

/// <summary>
/// Abre programas, atalhos e links SEM privilégio de administrador. O RESET EXPLODE roda elevado, e tudo que ele inicia
/// com Process.Start herda o administrador (jogos, ferramentas auxiliares, o navegador...). Aqui o pedido é feito ao
/// Explorer (que roda como usuário comum) pelo IShellDispatch2.ShellExecute da área de trabalho — técnica descrita por
/// Raymond Chen ("How can I launch an unelevated process from my elevated process").
/// </summary>
public static class ShellLauncher
{
    /// <summary>Abre um arquivo, URL ou atalho como usuário comum. Lança exceção se o Explorer não estiver disponível.</summary>
    public static void Open(string file, string? args = null, string? workDir = null, bool hidden = false)
    {
        Exception? error = null;
        // As interfaces do Shell exigem uma thread STA.
        var t = new Thread(() =>
        {
            try { Run(file, args ?? "", workDir ?? "", hidden ? 0 : 1); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        if (!t.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("O Explorer não respondeu.");
        if (error != null)
            throw new InvalidOperationException("Não foi possível abrir sem privilégio de administrador (o Explorer precisa estar aberto): " + error.Message, error);
    }

    /// <summary>Abre uma página no navegador padrão, como usuário comum. Devolve false se falhar.</summary>
    public static bool TryOpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http" or "ms-windows-store")) return false;
        try { Open(u.AbsoluteUri); return true; } catch { return false; }
    }

    private static void Run(string file, string args, string dir, int show)
    {
        var shellWindows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, true)!)!;
        try
        {
            object loc = CSIDL_DESKTOP, root = Type.Missing;
            int hr = shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out object desktop);
            if (hr != 0 || desktop == null) throw new COMException("Área de trabalho do Explorer não encontrada.", hr);

            var sp = (IServiceProvider)desktop;
            Guid sid = SID_STopLevelBrowser, iidBrowser = typeof(IShellBrowser).GUID;
            Marshal.ThrowExceptionForHR(sp.QueryService(ref sid, ref iidBrowser, out object browserObj));
            var browser = (IShellBrowser)browserObj;
            Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out IShellView view));
            Guid iidDispatch = IID_IDispatch;
            Marshal.ThrowExceptionForHR(view.GetItemObject(SVGIO_BACKGROUND, ref iidDispatch, out object folderView));

            dynamic shellApp = ((dynamic)folderView).Application;
            shellApp.ShellExecute(file, args, dir, "open", show); // 1 = SW_SHOWNORMAL, 0 = SW_HIDE
        }
        finally { Marshal.FinalReleaseComObject(shellWindows); }
    }

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private const int CSIDL_DESKTOP = 0, SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void SlotCount(); void SlotItem(); void SlotNewEnum(); void SlotRegister();
        void SlotRegisterPending(); void SlotRevoke(); void SlotOnNavigate(); void SlotOnActivated();
        [PreserveSig]
        int FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc, [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
                         int swClass, out int phwnd, int swfwOptions, [MarshalAs(UnmanagedType.IDispatch)] out object ppdispOut);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void SlotGetWindow(); void SlotContextSensitiveHelp(); void SlotInsertMenusSB(); void SlotSetMenuSB();
        void SlotRemoveMenusSB(); void SlotSetStatusTextSB(); void SlotEnableModelessSB(); void SlotTranslateAcceleratorSB();
        void SlotBrowseObject(); void SlotGetViewStateStream(); void SlotGetControlWindow(); void SlotSendControlMsg();
        [PreserveSig] int QueryActiveShellView(out IShellView ppshv);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void SlotGetWindow(); void SlotContextSensitiveHelp(); void SlotTranslateAccelerator(); void SlotEnableModeless();
        void SlotUIActivate(); void SlotRefresh(); void SlotCreateViewWindow(); void SlotDestroyViewWindow();
        void SlotGetCurrentInfo(); void SlotAddPropertySheetPages(); void SlotSaveViewState(); void SlotSelectItem();
        [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }
}
