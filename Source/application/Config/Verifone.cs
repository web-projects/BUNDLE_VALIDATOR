using System.Collections.Generic;

namespace DEVICE_CORE
{
    public class Verifone
    {
        public int SortOrder { get; set; }
        public string[] SupportedDevices { get; set; }
        public List<string> ADKLoggerBundles { get; set; } = new List<string>();
        public bool EnableADKLogger { get; set; }
        public bool ADKLoggerReset { get; set; }
    }
}
