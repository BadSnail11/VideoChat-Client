using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using System;

namespace VideoChat_Client.Services
{
    public class MicrophoneService : IDisposable
    {
        private WaveInEvent _waveIn;
        private bool _isRecording;

        public event Action<byte[]> AudioDataAvailable;

        public void StartRecording()
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = 0, // Дефолтное устройство
                WaveFormat = new WaveFormat(44100, 16, 1) // 44.1kHz, 16-bit, mono
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            _isRecording = true;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (!_isRecording) return;

            var buffer = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
            AudioDataAvailable?.Invoke(buffer);
        }

        public void StopRecording()
        {
            if (_waveIn != null)
            {
                _isRecording = false;
                _waveIn.StopRecording();
                _waveIn.DataAvailable -= OnDataAvailable;
            }
        }

        public void Dispose()
        {
            StopRecording();
            _waveIn?.Dispose();
        }
    }
}
