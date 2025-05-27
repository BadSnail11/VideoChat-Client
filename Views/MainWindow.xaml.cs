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
        private ObservableCollection<User> _contacts = new ObservableCollection<User>();

        private CameraService _cameraService;
        private MicrophoneService _microphoneService;
        private NetworkService _networkService;
        private AudioService _audioService;

        private Guid _currentCallId;
        private enum CallState { None, Outgoing, Incoming, Active }
        private CallState _currentCallState = CallState.None;

        public MainWindow()
        {
            InitializeComponent();

            Env.TraversePath().Load();
            string SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")!;
            string SupabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY")!;
            var supabase = new Client(
                SupabaseUrl,
                SupabaseKey);
            _contactsService = new ContactsService(supabase);
            _callsService = new CallsService(supabase);

            ContactsListView.ItemsSource = _contacts;

            _cameraService = new CameraService();
            _microphoneService = new MicrophoneService();
            _audioService = new AudioService();
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

            _networkService.VideoFrameReceived += OnRecievedVideo;
            _networkService.AudioDataReceived += OnRecievedAudio;


            LoadContacts();
            SetupEventHandlers();
        }
        private void SetupEventHandlers()
        {
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
                var foundUsers = await _contactsService.SearchUsers(searchTerm);
                var user = foundUsers.FirstOrDefault();

                if (user == null)
                {
                    ShowUserNotFound();
                    return;
                }
                DisplayUserProfile(user);

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

            AddContactButton.Visibility = ShouldHideAddButton(user.Id)
                ? Visibility.Collapsed
                : Visibility.Visible;

            await LoadCallHistory(user.Id);

            UserPanel.Visibility = Visibility.Visible;
            DefaultMessageText.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private async Task LoadCallHistory(Guid contactId)
        {
            try
            {
                //if (App.CurrentUser == null) return;

                //var history = await _callsService.GetCallHistory(
                //    App.CurrentUser.Id,
                //    contactId);

                //CallHistoryListView.ItemsSource = history
                //    .OrderByDescending(c => c.StartedAt)
                //    .ToList();
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

                    LoadContacts();

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

        private void OnRecievedVideo(BitmapImage image)
        {
            Dispatcher.Invoke(() =>
            {
                RemoteVideoDisplay.Source = image;
            });
        }

        private void OnRecievedAudio(byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                _audioService.EnqueueAudioData(data);
            });
        }

        private void OnCameraFrameReady(BitmapImage image)
        {
            Dispatcher.Invoke(() =>
            {
                LocalVideoPreview.Source = image;
            });
        }

        private void ShowCallUI(CallState state, string statusText = "")
        {
            Dispatcher.Invoke(() =>
            {
                DefaultMessageText.Visibility = Visibility.Collapsed;
                UserPanel.Visibility = Visibility.Collapsed;

                CallGrid.Visibility = Visibility.Visible;
                CallStatusText.Text = statusText;

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
            if (_currentCallState.Equals(CallState.Incoming) || _currentCallState.Equals(CallState.Active))
                return;
            ShowCallUI(CallState.Incoming, $"Входящий вызов от {GetCallerName(callId)}");
        }
        private void OnCallAccepted(Guid callId)
        {
            if (_currentCallState == CallState.Active)
                return;
            ShowCallUI(CallState.Active);
            StartMediaDevices();
            _cameraService.SetNetworkTarget(_networkService);
            _microphoneService.SetNetworkTarget(_networkService);
            _networkService.StartStreaming();
        }

        private void OnCallRejected(Guid callId)
        {
            if (_currentCallState == CallState.Active)
                return;
            ShowCallUI(CallState.None);
            MessageBox.Show($"Вызов отклонен");
        }

        private void OnCallEnded(Guid callId)
        {
            ShowCallUI(CallState.None);
            _networkService.StopStreaming();
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

                // Создаем запись о звонке
                //var call = await _callsService.StartCall(
                //    App.CurrentUser.Id,
                //    _selectedUser.Id,
                //    GetLocalIpAddress(),
                //    12345);

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

                await LoadCallHistory(_selectedUser.Id);
            }
            catch (Exception ex)
            {
                if (callId != Guid.Empty)
                {
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

            _ = Task.Run(() => _networkService.RequestCall(_currentCallId));
        }

        private async void AcceptCallButton_Click(object sender, RoutedEventArgs e)
        {
            await _networkService.AcceptCall(_currentCallId);
            ShowCallUI(CallState.Active);
            StartMediaDevices();
            _cameraService.SetNetworkTarget(_networkService);
            _microphoneService.SetNetworkTarget(_networkService);
            _networkService.StartStreaming();
        }

        private async void RejectCallButton_Click(object sender, RoutedEventArgs e)
        {
            await _networkService.RejectCall(_currentCallId);
            ShowCallUI(CallState.None);
            StopMediaDevices();
        }

        private async void EndCallButton_Click(object sender, RoutedEventArgs e)
        {
            await _networkService.EndCall(_currentCallId);
            await EndCall(_currentCallId);
            ShowCallUI(CallState.None);
            StopMediaDevices();
        }

        private void StartMediaDevices()
        {
            try
            {
                _cameraService.FrameReady += OnCameraFrameReady;
                _cameraService.StartCamera();
                LocalVideoPreview.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка запуска устройств: {ex.Message}");
            }

            try
            {
                _microphoneService.AudioDataAvailable += OnAudioDataAvailable;
                _microphoneService.StartCapture();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка запуска устройств: {ex.Message}");
            }
            
        }

        private void OnAudioDataAvailable(byte[] audioData)
        {
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
                StopMediaDevices();

                // Обновляем статус звонка
                //await _callsService.UpdateCallStatus(
                //    callId,
                //    "completed",
                //    GetLocalIpAddress(),
                //    12346);

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
                _cameraService.StopCamera();
                _cameraService.FrameReady -= OnCameraFrameReady;
                LocalVideoPreview.Source = null;
                LocalVideoPreview.Visibility = Visibility.Collapsed;

                _microphoneService.Dispose();
                _microphoneService.AudioDataAvailable -= OnAudioDataAvailable;

                //_audioService.StopPlayback();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка остановки устройств: {ex.Message}");
            }
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