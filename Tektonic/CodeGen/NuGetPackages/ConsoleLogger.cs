using NuGet.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tektonic.CodeGen.NuGetPackages
{
    public class ConsoleLogger : LoggerBase
    {
        public ConsoleLogger()
        {

        }

        protected override bool DisplayMessage(LogLevel messageLevel)
        {
            return base.DisplayMessage(messageLevel);
        }

        public override void Log(ILogMessage message)
        {
            Console.WriteLine($"{message.Time} [{message.Level}] {message.Message}");
        }

        public override async Task LogAsync(ILogMessage message)
        {
            Log(message);
        }
    }
}
