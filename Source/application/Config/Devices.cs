using System.Collections.Generic;
using DEVICE_CORE;

namespace VerifoneBundleValidator.Config
{
    public class Devices
    {
        public Verifone Verifone { get; set; }
        public List<string> ComPortBlackList { get; set; } = new List<string>();
    }
}
