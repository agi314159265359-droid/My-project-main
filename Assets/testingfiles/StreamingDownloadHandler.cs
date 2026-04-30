using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;

namespace Mikk.Avatar.Expression
{
    /// <summary>
    /// Custom download handler that allows reading streaming data as it arrives.
    /// Used by the TTS streaming system.
    /// </summary>
    public class StreamingDownloadHandler : DownloadHandlerScript
    {
        private List<byte> receivedData = new List<byte>();
        private readonly object dataLock = new object();
        private bool isComplete = false;

        public bool IsComplete => isComplete;

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            lock (dataLock)
            {
                for (int i = 0; i < dataLength; i++)
                {
                    receivedData.Add(data[i]);
                }
            }
            return true;
        }

        protected override void CompleteContent()
        {
            isComplete = true;
        }

        /// <summary>
        /// Get available data starting from the given offset.
        /// Returns only NEW data since the last call with this offset.
        /// </summary>
        public byte[] GetAvailableData(int offset)
        {
            lock (dataLock)
            {
                if (offset >= receivedData.Count)
                    return new byte[0];

                int count = receivedData.Count - offset;
                byte[] result = new byte[count];
                receivedData.CopyTo(offset, result, 0, count);
                return result;
            }
        }

        public int TotalBytesReceived
        {
            get
            {
                lock (dataLock) { return receivedData.Count; }
            }
        }
    }
}