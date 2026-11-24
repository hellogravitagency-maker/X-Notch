// Copyright (c) 2026 Gravit. All rights reserved.
// X-Notch: Autonomous Desktop Intelligence & Interaction Platform.

using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using XNotch.Models;

namespace XNotch.Services
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NotchStateData
    {
        public const uint ExpectedMagic = 0x584E4F54; // 'XNOT'
        public const uint CurrentVersion = 1;

        public uint Magic;
        public uint Version;
        public long TimestampTicks;
        public int Dock;                // 0 = Top, 1 = Left, 2 = Right
        public double Width;            // DIPs width
        public double Height;           // DIPs height
        public int State;               // 0 = Closed, 1 = Peeking, 2 = Open, 3 = Hud
        public byte IsExpanded;
        public byte IsVisible;
        public byte HasActiveMedia;
        public byte HasNotification;
        public byte HasClock;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
    }

    /// <summary>
    /// Broadcasts real-time island dimensions, dock orientation, and states to X-Menu &amp; X-Dock
    /// via blazing-fast zero-allocation local shared memory.
    /// Completely isolated and safe: failures in IPC will never crash or interrupt X-Notch.
    /// </summary>
    public sealed class XDesktopBroadcastService : IDisposable
    {
        private const string MmfName = "Local\\XDesktop_Notch_State";
        private const int StructSize = 48;

        private static XDesktopBroadcastService? _instance;
        public static XDesktopBroadcastService Instance => _instance ??= new XDesktopBroadcastService();

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private readonly object _lock = new();

        public XDesktopBroadcastService()
        {
            try
            {
                _mmf = MemoryMappedFile.CreateOrOpen(MmfName, StructSize, MemoryMappedFileAccess.ReadWrite);
                _accessor = _mmf.CreateViewAccessor(0, StructSize, MemoryMappedFileAccess.Write);
            }
            catch
            {
                // Fallback gracefully if shared memory cannot be allocated
                _accessor = null;
                _mmf = null;
            }
        }

        public void Publish(
            double width,
            double height,
            NotchDock dock,
            NotchState state,
            bool isExpanded,
            bool isVisible,
            bool hasActiveMedia = false,
            bool hasNotification = false,
            bool hasClock = true)
        {
            if (_accessor == null) return;

            var data = new NotchStateData
            {
                Magic = NotchStateData.ExpectedMagic,
                Version = NotchStateData.CurrentVersion,
                TimestampTicks = DateTime.UtcNow.Ticks,
                Dock = dock switch
                {
                    NotchDock.Left => 1,
                    NotchDock.Right => 2,
                    _ => 0
                },
                Width = width,
                Height = height,
                State = (int)state,
                IsExpanded = (byte)(isExpanded ? 1 : 0),
                IsVisible = (byte)(isVisible ? 1 : 0),
                HasActiveMedia = (byte)(hasActiveMedia ? 1 : 0),
                HasNotification = (byte)(hasNotification ? 1 : 0),
                HasClock = (byte)(hasClock ? 1 : 0),
                Reserved1 = 0,
                Reserved2 = 0,
                Reserved3 = 0
            };

            lock (_lock)
            {
                try
                {
                    _accessor.Write(0, ref data);
                }
                catch
                {
                    // Swallowed to guarantee zero-impact on host rendering
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _accessor?.Dispose();
                _accessor = null;
                _mmf?.Dispose();
                _mmf = null;
            }
        }
    }
}
