using System;

namespace DevSAK.Services
{
    public sealed class InvalidPasswordException : Exception
    {
        public InvalidPasswordException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
