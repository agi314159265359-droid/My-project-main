using System.Collections.Generic;
using UnityEngine.Networking;

public class StreamingDownloadHandler : DownloadHandlerScript
{
    private List<byte> receivedData = new List<byte>();
    private bool isComplete = false;
    private readonly object dataLock = new object();

    public bool IsComplete
    {
        get
        {
            lock (dataLock)
            {
                return isComplete;
            }
        }
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data != null && dataLength > 0)
        {
            lock (dataLock)
            {
                for (int i = 0; i < dataLength; i++)
                {
                    receivedData.Add(data[i]);
                }
            }
        }
        return true;
    }

    protected override void CompleteContent()
    {
        lock (dataLock)
        {
            isComplete = true;
        }
    }

    public byte[] GetAvailableData(int fromIndex)
    {
        lock (dataLock)
        {
            if (fromIndex >= receivedData.Count)
                return new byte[0];

            int availableCount = receivedData.Count - fromIndex;
            byte[] result = new byte[availableCount];

            for (int i = 0; i < availableCount; i++)
            {
                result[i] = receivedData[fromIndex + i];
            }

            return result;
        }
    }

    protected override void ReceiveContentLengthHeader(ulong contentLength)
    {
        // Optional: Pre-allocate buffer if content length is known
        if (contentLength > 0 && contentLength < int.MaxValue)
        {
            lock (dataLock)
            {
                receivedData.Capacity = (int)contentLength;
            }
        }
    }
}
