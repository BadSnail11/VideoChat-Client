using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace VideoChat_Client.Services
{
    public class CameraService : IDisposable
    {
        private VideoCaptureDevice _videoSource;
        private bool _isRunning;

        public event Action<BitmapImage> FrameReady;

        public void StartCamera()
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices.Count == 0)
                throw new Exception("Камеры не найдены");

            _videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
            _videoSource.NewFrame += OnNewFrame;
            _videoSource.Start();
            _isRunning = true;
        }

        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!_isRunning) return;

            using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
            {
                var bitmapImage = ConvertBitmapToBitmapImage(bitmap);
                FrameReady?.Invoke(bitmapImage);
            }
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            // Создаем зеркальное отражение
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

        public void StopCamera()
        {
            if (_videoSource == null) return;

            _isRunning = false;

            // Создаем копию ссылки для безопасной остановки
            var videoSource = _videoSource;
            _videoSource = null;

            // Останавливаем в фоновом потоке
            Task.Run(() =>
            {
                try
                {
                    if (videoSource.IsRunning)
                    {
                        videoSource.SignalToStop();

                        // Ждем остановки, но не более 1 секунды
                        for (int i = 0; i < 10 && videoSource.IsRunning; i++)
                        {
                            Thread.Sleep(100);
                        }

                        if (videoSource.IsRunning)
                        {
                            videoSource.Stop();
                        }
                    }
                }
                catch { /* Игнорируем ошибки при остановке */ }
            });
        }

        public void Dispose()
        {
            StopCamera();
            _videoSource = null;
        }
    }
}
