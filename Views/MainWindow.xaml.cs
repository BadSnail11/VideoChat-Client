using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VideoChat_Client.Services;
using VideoChat_Client.Models;
using Supabase;
using DotNetEnv;

namespace VideoChat_Client.Views
{
    public partial class MainWindow : Window
    {
        private readonly ContactsService _contactsService;
        private readonly CallsService _callsService;
        private User _selectedUser;
        private Client _supabaseClient;
        //private User _selectedContact;
        //private List<User> _allContacts = new List<User>();

        public MainWindow()
        {
            InitializeComponent();

            Env.TraversePath().Load();
            string SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
            string SupabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY")!;
            // Инициализация Supabase клиента
            var supabase = new Client(
                SupabaseUrl,
                SupabaseKey);
            _contactsService = new ContactsService(supabase);
            _callsService = new CallsService(supabase);

            LoadUserContacts();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            // Обработка нажатия Enter в поле поиска
            SearchTextBox.KeyDown += (sender, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    SearchButton_Click(sender, e);
                }
            };
        }

        private async void LoadUserContacts()
        {
            try
            {
                if (App.CurrentUser == null)
                {
                    ShowError("Пользователь не авторизован");
                    return;
                }

                var contacts = await _contactsService.GetUserContacts(App.CurrentUser.Id);
                ContactsListView.ItemsSource = contacts;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка загрузки контактов: {ex.Message}");
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string username = SearchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                ShowError("Введите имя пользователя");
                return;
            }

            try
            {
                var users = await _contactsService.SearchUsers(username);
                var foundUser = users.FirstOrDefault();

                if (foundUser == null)
                {
                    ShowError("Пользователь не найден");
                    UserPanel.Visibility = Visibility.Collapsed;
                    DefaultMessageText.Visibility = Visibility.Visible;
                    return;
                }

                // Показываем информацию о пользователе
                _selectedUser = foundUser;
                FoundUsernameText.Text = foundUser.Username;

                // Загружаем историю звонков
                var callHistory = await _callsService.GetCallHistory(
                    App.CurrentUser.Id,
                    foundUser.Id);

                CallHistoryListView.ItemsSource = callHistory;

                // Показываем панель пользователя
                UserPanel.Visibility = Visibility.Visible;
                DefaultMessageText.Visibility = Visibility.Collapsed;
                ErrorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка поиска: {ex.Message}");
            }
        }

        private void ContactsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = ContactsListView.SelectedItem as User;
            if (_selectedUser == null) return;

            SearchTextBox.Text = _selectedUser.Username;
            SearchButton_Click(sender, e);
        }

        private void DisplayUserProfile(User user)
        {
            _selectedUser = user;

            // Обновление UI
            FoundUsernameText.Text = user.Username;
            UserPanel.Visibility = Visibility.Visible;
            DefaultMessageText.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private async Task LoadCallHistory(Guid contactId)
        {
            try
            {
                if (App.CurrentUser == null) return;

                var history = await _callsService.GetCallHistory(
                    App.CurrentUser.Id,
                    contactId);

                CallHistoryListView.ItemsSource = history;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка загрузки истории: {ex.Message}");
            }
        }

        private void ShowUserNotFound()
        {
            UserPanel.Visibility = Visibility.Collapsed;
            DefaultMessageText.Visibility = Visibility.Visible;
            ErrorText.Text = "Пользователь не найден";
            ErrorText.Visibility = Visibility.Visible;
        }

        private async void CallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null || App.CurrentUser == null) return;

            CallButton.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed;

            try
            {
                // Создаем запись о звонке
                var call = await _callsService.StartCall(
                    App.CurrentUser.Id,
                    _selectedUser.Id,
                    "127.0.0.1", // Здесь будет реальный IP
                    12345);      // Здесь будет реальный порт

                // Обновляем историю звонков
                var callHistory = await _callsService.GetCallHistory(
                    App.CurrentUser.Id,
                    _selectedUser.Id);

                CallHistoryListView.ItemsSource = callHistory;

                MessageBox.Show("Звонок начат", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка звонка: {ex.Message}");
            }
            finally
            {
                CallButton.IsEnabled = true;
            }
        }

        private string GetLocalIpAddress()
        {
            // Заглушка - в реальном приложении нужно получить реальный IP
            return "127.0.0.1";
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        protected override async void OnClosed(EventArgs e)
        {
            if (App.CurrentUser != null)
            {
                Env.TraversePath().Load();
                string SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
                string SupabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY")!;
                // Инициализация Supabase клиента
                var supabase = new Client(
                    SupabaseUrl,
                    SupabaseKey);
                var authService = new AuthService(supabase);
                await authService.Logout(App.CurrentUser.Id);
            }
            base.OnClosed(e);
        }
    }
}