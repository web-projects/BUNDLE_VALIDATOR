using Common.Execution;
using System;

namespace Execution
{
    public class AppExecConfig
    {
        public Modes.Execution ExecutionMode { get; set; }
        public ConsoleColor ForeGroundColor { get; set; }
        public ConsoleColor BackGroundColor { get; set; }
        public bool ADKLoggerContact { get; set; }
        public bool ADKLoggerContactless { get; set; }
    }
}
