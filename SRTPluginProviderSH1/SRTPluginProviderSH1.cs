using ProcessMemory;
using SRTPluginBase;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace SRTPluginProviderSH1
{
    public class SRTPluginProviderSH1 : IPluginProvider
    {
        private Process              process;
        private GameMemorySH1Scanner gameMemoryScanner;
        private IPluginHostDelegates hostDelegates;

        public IPluginInfo Info        => new PluginInfo();
        public bool        GameRunning => true;

        public int Startup(IPluginHostDelegates hostDelegates)
        {
            this.hostDelegates = hostDelegates;
            gameMemoryScanner  = new GameMemorySH1Scanner();

            process = GetProcess();
            if (process != null)
                gameMemoryScanner.Initialize(process);

            return 0;
        }

        public int Shutdown()
        {
            gameMemoryScanner?.Dispose();
            gameMemoryScanner = null;
            process           = null;
            return 0;
        }

        public object PullData()
        {
            try
            {
                // Auto-reconnect whenever the scanner loses the process.
                if (!gameMemoryScanner.ProcessRunning)
                {
                    process = GetProcess();
                    if (process != null)
                        gameMemoryScanner.Initialize(process);
                }

                if (!gameMemoryScanner.ProcessRunning)
                    return null;

                return gameMemoryScanner.Refresh();
            }
            catch (Win32Exception ex)
            {
                if ((Win32Error)ex.NativeErrorCode != Win32Error.ERROR_PARTIAL_COPY)
                    hostDelegates.ExceptionMessage.Invoke(ex);
                return null;
            }
            catch (Exception ex)
            {
                hostDelegates.ExceptionMessage.Invoke(ex);
                return null;
            }
        }

        /// <summary>
        /// Matches any process whose name starts with "duckstation" (case-insensitive).
        /// This covers all known DuckStation build variants:
        ///   duckstation-qt-x64-ReleaseLTCG
        ///   duckstation-qt-x64-Release
        ///   duckstation-nogui-x64-ReleaseLTCG
        ///   duckstation-qt
        ///   etc.
        /// </summary>
        private Process GetProcess() =>
            Process.GetProcesses()
                   .Where(p => p.ProcessName.StartsWith("duckstation",
                                   StringComparison.OrdinalIgnoreCase))
                   .FirstOrDefault();
    }
}
