# SRTPluginProviderSH1

![Release](https://img.shields.io/github/v/release/ARESaurio/SRTPluginProviderSH1?label=current%20release&style=for-the-badge)
![Date](https://img.shields.io/github/release-date/ARESaurio/SRTPluginProviderSH1?style=for-the-badge)
![Downloads](https://img.shields.io/github/downloads/ARESaurio/SRTPluginProviderSH1/total?color=%23007EC6&style=for-the-badge)

SRT Plugin Provider for **Silent Hill 1** (SLUS-00707 / NTSC-U) running on **DuckStation** (x64).

## Requirements

- [SRT Host](https://github.com/SpeedrunTooling/SRTHost) (64-bit / x64)
- [DuckStation](https://www.duckstation.org/) x64 — tested on **`0.1-11026-g5e7be496a (dev)`**
- Silent Hill 1 **SLUS-00707** (NTSC-U)

## Features (v1.0)

| Field | Description |
|-------|-------------|
| `IGTFormattedString` | In-game time (`hh:mm:ss`) |
| `IGTRaw` | Raw IGT ticks (4096 ticks/second) |
| `HarryHP` | HP as 0–100% (derived from health status tier) |
| `HarryHealthStatus` | Health tier: 6=Green, 5=Green-Yellow, 4=Yellow, 3=Orange, 2=Orange-Red, 1=Red |
| `HarryHealthStatusName` | Status label: Fine / Caution / Danger / Dead |
| `SaveCount` | Number of saves performed |

## Memory Addresses

Source: [Polymega/SilentHillDatabase](https://github.com/Polymega/SilentHillDatabase)

| Address | Size | Description |
|---------|------|-------------|
| `0x800BCC84` | int32 | IGT (ticks at 4096/sec) |
| `0x800BA0BD` | byte | Damage taken counter (deferred — v2) |
| `0x800BA0BE` | int16 | Health status tier (1–6) |
| `0x800BCADA` | int16 | Save count |

## RAM Base Detection

The plugin uses a two-stage strategy to locate DuckStation's PS1 RAM in Windows memory:

1. **Signature scan** — searches DuckStation's main module for a known instruction pattern that points to the PS1 RAM base.
2. **Delta-based brute-force scan** — monitors IGT across all readable memory regions; the region whose value advances at ~4096 ticks/second is confirmed as the PS1 RAM base.

## Building

```bash
dotnet build -c Release
```

Output copies to `E:\SRT\plugins\SRTPluginProviderSH1\` via post-build event.

## Known Limitations (v1.0)

- **HP resolution** is tier-based (6 tiers = Fine/Caution/Danger), not exact HP points. This reflects how Silent Hill 1 internally represents health.
- **RoomID** tracking is deferred (address not stable across all game states).
- **DamageTaken counter** deferred — the raw byte at `0x800BA0BD` is GTE-volatile and requires frame-synced reads for accurate display.

## Roadmap

- [ ] Damage taken counter (with frame-synced reading)
- [ ] Room ID tracking (address needs further research — not in Polymega DB)
- [ ] Inventory tracking (items, weapons, ammo)
- [ ] Map completion tracking
- [ ] Support for **Silent Hill 1 Japan** (SLPM-86192) — separate address research required
- [ ] Support for additional emulators (ePSXe, PCSX-Redux, BizHawk)

## Credits

| Source | Contribution |
|--------|-------------|
| [Polymega/SilentHillDatabase](https://github.com/Polymega/SilentHillDatabase) | Definitive memory address map for SLUS-00707 (HP, inventory, IGT, saves) |
| GameShark code databases (CodeTwink, CheatCC, AlmarsGuides) | Initial HP address confirmation via Infinite Health codes (`300BA0BD 0040`) |
| [DuckStation](https://github.com/stenzek/duckstation) | PS1 emulator used for debugging (CPU debugger, memory viewer, breakpoints) |
| [SRTHost](https://github.com/SpeedrunTooling/SRTHost) by Travis Gutjahr | Plugin host framework this provider is built for |
