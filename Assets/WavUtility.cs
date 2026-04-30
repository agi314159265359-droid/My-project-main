using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class WavUtility
{
    public static byte[] ConvertToWav(float[] samples, int sampleRate, int channels)
    {
        using var memStream = new MemoryStream();
        using var writer = new BinaryWriter(memStream);

        int byteRate = sampleRate * channels * 2;
        int dataSize = samples.Length * 2;

        // WAV Header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Subchunk1Size
        writer.Write((short)1); // PCM format
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)(channels * 2)); // BlockAlign
        writer.Write((short)16); // BitsPerSample

        // Data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (float sample in samples)
        {
            short intSample = (short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
            writer.Write(intSample);
        }

        return memStream.ToArray();
    }
}

