namespace GeminiNetworkAssistant.Models;

public sealed class EndpointResult
{
    public required string Host { get; init; }
    public string DnsResult { get; set; } = "Не проверено";
    public string TcpResult { get; set; } = "Не проверено";
    public string HttpResult { get; set; } = "Не проверено";
    public bool DnsOk { get; set; }
    public bool TcpOk { get; set; }
    public bool HttpResponded { get; set; }
}

public sealed class DiagnosticReport
{
    public bool InternetReachable { get; set; }
    public bool DnsHealthy { get; set; }
    public int SuccessfulHttpsChecks { get; set; }
    public List<EndpointResult> Endpoints { get; set; } = [];
}
