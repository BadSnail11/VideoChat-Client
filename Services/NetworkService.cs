﻿using System;
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
using System.IdentityModel.Tokens.Jwt;

namespace VideoChat_Client.Services
{
    public class NetworkService : IDisposable
    {
        private TcpClient _tcpClient;
        private BinaryReader _tcpReader;
        private BinaryWriter _tcpWriter;

        private UdpClient _udpClient;
        private IPEndPoint _remoteEndPoint;

        private readonly Guid _userId;
        private readonly string _serverIp;
        private readonly int _serverPort;
        private readonly int _udpPort;

        public bool IsConnected => _tcpClient?.Connected == true;
        public event Action<Guid, IPEndPoint> OnIncomingCall;
        public event Action<bool> OnCallResponse;
        public event Action<Guid> OnIncomingCallRequest;
        public event Action<Guid> OnCallAccepted;
        public event Action<Guid> OnCallRejected;
        public event Action<Guid> OnCallEnded;
        public event Action<Guid> OnHeartbeatReceived;

        public enum CallStatuses
        {
            requesting,
            answering,
            oncall,
            waiting
        }
        public CallStatuses callStatus = CallStatuses.waiting;
        private readonly VideoCodec _videoCodec = new VideoCodec();
        private readonly AudioCodec _audioCodec = new AudioCodec();
        private readonly ConcurrentQueue<byte[]> _videoQueue = new();
        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private CancellationTokenSource _streamingCts;

        public event Action<BitmapImage> VideoFrameReceived;
        public event Action<byte[]> AudioDataReceived;

        private bool _sendingRequest = false;
        private bool _sendingAnswer = false;
        private enum PacketType : byte { Video = 0x01, Audio = 0x02, Control = 0x03 }

        public enum ControlPacketType : byte
        {
            CallRequest = 0x10,
            CallAccepted = 0x11,
            CallRejected = 0x12,
            CallEnded = 0x13,
            Heartbeat = 0x14
        }

        public class CallControlPacket
        {
            public ControlPacketType Type { get; set; }
            public Guid CallId { get; set; }
            public DateTime Timestamp { get; set; }
            public string AdditionalInfo { get; set; }
        }

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
                if (App.Ip != "")
                    _tcpClient = new TcpClient(new IPEndPoint(IPAddress.Parse(App.Ip), 0));
                else
                    _tcpClient = new TcpClient();

                await _tcpClient.ConnectAsync(_serverIp, _serverPort);

                _tcpReader = new BinaryReader(_tcpClient.GetStream());
                _tcpWriter = new BinaryWriter(_tcpClient.GetStream());

                await UpdateClientAsync();

                HandleIncomingCalls();
            }
            catch (Exception ex)
            {
                Dispose();
                throw new Exception("Ошибка подключения к серверу", ex);
            }
        }

        public async Task UpdateClientAsync()
        {
            var publicIp = GetPublicIP();
            var localIp = GetLocalIP();

            _tcpWriter.Write(_userId.ToByteArray());
            _tcpWriter.Write(publicIp);
            _tcpWriter.Write((ushort)_udpPort);
            _tcpWriter.Write(localIp);
            _tcpWriter.Write((ushort)_udpPort);
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
            if (App.Ip != "")
                return App.Ip;
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

            _tcpWriter.Write((byte)0x01);
            _tcpWriter.Write(targetUserId.ToByteArray());
            _tcpWriter.Flush();
            callStatus = CallStatuses.requesting;
        }

        public async void HandleIncomingCalls()
        {
            while (IsConnected)
            {
                var command = _tcpReader.ReadByte();
                if (command == 0x01)
                {
                    var callerId = new Guid(_tcpReader.ReadBytes(16));
                    var ipString = _tcpReader.ReadString();
                    var port = _tcpReader.ReadInt16();
                    var _newRemoteEndPoint = new IPEndPoint(IPAddress.Parse(ipString), port);
                    if (_remoteEndPoint != null && _remoteEndPoint.Equals(_newRemoteEndPoint))
                        continue;
                    _remoteEndPoint = _newRemoteEndPoint;
                    InitializeUdpClient();

                    _ = Task.Run(() => ListenForUdpData(_remoteEndPoint));
                }
            }
        }
         
        private void InitializeUdpClient()
        {
            try
            {
                _udpClient?.Dispose();
                if (App.Ip != "")
                    _udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(App.Ip), _udpPort));
                else
                    _udpClient = new UdpClient(_udpPort);
                _udpClient.EnableBroadcast = true;
                _streamingCts = new CancellationTokenSource();
                Console.WriteLine($"UDP клиент запущен на порту {_udpPort}");
            }
            catch (SocketException)
            {
                if (App.Ip != "")
                    _udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(App.Ip), 0));
                else
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

                    if (result.RemoteEndPoint.Address.Equals(remoteEndPoint.Address) &&
                        result.RemoteEndPoint.Port == remoteEndPoint.Port)
                    {
                        ProcessIncomingData(result.Buffer);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка приема UDP данных: {ex.Message}");
            }
        }

        private void ProcessIncomingData(byte[] data)
        {
            _ = Task.Run(() => StartReceiving(_streamingCts.Token));
            Console.WriteLine($"Получено {data.Length} байт данных");
        }

        public void StartStreaming()
        {
            _ = Task.Run(() => SendVideoPackets(_remoteEndPoint, _streamingCts.Token));
            _ = Task.Run(() => SendAudioPackets(_remoteEndPoint, _streamingCts.Token));
        }

        public void StopStreaming()
        {
            _streamingCts?.Cancel();
        }

        public void EnqueueVideoFrame(Bitmap frame)
        {
            try
            {
                var compressed = _videoCodec.Compress(frame);
                _videoQueue.Enqueue(compressed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Video compression error: {ex.Message}");
            }
        }

        public void EnqueueAudioSamples(byte[] pcmData)
        {
            try
            {
                var compressed = _audioCodec.Compress(pcmData);
                _audioQueue.Enqueue(compressed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Audio compression error: {ex.Message}");
            }
        }

        private async Task SendVideoPackets(IPEndPoint endoint, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_videoQueue.TryDequeue(out var videoData))
                {
                    await SendMediaPacket(endoint, PacketType.Video, videoData, ct);
                }
            }
            await Task.Delay(10, ct);
        }

        private async Task SendAudioPackets(IPEndPoint endpoint, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_audioQueue.TryDequeue(out var audioData))
                {
                    await SendMediaPacket(endpoint, PacketType.Audio, audioData, ct);
                }
            }
            await Task.Delay(20, ct);
        }

        private async Task SendControlPacket(IPEndPoint endpoint, ControlPacketType type,
        Guid callId, CancellationToken ct, string additionalInfo = null)
        {
            try
            {
                var packet = new CallControlPacket
                {
                    Type = type,
                    CallId = callId,
                    Timestamp = DateTime.UtcNow,
                    AdditionalInfo = additionalInfo
                };

                var data = SerializeControlPacket(packet);
                await SendMediaPacket(endpoint, PacketType.Control, data, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Control packet send error: {ex.Message}");
            }
        }

        public async Task AcceptCall(Guid callId)
        {
            //_sendingAnswer = true;
            while (callStatus == CallStatuses.answering)
                //for (int i = 0; i < 15; i++)
                await SendControlPacket(_remoteEndPoint, ControlPacketType.CallAccepted, callId, _streamingCts.Token);
        }

        public async Task RejectCall(Guid callId)
        {
            //_sendingAnswer = true;
            while (callStatus == CallStatuses.answering)
                //for (int i = 0; i < 15; i++)
                await SendControlPacket(_remoteEndPoint, ControlPacketType.CallRejected, callId, _streamingCts.Token);
        }

        public async Task EndCall(Guid callId)
        {
            _streamingCts.Cancel();
            for (int i = 0; i < 5; i++)
                await SendControlPacket(_remoteEndPoint, ControlPacketType.CallEnded, callId, _streamingCts.Token);
        }

        public async Task RequestCall(Guid callId)
        {
            //_sendingRequest = true;
            while (callStatus == CallStatuses.requesting)
                await SendControlPacket(_remoteEndPoint, ControlPacketType.CallRequest, callId, _streamingCts.Token);
        }

        private byte[] SerializeControlPacket(CallControlPacket packet)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((byte)packet.Type);
                writer.Write(packet.CallId.ToByteArray());
                writer.Write(packet.Timestamp.ToBinary());
                writer.Write(packet.AdditionalInfo ?? string.Empty);
                return ms.ToArray();
            }
        }

        private CallControlPacket DeserializeControlPacket(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                return new CallControlPacket
                {
                    Type = (ControlPacketType)reader.ReadByte(),
                    CallId = new Guid(reader.ReadBytes(16)),
                    Timestamp = DateTime.FromBinary(reader.ReadInt64()),
                    AdditionalInfo = reader.ReadString()
                };
            }
        }

        private async Task SendMediaPacket(IPEndPoint endpoint, PacketType type, byte[] data, CancellationToken ct)
        {
            try
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write((byte)type);
                    writer.Write(data.Length);
                    writer.Write(data);

                    await _udpClient.SendAsync(ms.ToArray(), (int)ms.Length, endpoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send error ({type}): {ex.Message}");
            }
        }

        public async Task StartReceiving(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync(ct);
                    ProcessIncomingPacket(result.Buffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start receiving error: {ex.Message}");
            }
        }

        private void ProcessIncomingPacket(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var reader = new BinaryReader(ms))
                {
                    var type = (PacketType)reader.ReadByte();
                    var length = reader.ReadInt32();
                    var payload = reader.ReadBytes(length);

                    switch (type)
                    {
                        case PacketType.Video:
                            //_sendingAnswer = false;
                            callStatus = CallStatuses.oncall;
                            var frame = _videoCodec.Decompress(payload);
                            VideoFrameReceived?.Invoke(ConvertBitmapToBitmapImage(frame));
                            break;
                        case PacketType.Audio:
                            //_sendingAnswer = false;
                            callStatus = CallStatuses.oncall;
                            var pcmData = _audioCodec.Decompress(payload);
                            AudioDataReceived?.Invoke(pcmData);
                            break;
                        case PacketType.Control:
                            var controlPacket = DeserializeControlPacket(payload);
                            HandleControlPacket(controlPacket);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Packet processing error: {ex.Message}");
            }
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);

            using (var memory = new System.IO.MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }
        private void HandleControlPacket(CallControlPacket packet)
        {
            switch (packet.Type)
            {
                case ControlPacketType.CallRequest:
                    if (callStatus != CallStatuses.waiting) break;
                    callStatus = CallStatuses.answering;
                    OnIncomingCallRequest?.Invoke(packet.CallId);
                    break;
                case ControlPacketType.CallAccepted:
                    if (callStatus != CallStatuses.requesting) break;
                    //_sendingRequest = false;
                    callStatus = CallStatuses.oncall;
                    OnCallAccepted?.Invoke(packet.CallId);
                    break;

                case ControlPacketType.CallRejected:
                    if (callStatus != CallStatuses.requesting) break;
                    //_sendingRequest = false;
                    callStatus = CallStatuses.waiting;
                    OnCallRejected?.Invoke(packet.CallId);
                    break;

                case ControlPacketType.CallEnded:
                    callStatus = CallStatuses.waiting;
                    OnCallEnded?.Invoke(packet.CallId);
                    break;

                case ControlPacketType.Heartbeat:
                    break;
            }
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
