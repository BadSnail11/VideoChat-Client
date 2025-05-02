using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VideoChat_Client.Services;
using Supabase;
using DotNetEnv;

namespace VideoChat_Client.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;

        public LoginWindow()
        {
            InitializeComponent();

            Env.TraversePath().Load();
            string SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
            string SupabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY")!;
            // Инициализация Supabase клиента
            var supabase = new Client(
                SupabaseUrl,
                SupabaseKey);

            _authService = new AuthService(supabase);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Имя пользователя и пароль обязательны");
                return;
            }

            var user = await _authService.Login(username, password);

            if (user != null)
            {
                // Сохраняем текущего пользователя
                App.CurrentUser = user;

                // Открываем главное окно
                var mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
            else
            {
                ShowError("Неверное имя пользователя или пароль");
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Имя пользователя и пароль обязательны");
                return;
            }

            if (password.Length < 4)
            {
                ShowError("Пароль должен быть не менее 4 символов");
                return;
            }

            var success = await _authService.Register(username, password);

            if (success)
            {

                //ShowError("Регистрация успешна! Теперь войдите.", isError: false);
                // Сохраняем текущего пользователя
                LoginButton_Click(sender, e);
            }
            else
            {
                ShowError("Имя пользователя уже занято");
            }
        }

        private void ShowError(string message, bool isError = true)
        {
            MessageText.Text = message;
            MessageText.Foreground = isError ? Brushes.Red : Brushes.Green;
            MessageText.Visibility = Visibility.Visible;
        }
    }
}
