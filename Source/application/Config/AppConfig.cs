using DEVICE_CORE;
using System;

namespace VerifoneBundleValidator.Config
{
    [Serializable]
    internal class AppConfig
    {
        public Application Application { get; set; }
        public Devices Devices { get; set; }
        public LoggerManager LoggerManager { get; set; }
    }
}
