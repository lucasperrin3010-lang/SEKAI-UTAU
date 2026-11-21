using System;
using System.IO;

namespace SekaiUtau.Voicebank;

/// <summary>Writes 16-bit mono PCM WAV files from raw samples.</summary>
public static class WavFile {
    public static void Write(string path, short[] samples, int start, int length, int sampleRate) {
        if (length < 0) {
            length = 0;
        }
        int dataBytes = length * 2;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + dataBytes);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16);
        writer.Write((short)1);              // PCM
        writer.Write((short)1);              // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);        // byte rate
        writer.Write((short)2);              // block align
        writer.Write((short)16);             // bits per sample
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(dataBytes);
        var buffer = new byte[Math.Max(0, length) * 2];
        for (int i = 0; i < length; i++) {
            short sample = samples[start + i];
            buffer[i * 2] = (byte)(sample & 0xFF);
            buffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        writer.Write(buffer);
    }
}
