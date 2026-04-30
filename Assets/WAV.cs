using UnityEngine;

using System;
using UnityEngine;

public class WAV
{
    // Convert two bytes to one float in the range -1 to 1
    static float bytesToFloat(byte firstByte, byte secondByte)
    {
        // convert two bytes to one short (little endian)
        short s = (short)((secondByte << 8) | firstByte);
        // convert to range from -1 to (just below) 1
        return s / 32768.0F;
    }

    public float[] LeftChannel { get; private set; }
    public int ChannelCount { get; private set; }
    public int SampleCount { get; private set; }
    public int Frequency { get; private set; }

    public WAV(byte[] wav)
    {
        // Extract format info
        ChannelCount = wav[22]; // Mono = 1, Stereo = 2
        Frequency = BitConverter.ToInt32(wav, 24);
        int pos = 12;

        // Find "data" chunk
        while (!(wav[pos] == 'd' && wav[pos + 1] == 'a' &&
                 wav[pos + 2] == 't' && wav[pos + 3] == 'a'))
        {
            pos += 4;
            int chunkSize = BitConverter.ToInt32(wav, pos);
            pos += 4 + chunkSize;
        }
        pos += 8;

        SampleCount = (wav.Length - pos) / 2; // 2 bytes per sample (16-bit)
        LeftChannel = new float[SampleCount];

        // Read the data into the float array
        int i = 0;
        while (pos < wav.Length)
        {
            LeftChannel[i] = bytesToFloat(wav[pos], wav[pos + 1]);
            pos += 2;
            i++;
        }
    }
}

