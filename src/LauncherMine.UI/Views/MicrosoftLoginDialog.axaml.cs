using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LauncherMine.Core.Auth;
using LauncherMine.Core.Models;
using LauncherMine.Core.Security;

namespace LauncherMine.UI.Views;

public partial class MicrosoftLoginDialog : Window
{
    private readonly MicrosoftAuthService _authService;
    private CancellationTokenSource? _cts;
    private bool _completed;

    public MicrosoftLoginDialog()
    {
        InitializeComponent();
        _authService = new MicrosoftAuthService(SecureVaultFactory.Create());
        Opened += (_, _) => _ = StartLoginAsync();
    }

    public async Task StartLoginAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            Dispatcher.UIThread.Post(() => StatusText.Text = "Solicitando código à Microsoft...");
            var deviceCode = await _authService.InitiateDeviceCodeLoginAsync(_cts.Token);

            // Abre o navegador automaticamente na página de verificação (com código já preenchido quando disponível)
            string verifyUrl = deviceCode.VerificationUriComplete ?? deviceCode.VerificationUri;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = verifyUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Navegador indisponível — o usuário pode digitar manualmente
            }

            Dispatcher.UIThread.Post(() =>
            {
                UserCodeText.Text = deviceCode.UserCode;
                StatusText.Text = "Aguardando autorização do usuário...";
            });

            var account = await _authService.PollDeviceCodeResultAsync(deviceCode, _cts.Token);
            _completed = true;
            Close(account);
        }
        catch (OperationCanceledException)
        {
            Close(null);
        }
        catch (System.Exception ex)
        {
            // Mantém o dialog aberto exibindo o erro — o usuário decide cancelar
            Dispatcher.UIThread.Post(() => StatusText.Text = $"Erro: {ex.Message}");
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        if (!_completed)
        {
            _cts?.Cancel();
        }
        Close(null);
    }
}
