<!-- markdownlint-disable MD033 -->
<div align="center">

<img src="./assets/logo/logo.png" alt="PulseDesk logo" width="280" />

**Your Windows machine, explained.**

[![.NET](https://img.shields.io/badge/.NET_10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI_3-0078D4?style=flat-square&logo=windows&logoColor=white)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Windows App SDK](https://img.shields.io/badge/Windows_App_SDK-2.1-00a2ed?style=flat-square)](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
[![License](https://img.shields.io/badge/License-MIT-yellow?style=flat-square)](LICENSE)

[Overview](#overview) · [Features](#features) · [Getting started](#getting-started) · [What PulseDesk shows](#what-pulsedesk-shows) · [Project structure](#project-structure) · [Troubleshooting](#troubleshooting)

</div>

<p align="center">
  <img src="./assets/screenshots/1.png" alt="PulseDesk screenshot" width="960" />
</p>

## Overview

PulseDesk is a native, local-first Windows desktop app that gives you a compact, live view of your system health — CPU, RAM, GPU, network, temperatures, drives, and the top processes behind the load. It is designed for the moment your PC feels slow and Task Manager doesn't give you the full picture fast enough.

A single window, live metrics, and short summaries that help you understand what the machine is doing right now.

> [!NOTE]
> PulseDesk is intentionally local-first and lightweight. No browser, no cloud service, no background infrastructure required.

> [!IMPORTANT]
> PulseDesk is a Windows-only desktop app targeting WinUI 3 on .NET 10 with Windows performance counters and system APIs that are not available cross-platform.

## Features

- **CPU** — Live usage with user, kernel, and idle breakdown, plus top processes
- **Memory** — Used, free, and total physical memory with top processes by working set
- **GPU** — Real-time GPU engine utilization with top GPU-heavy processes
- **Network** — Download and upload throughput for the active adapter
- **Temperature** — Thermal zone monitoring when Windows exposes sensors
- **Drives** — Fixed-drive capacity, free space, and utilization at a glance
- **Battery** — Charge percentage, AC/battery status, and remaining time (laptops)
- **System tray** — Minimizes to the notification area for always-on monitoring
- **Responsive layout** — Adapts from 6 columns down to 1 on narrower windows
- **Mica backdrop** — Native Windows 11 material for a clean, modern look
- **Why it feels slow** — A built-in lag analyzer that explains the likely cause when your PC feels unresponsive
- **Bottleneck detection** — Rule-based bottleneck analyzer that identifies CPU, GPU, memory, or I/O bottlenecks and surfaces plain-language recommendations (`BottleneckService`).
- **Settings page** — Persistent user preferences for polling interval, startup behavior, and theme via the Settings UI and `SettingsService` (`SettingsPage.xaml`).
- **Improved top-process sampling** — Lower-noise, more accurate top-process metrics including richer GPU/memory/cpu breakdowns (`TopProcessesService`).
- **Tray enhancements** — Compact quick-view, pause/resume monitoring, and quick actions from the system tray (`TrayIconService`).
- **Drive monitoring improvements** — Faster, more reliable per-drive enumeration and capacity updates (`DriveService`).
- **Multi-arch packaging** — Updated MSIX publish profiles and build support for x86, x64 and ARM64.
- **Reliability & smoothing** — Reduced noisy spikes and better fallbacks when counters or sensors are unavailable.

## Getting started

### Prerequisites

- Windows 10 version 1809 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git](https://git-scm.com/downloads)

Visual Studio 2022 with the WinUI and Windows App SDK workloads gives the best editing and packaging experience, but the project builds and runs from the command line too.

### Clone and build

```powershell
git clone https://github.com/kasuken/pulsedesk.git
cd pulsedesk
dotnet restore PulseDesk.slnx
dotnet build PulseDesk.slnx
```

### Run the app

```powershell
dotnet run --project .\PulseDesk\PulseDesk.csproj
```

The app opens as a native Windows desktop window with live-updating metrics.

## What PulseDesk shows

| Card | Source | Details |
|------|--------|---------|
| **CPU** | Performance counters | Total load, user/kernel/idle split, top processes |
| **Memory** | `GlobalMemoryStatusEx` | Load %, used/available/total, top processes by working set |
| **GPU** | GPU Engine counters | 3D engine utilization, top GPU-heavy processes |
| **Temperature** | Thermal Zone counters | Current and peak temps (hardware-dependent) |
| **Network** | Network adapter stats | Download/upload throughput on the active adapter |
| **Drives** | `DriveInfo` | Label, file system, capacity, free space, utilization |
| **Battery** | `GetSystemPowerStatus` | Charge %, AC/battery, remaining time, battery saver |

> [!TIP]
> Some metrics depend on the counters, drivers, and sensors exposed by the current machine. "Unavailable" usually means the underlying Windows data source is missing, not that PulseDesk failed.

## Why it feels slow (lag analysis)

PulseDesk includes a lightweight, rule-based analyzer that interprets the live metrics and surfaces plain-language explanations when it detects sustained pressure. The analysis runs locally (no cloud) and looks for correlated signals such as:

- Sustained high CPU (with user/kernel breakdown) and top CPU-consuming processes
- High memory load combined with low free RAM and the largest processes (risk of paging)
- High GPU utilization and the top GPU processes
- High peak temperatures that may trigger thermal throttling
- Battery saver / on-battery power modes that limit performance
- Low drive free space when memory pressure is present

The UI shows a short status line and up to three top findings in the dashboard under "Why it feels slow." The analyzer is conservative — it requires metrics to be sustained across multiple polling intervals to avoid noisy or transient alerts.

This feature is intended to give quick, actionable insight (e.g., which process or condition is most likely causing stutter) and to point you toward the next step (close an offending app, plug in your laptop, free disk space, etc.).

## Tech stack

- **.NET 10** with nullable reference types
- **WinUI 3** with Windows App SDK 2.1
- **CommunityToolkit.WinUI** controls
- Native Windows performance counters and system APIs
- MSIX-ready packaging with publish profiles for **x86**, **x64**, and **ARM64**

## Project structure

```text
PulseDesk.slnx                Root solution
PulseDesk/
  App.xaml(.cs)               Application entry point
  MainWindow.xaml(.cs)        Main dashboard window
  DriveViewModel.cs           Drive card data model
  ProcessRowViewModel.cs      Top-process row data model
  Services/
    CpuService.cs             CPU sampling and smoothing
    MemoryService.cs           Memory usage via Win32
    GpuService.cs              GPU engine utilization
    GpuTopProcessesService.cs  GPU top processes
    NetworkService.cs          Network throughput
    TemperatureService.cs      Thermal zone readings
    DriveService.cs            Fixed-drive enumeration
    BatteryService.cs          Battery and power status
    TopProcessesService.cs     CPU/memory top processes
    TrayIconService.cs         System tray icon (Win32)
    ByteFormatter.cs           Human-readable byte formatting
assets/
  logo/                       App logo
  screenshots/                App screenshots
```

## Troubleshooting

Common situations:

- **First readings show zero** — CPU and GPU counters need a short warm-up period after launch.
- **GPU unavailable** — GPU Engine performance counters may be disabled or not exposed on some systems.
- **Temperature unavailable** — Many desktops and some laptops do not publish thermal zone data through Windows.
- **Network shows nothing** — PulseDesk tracks the active non-loopback, non-tunnel adapter. VPN and virtual adapters are ignored.

If the app fails to build or run:

```powershell
dotnet restore PulseDesk.slnx
dotnet build PulseDesk.slnx
dotnet run --project .\PulseDesk\PulseDesk.csproj
```