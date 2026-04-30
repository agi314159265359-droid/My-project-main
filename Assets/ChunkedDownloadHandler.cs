using UnityEngine;
using UnityEngine.Networking;

public class ChunkedDownloadHandler : DownloadHandlerScript
{
    private readonly System.Action<byte[], int> onChunk;

    public ChunkedDownloadHandler(System.Action<byte[], int> onChunkCallback) : base(new byte[1024])
    {
        this.onChunk = onChunkCallback;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        onChunk?.Invoke(data, dataLength);
        return true;
    }

    protected override void CompleteContent()
    {
        Debug.Log("Streaming complete.");
    }
}

