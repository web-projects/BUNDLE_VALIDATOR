using Common.Config.Config;
using Common.LoggerManager;
using System;
using System.Diagnostics;
using System.IO;

namespace Config.Helpers
{
    public static class FileCoordinator
    {
        private static string targetDummyFile = Path.Combine(Constants.TargetDirectory, Constants.TargetDummyFile);
        private static object locker = new object();

        public static bool DoWork(FileCoordinatorOps ops)
        {
            lock (locker)
            {
                switch (ops)
                {
                    case FileCoordinatorOps.DummyCreate:
                    {
                        // create dummy file to indicate task completion
                        if (!File.Exists(targetDummyFile))
                        {
                            File.Create(targetDummyFile);
                        }
                        break;
                    }
                    case FileCoordinatorOps.DummyDelete:
                    {
                        if (File.Exists(targetDummyFile))
                        {
                            try
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                File.Delete(targetDummyFile);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"File Write Exception: [{ex}].");
                                Logger.error($"File Write Exception: [{ex}].");
                            }
                        }
                        break;
                    }

                    case FileCoordinatorOps.DummyExists:
                    {
                        return File.Exists(targetDummyFile);
                    }
                }
            }

            return true;
        }
    }

    public enum FileCoordinatorOps
    {
        DummyCreate = 0,
        DummyDelete = 1,
        DummyExists = 2
    }
}
