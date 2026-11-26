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
    }

    public async Task<AccountProfile?> StartLoginAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            Dispatcher.UIThread.Post(() => StatusText.Text = "Solicitando código à Microsoft...");
            var deviceCode = await _authService.InitiateDeviceCodeLoginAsync(_cts.Token);

            Dispatcher.UIThread.Post(() =>
            {
                UserCodeText.Text = deviceCode.UserCode;
                StatusText.Text = "Aguardando autorização do usuário...";
            });

            var account = await _authService.PollDeviceCodeResultAsync(deviceCode, _cts.Token);
            _completed = true;
            Close(account);
            return account;
        }
        catch (OperationCanceledException)
        {
            Close(null);
            return null;
        }
        catch (System.Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusText.Text = $"Erro: {ex.Message}");
            Close(null);
            return null;
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
