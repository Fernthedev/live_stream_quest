using System;
using System.IO;

namespace LiveStreamQuest.Extensions;

public static class StreamExtensions
{
    private const int Uint64BytesToRead = 8;

    public static ulong ReadUint64(this Stream stream, byte[]? lengthBuffer)
    {
        lengthBuffer ??= new byte[Uint64BytesToRead];
        // Read the length (UInt64) of the message
        var totalBytesRead = 0;

        while (totalBytesRead < Uint64BytesToRead)
        {
            var bytesRead = stream.Read(lengthBuffer, totalBytesRead, Uint64BytesToRead - totalBytesRead);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed prematurely.");
            }

            totalBytesRead += bytesRead;
        }
        
        return BitConverter.ToUInt64(lengthBuffer, 0);
    }
}