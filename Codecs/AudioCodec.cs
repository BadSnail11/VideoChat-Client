using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Codecs;

namespace VideoChat_Client.Codecs
{
    public class AudioCodec
    {
        // Сжатие PCM в G.711 a-law (соотношение 2:1)
        public byte[] Compress(byte[] pcmData)
        {
            var compressed = new byte[pcmData.Length / 2];
            for (int i = 0, j = 0; i < pcmData.Length; i += 2, j++)
            {
                short sample = BitConverter.ToInt16(pcmData, i);
                compressed[j] = ALawEncoder.LinearToALawSample(sample);
            }
            return compressed;
        }

        // Распаковка G.711 в PCM
        public byte[] Decompress(byte[] compressed)
        {
            var pcmData = new byte[compressed.Length * 2];
            for (int i = 0, j = 0; i < compressed.Length; i++, j += 2)
            {
                short sample = ALawDecoder.ALawToLinearSample(compressed[i]);
                Buffer.BlockCopy(BitConverter.GetBytes(sample), 0, pcmData, j, 2);
            }
            return pcmData;
        }
    }
}
