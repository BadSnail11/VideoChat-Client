using System.Configuration;
using System.Data;
using System.Windows;
using VideoChat_Client.Services;
using VideoChat_Client.Views;
using Supabase;
using DotNetEnv;

namespace VideoChat_Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static Models.User? CurrentUser { get; set; }

    public static string Ip = "";
    public static int Port = 0;

    private Dictionary<string, string> ParseArguments(string[] args)
    {
        var argsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string arg in args)
        {
            if (arg.StartsWith("--") && arg.Contains("="))
            {
                string[] parts = arg.Split('=');
                if (parts.Length == 2)
                {
                    string key = parts[0].TrimStart('-');
                    string value = parts[1];
                    argsDict[key] = value;
                }
            }
        }

        return argsDict;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Dictionary<string, string> argsDict = ParseArguments(e.Args);

        if (argsDict.TryGetValue("ip", out string ip))
        {
            Ip = ip;
        }
        else
        {
            Ip = "";
        }
        if (argsDict.TryGetValue("port", out string port))
        {
            Port = int.Parse(port);
        }
        else
        {
            Port = 0;
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (CurrentUser != null)
        {
            try
            {
                Env.TraversePath().Load();
                string SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
                string SupabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY")!;
                // Инициализация Supabase клиента
                var supabase = new Client(
                    SupabaseUrl,
                    SupabaseKey);
                var authService = new AuthService(supabase);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)); // Таймаут 5 секунд
                authService.Logout(CurrentUser.Id).WaitAsync(cts.Token);
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не прерываем выход
                Console.WriteLine($"Error during logout: {ex.Message}");
            }
        }
        base.OnExit(e);
    }
}

