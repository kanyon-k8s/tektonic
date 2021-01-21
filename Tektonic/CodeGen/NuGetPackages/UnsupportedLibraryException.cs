using System;
using System.Runtime.Serialization;

namespace Tektonic.CodeGen.Packages
{
    [Serializable]
    internal class UnsupportedLibraryException : Exception
    {
        public UnsupportedLibraryException()
        {
        }

        public string PackageName { get; }

        public UnsupportedLibraryException(string packageName) : base()
        {
            PackageName = packageName;
        }

        public UnsupportedLibraryException(string packageName, string message) : base(message) 
        { 
            PackageName = packageName; 
        }

        public UnsupportedLibraryException(string packageName, string message, Exception innerException) : base(message, innerException)
        {
            PackageName = packageName;
        }

        protected UnsupportedLibraryException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}