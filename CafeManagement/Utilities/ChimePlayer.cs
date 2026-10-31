using System;
using System.IO;
using System.Media;

namespace CafeManagement.Utilities
{
    public static class ChimePlayer
    {
        private static byte[]? _cachedWavBytes;

        public static void PlayTinhTinh()
        {
            try
            {
                if (_cachedWavBytes == null)
                {
                    _cachedWavBytes = GenerateTinhTinhWav();
                }

                using var ms = new MemoryStream(_cachedWavBytes);
                using var player = new SoundPlayer(ms);
                player.Play();
            }
            catch
            {
                try
                {
                    SystemSounds.Exclamation.Play();
                }
                catch { }
            }
        }

        private static byte[] GenerateTinhTinhWav()
        {
            int sampleRate = 44100;
            // 2 notes: "Tinh" (D6: 1174.66 Hz) and "Tinh" (G6: 1567.98 Hz)
            // Sequence: Note 1 -> Pause -> Note 2 -> Pause -> Note 2
            double[] freqSeq = { 1174.66, 1567.98, 1760.00 };
            double[] durSeq = { 0.22, 0.22, 0.35 };
            double[] delaySeq = { 0.0, 0.18, 0.36 };

            int totalSamples = (int)(sampleRate * 0.85);
            short[] samples = new short[totalSamples];

            for (int n = 0; n < freqSeq.Length; n++)
            {
                double freq = freqSeq[n];
                double duration = durSeq[n];
                int startSample = (int)(sampleRate * delaySeq[n]);
                int noteSamples = (int)(sampleRate * duration);

                for (int i = 0; i < noteSamples; i++)
                {
                    int idx = startSample + i;
                    if (idx >= totalSamples) break;

                    double t = (double)i / sampleRate;
                    // Exponential decay envelope for bell chime effect
                    double envelope = Math.Exp(-5.0 * t / duration);
                    
                    // Fundamental + 2nd Harmonic for rich bell timbre
                    double val = 0.7 * Math.Sin(2.0 * Math.PI * freq * t) + 
                                 0.3 * Math.Sin(2.0 * Math.PI * (freq * 2.0) * t);

                    short pcm = (short)(val * envelope * 28000); // High volume 28000/32767
                    
                    int mixed = samples[idx] + pcm;
                    samples[idx] = (short)Math.Clamp(mixed, -32767, 32767);
                }
            }

            // Construct 44-byte WAV header + PCM data
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            int subChunk2Size = totalSamples * sizeof(short);
            int chunkSize = 36 + subChunk2Size;

            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(chunkSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16); // Subchunk1Size (PCM)
            writer.Write((short)1); // AudioFormat (PCM)
            writer.Write((short)1); // NumChannels (Mono)
            writer.Write(sampleRate); // SampleRate
            writer.Write(sampleRate * sizeof(short)); // ByteRate
            writer.Write((short)2); // BlockAlign
            writer.Write((short)16); // BitsPerSample
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(subChunk2Size);

            foreach (var s in samples)
            {
                writer.Write(s);
            }

            return stream.ToArray();
        }
    }
}
