using Common.Config.Config;
using Common.Execution;
using Common.LoggerManager;
using Common.XO.Requests;
using Config.Helpers;
using Execution;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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
        static private Config configuration;

        static async Task Main(string[] args)
        {
            SetupWindow();

            // setup working environment
            DirectoryInfo di = SetupEnvironment();

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

                // IDLE SCREEN
                await application.Command(LinkDeviceActionType.DisplayIdleScreen).ConfigureAwait(false);
            }

            // wait for dummy file to be deleted
            while (FileCoordinator.DoWork(FileCoordinatorOps.DummyExists))
            {
                await Task.Delay(1000);
            }

            applicationIsExiting = true;

            application.Shutdown();

            // delete working directory
            DeleteWorkingDirectory(di);

            Console.WriteLine("\r\n\r\nPress <ENTER> key to exit...");
#if !DEBUG
            ConsoleKeyInfo keypressed = Console.ReadKey(true);

            while (keypressed.Key != ConsoleKey.Enter)
            {
                keypressed = Console.ReadKey(true);
                Thread.Sleep(100);
            }
#endif

            Console.WriteLine("APPLICATION EXITING ...");
            Console.WriteLine("");
        }

        static private DirectoryInfo SetupEnvironment()
        {
            DirectoryInfo di = null;

            // create working directory
            if (!Directory.Exists(Constants.TargetDirectory))
            {
                di = Directory.CreateDirectory(Constants.TargetDirectory);
            }

            try
            {
                // Get appsettings.json config - AddEnvironmentVariables() requires package: Microsoft.Extensions.Configuration.EnvironmentVariables
                //configuration = (IConfiguration)new ConfigurationBuilder()
                configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build()
                    .Get<Config>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Application Exception: [{ex}].");
            }

            // logger manager
            SetLogging();

            // Screen Colors
            SetScreenColors();

            Console.WriteLine($"\r\n==========================================================================================");
            Console.WriteLine($"{Assembly.GetEntryAssembly().GetName().Name} - Version {Assembly.GetEntryAssembly().GetName().Version}");
            Console.WriteLine($"==========================================================================================\r\n");

            return di;
        }

        static private void SetupWindow()
        {
            Console.BufferHeight = Int16.MaxValue - 1;
            //Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
            Console.CursorVisible = false;

            IntPtr handle = GetConsoleWindow();
            IntPtr sysMenu = GetSystemMenu(handle, false);

            if (handle != IntPtr.Zero)
            {
                //DeleteMenu(sysMenu, SC_MINIMIZE, MF_BYCOMMAND);
                DeleteMenu(sysMenu, SC_MAXIMIZE, MF_BYCOMMAND);
                DeleteMenu(sysMenu, SC_SIZE, MF_BYCOMMAND);
            }
        }

        static Modes.Execution ParseArguments(string[] args)
        {
            Modes.Execution mode = Modes.Execution.Console;

            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "/C":
                        {
                            mode = Modes.Execution.Console;
                            break;
                        }

                    case "/S":
                        {
                            mode = Modes.Execution.StandAlone;
                            break;
                        }
                }
            }

            return mode;
        }

        static private void DeleteWorkingDirectory(DirectoryInfo di)
        {
            if (di == null)
            {
                di = new DirectoryInfo(Constants.TargetDirectory);
            }

            if (di != null)
            {
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }

                di.Delete();
            }
            else if (Directory.Exists(Constants.TargetDirectory))
            {
                di = new DirectoryInfo(Constants.TargetDirectory);
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }

                Directory.Delete(Constants.TargetDirectory);
            }
        }

        static void AppSettingsUpdate()
        {
            try
            {
                var jsonWriteOptions = new JsonSerializerOptions()
                {
                    WriteIndented = true
                };

                jsonWriteOptions.Converters.Add(new JsonStringEnumConverter());

                string newJson = JsonSerializer.Serialize(configuration, jsonWriteOptions);
                Debug.WriteLine($"{newJson}");

                string appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                File.WriteAllText(appSettingsPath, newJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in saving settings: {ex}");
            }
        }

        static void SetLogging()
        {
            try
            {
                string[] logLevels = configuration.LoggerManager.Logging.Levels.Split("|");

                if (logLevels.Length > 0)
                {
                    string fullName = Assembly.GetEntryAssembly().Location;
                    string logname = Path.GetFileNameWithoutExtension(fullName) + ".log";
                    string path = Directory.GetCurrentDirectory();
                    string filepath = path + "\\logs\\" + logname;

                    int levels = 0;
                    foreach (string item in logLevels)
                    {
                        foreach (LOGLEVELS level in LogLevels.LogLevelsDictonary.Where(x => x.Value.Equals(item)).Select(x => x.Key))
                        {
                            levels += (int)level;
                        }
                    }

                    Logger.SetFileLoggerConfiguration(filepath, levels);

                    Logger.info($"{Assembly.GetEntryAssembly().GetName().Name} ({Assembly.GetEntryAssembly().GetName().Version}) - LOGGING INITIALIZED.");
                }
            }
            catch (Exception e)
            {
                Logger.error("main: SetupLogging() - exception={0}", e.Message);
            }
        }

        static void SetScreenColors()
        {
            try
            {
                // Set Foreground color
                Console.ForegroundColor = GetColor(configuration.Application.Colors.ForeGround);

                // Set Background color
                Console.BackgroundColor = GetColor(configuration.Application.Colors.BackGround);

                Console.Clear();
            }
            catch (Exception ex)
            {
                Logger.error("main: SetScreenColors() - exception={0}", ex.Message);
            }
        }

        static ConsoleColor GetColor(string color) => color switch
        {
            "BLACK" => ConsoleColor.Black,
            "DARKBLUE" => ConsoleColor.DarkBlue,
            "DARKGREEEN" => ConsoleColor.DarkGreen,
            "DARKCYAN" => ConsoleColor.DarkCyan,
            "DARKRED" => ConsoleColor.DarkRed,
            "DARKMAGENTA" => ConsoleColor.DarkMagenta,
            "DARKYELLOW" => ConsoleColor.DarkYellow,
            "GRAY" => ConsoleColor.Gray,
            "DARKGRAY" => ConsoleColor.DarkGray,
            "BLUE" => ConsoleColor.Blue,
            "GREEN" => ConsoleColor.Green,
            "CYAN" => ConsoleColor.Cyan,
            "RED" => ConsoleColor.Red,
            "MAGENTA" => ConsoleColor.Magenta,
            "YELLOW" => ConsoleColor.Yellow,
            "WHITE" => ConsoleColor.White,
            _ => throw new Exception($"Invalid color identifier '{color}'.")
        };
    }
}
