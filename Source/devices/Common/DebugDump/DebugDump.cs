using Common.Constants;
using System.IO;
using System.Threading.Tasks;

namespace Devices.Common.DebugDump
{
    /// <summary>
    /// Only to be used for debugging RELEASE BUILDS and not intended for
    /// Customer distribution.
    /// </summary>
    public static class DebugDump
    {
#if _DEBUG
        private static readonly object lockObj = new object();
        private static string filePath = string.Empty;

        // expected name format: logs/SN_221010_150120.tgz"
        public static void SetDestination(string fileName)
        {
            string logsDir = Directory.GetCurrentDirectory() + Path.Combine("\\", LogDirectories.LogDirectory);
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            string targetFilePath = fileName;

            if (fileName.StartsWith("logs/"))
            {
                string temp = Path.GetFileNameWithoutExtension(fileName);
                if (!string.IsNullOrWhiteSpace(temp))
                {
                    targetFilePath = $"{temp}_DEBUG_DUMP.txt";
                }
            }

            filePath = Path.Combine(logsDir, targetFilePath);
        }

        public static Task LoggerWriter(string message)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                lock (lockObj)
                {
                    using (StreamWriter streamWriter = new StreamWriter(filePath, append: true))
                    {
                        streamWriter.WriteLine(message);
                        streamWriter.Close();
                    }
                }
            }
            return Task.FromResult(0);
        }
#endif
    }
}
