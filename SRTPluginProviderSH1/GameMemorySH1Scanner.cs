using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SRTPluginProviderSH1
{
    /// <summary>
    /// Reads Silent Hill 1 (SLUS-00707 / NTSC-U) memory from the DuckStation emulator (x64).
    ///
    /// === RAM base detection strategy ===
    ///
    ///  Stage 1 — Signature scan (finds a static pointer in DuckStation's module; fast).
    ///
    ///  Stage 2 — Delta-based brute-force scan (reliable fallback):
    ///    On each Refresh(), reads the value at OFFSET_IGT from every large-enough region.
    ///    The REAL IGT advances at 4096 ticks/second.  Any candidate whose value increased
    ///    by a delta consistent with 7–200 fps gets a confirmation streak.  Once
    ///    CONFIRM_FRAMES_REQUIRED consecutive valid frames are seen, that region becomes
    ///    ramBase.  Static values and random noise are automatically ignored.
    ///
    /// === Key offsets (SLUS-00707, source: Polymega/SilentHillDatabase) ===
    ///   IGT           0x800BCC84  int32, ticks at 4096 units/second
    ///   DamageTaken   0x800BA0BD  byte,  starts 0x40; rises with damage; wraps → HealthStatus--
    ///   HealthStatus  0x800BA0BE  int16, 0x06=Green … 0x01=Red, 0x00=Dead
    ///   SaveCount     0x800BCADA  int16
    /// </summary>
    public class GameMemorySH1Scanner : IDisposable
    {
        // ── Win32 P/Invoke ────────────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ulong VirtualQueryEx(IntPtr hProcess, ulong lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, ulong lpBaseAddress,
            byte[] lpBuffer, int nSize, out int lpNumberOfBytesRead);

        private const uint PROCESS_VM_READ           = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint MEM_COMMIT                = 0x1000;
        private const uint PAGE_NOACCESS             = 0x01;
        private const uint PAGE_GUARD                = 0x100;

        [StructLayout(LayoutKind.Explicit, Size = 48)]
        private struct MEMORY_BASIC_INFORMATION
        {
            [FieldOffset( 0)] public ulong BaseAddress;
            [FieldOffset( 8)] public ulong AllocationBase;
            [FieldOffset(16)] public uint  AllocationProtect;
            [FieldOffset(24)] public ulong RegionSize;
            [FieldOffset(32)] public uint  State;
            [FieldOffset(36)] public uint  Protect;
            [FieldOffset(40)] public uint  Type;
        }

        // ── Signature ─────────────────────────────────────────────────────────
        private static readonly byte[] SIG_PATTERN = { 0x48, 0x89, 0x0D, 0x00, 0x00, 0x00, 0x00, 0xB8 };
        private static readonly byte[] SIG_MASK    = { 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF };

        // ── SH1 NTSC-U memory offsets (SLUS-00707, source: Polymega/SilentHillDatabase) ─
        private const ulong OFFSET_IGT           = 0x000BCC84UL; // 0x800BCC84 — IGT (4-byte, LSWord+MSWord)
        private const ulong OFFSET_HARRY_STATUS  = 0x000BA0BEUL; // 0x800BA0BE — health status (2-byte: 0x06=Green … 0x01=Red)
        // 0x800BA0BD — damage taken byte (deferred: see frame-sync pattern note below)
        // 0x800BCCBC — room ID int32 (deferred: address unreliable across game states)
        private const ulong OFFSET_SAVE_COUNT    = 0x000BCADAUL; // 0x800BCADA — number of saves (2-byte)
        private const ulong MIN_REGION = OFFSET_IGT + 4UL;

        // Delta validation: IGT runs at 4096 ticks/sec.
        //   At  7 fps → 585 ticks/frame
        //   At 30 fps → 136 ticks/frame
        //   At 60 fps →  68 ticks/frame
        //   At 200fps →  20 ticks/frame
        // Accept any delta in [15, 700] — comfortably covers all real refresh rates
        // while rejecting static values (delta=0) and random noise (huge deltas).
        private const int DELTA_MIN = 15;
        private const int DELTA_MAX = 700;

        // ── State ─────────────────────────────────────────────────────────────
        private IntPtr        processHandle  = IntPtr.Zero;
        private uint          processId;
        private ulong         ramBase        = 0;
        private List<ulong>   candidates     = new List<ulong>();
        private int[]         prevValues;    // previous IGT reads per candidate (delta scan)
        private int[]         confirmFrames; // consecutive valid-delta frame count per candidate
        private bool          processFound   = false;
        private Process       cachedProcess;
        private GameMemorySH1 gameMemoryValues;

        public bool HasScanned    { get; private set; }
        public bool ProcessRunning =>
            processHandle != IntPtr.Zero && !IsProcessExited();

        // ── Init ──────────────────────────────────────────────────────────────

        internal GameMemorySH1Scanner()
        {
            gameMemoryValues = new GameMemorySH1();
        }

        internal void Initialize(Process process)
        {
            if (process == null) return;
            ReleaseHandle();

            cachedProcess = process;
            processId     = (uint)process.Id;
            processHandle = OpenProcess(
                PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
                bInheritHandle: false,
                processId);

            processFound = processHandle != IntPtr.Zero;
            if (!processFound) return;

            // Stage 1: signature scan.
            ulong sig = FindRamBaseViaSignatureScan(process);
            if (sig != 0) { ramBase = sig; return; }

            // Stage 2: collect candidates for delta scan.
            candidates    = CollectBruteForceRegions();
            prevValues    = new int[candidates.Count];
            confirmFrames = new int[candidates.Count];
            // Seed prevValues with the first read (delta will show on next Refresh).
            for (int i = 0; i < candidates.Count; i++)
                prevValues[i] = ReadInt32(candidates[i] + OFFSET_IGT);
        }

        // ── Stage 1: Signature scan ───────────────────────────────────────────

        private ulong FindRamBaseViaSignatureScan(Process process)
        {
            ProcessModule mainModule = null;
            try { mainModule = process.MainModule; }
            catch { return 0; }
            if (mainModule == null) return 0;

            ulong modBase = (ulong)(long)mainModule.BaseAddress;
            int   modSize = mainModule.ModuleMemorySize;

            const int CHUNK   = 0x10000;
            const int OVERLAP = 8;
            byte[] buf = new byte[CHUNK + OVERLAP];

            for (int off = 0; off < modSize; off += CHUNK)
            {
                int toRead = Math.Min(CHUNK + OVERLAP, modSize - off);
                if (!ReadProcessMemory(processHandle, modBase + (ulong)off,
                                       buf, toRead, out int read) || read < 8) continue;

                for (int i = 0; i <= read - 8; i++)
                {
                    bool match = true;
                    for (int b = 0; b < 8 && match; b++)
                        if ((buf[i + b] & SIG_MASK[b]) != SIG_PATTERN[b]) match = false;
                    if (!match) continue;

                    long instrEnd = (long)modBase + off + i + 7;
                    int  disp32   = BitConverter.ToInt32(buf, i + 3);
                    long ptrAddr  = instrEnd + disp32;

                    var ptrBuf = new byte[8];
                    if (!ReadProcessMemory(processHandle, (ulong)ptrAddr,
                                          ptrBuf, 8, out int pr) || pr != 8) continue;

                    ulong candidate = BitConverter.ToUInt64(ptrBuf, 0);
                    if (candidate == 0) continue;

                    var testBuf = new byte[4];
                    if (ReadProcessMemory(processHandle, candidate + OFFSET_IGT, testBuf, 4, out _))
                        return candidate;
                }
            }
            return 0;
        }

        // ── Stage 2: Candidate collection ────────────────────────────────────

        private List<ulong> CollectBruteForceRegions()
        {
            var   list  = new List<ulong>();
            ulong addr  = 0;
            uint  mbiSz = (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

            while (VirtualQueryEx(processHandle, addr, out MEMORY_BASIC_INFORMATION mbi, mbiSz) != 0)
            {
                bool committed = (mbi.State & MEM_COMMIT) != 0;
                bool readable  = committed
                              && (mbi.Protect & PAGE_NOACCESS) == 0
                              && (mbi.Protect & PAGE_GUARD)    == 0
                              && mbi.Protect != 0;

                if (readable && mbi.RegionSize >= MIN_REGION)
                    list.Add(mbi.BaseAddress);

                ulong next = mbi.BaseAddress + mbi.RegionSize;
                if (next <= mbi.BaseAddress) break;
                addr = next;
            }
            return list;
        }

        // Number of consecutive frames a candidate must show a valid delta before
        // being promoted.  10 frames at typical 30 fps = ~333 ms of observation.
        private const int CONFIRM_FRAMES_REQUIRED = 10;

        /// <summary>
        /// For each candidate, reads IGT and tracks how many consecutive frames show
        /// a delta in [DELTA_MIN, DELTA_MAX]. Once CONFIRM_FRAMES_REQUIRED consecutive
        /// valid frames are seen, promotes the candidate to ramBase.
        /// Returns the confirmed ramBase, or 0 if not yet found.
        /// </summary>
        private ulong TryDeltaScan()
        {
            if (candidates.Count == 0 || prevValues == null) return 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                int curr  = ReadInt32(candidates[i] + OFFSET_IGT);
                int delta = curr - prevValues[i];
                prevValues[i] = curr;

                if (curr > 0 && delta >= DELTA_MIN && delta <= DELTA_MAX)
                {
                    confirmFrames[i]++;
                    if (confirmFrames[i] >= CONFIRM_FRAMES_REQUIRED)
                        return candidates[i]; // CONFIRMED: 10 consecutive valid frames
                }
                else
                {
                    confirmFrames[i] = 0; // Invalid delta — reset streak
                }
            }
            return 0;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        internal IGameMemorySH1 Refresh()
        {
            if (ramBase == 0)
            {
                // Retry signature scan (cheap, and might succeed after game fully loads).
                if (cachedProcess != null)
                {
                    ulong sig = FindRamBaseViaSignatureScan(cachedProcess);
                    if (sig != 0) ramBase = sig;
                }

                // Delta scan over all brute-force candidates.
                if (ramBase == 0)
                    ramBase = TryDeltaScan();
            }

            if (ramBase != 0)
            {
                gameMemoryValues._igtRaw = ReadInt32(ramBase + OFFSET_IGT);

                // Health status (0xBA0BE): 2-byte tier (0x06=Green … 0x01=Red, 0x00=Dead).
                // Changes infrequently (only on tier overflow) — direct read is stable.
                gameMemoryValues._harryHealthStatus = ReadInt16(ramBase + OFFSET_HARRY_STATUS);

                gameMemoryValues._saveCount = ReadInt16(ramBase + OFFSET_SAVE_COUNT);
            }
            else
            {
                gameMemoryValues._igtRaw            = 0;
                gameMemoryValues._harryHealthStatus = 0;
                gameMemoryValues._saveCount         = 0;
            }

            gameMemoryValues._processFound = processFound;
            gameMemoryValues._ramBaseHex   = ramBase != 0 ? $"0x{ramBase:X16}" : "0x0";

            HasScanned = true;
            return gameMemoryValues;
        }

        // ── Low-level reads ───────────────────────────────────────────────────
        //
        // NOTE — Frame-synced read pattern (for future GTE-volatile addresses):
        // Some PS1 addresses (e.g. 0xBA0BD damage counter) are overwritten each frame
        // by the GTE co-processor. To read them reliably, wait for IGT to advance
        // (= start of a new game-logic frame) and read immediately before GTE runs:
        //
        //   private byte ReadByteFrameSynced(ulong igtAddr, ulong byteAddr)
        //   {
        //       int baseIGT = ReadInt32(igtAddr);
        //       var deadline = DateTime.UtcNow.AddMilliseconds(100);
        //       while (DateTime.UtcNow < deadline)
        //           if (ReadInt32(igtAddr) != baseIGT)
        //               return ReadByte(byteAddr);
        //       return ReadByte(byteAddr); // timeout — best-effort
        //   }

        private byte ReadByte(ulong address)
        {
            var buf = new byte[1];
            ReadProcessMemory(processHandle, address, buf, 1, out int read);
            return read == 1 ? buf[0] : (byte)0;
        }

        private int ReadInt32(ulong address)
        {
            var buf = new byte[4];
            ReadProcessMemory(processHandle, address, buf, 4, out int read);
            return read == 4 ? BitConverter.ToInt32(buf, 0) : 0;
        }

        private short ReadInt16(ulong address)
        {
            var buf = new byte[2];
            ReadProcessMemory(processHandle, address, buf, 2, out int read);
            return read == 2 ? BitConverter.ToInt16(buf, 0) : (short)0;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private bool IsProcessExited()
        {
            try   { return Process.GetProcessById((int)processId).HasExited; }
            catch { return true; }
        }

        private void ReleaseHandle()
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }
            ramBase       = 0;
            candidates    = new List<ulong>();
            prevValues    = null;
            confirmFrames = null;
            processFound  = false;
            cachedProcess = null;
            HasScanned    = false;
        }

        // ── IDisposable ───────────────────────────────────────────────────────
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue) { ReleaseHandle(); disposedValue = true; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~GameMemorySH1Scanner() => Dispose(false);
    }
}
