using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace SRTPluginProviderSH1
{
    public struct GameMemorySH1 : IGameMemorySH1
    {
        private const string IGT_FORMAT = @"hh\:mm\:ss";

        public string GameName    => "SH1";
        public string VersionInfo => FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;

        // -- In-Game Time ----------------------------------------------------------
        public int IGTRaw { get => _igtRaw; }
        internal int _igtRaw;

        public TimeSpan IGTTimeSpan        => TimeSpan.FromSeconds(_igtRaw / 4096f);
        public string   IGTFormattedString => IGTTimeSpan.ToString(IGT_FORMAT, CultureInfo.InvariantCulture);

        // -- Player HP (SLUS-00707) ------------------------------------------------
        // 0xBA0BE: health status tier (2-byte). 0x06=Green … 0x01=Red, 0x00=Dead.
        // DamageTaken (0xBA0BD) deferred — starts at 0x40, not stable for display.
        public short HarryHealthStatus { get => _harryHealthStatus; }
        internal short _harryHealthStatus;

        public string HarryHealthStatusName => _harryHealthStatus switch
        {
            6 => "Fine",
            5 => "Caution",
            4 => "Caution",
            3 => "Danger",
            2 => "Danger",
            1 => "Danger",
            _ => "Dead"
        };

        // Normalized 0-100 HP: each status tier = ~16.67 points.
        public int HarryHP => (int)Math.Round(_harryHealthStatus / 6.0 * 100.0);

        // -- Game State (v1: RoomID deferred) -------------------------------------
        public int SaveCount { get => _saveCount; }
        internal int _saveCount;

        // -- Diagnostics -----------------------------------------------------------
        public bool   ProcessFound { get => _processFound; }
        internal bool _processFound;

        public string RamBaseHex { get => _ramBaseHex; }
        internal string _ramBaseHex;
    }
}
