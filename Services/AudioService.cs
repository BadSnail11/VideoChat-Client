using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VideoChat_Client.Services
{
    class AudioService : IDisposable
    {
        private readonly WaveOutEvent _waveOut;
        private readonly BufferedWaveProvider _waveProvider;
        private readonly ConcurrentQueue<byte[]> _audioQueue;
        private bool _isPlaying;
        private readonly object _lock = new object();

        public AudioService()
        {
            // Настройка формата аудио (должен совпадать с форматом входящего потока)
            var waveFormat = new WaveFormat(44100, 16, 2); // 44.1kHz, 16bit, stereo

            _waveOut = new WaveOutEvent();
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2), // Буфер на 2 секунды
                DiscardOnBufferOverflow = true // Отбрасываем данные при переполнении
            };

            _waveOut.Init(_waveProvider);
            _audioQueue = new ConcurrentQueue<byte[]>();
        }

        public void StartPlayback()
        {
            lock (_lock)
            {
                if (_isPlaying) return;

                _waveOut.Play();
                _isPlaying = true;

                // Запускаем фоновую задачу для обработки очереди
                Task.Run(ProcessAudioQueue);
            }
        }

        public void StopPlayback()
        {
            lock (_lock)
            {
                if (!_isPlaying) return;

                _waveOut.Stop();
                _isPlaying = false;
                _waveProvider.ClearBuffer();
            }
        }

        public void EnqueueAudioData(byte[] pcmData)
        {
            if (pcmData != null && pcmData.Length > 0)
            {
                _audioQueue.Enqueue(pcmData);
            }
        }

        private void ProcessAudioQueue()
        {
            while (_isPlaying)
            {
                if (_audioQueue.TryDequeue(out var audioData))
                {
                    try
                    {
                        // Добавляем данные в буфер воспроизведения
                        _waveProvider.AddSamples(audioData, 0, audioData.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Audio playback error: {ex.Message}");
                    }
                }
                else
                {
                    // Если очередь пуста, небольшая пауза
                    Thread.Sleep(10);
                }
            }
        }

        public void Dispose()
        {
            StopPlayback();
            _waveOut?.Dispose();
            _waveProvider?.ClearBuffer();
        }
    }
}
