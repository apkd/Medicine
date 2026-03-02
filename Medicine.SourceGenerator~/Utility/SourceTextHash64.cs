using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.Text;

public static class SourceTextHash64
{
    static readonly Vector<ulong> mix = new(0x9E3779B185EBCA87UL);

    [ThreadStatic] static char[]? charsBuffer;

    static char[] RentBuffer(int minimumLength = 8192)
    {
        var buffer = charsBuffer;
        if (buffer is not null && buffer.Length >= minimumLength)
            return buffer;

        return charsBuffer = new char[minimumLength];
    }

    public static ulong CalculateChecksum64(this SourceText text)
    {
        int length = text.Length;
        if (length is 0)
            return 0;

        var buffer = RentBuffer(262144);

        ulong a = 0x243F6A8885A308D3UL ^ (ulong)length;
        ulong b = 0x13198A2E03707344UL ^ ((ulong)length << 01);
        ulong c = 0xA4093822299F31D0UL ^ ((ulong)length << 32);
        ulong d = 0x082EFA98EC4E6C89UL ^ ((ulong)length << 07);

        int width = Vector<ulong>.Count;
        var vaAcc = new Vector<ulong>(a);
        var vbAcc = new Vector<ulong>(b);

        int offset = 0;

        while (offset < length)
        {
            int chunkLength = length - offset;
            if (chunkLength > buffer.Length)
                chunkLength = buffer.Length;

            text.CopyTo(offset, buffer, 0, chunkLength);

            int alignedChars = chunkLength & ~3;
            var words = MemoryMarshal.Cast<char, ulong>(buffer.AsSpan(0, alignedChars));
            int vectorLength = words.Length / width;
            var vectors = MemoryMarshal.Cast<ulong, Vector<ulong>>(words[..(vectorLength * width)]);

            for (int vi = 0; vi < vectors.Length; vi++)
            {
                var v = vectors[vi];
                vaAcc += v;
                vbAcc += v + mix;
            }

            int i = vectorLength * width;
            for (; i < words.Length; i++)
            {
                ulong x = words[i];
                a += x;
                b += x;
            }

            for (int charIndex = alignedChars; charIndex < chunkLength; charIndex++)
                c += buffer[charIndex];

            a ^= RotateLeft((ulong)chunkLength, 13);
            b += (ulong)chunkLength * 0x9E3779B185EBCA87UL;
            d ^= (ulong)chunkLength;

            offset += chunkLength;
        }

        for (int lane = 0; lane < width; lane++)
        {
            a += vaAcc[lane];
            b += vbAcc[lane];
        }

        c ^= RotateLeft(a, 19);
        d += RotateLeft(b, 29);

        return Finalize64(a ^ RotateLeft(b, 17) ^ RotateLeft(c, 31) ^ RotateLeft(d, 47));
    }

    static ulong RotateLeft(ulong value, int offset)
        => value << offset | value >> (64 - offset);

    static ulong Finalize64(ulong hash)
    {
        unchecked
        {
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            hash ^= hash >> 33;
            return hash;
        }
    }
}
