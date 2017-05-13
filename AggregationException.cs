using System;

namespace GZipTest2
{
    [Serializable]
    public class AggregateException : Exception
    {
        private const string DefaultMessage = "Aggregated exception";

        private readonly Exception[] _exceptions;

        public AggregateException(params Exception[] exceptions)
            : this(DefaultMessage)
        {
        }

        public AggregateException(string message, params Exception[] exceptions)
            : base(message)
        {
            if (exceptions == null) throw new ArgumentNullException("exceptions");
            _exceptions = exceptions;
        }

        public Exception[] InnerExceptions
        {
            get { return _exceptions; }
        }
    }
}