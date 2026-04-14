using SRTPluginBase;
using System;

namespace SRTPluginProviderSH1
{
    internal class PluginInfo : IPluginInfo
    {
        public string Name        => "Game Memory Provider (Silent Hill 1 (1999) — DuckStation)";
        public string Description => "A game memory provider plugin for Silent Hill 1 (NTSC-U) running on the DuckStation emulator.";
        public string Author      => "Ares";
        public Uri    MoreInfoURL => new Uri("https://github.com/SpeedrunTooling/SRTPluginProviderSH1");

        public int VersionMajor    => assemblyVersion.Major;
        public int VersionMinor    => assemblyVersion.Minor;
        public int VersionBuild    => assemblyVersion.Build;
        public int VersionRevision => assemblyVersion.Revision;

        private readonly Version assemblyVersion =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    }
}
