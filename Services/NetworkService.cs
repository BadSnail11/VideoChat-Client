using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Concurrent;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Drawing;
using System.Windows.Media.Imaging;
using VideoChat_Client.Codecs;

namespace VideoChat_Client.Services
{
    public class NetworkService : IDisposable
    {
        // TCP для сигнализации
        private TcpClient _tcpClient;
        private BinaryReader _tcpReader;
        private BinaryWriter _tcpWriter;

        // UDP для медиаданных
        private UdpClient _udpClient;
        private IPEndPoint _remoteEndPoint;

        // Настройки
        private readonly Guid _userId;
        private readonly string _serverIp;
        private readonly int _serverPort;
        private readonly int _udpPort;

        // Состояние
        public bool IsConnected => _tcpClient?.Connected == true;
        public event Action<Guid, IPEndPoint> OnIncomingCall;
        public event Action<bool> OnCallResponse;

        // Добавляем буферы и кодеки
        private readonly VideoCodec _videoCodec = new VideoCodec();
        private readonly AudioCodec _audioCodec = new AudioCodec();
        private readonly ConcurrentQueue<byte[]> _videoQueue = new();
        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private CancellationTokenSource _streamingCts;


        public NetworkService(Guid userId, string serverIp, int serverPort = 5000, int udpPort = 12345)
        {
            _userId = userId;
            _serverIp = serverIp;
            _serverPort = serverPort;
            _udpPort = udpPort;
        }

        /// <summary>
        /// Подключение к сигнальному серверу
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                // Подключаемся к TCP серверу
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_serverIp, _serverPort);

                _tcpReader = new BinaryReader(_tcpClient.GetStream());
                _tcpWriter = new BinaryWriter(_tcpClient.GetStream());

                // Регистрируем клиента
                await UpdateClientAsync();

                // Запускаем фоновую задачу для прослушки сервера
                HandleIncomingCalls();
            }
            catch (Exception ex)
            {
                Dispose();
                throw new Exception("Ошибка подключения к серверу", ex);
            }
        }

        private async Task UpdateClientAsync()
        {
            var publicIp = GetPublicIP();
            var localIp = GetLocalIP();

            // Отправляем регистрационные данные
            _tcpWriter.Write(_userId.ToByteArray());
            _tcpWriter.Write(IPAddress.Parse(publicIp).GetAddressBytes());
            _tcpWriter.Write(_udpPort); // Порт для входящих UDP соединений
            _tcpWriter.Write(IPAddress.Parse(localIp).GetAddressBytes());
            _tcpWriter.Write(_udpPort); // Локальный UDP порт
            _tcpWriter.Flush();
        }

        private string GetPublicIP()
        {
            try
            {
                using (var client = new WebClient())
                {
                    return client.DownloadString("https://api.ipify.org").Trim();
                }
            }
            catch
            {
                return "0.0.0.0";
            }
        }

        private string GetLocalIP()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                return GetFallbackLocalIP();
            }
            return "0.0.0.0";
        }

        private string GetFallbackLocalIP()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "0.0.0.0";
        }


        /// <summary>
        /// Инициировать звонок
        /// </summary>
        public async void InitiateCallAsync(Guid targetUserId)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Нет подключения к серверу");

            // Отправляем запрос на звонок
            _tcpWriter.Write((byte)0x01); // Команда CALL_REQUEST
            _tcpWriter.Write(targetUserId.ToByteArray());
            _tcpWriter.Flush();

            // Ждем ответа
            var ipBytes = _tcpReader.ReadBytes(4);
            var port = _tcpReader.ReadInt32();

            _remoteEndPoint = new IPEndPoint(new IPAddress(ipBytes), port);
            //return new IPEndPoint(new IPAddress(ipBytes), port);
            await ListenForUdpData(_remoteEndPoint);
        }

        public async void HandleIncomingCalls()
        {
            while (IsConnected)
            {
                var command = _tcpReader.ReadByte();
                if (command == 0x01) // Входящий звонок
                {
                    var callerId = new Guid(_tcpReader.ReadBytes(16));
                    var ipBytes = _tcpReader.ReadBytes(4);
                    var port = _tcpReader.ReadInt32();
                    _remoteEndPoint = new IPEndPoint(new IPAddress(ipBytes), port);

                    InitializeUdpClient();

                    _ = Task.Run(() => ListenForUdpData(_remoteEndPoint));
                }
            }
        }
        //private async Task ListenToServerAsync()
        //{
        //    try
        //    {
        //        var buffer = new byte[1024];
        //        var stream = _tcpClient.GetStream();

        //        while (IsConnected)
        //        {
        //            // Асинхронно читаем команду
        //            var bytesRead = await stream.ReadAsync(buffer, 0, 1);
        //            if (bytesRead == 0) break; // Соединение закрыто

        //            var command = buffer[0];

        //            if (command == 0x01) // INCOMING_CALL
        //            {
        //                // Читаем остальные данные синхронно (так проще для бинарных данных)
        //                var callerIdBytes = _tcpReader.ReadBytes(16);
        //                var ipBytes = _tcpReader.ReadBytes(4);
        //                var port = _tcpReader.ReadInt32();

        //                var callerId = new Guid(callerIdBytes);
        //                var endPoint = new IPEndPoint(new IPAddress(ipBytes), port);

        //                OnIncomingCall?.Invoke(callerId, endPoint);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Ошибка прослушки сервера: {ex.Message}");
        //        Dispose();
        //    }
        //}

        private void InitializeUdpClient()
        {
            try
            {
                // Пытаемся использовать указанный порт
                _udpClient?.Dispose();
                _udpClient = new UdpClient(_udpPort);
                _udpClient.EnableBroadcast = true;
                Console.WriteLine($"UDP клиент запущен на порту {_udpPort}");
            }
            catch (SocketException)
            {
                // Если порт занят, используем случайный
                _udpClient = new UdpClient(0);
                Console.WriteLine($"UDP клиент запущен на случайном порту {((IPEndPoint)_udpClient.Client.LocalEndPoint).Port}");
            }
        }
        private async Task ListenForUdpData(IPEndPoint remoteEndPoint)
        {
            try
            {
                while (IsConnected && _udpClient != null)
                {
                    var result = await _udpClient.ReceiveAsync();

                    // Фильтруем только пакеты от ожидаемого отправителя
                    if (result.RemoteEndPoint.Address.Equals(remoteEndPoint.Address) &&
                        result.RemoteEndPoint.Port == remoteEndPoint.Port)
                    {
                        ProcessIncomingData(result.Buffer);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Клиент был закрыт
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка приема UDP данных: {ex.Message}");
            }
        }

        private void ProcessIncomingData(byte[] data)
        {
            // Здесь будет обработка полученных медиаданных
            // Например:
            // OnVideoFrameReceived?.Invoke(data);
            Console.WriteLine($"Получено {data.Length} байт данных");
        }

        public async Task<bool> SendUdpDataAsync(
        IPEndPoint remoteEndPoint,
        byte[] data,
        int maxRetries = 3,
        int retryDelayMs = 100)
        {
            if (_udpClient == null || _udpClient.Client == null)
            {
                Console.WriteLine("UDP клиент не инициализирован");
                return false;
            }

            int attempt = 0;
            Exception lastError = null;

            while (attempt <= maxRetries)
            {
                try
                {
                    // Для последней попытки используем меньший таймаут
                    var timeout = attempt == maxRetries ? 2000 : 5000;
                    using (var cts = new CancellationTokenSource(timeout))
                    {
                        var sendTask = _udpClient.SendAsync(data, data.Length, remoteEndPoint);
                        var completedTask = await Task.WhenAny(
                            sendTask,
                            Task.Delay(-1, cts.Token)
                        );

                        if (completedTask == sendTask)
                        {
                            // Успешная отправка
                            if (attempt > 0)
                                Console.WriteLine($"Успешная отправка после {attempt} попыток");

                            return true;
                        }
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    lastError = ex;
                    Console.WriteLine($"Таймаут отправки (попытка {attempt})");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Console.WriteLine($"Ошибка отправки: {ex.Message}");
                }

                attempt++;
                if (attempt <= maxRetries)
                {
                    await Task.Delay(retryDelayMs);
                }
            }

            Console.WriteLine($"Не удалось отправить данные после {maxRetries} попыток. Последняя ошибка: {lastError?.Message}");
            return false;
        }

        public void Dispose()
        {
            _tcpReader?.Dispose();
            _tcpWriter?.Dispose();
            _tcpClient?.Dispose();
            _udpClient?.Dispose();
        }
    }
}
