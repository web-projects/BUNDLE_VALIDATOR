using Common.Execution;
using Common.XO.Requests;
using Config.Helpers;
using Execution;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VerifoneBundleValidator.Config;

namespace DEVICE_CORE
{
    class Program
    {
        #region --- Win32 API ---
        private const int MF_BYCOMMAND = 0x00000000;
        public const int SC_CLOSE = 0xF060;
        public const int SC_MINIMIZE = 0xF020;
        public const int SC_MAXIMIZE = 0xF030;
        public const int SC_SIZE = 0xF000;
        // window position
        const short SWP_NOMOVE = 0X2;
        const short SWP_NOSIZE = 1;
        const short SWP_NOZORDER = 0X4;
        const int SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        public static extern int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int Y, int cx, int cy, int wFlags);
        #endregion --- Win32 API ---

        static readonly DeviceActivator activator = new DeviceActivator();

        static bool applicationIsExiting = false;

        //static private IConfiguration configuration;
        static private AppConfig configuration;

        static async Task Main(string[] args)
        {
            configuration = SetupEnvironment.SetEnvironment();

            // save current colors
            ConsoleColor foreGroundColor = Console.ForegroundColor;
            ConsoleColor backGroundColor = Console.BackgroundColor;

            // Device discovery
            string pluginPath = Path.Combine(Environment.CurrentDirectory, "DevicePlugins");

            IDeviceApplication application = activator.Start(pluginPath);

            await application.Run(new AppExecConfig
            {
                ExecutionMode = Modes.Execution.Console,
                ForeGroundColor = foreGroundColor,
                BackGroundColor = backGroundColor,
            }).ConfigureAwait(false);

            if (application.TargetDevicesCount() > 0)
            {
                // VIPA VERSION
                //await application.Command(LinkDeviceActionType.ReportVipaVersions).ConfigureAwait(false);
                //await Task.Delay(15000);

                // VERIFONE BUNDLE VERSIONS
                await application.Command(LinkDeviceActionType.ReportBundleVersions).ConfigureAwait(false);

                // Wait just a little
                //await Task.Delay(3000);

                // IDLE SCREEN
                //await application.Command(LinkDeviceActionType.DisplayIdleScreen).ConfigureAwait(false);
            }

            // wait for dummy file to be deleted
            while (FileCoordinator.DoWork(FileCoordinatorOps.DummyExists))
            {
                await Task.Delay(1000);
            }

            applicationIsExiting = true;

            application.Shutdown();

            // delete working directory
            SetupEnvironment.DeleteWorkingDirectory();

            // Save window position
            SetupEnvironment.WaitForExitKeyPress();

            Console.WriteLine("APPLICATION EXITING ...");
            Console.WriteLine("");
        }
    }
}
