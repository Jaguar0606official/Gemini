using System.Net;
using System.Net.Sockets;
using GeminiNetworkAssistant.Models;

namespace GeminiNetworkAssistant;

public sealed class NetworkDiagnosticService
{
    // Root URLs are only connectivity probes. 403/404 can be normal for API roots.
    private static readonly string[] Hosts =
    [
        "gemini.google.com",
        "accounts.google.com",
        "cloudcode-pa.googleapis.com",
        "businessaicode.googleapis.com",
        "aiplatform.googleapis.com"
    ];

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public async Task<DiagnosticReport> RunAsync()
    {
        var report = new DiagnosticReport();

        foreach (var host in Hosts)
        {
            var item = new EndpointResult { Host = host };

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                item.DnsOk = addresses.Length > 0;
                item.DnsResult = item.DnsOk
                    ? string.Join(", ", addresses.Take(3).Select(a => a.ToString()))
                    : "Нет адресов";
            }
            catch (Exception ex)
            {
                item.DnsResult = ex.GetType().Name + ": " + ex.Message;
            }

            if (item.DnsOk)
            {
                try
                {
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await tcp.ConnectAsync(host, 443, cts.Token);
                    item.TcpOk = true;
                    item.TcpResult = "Соединение установлено";
                }
                catch (Exception ex)
                {
                    item.TcpResult = ex.GetType().Name + ": " + ex.Message;
                }
            }
            else
            {
                item.TcpResult = "Пропущено: DNS не сработал";
            }

            if (item.TcpOk)
            {
                try
                {
                    using var response = await _http.GetAsync(
                        $"https://{host}/",
                        HttpCompletionOption.ResponseHeadersRead);
                    item.HttpResponded = true;
                    item.HttpResult = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                }
                catch (HttpRequestException ex) when (ex.StatusCode is not null)
                {
                    // A non-2xx HTTP response still proves HTTPS reached the server.
                    item.HttpResponded = true;
                    item.HttpResult = $"{(int)ex.StatusCode} {ex.StatusCode} (сервер ответил)";
                }
                catch (Exception ex)
                {
                    item.HttpResult = ex.GetType().Name + ": " + ex.Message;
                }
            }
            else
            {
                item.HttpResult = "Пропущено: TCP недоступен";
            }

            report.Endpoints.Add(item);
        }

        report.DnsHealthy = report.Endpoints.Any(x => x.DnsOk);
        report.InternetReachable = report.Endpoints.Any(x => x.TcpOk);
        report.SuccessfulHttpsChecks = report.Endpoints.Count(x => x.HttpResponded);
        return report;
    }
}
