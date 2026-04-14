using System;

namespace SRTPluginProviderSH1
{
    public interface IGameMemorySH1
    {
        string GameName    { get; }
        string VersionInfo { get; }

        // -- In-Game Time ----------------------------------------------------------
        int      IGTRaw             { get; }
        TimeSpan IGTTimeSpan        { get; }
        string   IGTFormattedString { get; }

        // -- Player HP (SLUS-00707 database confirmed) ------------------------------
        // HealthStatus: 2-byte at 0xBA0BE — 0x06=Green … 0x01=Red
        // HarryHP: normalized 0-100 from HealthStatus (100=Green, 17=Red, 0=Dead)
        short  HarryHealthStatus { get; }
        string HarryHealthStatusName { get; }
        int    HarryHP           { get; } // 0-100 percentage derived from HealthStatus

        // -- Game State (v1: RoomID deferred — address not reliable yet) -----------
        int SaveCount { get; }

        // -- Diagnostics -----------------------------------------------------------
        bool   ProcessFound { get; }
        string RamBaseHex   { get; }
    }
}
