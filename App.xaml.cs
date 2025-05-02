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
                authService.Logout(CurrentUser.Id).Wait();
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не прерываем выход
                Console.WriteLine($"Error during logout: {ex.Message}");
            }
        }
        base.OnExit(e);
        this.Shutdown();
    }
}

