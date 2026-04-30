using System;
using UnityEngine.Networking;

namespace Mikk.Avatar
{
    /// <summary>
    /// Custom download handler that allows reading streaming data
    /// as it arrives instead of waiting for the full response.
    /// </summary>
    public class StreamingDownloadHandler : DownloadHandlerScript
    {
        private byte[] _buffer = new byte[0];
        private readonly object _lock = new object();
        private bool _isComplete;

        public bool IsComplete
        {
            get { lock (_lock) return _isComplete; }
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0) return true;

            lock (_lock)
            {
                int oldLen = _buffer.Length;
                Array.Resize(ref _buffer, oldLen + dataLength);
                Array.Copy(data, 0, _buffer, oldLen, dataLength);
            }

            return true;
        }

        protected override void CompleteContent()
        {
            lock (_lock)
            {
                _isComplete = true;
            }
        }

        /// <summary>
        /// Get available data starting from the given offset.
        /// Thread-safe.
        /// </summary>
        public byte[] GetAvailableData(int fromOffset)
        {
            lock (_lock)
            {
                if (fromOffset >= _buffer.Length) return Array.Empty<byte>();

                int available = _buffer.Length - fromOffset;
                byte[] result = new byte[available];
                Array.Copy(_buffer, fromOffset, result, 0, available);
                return result;
            }
        }

        public int TotalBytesReceived
        {
            get { lock (_lock) return _buffer.Length; }
        }
    }
}