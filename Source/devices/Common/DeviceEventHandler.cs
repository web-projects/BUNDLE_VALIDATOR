using Devices.Common.Helpers;
using System.Threading.Tasks;
using static Devices.Common.Constants.LogMessage;

namespace Devices.Common
{
    public delegate void DeviceLogHandler(LogLevel logLevel, string message);
    public delegate object DeviceEventHandler(DeviceEvent deviceEvent, DeviceInformation deviceInformation);
    public delegate Task ComPortEventHandler(PortEventType comPortEvent, string portNumber);
    public delegate void QueueEventHandler();
}
