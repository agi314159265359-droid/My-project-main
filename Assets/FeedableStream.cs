using System;
using System.IO;

public class FeedableStream : Stream
{
    
    private bool isClosed = false;
 private MemoryStream internalStream = new MemoryStream();
    private object streamLock = new object();

    public void FeedData(byte[] data, int length)
    {
        lock (streamLock)
        {
            long pos = internalStream.Position;
            internalStream.Seek(0, SeekOrigin.End);
            internalStream.Write(data, 0, length);
            internalStream.Seek(pos, SeekOrigin.Begin);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (streamLock)
        {
            return internalStream.Read(buffer, offset, count);
        }
    }

    public void CloseStream() => isClosed = true;

    public override bool CanRead => !isClosed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => internalStream.Length;

    public override long Position
    {
        get => internalStream.Position;
        set => throw new NotSupportedException();
    }


    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

