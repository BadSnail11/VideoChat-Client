using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoChat_Client.Codecs
{
    public class VideoCodec
    {
        private readonly ImageCodecInfo _jpegEncoder;
        private readonly EncoderParameters _encoderParams;

        public int Quality { get; set; } = 50; // Качество от 0 до 100

        public VideoCodec()
        {
            _jpegEncoder = GetEncoderInfo("image/jpeg");
            _encoderParams = new EncoderParameters(1);
            SetQuality(Quality);
        }

        public byte[] Compress(Bitmap frame)
        {
            using (var ms = new MemoryStream())
            {
                frame.Save(ms, _jpegEncoder, _encoderParams);
                return ms.ToArray();
            }
        }

        public Bitmap Decompress(byte[] compressed)
        {
            using (var ms = new MemoryStream(compressed))
            {
                return new Bitmap(ms);
            }
        }

        private void SetQuality(int quality)
        {
            _encoderParams.Param[0] = new EncoderParameter(
                Encoder.Quality,
                (long)quality);
        }

        private static ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            return codecs.FirstOrDefault(codec => codec.MimeType == mimeType);
        }
    }
}
