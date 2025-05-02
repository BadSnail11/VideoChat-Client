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
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace VideoChat_Client.Views
{
    public partial class MainWindow : Window
    {
        private readonly ContactsService _contactsService;
        private readonly CallsService _callsService;
        private User _selectedUser;
        private Client _supabaseClient;
        //private User _selectedContact;
        private ObservableCollection<User> _contacts = new ObservableCollection<User>();

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

            ContactsListView.ItemsSource = _contacts;

            LoadContacts();
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

        private async void LoadContacts()
        {
            if (App.CurrentUser == null) return;

            try
            {
                var contacts = await _contactsService.GetUserContactsWithDetails(App.CurrentUser.Id);

                // Обновляем коллекцию в UI потоке
                Dispatcher.Invoke(() =>
                {
                    _contacts.Clear();
                    foreach (var contact in contacts)
                    {
                        _contacts.Add(contact);
                    }
                    Console.WriteLine($"Обновлено контактов: {_contacts.Count}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки контактов: {ex.Message}");
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = SearchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(searchTerm))
            {
                ShowError("Введите имя пользователя для поиска");
                return;
            }

            try
            {
                // Поиск пользователей
                var foundUsers = await _contactsService.SearchUsers(searchTerm);
                var user = foundUsers.FirstOrDefault();

                if (user == null)
                {
                    ShowUserNotFound();
                    return;
                }

                // Отображение найденного пользователя
                DisplayUserProfile(user);

                // Загрузка истории звонков
                await LoadCallHistory(user.Id);
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка поиска: {ex.Message}");
            }
        }

        private async void ContactsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ContactsListView.SelectedItem is User selectedContact)
            {
                _selectedUser = selectedContact;
                DisplayUserProfile(selectedContact);
            }
        }

        private async void DisplayUserProfile(User user)
        {
            _selectedUser = user;
            FoundUsernameText.Text = user.Username;

            // Проверяем, есть ли пользователь в контактах
            AddContactButton.Visibility = ShouldHideAddButton(user.Id)
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Загружаем историю звонков
            await LoadCallHistory(user.Id);

            // Показываем панель пользователя
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

                CallHistoryListView.ItemsSource = history
                    .OrderByDescending(c => c.StartedAt)
                    .ToList();
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
            if (_selectedUser == null || App.CurrentUser == null)
            {
                ShowError("Пользователь не выбран");
                return;
            }

            DateTime callStartTime = DateTime.UtcNow;
            Guid callId = Guid.Empty;

            try
            {
                CallButton.IsEnabled = false;
                ErrorText.Visibility = Visibility.Collapsed;

                // Начало звонка
                var call = await _callsService.StartCall(
                    App.CurrentUser.Id,
                    _selectedUser.Id,
                    GetLocalIpAddress(),
                    12345);

                callId = call.Id; // Сохраняем ID звонка

                // Здесь будет реальная логика звонка через UDP
                // Имитируем звонок длительностью 2 секунды
                await Task.Delay(2000);

                // Рассчитываем длительность
                TimeSpan callDuration = DateTime.UtcNow - callStartTime;

                // Обновляем статус и длительность звонка
                await _callsService.UpdateCallStatus(
                    callId,
                    "completed",
                    GetLocalIpAddress(),
                    12346);

                await _callsService.UpdateCallDuration(callId, callDuration);

                // Обновление истории
                await LoadCallHistory(_selectedUser.Id);

                MessageBox.Show($"Звонок завершен. Длительность: {FormatDuration(callDuration)}",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (callId != Guid.Empty)
                {
                    // Если звонок был создан, но произошла ошибка
                    TimeSpan callDuration = DateTime.UtcNow - callStartTime;
                    await _callsService.UpdateCallStatus(callId, "failed");
                    await _callsService.UpdateCallDuration(callId, callDuration);
                }

                ShowError($"Ошибка звонка: {ex.Message}");
            }
            finally
            {
                CallButton.IsEnabled = true;
            }
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return duration.ToString(@"hh\:mm\:ss");

            return duration.ToString(@"mm\:ss");
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

        private async void AddContactButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null || App.CurrentUser == null) return;

            try
            {
                AddContactButton.IsEnabled = false;
                AddContactButton.Content = "Добавление...";

                bool success = await _contactsService.AddContact(
                    App.CurrentUser.Id,
                    _selectedUser.Id);

                if (success)
                {
                    //ShowError("Контакт успешно добавлен", isError: false);

                    // Обновляем список контактов
                    LoadContacts();

                    // Скрываем кнопку, если контакт уже добавлен
                    AddContactButton.Visibility = ShouldHideAddButton(_selectedUser.Id) ? Visibility.Visible : Visibility.Hidden;
                }
                else
                {
                    ShowError("Контакт уже существует");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка: {ex.Message}");
            }
            finally
            {
                AddContactButton.IsEnabled = true;
                AddContactButton.Content = "Добавить в контакты";
            }
        }

        private bool ShouldHideAddButton(Guid contactId)
        {
            return _contacts.Any(c => c.Id == contactId);
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