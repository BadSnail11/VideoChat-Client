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
using System.ComponentModel;

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

        private CameraService _cameraService;
        private MicrophoneService _microphoneService;
        private NetworkService _networkService;

        private Guid _currentCallId;
        private enum CallState { None, Outgoing, Incoming, Active }
        private CallState _currentCallState = CallState.None;

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

            _cameraService = new CameraService();
            _microphoneService = new MicrophoneService();
            _cameraService.FrameReady += OnCameraFrameReady;
            _microphoneService.AudioDataAvailable += OnAudioDataAvailable;

            int port;
            if (App.Port != 0)
                port = App.Port;
            else
                port = 12345;
            _networkService = new NetworkService(App.CurrentUser.Id, Environment.GetEnvironmentVariable("SERVER_IP"), int.Parse(Environment.GetEnvironmentVariable("SERVER_PORT")), port);
            _ = Task.Run(() => _networkService.ConnectAsync());

            _networkService.OnIncomingCallRequest += OnIncomingCall;
            _networkService.OnCallAccepted += OnCallAccepted;
            _networkService.OnCallRejected += OnCallRejected;
            _networkService.OnCallEnded += OnCallEnded;

            //Loaded += MainWindow_Loaded;
            //Closing += MainWindow_Closing;

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

        //
        // Работа с контактами
        //

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

        //
        // Работа со звонком
        //

        private void OnCameraFrameReady(BitmapImage image)
        {
            Dispatcher.Invoke(() =>
            {
                //LocalVideoDisplay.Source = image;
                LocalVideoPreview.Source = image;
            });
        }

        private void ShowCallUI(CallState state, string statusText = "")
        {
            Dispatcher.Invoke(() =>
            {
                // Скрываем все не относящееся к звонку
                DefaultMessageText.Visibility = Visibility.Collapsed;
                UserPanel.Visibility = Visibility.Collapsed;

                // Показываем интерфейс звонка
                CallGrid.Visibility = Visibility.Visible;
                CallStatusText.Text = statusText;

                // Настраиваем элементы в зависимости от состояния
                switch (state)
                {
                    case CallState.Outgoing:
                        IncomingCallButtons.Visibility = Visibility.Collapsed;
                        EndCallButton.Visibility = Visibility.Visible;
                        CallStatusText.Text = "Вызов...";
                        break;

                    case CallState.Incoming:
                        IncomingCallButtons.Visibility = Visibility.Visible;
                        EndCallButton.Visibility = Visibility.Collapsed;
                        CallStatusText.Text = "Входящий вызов";
                        break;

                    case CallState.Active:
                        IncomingCallButtons.Visibility = Visibility.Collapsed;
                        EndCallButton.Visibility = Visibility.Visible;
                        CallStatusText.Text = "Звонок активен";
                        break;

                    case CallState.None:
                        CallGrid.Visibility = Visibility.Collapsed;
                        DefaultMessageText.Visibility =
                            ContactsListView.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
                        UserPanel.Visibility =
                            ContactsListView.SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;
                        break;
                }

                _currentCallState = state;
            });
        }

        private String GetCallerName(Guid callId)
        {
            return "usr";
        }

        private void OnIncomingCall(Guid callId)
        {
            ShowCallUI(CallState.Incoming, $"Входящий вызов от {GetCallerName(callId)}");
        }
        private void OnCallAccepted(Guid callId)
        {
            ShowCallUI(CallState.Active);
            StartMediaDevices();
        }

        private void OnCallRejected(Guid callId)
        {
            ShowCallUI(CallState.None);
            MessageBox.Show($"Вызов отклонен");
        }

        private void OnCallEnded(Guid callId)
        {
            ShowCallUI(CallState.None);
            StopMediaDevices();
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
                CallButton.Content = "Завершить звонок";

                // Начало звонка
                //StartMediaDevices();

                // Создаем запись о звонке
                //var call = await _callsService.StartCall(
                //    App.CurrentUser.Id,
                //    _selectedUser.Id,
                //    GetLocalIpAddress(),
                //    12345);

                //if (!_networkManager.IsConnected)
                //{
                //    await _networkManager.ConnectAsync();
                //}

                //_networkManager.OnCallResponse += HandleCallResponse;
                //_networkManager.OnIncomingCall += HandleIncomingCall;

                await StartNewCall(_selectedUser.Id);

                // Рассчитываем длительность
                TimeSpan callDuration = DateTime.UtcNow - callStartTime;

                // Обновляем статус и длительность звонка
                //await _callsService.UpdateCallStatus(
                //    callId,
                //    "completed",
                //    GetLocalIpAddress(),
                //    12346);

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
                CallButton.Content = "Позвонить";
            }
        }

        private async Task StartNewCall(Guid tartgetId)
        {
            // Начало звонка
            //StartMediaDevices();


            //// Создаем запись о звонке
            //var call = await _callsService.StartCall(
            //    App.CurrentUser.Id,
            //    _selectedUser.Id,
            //    GetLocalIpAddress(),
            //    _networkManager.UdpPort);

            // Обновляем UI
            CallButton.Content = "Завершить звонок";

            await _networkService.UpdateClientAsync();

            _networkService.InitiateCallAsync(tartgetId);

            //await _networkService.RequestCall(_currentCallId);
            _ = Task.Run(() => _networkService.RequestCall(_currentCallId));

            //_cameraService.SetNetworkTarget(_networkService);
            //_microphoneService.SetNetworkTarget(_networkService);
        }

        private async void AcceptCallButton_Click(object sender, RoutedEventArgs e)
        {
            await _networkService.AcceptCall(_currentCallId);
            ShowCallUI(CallState.Active);
            StartMediaDevices();
        }

        private async void RejectCallButton_Click(object sender, RoutedEventArgs e)
        {
            await _networkService.RejectCall(_currentCallId);
            ShowCallUI(CallState.None);
        }

        private async void EndCallButton_Click(object sender, RoutedEventArgs e)
        {
            await _networkService.EndCall(_currentCallId);
            await EndCall(_currentCallId);
            ShowCallUI(CallState.None);
        }

        private void StartMediaDevices()
        {
            try
            {
                // Запуск камеры
                _cameraService.FrameReady += OnCameraFrameReady;
                _cameraService.StartCamera();

                // Запуск микрофона
                _microphoneService.AudioDataAvailable += OnAudioDataAvailable;
                _microphoneService.StartCapture();

                // Показываем видео
                LocalVideoPreview.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка запуска устройств: {ex.Message}");
            }
        }

        private void OnAudioDataAvailable(byte[] audioData)
        {
            // Здесь будет отправка аудио по сети
            Debug.WriteLine($"Получено аудиоданных: {audioData.Length} байт");
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return duration.ToString(@"hh\:mm\:ss");

            return duration.ToString(@"mm\:ss");
        }

        private async Task EndCall(Guid callId)
        {
            try
            {
                // Останавливаем устройства
                StopMediaDevices();

                // Обновляем статус звонка
                //await _callsService.UpdateCallStatus(
                //    callId,
                //    "completed",
                //    GetLocalIpAddress(),
                //    12346);

                // Обновляем историю
                await LoadCallHistory(_selectedUser.Id);
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка завершения звонка: {ex.Message}");
            }
        }

        private void StopMediaDevices()
        {
            try
            {
                // Останавливаем камеру
                _cameraService.StopCamera();
                _cameraService.FrameReady -= OnCameraFrameReady;
                LocalVideoPreview.Source = null;
                LocalVideoPreview.Visibility = Visibility.Collapsed;

                // Останавливаем микрофон
                _microphoneService.Dispose();
                _microphoneService.AudioDataAvailable -= OnAudioDataAvailable;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка остановки устройств: {ex.Message}");
            }
        }

        private async Task StopAllDevicesAsync(CancellationToken token)
        {
            var stopCameraTask = Task.Run(() => _cameraService?.StopCamera(), token);
            var stopMicTask = Task.Run(() => _microphoneService?.Dispose(), token);

            await Task.WhenAll(stopCameraTask, stopMicTask);
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