using System.Diagnostics;
using System.Windows;

namespace KResetExplode;

/// <summary>Aba RESET FERRAMENTAS · Instalar apps: essenciais pós-formatação pelo winget.</summary>
public partial class MainWindow
{
    private List<InstallableApp>? _installApps;
    private bool _appsBusy, _appsStop;

    private void InitApps()
    {
        if (_appsBusy) return;
        if (_installApps == null)
        {
            _installApps = AppInstaller.Catalog();
            AppsList.ItemsSource = _installApps;
            AppInstaller.RefreshInstalled(_installApps);
            foreach (var a in _installApps) a.Selected = a.PreSelect && !a.Installed;
        }
        else AppInstaller.RefreshInstalled(_installApps);

        bool winget = AppInstaller.WingetAvailable;
        BtnAppsStore.Visibility = winget ? Visibility.Collapsed : Visibility.Visible;
        BtnAppsInstall.IsEnabled = winget;
        int installed = _installApps.Count(a => a.Installed);
        AppsStatus.Text = winget
            ? $"{installed} de {_installApps.Count} já instalados. Marque o que quer e clique em INSTALAR SELECIONADOS."
            : "O winget não foi encontrado. Em PC recém-formatado ele aparece depois que a Microsoft Store atualiza o " +
              "\"Instalador de Aplicativo\": use o botão abaixo, espere atualizar e abra esta página de novo.";
    }

    private void BtnAppsAll_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        foreach (var a in _installApps) a.Selected = !a.Installed;
    }

    private void BtnAppsNone_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        foreach (var a in _installApps) a.Selected = false;
    }

    private void BtnAppsStore_Click(object sender, RoutedEventArgs e)
    {
        // Página do "Instalador de Aplicativo" (App Installer), que traz o winget.
        if (!ShellLauncher.TryOpenUrl("ms-windows-store://pdp/?productid=9NBLGGH4NNS1"))
            AppsStatus.Text = "Não foi possível abrir a Microsoft Store.";
    }

    private async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null) return;
        if (_appsBusy) { _appsStop = true; BtnAppsInstall.IsEnabled = false; AppsStatus.Text = "Parando depois do app atual…"; return; }

        var sel = _installApps.Where(a => a.Selected && !a.Installed).ToList();
        if (sel.Count == 0) { AppsStatus.Text = "Nenhum app selecionado."; return; }
        if (sel.Any(a => a.WingetId == "RARLab.WinRAR") && sel.Any(a => a.WingetId == "7zip.7zip"))
        {
            var r = MessageBox.Show("WinRAR e 7-Zip fazem a mesma coisa. Instalar os dois mesmo assim?", "Instalar apps",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        _appsBusy = true; _appsStop = false;
        BtnAppsInstall.Content = "PARAR";
        foreach (var a in sel) a.Status = "Na fila.";
        int ok = 0, done = 0;
        var failed = new List<string>();
        AppsProgress.ScaleX = 0;

        foreach (var app in sel)
        {
            if (_appsStop) { app.Status = "Não instalado (parado)."; continue; }
            AppsStatus.Text = $"Instalando {app.Name} ({done + 1} de {sel.Count})… baixando do site oficial, pode levar alguns minutos.";
            app.Status = "Instalando…";
            try
            {
                var (success, msg) = await AppInstaller.InstallAsync(app, CancellationToken.None);
                app.Status = msg;
                if (success) { ok++; app.Selected = false; }
                else failed.Add(app.Name);
            }
            catch (Exception ex) { app.Status = $"Falhou: {ex.Message}"; failed.Add(app.Name); }
            done++;
            AppsProgress.ScaleX = (double)done / sel.Count;
        }

        AppInstaller.RefreshInstalled(_installApps);
        _appsBusy = false;
        BtnAppsInstall.Content = "INSTALAR SELECIONADOS";
        BtnAppsInstall.IsEnabled = true;
        AppsStatus.Text = $"{ok} de {sel.Count} instalados." +
                          (failed.Count > 0 ? $" Falharam: {string.Join(", ", failed)} (veja o motivo na lista)." : "") +
                          (_appsStop ? " Instalação interrompida." : "");
    }

    // ---------------- Atualizações dos programas instalados ----------------

    private List<AppUpdate> _appUpdates = new();

    private async void BtnUpdScan_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) return;
        _appsBusy = true;
        BtnUpdScan.IsEnabled = false;
        UpdStatus.Text = "Procurando atualizações pelo winget…";
        try
        {
            _appUpdates = await AppInstaller.ListUpdatesAsync(CancellationToken.None);
            UpdList.ItemsSource = _appUpdates;
            UpdStatus.Text = _appUpdates.Count == 0
                ? "Todos os programas reconhecidos pelo winget estão atualizados."
                : $"{_appUpdates.Count} programa(s) com versão nova. Os de versão desconhecida vêm desmarcados (o winget não sabe a versão instalada).";
            BtnUpdRun.IsEnabled = _appUpdates.Count > 0;
        }
        catch (Exception ex) { UpdStatus.Text = "Falhou: " + ex.Message; }
        finally { _appsBusy = false; BtnUpdScan.IsEnabled = true; }
    }

    private async void BtnUpdRun_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) { _appsStop = true; BtnUpdRun.IsEnabled = false; UpdStatus.Text = "Parando depois do programa atual…"; return; }
        var sel = _appUpdates.Where(u => u.Selected).ToList();
        if (sel.Count == 0) { UpdStatus.Text = "Nenhum programa marcado."; return; }
        if (MessageBox.Show(this, $"Atualizar {sel.Count} programa(s)?\n\nFeche esses programas antes (um programa aberto pode não atualizar).",
                "Atualizar apps", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _appsBusy = true; _appsStop = false;
        BtnUpdRun.Content = "PARAR";
        BtnUpdScan.IsEnabled = false;
        foreach (var u in sel) u.Status = "Na fila.";
        int ok = 0, done = 0;
        foreach (var u in sel)
        {
            if (_appsStop) { u.Status = "Não atualizado (parado)."; continue; }
            UpdStatus.Text = $"Atualizando {u.Name} ({done + 1} de {sel.Count})…";
            u.Status = "Atualizando…";
            try
            {
                var (success, msg) = await AppInstaller.UpgradeAsync(u, CancellationToken.None);
                u.Status = success ? "Atualizado." : msg;
                if (success) { ok++; u.Selected = false; }
            }
            catch (Exception ex) { u.Status = "Falhou: " + ex.Message; }
            done++;
        }
        _appsBusy = false;
        BtnUpdRun.Content = "ATUALIZAR MARCADOS";
        BtnUpdRun.IsEnabled = true;
        BtnUpdScan.IsEnabled = true;
        UpdStatus.Text = $"{ok} de {sel.Count} atualizado(s)." + (ok < done ? " Veja o motivo dos que falharam na lista." : "") + (_appsStop ? " Interrompido." : "");
    }
}
