using System;
using System.Runtime.Serialization;

namespace Tektonic.CodeGen
{
    [Serializable]
    internal class PackageAlreadyInstalledException : Exception
    {
        public PackageAlreadyInstalledException()
        {
        }

        public PackageAlreadyInstalledException(string message) : base(message)
        {
        }

        public PackageAlreadyInstalledException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PackageAlreadyInstalledException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}