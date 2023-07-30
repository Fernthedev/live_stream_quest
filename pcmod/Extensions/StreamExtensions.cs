using System;
using System.IO;

namespace LiveStreamQuest.Extensions;

public static class StreamExtensions
{
    public static ulong ReadUint64(this Stream stream)
    {
        // Read the length (UInt64) of the message
        const int bytesToRead = 8;
        var lengthBuffer = new byte[bytesToRead];
        var totalBytesRead = 0;

        while (totalBytesRead < bytesToRead)
        {
            var bytesRead = stream.Read(lengthBuffer, totalBytesRead, bytesToRead - totalBytesRead);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed prematurely.");
            }

            totalBytesRead += bytesRead;
        }
        
        return BitConverter.ToUInt64(lengthBuffer, 0);
    }
}