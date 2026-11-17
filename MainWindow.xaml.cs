using System.Windows;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Principal;

namespace GeminiNetworkAssistant;

public partial class MainWindow : Window
{
    private readonly NetworkDiagnosticService _diagnostics = new();
    private readonly Dictionary<int, string[]> _dnsSnapshot = new();

    public MainWindow()
    {
        InitializeComponent();
        Log("GNA 0.4 запущен. Сетевые настройки меняются только вручную по команде пользователя.");
        RefreshAdapters();
    }

    private sealed class AdapterItem
    {
        public int InterfaceIndex { get; init; }
        public string DisplayName { get; init; } = "";
        public override string ToString() => DisplayName;
    }

    private void RefreshAdapters()
    {
        AdapterBox.Items.Clear();
        _dnsSnapshot.Clear();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var props = ni.GetIPProperties();
                var index = props.GetIPv4Properties()?.Index;
                if (index is null) continue;
                var dns = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString()).ToArray();
                _dnsSnapshot[index.Value] = dns;
                AdapterBox.Items.Add(new AdapterItem
                {
                    InterfaceIndex = index.Value,
                    DisplayName = $"{ni.Name} ({ni.NetworkInterfaceType}) — {string.Join(", ", dns)}"
                });
            }
            catch { }
        }
        if (AdapterBox.Items.Count > 0) AdapterBox.SelectedIndex = 0;
        DnsLog("Список адаптеров обновлён. Снимок текущих IPv4 DNS сохранён в памяти до закрытия программы.");
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string command)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить PowerShell.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr);
    }

    private AdapterItem? SelectedAdapter => AdapterBox.SelectedItem as AdapterItem;

    private async void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdministrator())
        {
            DnsLog("Нужны права администратора. Закрой программу и запусти её через «Запуск от имени администратора».");
            return;
        }
        if (SelectedAdapter is null || DnsProviderBox.SelectedItem is not System.Windows.Controls.ComboBoxItem provider)
        {
            DnsLog("Выбери сетевой адаптер и DNS-провайдера.");
            return;
        }
        var servers = (provider.Tag?.ToString() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (servers.Length == 0) return;
        var index = SelectedAdapter.InterfaceIndex;
        var list = string.Join(",", servers.Select(x => $"'{x}'"));
        var result = await RunPowerShellAsync($"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses @({list}) -ErrorAction Stop; Clear-DnsClientCache; Write-Output 'DNS_APPLIED'");
        DnsLog(result.Output);
        if (result.ExitCode == 0) DnsLog("Готово. DNS изменён; проверь доступность сайтов. Это не снимает региональные ограничения.");
    }

    private async void RestoreDns_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdministrator())
        {
            DnsLog("Нужны права администратора для восстановления DNS.");
            return;
        }
        if (SelectedAdapter is null) return;
        var index = SelectedAdapter.InterfaceIndex;
        if (!_dnsSnapshot.TryGetValue(index, out var servers) || servers.Length == 0)
        {
            DnsLog("Для этого адаптера не сохранён исходный IPv4 DNS. Обнови список до изменения настроек или восстанови DNS вручную.");
            return;
        }
        var list = string.Join(",", servers.Select(x => $"'{x}'"));
        var result = await RunPowerShellAsync($"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses @({list}) -ErrorAction Stop; Clear-DnsClientCache; Write-Output 'DNS_RESTORED'");
        DnsLog(result.Output);
    }

    private async void ShowDns_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAdapter is null) return;
        var result = await RunPowerShellAsync($"Get-DnsClientServerAddress -InterfaceIndex {SelectedAdapter.InterfaceIndex} -AddressFamily IPv4 | Format-List InterfaceAlias,ServerAddresses");
        DnsLog(result.Output);
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e) => RefreshAdapters();

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        RunDiagnosticsButton.IsEnabled = false;
        RunDiagnosticsButton.Content = "Проверяем...";
        LogBox.Clear();
        try
        {
            var result = await _diagnostics.RunAsync();
            foreach (var endpoint in result.Endpoints)
            {
                Log($"[{endpoint.Host}]");
                Log($"  DNS: {endpoint.DnsResult}");
                Log($"  TCP 443: {endpoint.TcpResult}");
                Log($"  HTTPS: {endpoint.HttpResult}");
                Log("");
            }
            Log("Диагностика завершена. HTTP 403/404 означает ответ сервера, но не подтверждает доступность самого сервиса.");
        }
        catch (Exception ex) { Log("Ошибка: " + ex.Message); }
        finally { RunDiagnosticsButton.IsEnabled = true; RunDiagnosticsButton.Content = "Запустить диагностику"; }
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void DnsLog(string message)
    {
        DnsLogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        DnsLogBox.ScrollToEnd();
    }
}
