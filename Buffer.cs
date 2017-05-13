namespace GZipTest2
{
    public sealed class Buffer
    {
        private byte[] _data;

        public Buffer(int initialCapacity)
        {
            _data = new byte[initialCapacity];
        }

        public byte[] Data { get { return _data; } }

        public int Capacity
        {
            get { return _data.Length; }
            set
            {
                if (_data.Length < value)
                    _data = new byte[value];
            }
        }
    }
}