using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GeminiNetworkAssistant.Models;

namespace GeminiNetworkAssistant;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AdapterRow> _rows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeminiNetworkAssistant", "dns-backup.json");
    private bool _monitoring;
    private bool _monitorBusy;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        AdapterGrid.ItemsSource = _rows;
        _timer.Tick += async (_, _) => await CheckPublicIpAsync(true);
        RefreshAdapters();
        Write(MainLog, "Программа запущена. Автоматических изменений сети не выполняется.");
        Write(DnsLog, $"Резервная копия DNS: {_backupPath}");
    }

    private sealed class AdapterRow
    {
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Ipv4 { get; init; } = "";
        public string Gateway { get; init; } = "";
        public string Dns { get; init; } = "";
    }
    private sealed class DnsBackup
    {
        public int InterfaceIndex { get; set; }
        public string Alias { get; set; } = "";
        public bool DhcpEnabled { get; set; }
        public string[] Servers { get; set; } = [];
        public DateTime SavedAt { get; set; }
    }

    private static void Write(System.Windows.Controls.TextBox box, string message)
    {
        box.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        box.ScrollToEnd();
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private static async Task<(int Code, string Output)> PowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script));
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить PowerShell.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        var output = await stdoutTask;
        var error = await stderrTask;
        return (p.ExitCode, string.IsNullOrWhiteSpace(error) ? output.Trim() : (output + Environment.NewLine + error).Trim());
    }

    private void RefreshAdapters()
    {
        var selectedIndex = (DnsAdapterBox.SelectedItem as AdapterInfo)?.InterfaceIndex;
        var list = new List<AdapterInfo>();
        _rows.Clear();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var ip = ni.GetIPProperties();
                var idx = ip.GetIPv4Properties()?.Index;
                if (idx is null) continue;
                var v4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToArray();
                var gw = ip.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Select(g => g.Address.ToString()).ToArray();
                var dns = ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
                var kind = ni.NetworkInterfaceType.ToString();
                var info = new AdapterInfo { InterfaceIndex = idx.Value, Name = ni.Name, Description = ni.Description, Dns = dns.Length == 0 ? "не указаны" : string.Join(", ", dns), Kind = kind };
                list.Add(info);
                _rows.Add(new AdapterRow { Name = ni.Name, Kind = kind, Ipv4 = v4.Length == 0 ? "—" : string.Join(", ", v4), Gateway = gw.Length == 0 ? "—" : string.Join(", ", gw), Dns = info.Dns });
            }
            catch { }
        }
        DnsAdapterBox.ItemsSource = list;
        DnsAdapterBox.SelectedItem = selectedIndex.HasValue ? list.FirstOrDefault(x => x.InterfaceIndex == selectedIndex.Value) : null;
        if (DnsAdapterBox.SelectedIndex < 0 && list.Count > 0)
            DnsAdapterBox.SelectedItem = list.FirstOrDefault(x => x.Kind is "Ethernet" or "Wireless80211") ?? list[0];
        Write(MainLog, $"Обнаружено активных IPv4-адаптеров: {list.Count}. Выбор основного маршрута ориентировочный — проверь шлюз и адаптер VPN.");
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e) => RefreshAdapters();

    private async void CheckPublic_Click(object sender, RoutedEventArgs e) => await CheckPublicIpAsync(false);

    private async Task CheckPublicIpAsync(bool fromMonitor)
    {
        if (_monitorBusy) return;
        _monitorBusy = true;
        try
        {
            if (!fromMonitor) CheckPublicButton.IsEnabled = false;
            var ipText = (await _http.GetStringAsync("https://api.ipify.org")).Trim();
            if (!IPAddress.TryParse(ipText, out _)) throw new InvalidOperationException("Сервис вернул некорректный IP: " + ipText);
            PublicIpText.Text = ipText;
            try
            {
                using var doc = JsonDocument.Parse(await _http.GetStringAsync("https://ipwho.is/" + Uri.EscapeDataString(ipText)));
                var root = doc.RootElement;
                var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                var country = success && root.TryGetProperty("country", out var c) ? c.GetString() : null;
                var region = success && root.TryGetProperty("region", out var r) ? r.GetString() : null;
                var city = success && root.TryGetProperty("city", out var cityEl) ? cityEl.GetString() : null;
                var isp = success && root.TryGetProperty("connection", out var conn) && conn.TryGetProperty("isp", out var ispEl) ? ispEl.GetString() : null;
                PublicGeoText.Text = success ? $"{country} / {region} / {city}\nПровайдер: {isp}" : "IP определён, геоданные недоступны.";
                Write(MainLog, $"Публичный IP: {ipText}; страна выхода: {country ?? "не определена"}; ISP: {isp ?? "не определён"}.");
            }
            catch (Exception ex)
            {
                PublicGeoText.Text = "IP определён, но геолокация не получена: " + ex.Message;
                Write(MainLog, "Не удалось получить геоданные: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            PublicIpText.Text = "Не удалось определить";
            PublicGeoText.Text = ex.Message;
            Write(MainLog, "Проверка внешнего IP не удалась: " + ex.Message);
        }
        finally
        {
            _monitorBusy = false;
            if (!fromMonitor) CheckPublicButton.IsEnabled = true;
        }
    }

    private async void Monitor_Click(object sender, RoutedEventArgs e)
    {
        _monitoring = !_monitoring;
        if (_monitoring)
        {
            MonitorButton.Content = "Остановить мониторинг";
            _timer.Start();
            Write(MainLog, "Фоновая проверка внешнего IP запущена: раз в 60 секунд.");
            await CheckPublicIpAsync(true);
        }
        else
        {
            _timer.Stop();
            MonitorButton.Content = "Начать мониторинг (60 сек)";
            Write(MainLog, "Фоновая проверка остановлена.");
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true; DiagButton.IsEnabled = false; DiagButton.Content = "Проверяем…"; DiagLog.Clear();
        string[] hosts = ["gemini.google.com", "accounts.google.com", "antigravity.google", "cloudcode-pa.googleapis.com", "businessaicode.googleapis.com", "aiplatform.googleapis.com"];
        try
        {
            foreach (var host in hosts)
            {
                Write(DiagLog, $"[{host}]");
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(host);
                    Write(DiagLog, addresses.Length > 0 ? "DNS: OK — " + string.Join(", ", addresses.Take(4).Select(a => a.ToString())) : "DNS: адресов нет");
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await tcp.ConnectAsync(host, 443, cts.Token);
                    Write(DiagLog, "TCP 443: соединение установлено");
                    try
                    {
                        using var response = await _http.GetAsync("https://" + host + "/", HttpCompletionOption.ResponseHeadersRead);
                        Write(DiagLog, $"HTTPS: {(int)response.StatusCode} {response.ReasonPhrase}");
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode is not null) { Write(DiagLog, $"HTTPS: {(int)ex.StatusCode} {ex.StatusCode} — сервер ответил"); }
                }
                catch (Exception ex) { Write(DiagLog, "ОШИБКА: " + ex.GetType().Name + " — " + ex.Message); }
                Write(DiagLog, "");
            }
            Write(DiagLog, "Важно: успешные DNS/TCP/HTTPS проверки не подтверждают региональную доступность продукта или допуск аккаунта.");
        }
        finally { _busy = false; DiagButton.IsEnabled = true; DiagButton.Content = "Запустить полную проверку"; }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(DiagLog.Text); Write(MainLog, "Журнал диагностики скопирован в буфер обмена."); }
        catch (Exception ex) { Write(MainLog, "Не удалось скопировать журнал: " + ex.Message); }
    }

    private async Task<DnsBackup?> GetBackupAsync(int index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_backupPath)!);
        List<DnsBackup> backups = [];
        if (File.Exists(_backupPath))
        {
            try { backups = JsonSerializer.Deserialize<List<DnsBackup>>(await File.ReadAllTextAsync(_backupPath)) ?? []; } catch { }
        }
        var existing = backups.FirstOrDefault(x => x.InterfaceIndex == index);
        if (existing != null) return existing;
        var result = await PowerShell($"$i=Get-NetIPInterface -InterfaceIndex {index} -AddressFamily IPv4; $d=Get-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily IPv4; [pscustomobject]@{{Alias=$i.InterfaceAlias;Dhcp=($i.Dhcp -eq 'Enabled');Servers=@($d.ServerAddresses)}} | ConvertTo-Json -Compress");
        if (result.Code != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new InvalidOperationException("Не удалось сохранить исходный DNS: " + result.Output);
        using var doc = JsonDocument.Parse(result.Output);
        var root = doc.RootElement;
        var servers = root.TryGetProperty("Servers", out var arr)
            ? (arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                : arr.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(arr.GetString()) ? [arr.GetString()!] : [])
            : [];
        var backup = new DnsBackup { InterfaceIndex = index, Alias = root.TryGetProperty("Alias", out var a) ? a.GetString() ?? "" : "", DhcpEnabled = root.TryGetProperty("Dhcp", out var dh) && dh.GetBoolean(), Servers = servers, SavedAt = DateTime.Now };
        backups.Add(backup);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(backups, new JsonSerializerOptions { WriteIndented = true }));
        return backup;
    }

    private async void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdmin()) { Write(DnsLog, "Для изменения DNS запусти программу от имени администратора."); return; }
        if (DnsAdapterBox.SelectedItem is not AdapterInfo adapter || DnsProviderBox.SelectedItem is not System.Windows.Controls.ComboBoxItem provider) { Write(DnsLog, "Выбери адаптер и DNS-провайдера."); return; }
        try
        {
            await GetBackupAsync(adapter.InterfaceIndex);
            var servers = (provider.Tag?.ToString() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
            var values = "@(" + string.Join(",", servers.Select(PsQuote)) + ")";
            var res = await PowerShell($"Set-DnsClientServerAddress -InterfaceIndex {adapter.InterfaceIndex} -ServerAddresses {values}; Clear-DnsClientCache; 'DNS_APPLIED'");
            Write(DnsLog, res.Output);
            if (res.Code == 0) Write(DnsLog, "DNS применён. Исходный режим и адреса сохранены в резервной копии.");
        }
        catch (Exception ex) { Write(DnsLog, "Ошибка: " + ex.Message); }
    }

    private async void RestoreDns_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdmin()) { Write(DnsLog, "Для восстановления DNS запусти программу от имени администратора."); return; }
        if (DnsAdapterBox.SelectedItem is not AdapterInfo adapter) { Write(DnsLog, "Выбери адаптер."); return; }
        try
        {
            var backup = await GetBackupFromFileAsync(adapter.InterfaceIndex);
            if (backup == null) { Write(DnsLog, "Резервная копия для этого адаптера не найдена."); return; }
            var script = backup.DhcpEnabled
                ? $"Set-DnsClientServerAddress -InterfaceIndex {backup.InterfaceIndex} -ResetServerAddresses; Clear-DnsClientCache; 'DNS_DHCP_RESTORED'"
                : $"Set-DnsClientServerAddress -InterfaceIndex {backup.InterfaceIndex} -ServerAddresses @({string.Join(",", backup.Servers.Select(PsQuote))}); Clear-DnsClientCache; 'DNS_STATIC_RESTORED'";
            var res = await PowerShell(script);
            Write(DnsLog, res.Output);
            if (res.Code == 0) Write(DnsLog, backup.DhcpEnabled ? "Восстановлен автоматический DNS (DHCP)." : "Восстановлены сохранённые статические DNS.");
        }
        catch (Exception ex) { Write(DnsLog, "Ошибка восстановления: " + ex.Message); }
    }

    private async Task<DnsBackup?> GetBackupFromFileAsync(int index)
    {
        if (!File.Exists(_backupPath)) return null;
        var list = JsonSerializer.Deserialize<List<DnsBackup>>(await File.ReadAllTextAsync(_backupPath)) ?? [];
        return list.FirstOrDefault(x => x.InterfaceIndex == index);
    }

    private async void ShowDns_Click(object sender, RoutedEventArgs e)
    {
        if (DnsAdapterBox.SelectedItem is not AdapterInfo adapter) return;
        var res = await PowerShell($"Get-DnsClientServerAddress -InterfaceIndex {adapter.InterfaceIndex} -AddressFamily IPv4 | Format-List InterfaceAlias,ServerAddresses");
        Write(DnsLog, res.Output);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop(); _http.Dispose(); base.OnClosed(e);
    }
}
