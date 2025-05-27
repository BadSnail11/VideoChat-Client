using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using System;
using NAudio.CoreAudioApi;
using System.Net;

namespace VideoChat_Client.Services
{
    public class MicrophoneService : IDisposable
    {
        private WaveInEvent _waveIn;
        private bool _isRecording;
        private WasapiCapture _audioCapture;
        private NetworkService _networkService;
        private bool _isRunning;

        public event Action<byte[]> AudioDataAvailable;

        public void SetNetworkTarget(NetworkService networkService)
        {
            _networkService = networkService;
        }

        public void StartCapture()
        {
            _audioCapture = new WasapiCapture();
            _audioCapture.DataAvailable += OnAudioData;
            _audioCapture.StartRecording();
            _isRunning = true;
        }

        private void OnAudioData(object sender, WaveInEventArgs e)
        {
            if (!_isRunning) return;

            if (_networkService != null)
            {
                _networkService.EnqueueAudioSamples(e.Buffer);
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            _audioCapture?.Dispose();
        }
    }
}
