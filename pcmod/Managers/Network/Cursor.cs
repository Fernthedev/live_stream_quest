using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiveStreamQuest.Managers.Network;

/// <summary>
/// Simple read cursor over a preallocated byte buffer. Maintains a current position
/// and provides helpers to read from a <see cref="Stream"/> into the buffer.
/// </summary>
public class Cursor
{
    /// <summary>
    /// Initializes a new instance of <see cref="Cursor"/> with the provided buffer.
    /// The buffer is not copied; the instance holds a reference to the array.
    /// </summary>
    /// <param name="data">The backing buffer.</param>
    public Cursor(byte[] data)
    {
        Data = data;
    }

    /// <summary>
    /// The backing buffer used for reads.
    /// </summary>
    public byte[] Data { get; set; }

    /// <summary>
    /// The current write position in <see cref="Data"/>. Valid range: 0..Data.Length.
    /// Position equal to <c>Data.Length</c> indicates the buffer is full and further reads will return 0.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Reads up to <paramref name="amount"/> bytes from <paramref name="stream"/> into <see cref="Data"/> at <see cref="Position"/>.
    /// This method may read fewer bytes than requested if the stream has fewer bytes available.
    /// The <see cref="Position"/> is advanced by the number of bytes actually read.
    /// </summary>
    /// <param name="stream">The source stream to read from.</param>
    /// <param name="amount">Maximum number of bytes to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of bytes read (0 if end of stream or no space available).</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidOperationException">If <see cref="Data"/> is null or <see cref="Position"/> is out of range.</exception>
    public async Task<int> ReadFromStream(Stream stream, int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return 0;
        if (Position > Data.Length) throw new InvalidOperationException($"Position {Position} is out of range for buffer of length {Data.Length}.");

        int remaining = Data.Length - Position;
        if (remaining == 0) return 0;
        if (amount > remaining) amount = remaining;

        int read = await stream.ReadAsync(Data, Position, amount, cancellationToken).ConfigureAwait(false);
        Position += read;
        return read;
    }

    /// <summary>
    /// Ensures exactly <paramref name="amount"/> bytes are read from <paramref name="stream"/> into the buffer.
    /// Throws if the stream ends before the requested number of bytes are read or if there is insufficient space.
    /// </summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="amount">Number of bytes to read.</param>
    /// <returns>A task that completes when the requested bytes are read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="amount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">If there is not enough space or stream ends early.</exception>
    public async Task ReadAllFromStream(Stream stream, int amount, System.Threading.CancellationToken cancellationToken = default)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Position + amount > Data.Length)
        {
            throw new InvalidOperationException($"Not enough space in the buffer to read {amount} bytes. Current position: {Position}, buffer size: {Data.Length}.");
        }

        int read = 0;
        while (read < amount)
        {
            var tempRead = await ReadFromStream(stream, amount - read, cancellationToken).ConfigureAwait(false);
            if (tempRead == 0)
            {
                throw new InvalidOperationException($"Stream ended before all data was read. Read {read} bytes, expected {amount} bytes.");
            }

            read += tempRead;
        }
    }

    /// <summary>
    /// Resets the cursor position to zero.
    /// </summary>
    public void ResetPosition()
    {
        Position = 0;
    }
}
