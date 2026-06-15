using System;

namespace AriadneTS.Runtime
{
    public sealed class ScriptRuntimeException : Exception
    {
        public string Status { get; }

        internal ScriptRuntimeException(string status, string message)
            : base(message)
        {
            Status = status;
        }
    }
}

