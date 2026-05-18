using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DashboardForm());
        }
    }

    public sealed class DashboardForm : Form
    {
        private readonly ComboBox monitorSelector = new ComboBox();
        private readonly Button overlayButton = new Button();
        private readonly CheckBox alwaysOnTopBox = new CheckBox();
        private readonly TableLayoutPanel table = new DoubleBufferedTable();
        private readonly Panel configPanel = new Panel();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly Dictionary<string, ValueLabelPair> metrics = new Dictionary<string, ValueLabelPair>();
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        
        private readonly PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private readonly GpuTracker gpuTracker = new GpuTracker();
        
        private Rectangle normalBounds;
        private bool isOverlayMode;
        private Screen? selectedScreen;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public DashboardForm()
        {
            Text = "PC Component Monitoring";
            MinimumSize = new Size(320, 240);
            Size = new Size(450, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(18, 18, 20);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            MouseDown += Draggable_MouseDown;

            try { cpuCounter.NextValue(); } catch { }

            BuildUi();
            SetupTray();
            LoadScreens();

            refreshTimer.Interval = 1000;
            refreshTimer.Tick += (_, __) => UpdateMetrics();
            refreshTimer.Start();

            Shown += (_, __) =>
            {
                selectedScreen = Screen.FromControl(this);
                UpdateMetrics();
            };

            FormClosing += (_, __) =>
            {
                cpuCounter.Dispose();
                gpuTracker.Dispose();
                trayIcon.Dispose();
            };
        }

        private void SetupTray()
        {
            trayIcon.Text = "PC Component Monitoring";
            trayIcon.Visible = true;
            
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.LightSkyBlue);
                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }
            trayIcon.Icon = Icon.FromHandle(bmp.GetHicon());

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(new ToolStripMenuItem("Exit Overlay Mode", null, (_, __) => DisableOverlay()));
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(new ToolStripMenuItem("Exit Application", null, (_, __) => Application.Exit()));
            
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.DoubleClick += (_, __) => DisableOverlay();
        }

        private void BuildUi()
        {
            configPanel.Dock = DockStyle.Top;
            configPanel.Height = 72;
            configPanel.Padding = new Padding(12);
            configPanel.BackColor = Color.FromArgb(28, 28, 32);

            var monitorLabel = new Label { AutoSize = true, Text = "Monitor:", Location = new Point(12, 14) };
            monitorSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            monitorSelector.Location = new Point(76, 10);
            monitorSelector.Width = 200;
            monitorSelector.SelectedIndexChanged += (_, __) => { if (monitorSelector.SelectedItem is Screen s) selectedScreen = s; };

            overlayButton.Text = "Enable Overlay";
            overlayButton.Width = 120;
            overlayButton.Location = new Point(290, 8);
            overlayButton.Click += (_, __) => EnableOverlay();

            alwaysOnTopBox.Text = "Always on top";
            alwaysOnTopBox.AutoSize = true;
            alwaysOnTopBox.Location = new Point(76, 40);
            alwaysOnTopBox.Checked = true;
            alwaysOnTopBox.CheckedChanged += (_, __) => TopMost = alwaysOnTopBox.Checked;
            TopMost = true;

            configPanel.Controls.AddRange(new Control[] { monitorLabel, monitorSelector, overlayButton, alwaysOnTopBox });

            table.Dock = DockStyle.Fill;
            table.ColumnCount = 2;
            table.RowCount = 0;
            table.Padding = new Padding(10);
            table.BackColor = Color.FromArgb(18, 18, 20);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            table.AutoScroll = true;
            table.MouseDown += Draggable_MouseDown;

            AddMetric("CPU usage", out var cpu);
            AddMetric("RAM usage", out var ram);
            metrics["CPU usage"] = cpu;
            metrics["RAM usage"] = ram;

            // GPU metrics will be inserted here dynamically

            AddMetric("NPU usage", out var npu);
            AddMetric("CPU temp", out var cpuTemp);
            AddMetric("GPU temp", out var gpuTemp);
            AddMetric("System temp", out var sysTemp);
            AddMetric("Uptime", out var uptime);
            
            metrics["NPU usage"] = npu;
            metrics["CPU temp"] = cpuTemp;
            metrics["GPU temp"] = gpuTemp;
            metrics["System temp"] = sysTemp;
            metrics["Uptime"] = uptime;

            Controls.Add(table);
            Controls.Add(configPanel);
        }

        private void AddMetric(string name, out ValueLabelPair pair, int? atIndex = null)
        {
            table.SuspendLayout();
            int row = atIndex ?? table.RowCount;
            
            table.RowStyles.Insert(row, new RowStyle(SizeType.Absolute, isOverlayMode ? 22f : 30f));

            var nameLabel = new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gainsboro, AutoSize = false };
            var valueLabel = new Label { Text = "...", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(Font.FontFamily, isOverlayMode ? 9f : 10f, FontStyle.Bold), ForeColor = Color.LightSkyBlue, AutoSize = false };

            nameLabel.MouseDown += Draggable_MouseDown;
            valueLabel.MouseDown += Draggable_MouseDown;

            if (atIndex.HasValue)
            {
                // Shift controls down
                for (int r = table.RowCount - 1; r >= row; r--)
                {
                    var c1 = table.GetControlFromPosition(0, r);
                    var c2 = table.GetControlFromPosition(1, r);
                    if (c1 != null) table.SetRow(c1, r + 1);
                    if (c2 != null) table.SetRow(c2, r + 1);
                }
            }
            
            table.RowCount++;
            table.Controls.Add(nameLabel, 0, row);
            table.Controls.Add(valueLabel, 1, row);
            pair = new ValueLabelPair(nameLabel, valueLabel);
            table.ResumeLayout();
        }

        private void LoadScreens()
        {
            monitorSelector.Items.Clear();
            foreach (var screen in Screen.AllScreens) monitorSelector.Items.Add(screen);
            monitorSelector.DisplayMember = nameof(Screen.DeviceName);
            if (monitorSelector.Items.Count > 0) monitorSelector.SelectedIndex = 0;
        }

        private void EnableOverlay()
        {
            if (isOverlayMode) return;
            isOverlayMode = true;
            normalBounds = Bounds;

            table.SuspendLayout();
            configPanel.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            Opacity = 0.85;
            
            table.Padding = new Padding(5);
            foreach (RowStyle style in table.RowStyles) style.Height = 22f;
            foreach (var p in metrics.Values) p.Value.Font = new Font(p.Value.Font.FontFamily, 9f, FontStyle.Bold);

            var screen = selectedScreen ?? Screen.FromControl(this);
            Width = 240;
            Height = (table.RowCount * 22) + 15;
            Location = new Point(screen.Bounds.Right - Width - 20, screen.Bounds.Top + 20);

            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            table.ResumeLayout();
        }

        private void Draggable_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Control.ModifierKeys.HasFlag(Keys.Alt))
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void DisableOverlay()
        {
            if (!isOverlayMode) return;
            isOverlayMode = false;

            table.SuspendLayout();
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);

            configPanel.Visible = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            Opacity = 1.0;
            table.Padding = new Padding(10);
            foreach (RowStyle style in table.RowStyles) style.Height = 30f;
            foreach (var p in metrics.Values) p.Value.Font = new Font(p.Value.Font.FontFamily, 10f, FontStyle.Bold);
            
            if (normalBounds != Rectangle.Empty) Bounds = normalBounds;
            table.ResumeLayout();
        }

        private void UpdateMetrics()
        {
            bool addedGpu = false;
            var gpuUsages = gpuTracker.GetUsages();
            
            foreach (var gpu in gpuUsages)
            {
                string key = $"GPU: {gpu.Name}";
                if (!metrics.ContainsKey(key))
                {
                    AddMetric(key, out var pair, 2); // Insert after RAM
                    metrics[key] = pair;
                    addedGpu = true;
                }
                metrics[key].Value.Text = FormatPercent(gpu.Usage);
            }

            if (addedGpu && isOverlayMode)
            {
                Height = (table.RowCount * (isOverlayMode ? 22 : 30)) + (isOverlayMode ? 15 : 100);
            }

            metrics["CPU usage"].Value.Text = FormatPercent(GetCpuUsage());
            metrics["RAM usage"].Value.Text = FormatMemory(GetRamUsage());
            metrics["NPU usage"].Value.Text = FormatPercent(GetNpuUsage());
            metrics["CPU temp"].Value.Text = FormatTemperature(GetCpuTemperature());
            metrics["GPU temp"].Value.Text = FormatTemperature(GetGpuTemperature());
            metrics["System temp"].Value.Text = FormatTemperature(GetSystemTemperature());
            metrics["Uptime"].Value.Text = FormatUptime(Environment.TickCount64);
        }

        private static string FormatPercent(double? value) => value.HasValue ? $"{value.Value:0.0}%" : "N/A";
        private static string FormatTemperature(double? value) => value.HasValue ? $"{value.Value:0.0}°C" : "N/A";
        private static string FormatMemory(MemorySnapshot mem) => mem.IsValid ? $"{mem.UsedGb:0.0}/{mem.TotalGb:0.0}GB" : "N/A";

        private static string FormatUptime(long tickCount64)
        {
            var span = TimeSpan.FromMilliseconds(tickCount64);
            return $"{(int)span.TotalDays}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        private double? GetCpuUsage()
        {
            try { return Math.Max(0, Math.Min(100, cpuCounter.NextValue())); }
            catch { return null; }
        }

        private static MemorySnapshot GetRamUsage()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref mem)) return MemorySnapshot.Invalid;
            var total = mem.ullTotalPhys / 1024d / 1024d / 1024d;
            var used = total - (mem.ullAvailPhys / 1024d / 1024d / 1024d);
            return new MemorySnapshot(true, total, used, mem.dwMemoryLoad);
        }

        private static double? GetNpuUsage() => TryGetGenericUtilization("NPU");
        private static double? GetCpuTemperature() => GetThermalZoneTemperatures().OrderByDescending(t => t).FirstOrDefaultOrNull();
        private static double? GetGpuTemperature() { var temps = GetThermalZoneTemperatures(); return temps.Count > 1 ? temps.Max() : (double?)null; }
        private static double? GetSystemTemperature() { var temps = GetThermalZoneTemperatures(); return temps.Count > 0 ? temps.Average() : (double?)null; }

        private static List<double> GetThermalZoneTemperatures()
        {
            var results = new List<double>();
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["CurrentTemperature"] is uint raw)
                    {
                        var celsius = (raw / 10.0) - 273.15;
                        if (celsius > -30 && celsius < 130) results.Add(celsius);
                    }
                }
            }
            catch { }
            return results;
        }

        private static double? TryGetGenericUtilization(string keyword)
        {
            try
            {
                foreach (var category in PerformanceCounterCategory.GetCategories())
                {
                    if (!category.CategoryName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var instance in category.GetInstanceNames() ?? Array.Empty<string>())
                    {
                        foreach (var counterName in category.GetCounters(instance).Select(c => c.CounterName))
                        {
                            if (counterName.Contains("Util", StringComparison.OrdinalIgnoreCase) || counterName.Contains("%", StringComparison.OrdinalIgnoreCase))
                            {
                                try { using var c = new PerformanceCounter(category.CategoryName, counterName, instance, true); _ = c.NextValue(); return c.NextValue(); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX { public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys; public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual; }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private sealed class ValueLabelPair { public Label Name { get; } public Label Value { get; } public ValueLabelPair(Label name, Label value) { Name = name; Value = value; } }
        private readonly record struct MemorySnapshot(bool IsValid, double TotalGb, double UsedGb, double Percent) { public static MemorySnapshot Invalid => new MemorySnapshot(false, 0, 0, 0); }

        private class DoubleBufferedTable : TableLayoutPanel
        {
            public DoubleBufferedTable() { DoubleBuffered = true; }
        }
    }

    public sealed class GpuTracker : IDisposable
    {
        private readonly Dictionary<string, PerformanceCounter> counters = new Dictionary<string, PerformanceCounter>();
        private readonly Dictionary<string, string> luidToName = new Dictionary<string, string>();
        private DateTime lastCleanup = DateTime.Now;

        public List<(string Name, double Usage)> GetUsages()
        {
            var results = new List<(string Name, double Usage)>();
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine")) return results;
                
                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();
                var engine3DInstances = instanceNames.Where(n => n.EndsWith("engtype_3D")).ToList();
                
                foreach (var instance in engine3DInstances)
                {
                    if (!counters.ContainsKey(instance))
                    {
                        var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                        try { pc.NextValue(); } catch { continue; }
                        counters[instance] = pc;
                    }
                }

                if ((DateTime.Now - lastCleanup).TotalSeconds > 30)
                {
                    var stale = counters.Keys.Where(k => !engine3DInstances.Contains(k)).ToList();
                    foreach (var s in stale) { counters[s].Dispose(); counters.Remove(s); }
                    lastCleanup = DateTime.Now;
                }

                var luidGroups = new Dictionary<string, double>();
                foreach (var kvp in counters)
                {
                    try
                    {
                        string luid = ExtractLuid(kvp.Key);
                        double val = kvp.Value.NextValue();
                        if (!luidGroups.ContainsKey(luid)) luidGroups[luid] = 0;
                        luidGroups[luid] += val;
                    }
                    catch { }
                }

                foreach (var group in luidGroups)
                {
                    string name = GetGpuName(group.Key);
                    results.Add((name, group.Value));
                }
            }
            catch { }
            return results;
        }

        private string ExtractLuid(string instanceName)
        {
            var parts = instanceName.Split('_');
            int idx = Array.IndexOf(parts, "luid");
            if (idx != -1 && parts.Length > idx + 2) return $"{parts[idx + 1]}_{parts[idx + 2]}";
            return "unknown";
        }

        private string GetGpuName(string luid)
        {
            if (luidToName.TryGetValue(luid, out var name)) return name;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                var names = new List<string>();
                foreach (ManagementObject obj in searcher.Get())
                {
                    string n = obj["Name"]?.ToString() ?? "Generic GPU";
                    n = n.Replace("NVIDIA ", "").Replace("AMD ", "").Replace("Intel(R) ", "").Replace(" Graphics", "");
                    names.Add(n);
                }

                int index = luidToName.Count;
                if (names.Count > index)
                {
                    luidToName[luid] = names[index];
                    return names[index];
                }
            }
            catch { }

            return "GPU";
        }

        public void Dispose()
        {
            foreach (var pc in counters.Values) pc.Dispose();
            counters.Clear();
        }
    }

    internal static class Extensions { public static double? FirstOrDefaultOrNull(this IEnumerable<double> values) { foreach (var v in values) return v; return null; } }
}
