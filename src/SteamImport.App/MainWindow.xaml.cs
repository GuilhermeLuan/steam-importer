using System.ComponentModel;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Microsoft.Win32;
using SteamImport.Core;
using SteamImport.Infrastructure;
using MessageBox = System.Windows.MessageBox;

namespace SteamImport.App;

public partial class MainWindow : Window
{
    private SteamInstallation? installation;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindowLoaded;
        Closing += MainWindowClosing;
    }

    private void MainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (App.Current is App app && app.HandleMainWindowClosing())
        {
            e.Cancel = true;
        }
    }

    private void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ShowNetworkAddresses();
            if (TryApplySavedConfiguration())
            {
                WindowState = WindowState.Minimized;
                return;
            }

            var detected = WindowsSteamInstallationLocator.Find();
            if (detected is null)
            {
                App.Log.LogWarning("steam.detection-completed", "result=not-found");
                SteamStatusTextBlock.Text = "Steam não encontrada. Selecione manualmente a pasta da instalação.";
                return;
            }

            App.Log.LogInformation("steam.detection-completed", $"result=found accounts={detected.Accounts.Count}");
            ApplyInstallation(detected);
        }
        catch (Exception exception)
        {
            HandleUnexpectedFailure("steam.detection-failed", exception);
        }
    }

    private bool TryApplySavedConfiguration()
    {
        var startup = App.LocalStartup.Resume();
        if (startup.Configuration is null)
        {
            if (startup.SavedConfigurationInvalid)
            {
                App.Log.LogWarning("configuration.load-failed", "result=reconfiguration-required");
                ConfigurationStatusTextBlock.Text = "A configuração salva não é mais válida. Revise os campos abaixo.";
            }
            else
            {
                ConfigurationStatusTextBlock.Text = "Configuração pendente. Preencha os campos e confirme a conta Steam.";
            }

            return false;
        }

        try
        {
            var configuration = startup.Configuration;
            GamesRootTextBox.Text = configuration.GamesRootPath;
            SteamGridDbApiKeyPasswordBox.Password = configuration.SteamGridDbApiKey;
            var savedInstallation = SteamInstallation.Open(configuration.SteamRootPath);
            ApplyInstallation(savedInstallation);
            AccountComboBox.SelectedItem = savedInstallation.Accounts.Single(account =>
                string.Equals(account.Id, configuration.SteamAccountId, StringComparison.Ordinal));
            ConfigurationStatusTextBlock.Text = "Configuração pronta. O servidor remoto está disponível.";
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidLocalConfigurationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            FormatException)
        {
            App.Log.LogWarning("configuration.load-failed", "result=reconfiguration-required");
            ConfigurationStatusTextBlock.Text = "A configuração salva não é mais válida. Revise os campos abaixo.";
            return false;
        }
    }

    private void BrowseGamesRootClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta raiz que contém seus jogos",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            GamesRootTextBox.Text = dialog.FolderName;
        }
    }

    private void SaveConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (installation is null || AccountComboBox.SelectedItem is not SteamAccount account)
        {
            ConfigurationStatusTextBlock.Text = "Carregue uma instalação Steam e selecione uma conta local.";
            return;
        }

        try
        {
            App.SaveConfiguration(new LocalConfiguration(
                GamesRootTextBox.Text,
                SteamGridDbApiKeyPasswordBox.Password,
                installation.RootPath,
                account.Id));

            App.Log.LogInformation("configuration.saved", $"account={account.Id} result=ready");
            ConfigurationStatusTextBlock.Text = "Configuração salva e validada. A chave está protegida para este usuário do Windows.";
            ShowNetworkAddresses();
        }
        catch (InvalidLocalConfigurationException exception)
        {
            App.Log.LogWarning("configuration.rejected", "result=validation-failed");
            ConfigurationStatusTextBlock.Text = exception.Message;
            MessageBox.Show(
                this,
                exception.Message,
                "Configuração inválida",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            System.Security.SecurityException)
        {
            App.Log.LogError("configuration.save-failed", "result=reported", exception);
            ConfigurationStatusTextBlock.Text = "Não foi possível salvar a configuração local.";
            MessageBox.Show(this, exception.Message, "Falha ao salvar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowNetworkAddresses()
    {
        var addresses = LocalNetworkAddresses.GetHttpAddresses(SteamImport.Web.SteamImportServer.Port);
        NetworkAddressesTextBlock.Text = addresses.Count == 0
            ? $"REMOTE_URL // http://<IP-DESTE-PC>:{SteamImport.Web.SteamImportServer.Port}"
            : $"REMOTE_URL // {string.Join("  |  ", addresses)}";
    }

    private void LoadSteamClick(object sender, RoutedEventArgs e)
    {
        TryLoadInstallation(SteamPathTextBox.Text);
    }

    private void BrowseSteamClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta da instalação da Steam",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            TryLoadInstallation(dialog.FolderName);
        }
    }

    private void BrowseGameClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta do jogo",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var review = ManualImportPlanner.CreateReview(dialog.FolderName);
            App.Log.LogInformation(
                "game.review-created",
                $"candidates={review.ExecutableCandidates.Count}");
            GameFolderTextBox.Text = dialog.FolderName;
            DisplayNameTextBox.Text = review.DisplayName;
            ExecutableComboBox.ItemsSource = review.ExecutableCandidates;
            ExecutableComboBox.SelectedItem = review.RecommendedExecutable;
            OperationStatusTextBlock.Text = "Revise o nome e o executável antes de importar.";
            UpdateReviewState();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            App.Log.LogError("game.review-failed", "result=rejected", exception);
            MessageBox.Show(this, exception.Message, "Não foi possível analisar a pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            HandleUnexpectedFailure("game.review-failed", exception);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This UI boundary logs unexpected failures and reports them instead of terminating silently.")]
    private void ImportClick(object sender, RoutedEventArgs e)
    {
        if (AccountComboBox.SelectedItem is not SteamAccount account ||
            ExecutableComboBox.SelectedItem is not string executablePath)
        {
            return;
        }

        var request = new ManualGameImportRequest(
            account.ShortcutsPath,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamImport",
                "Backups",
                account.Id),
            DisplayNameTextBox.Text,
            executablePath);

        try
        {
            var existingShortcutCount = SteamShortcutStore.ReadAll(account.ShortcutsPath).Count;
            var appId = SteamShortcutAppId.Calculate(executablePath, DisplayNameTextBox.Text.Trim());
            App.Log.LogInformation(
                "import.confirmation-requested",
                $"account={account.Id} existingShortcuts={existingShortcutCount} appId={appId}");
            var confirmation = MessageBox.Show(
                this,
                $"Conta: {account.Id}\n" +
                $"Atalhos não Steam existentes: {existingShortcutCount}\n\n" +
                $"Adicionar '{DisplayNameTextBox.Text.Trim()}' preservando esses atalhos?",
                "Confirmar alteração do shortcuts.vdf",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                App.Log.LogInformation("import.cancelled", $"account={account.Id} reason=user-declined");
                OperationStatusTextBlock.Text = "Importação cancelada; nenhuma alteração foi feita.";
                return;
            }

            ImportButton.IsEnabled = false;
            var shortcut = new ManualGameImporter(new WindowsSteamProcessProbe(), App.Log).Import(request);
            OperationStatusTextBlock.Text = $"{shortcut.DisplayName} foi adicionado. Abra a Steam para conferir o atalho.";
            MessageBox.Show(this, "Jogo importado com sucesso.", "Steam Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (SteamIsRunningException exception)
        {
            App.Log.LogWarning("import.rejected-by-ui", "reason=steam-running");
            OperationStatusTextBlock.Text = exception.Message;
            MessageBox.Show(this, exception.Message, "Feche a Steam", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            App.Log.LogError("import.failed-at-ui", "result=reported", exception);
            OperationStatusTextBlock.Text = "A importação falhou; o arquivo original foi preservado ou restaurado.";
            MessageBox.Show(this, exception.Message, "Falha na importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            HandleUnexpectedFailure("import.failed-at-ui", exception);
        }
        finally
        {
            UpdateReviewState();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        App.Log.LogInformation("game.review-cancelled", "result=cleared");
        GameFolderTextBox.Clear();
        DisplayNameTextBox.Clear();
        ExecutableComboBox.ItemsSource = null;
        AppIdTextBlock.Text = string.Empty;
        OperationStatusTextBlock.Text = "Revisão cancelada; nenhuma alteração foi feita.";
        UpdateReviewState();
    }

    private void ReviewFieldChanged(object sender, RoutedEventArgs e)
    {
        UpdateReviewState();
    }

    private void TryLoadInstallation(string path)
    {
        try
        {
            var opened = SteamInstallation.Open(path);
            App.Log.LogInformation("steam.manual-load-completed", $"result=success accounts={opened.Accounts.Count}");
            ApplyInstallation(opened);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            App.Log.LogError("steam.manual-load-failed", "result=rejected", exception);
            installation = null;
            AccountComboBox.ItemsSource = null;
            SteamStatusTextBlock.Text = exception.Message;
            UpdateReviewState();
        }
        catch (Exception exception)
        {
            HandleUnexpectedFailure("steam.manual-load-failed", exception);
        }
    }

    private void ApplyInstallation(SteamInstallation value)
    {
        installation = value;
        SteamPathTextBox.Text = value.RootPath;
        AccountComboBox.ItemsSource = value.Accounts;
        AccountComboBox.SelectedIndex = value.Accounts.Count == 1 ? 0 : -1;
        SteamStatusTextBlock.Text = value.Accounts.Count switch
        {
            0 => "Nenhuma conta local encontrada. Entre na Steam ao menos uma vez.",
            1 => "Instalação e conta local detectadas.",
            _ => "Mais de uma conta local encontrada. Selecione a conta que receberá o atalho.",
        };
        UpdateReviewState();
    }

    private void UpdateReviewState()
    {
        if (ExecutableComboBox.SelectedItem is string executablePath &&
            !string.IsNullOrWhiteSpace(DisplayNameTextBox.Text))
        {
            var appId = SteamShortcutAppId.Calculate(executablePath, DisplayNameTextBox.Text.Trim());
            AppIdTextBlock.Text = $"0x{appId:X8} ({appId})";
        }
        else
        {
            AppIdTextBlock.Text = string.Empty;
        }

        ImportButton.IsEnabled = installation is not null &&
                                 AccountComboBox.SelectedItem is SteamAccount &&
                                 ExecutableComboBox.SelectedItem is string &&
                                 !string.IsNullOrWhiteSpace(DisplayNameTextBox.Text);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This method is called only from UI exception boundaries.")]
    private void HandleUnexpectedFailure(string eventName, Exception exception)
    {
        App.Log.LogError(eventName, "result=reported", exception);
        OperationStatusTextBlock.Text = "Ocorreu um erro inesperado. Os detalhes foram registrados no log.";
        MessageBox.Show(
            this,
            App.BuildUnexpectedErrorMessage(),
            "Erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
