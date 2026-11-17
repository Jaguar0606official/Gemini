namespace GeminiNetworkAssistant.Models;
public sealed class AdapterInfo
{
    public int InterfaceIndex { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Dns { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Display => $"{Name} [{Kind}] — {Dns}";
    public override string ToString() => Display;
}
