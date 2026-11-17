using System.Windows;
using System.Text;

namespace GeminiNetworkAssistant;

public partial class MainWindow : Window
{
    private readonly NetworkDiagnosticService _diagnostics = new();

    public MainWindow()
    {
        InitializeComponent();
        Log("Программа запущена. Изменения сетевых настроек не выполняются.");
    }

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        RunDiagnosticsButton.IsEnabled = false;
        RunDiagnosticsButton.Content = "Проверяем...";
        LogBox.Clear();

        try
        {
            var result = await _diagnostics.RunAsync();
            NetworkStatus.Text = result.InternetReachable ? "Есть связь" : "Нет связи";
            DnsStatus.Text = result.DnsHealthy ? "Работает" : "Есть ошибки";
            GoogleStatus.Text = $"{result.SuccessfulHttpsChecks}/{result.Endpoints.Count} HTTPS";

            foreach (var endpoint in result.Endpoints)
            {
                Log($"[{endpoint.Host}]");
                Log($"  DNS: {endpoint.DnsResult}");
                Log($"  TCP 443: {endpoint.TcpResult}");
                Log($"  HTTPS: {endpoint.HttpResult}");
                Log("");
            }

            Log("Диагностика завершена.");
            Log("Примечание: HTTP 404/403 означает, что сервер ответил; это не равнозначно успешной работе самого сервиса.");
        }
        catch (Exception ex)
        {
            Log("Ошибка диагностики: " + ex.Message);
        }
        finally
        {
            RunDiagnosticsButton.IsEnabled = true;
            RunDiagnosticsButton.Content = "Запустить диагностику";
        }
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
