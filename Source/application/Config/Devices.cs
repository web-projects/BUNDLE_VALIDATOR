using System.Collections.Generic;

namespace DEVICE_CORE
{
    public class Devices
    {
        public Verifone Verifone { get; set; }
        public List<string> ComPortBlackList { get; set; } = new List<string>();
    }
}
