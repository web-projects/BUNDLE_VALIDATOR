using Common.Config.Config;
using Common.LoggerManager;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Devices.Common.Constants.LogMessage;

namespace VerifoneBundleValidator.Config
{
    internal static class SetupEnvironment
    {
        #region --- Win32 API ---
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
        }

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

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", EntryPoint = "GetWindowPos")]
        public static extern UInt32 GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int Y, int cx, int cy, int wFlags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        #endregion --- Win32 API ---

        #region --- APPLICATION ENVIRONMENT ---

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler applicationExitHandler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static AppConfig configuration;

        private static string sourceDirectory;
        private static string workingDirectory;
        private static string logfilenamePath;
        private static DirectoryInfo directoryInfo;

        public static AppConfig SetEnvironment()
        {
            // default window setup
            SetupWindow();

            ConfigurationLoad();

            // Set initial window position
            RestoreWindowPosition();

            // logger manager
            SetLogging();

            // Screen Colors
            SetScreenColors(true);

            Console.WriteLine($"\r\n==========================================================================================");
            Console.WriteLine($"{Assembly.GetEntryAssembly().GetName().Name} - Version {Assembly.GetEntryAssembly().GetName().Version}");
            Console.WriteLine($"==========================================================================================\r\n");

            // Working Directories
            SetWorkingDirectories();

            return configuration;
        }

        public static string GetSourceDirectory()
            => sourceDirectory;

        public static string GetWorkingDirectory()
            => workingDirectory;

        public static string GetLogFilenamePath()
            => logfilenamePath;

        private static void SetupWindow()
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

        private static void ConfigurationLoad()
        {
            try
            {
                // Get appsettings.json config
                // AddEnvironmentVariables() requires package: Microsoft.Extensions.Configuration.EnvironmentVariables
                configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build()
                    .Get<AppConfig>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Application Exception: [{ex}].");
            }
        }

        private static void SetLogging()
        {
            try
            {
                //string[] logLevels = GetLoggingLevels(0);
                string[] logLevels = configuration.LoggerManager.Logging.Levels.Split("|");

                if (logLevels.Length > 0)
                {
                    string fullName = Assembly.GetEntryAssembly().Location;
                    string logname = Path.GetFileNameWithoutExtension(fullName) + ".log";
                    string path = Directory.GetCurrentDirectory();
                    logfilenamePath = path + "\\logs\\" + logname;

                    int levels = 0;
                    foreach (string item in logLevels)
                    {
                        foreach (LOGLEVELS level in LogLevels.LogLevelsDictonary.Where(x => x.Value.Equals(item)).Select(x => x.Key))
                        {
                            levels += (int)level;
                        }
                    }

                    Logger.SetFileLoggerConfiguration(logfilenamePath, levels);

                    Logger.info($"{Assembly.GetEntryAssembly().GetName().Name} ({Assembly.GetEntryAssembly().GetName().Version}) - LOGGING INITIALIZED.");
                }
            }
            catch (Exception e)
            {
                Logger.error("main: SetupLogging() - exception={0}", e.Message);
            }
        }

        private static void SetScreenColors(bool clearConsole)
        {
            if (configuration.Application.EnableColors)
            {
                try
                {
                    // Set Foreground color
                    //Console.ForegroundColor = GetColor(configuration.GetSection("Application:Colors").GetValue<string>("ForeGround"));
                    Console.ForegroundColor = GetColor(configuration.Application.Colors.ForeGround);

                    // Set Background color
                    //Console.BackgroundColor = GetColor(configuration.GetSection("Application:Colors").GetValue<string>("BackGround"));
                    Console.BackgroundColor = GetColor(configuration.Application.Colors.BackGround);

                    if (clearConsole)
                    {
                        Console.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Logger.error("main: SetScreenColors() - exception={0}", ex.Message);
                }
            }
        }

        private static ConsoleColor GetColor(string color) => color switch
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

        static void SetWorkingDirectories()
        {
            string fullName = Assembly.GetEntryAssembly().Location;
            string path = Directory.GetCurrentDirectory();

            if (!Directory.Exists(Constants.TargetDirectory))
            {
                directoryInfo = Directory.CreateDirectory(Constants.TargetDirectory);
            }
        }

        static public void DeleteWorkingDirectory()
        {
            try
            {
                if (directoryInfo == null)
                {
                    directoryInfo = new DirectoryInfo(Constants.TargetDirectory);
                }

                if (directoryInfo != null)
                {
                    foreach (FileInfo file in directoryInfo.GetFiles())
                    {
                        file.Delete();
                    }

                    directoryInfo.Delete();
                }
                else if (Directory.Exists(Constants.TargetDirectory))
                {
                    directoryInfo = new DirectoryInfo(Constants.TargetDirectory);
                    foreach (FileInfo file in directoryInfo.GetFiles())
                    {
                        file.Delete();
                    }

                    Directory.Delete(Constants.TargetDirectory);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in DeleteWorkingDirectory: {ex.Message}");
            }
        }

        #endregion --- APPLICATION ENVIRONMENT ---

        #region --- WINDOW PLACEMENT ---
        private static void RestoreWindowPosition()
        {
            IntPtr ptr = GetConsoleWindow();

            Rect parentWindowRectangle = new Rect()
            {
                Top = Convert.ToInt16(configuration.Application.WindowPosition.Top),
                Left = Convert.ToInt16(configuration.Application.WindowPosition.Left),
                Right = Convert.ToInt16(configuration.Application.WindowPosition.Width),
                Bottom = Convert.ToInt16(configuration.Application.WindowPosition.Height),
            };

            // int X, int Y, int nWidth, int nHeight
            MoveWindow(ptr,
                       parentWindowRectangle.Left, parentWindowRectangle.Top,
                       parentWindowRectangle.Right, parentWindowRectangle.Bottom,
                       true);
        }

        public static void SaveWindowPosition()
        {
            IntPtr ptr = GetConsoleWindow();
            Rect parentWindowRectangle = new Rect();
            GetWindowRect(ptr, ref parentWindowRectangle);

            configuration.Application.WindowPosition.Top = Convert.ToString(parentWindowRectangle.Top);
            configuration.Application.WindowPosition.Left = Convert.ToString(parentWindowRectangle.Left);
            configuration.Application.WindowPosition.Height = Convert.ToString(parentWindowRectangle.Bottom - parentWindowRectangle.Top);
            configuration.Application.WindowPosition.Width = Convert.ToString(parentWindowRectangle.Right - parentWindowRectangle.Left);

            AppSettingsUpdate(configuration);
        }
        #endregion --- WINDOW PLACEMENT ---

        private static void AppSettingsUpdate(AppConfig configuration)
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

        private static void SetConsoleExitEventHandler()
        {
            //_consoleCtrlHandler += s =>
            //{
            //    SaveWindowPosition();
            //    return false;
            //};

            //SetConsoleCtrlHandler(_consoleCtrlHandler, true);

            applicationExitHandler += new EventHandler(ExitHandler);
            SetConsoleCtrlHandler(applicationExitHandler, true);
        }

        private static bool ExitHandler(CtrlType sig)
        {
            Logger.info($"Shutting down: {sig}");

            SaveWindowPosition();

            // If the function handles the control signal, it should return TRUE.
            // If it returns FALSE, the next handler function in the list of handlers for this process is used (from MSDN).
            return false;
        }

        public static void DeviceLogger(LogLevel logLevel, string message)
        {
            Console.WriteLine($"[{logLevel}]: {message}");

            switch (logLevel)
            {
                case LogLevel.Debug:
                    {
                        Logger.debug(message);
                        break;
                    }

                case LogLevel.Info:
                    {

                        break;
                    }

                case LogLevel.Warn:
                    {
                        Logger.warning(message);
                        break;
                    }

                case LogLevel.Error:
                    {
                        Logger.error(message);
                        break;
                    }
            }
        }

        public static void WaitForExitKeyPress()
        {
//#if !DEBUG
            Console.WriteLine("\r\n\r\nPress <ENTER> key to exit...");

            ConsoleKeyInfo keypressed = Console.ReadKey(true);

            while (keypressed.Key != ConsoleKey.Enter)
            {
                keypressed = Console.ReadKey(true);
                System.Threading.Thread.Sleep(100);
            }

            Logger.info("Shutting down: <ENTER> key pressed");

            // Save Window Position on exit
            SaveWindowPosition();
//#endif
        }
    }
}
