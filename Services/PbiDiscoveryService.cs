using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace TableauToPbi.Services
{
    public class PbiInstance
    {
        public int Port { get; set; }
        public string ReportName { get; set; } = string.Empty;
        public string ConnectionString => $"Data Source=localhost:{Port}";
        public override string ToString() => ReportName;
    }

    public static class PbiDiscoveryService
    {
        public static List<PbiInstance> DiscoverInstances()
        {
            var instances = new List<PbiInstance>();

            try
            {
                // Find all msmdsrv.exe processes via WMI
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, ExecutablePath FROM Win32_Process WHERE Name='msmdsrv.exe'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    int msmdsrvPid = Convert.ToInt32(obj["ProcessId"]);
                    int parentPid  = Convert.ToInt32(obj["ParentProcessId"]);

                    // Get parent process name (should be PBIDesktop.exe or a Microsoft.Mashup.Container.*)
                    string parentName = GetProcessName(parentPid);

                    // Walk up one more level if parent is a container process
                    string reportName;
                    if (parentName.Contains("PBIDesktop", StringComparison.OrdinalIgnoreCase))
                    {
                        reportName = GetPbiReportName(parentPid);
                    }
                    else
                    {
                        // Walk up to grandparent
                        int grandParentPid = GetParentPid(parentPid);
                        string grandParentName = GetProcessName(grandParentPid);
                        if (grandParentName.Contains("PBIDesktop", StringComparison.OrdinalIgnoreCase))
                            reportName = GetPbiReportName(grandParentPid);
                        else
                            reportName = $"PBI Instance (pid {msmdsrvPid})";
                    }

                    int port = ReadPortFromFile(msmdsrvPid);
                    if (port > 0)
                        instances.Add(new PbiInstance { Port = port, ReportName = reportName });
                }
            }
            catch
            {
                // WMI not available in some environments — return empty
            }

            return instances;
        }

        private static string GetProcessName(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT Name FROM Win32_Process WHERE ProcessId={pid}");
                foreach (ManagementObject o in searcher.Get())
                    return o["Name"]?.ToString() ?? "";
            }
            catch { }
            return "";
        }

        private static int GetParentPid(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={pid}");
                foreach (ManagementObject o in searcher.Get())
                    return Convert.ToInt32(o["ParentProcessId"]);
            }
            catch { }
            return 0;
        }

        private static string GetPbiReportName(int pbiPid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pbiPid}");
                foreach (ManagementObject o in searcher.Get())
                {
                    string? cmd = o["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        var m = Regex.Match(cmd, "\"([^\"]+\\.pbix)\"", RegexOptions.IgnoreCase);
                        if (m.Success)
                            return Path.GetFileNameWithoutExtension(m.Groups[1].Value);
                    }
                }
            }
            catch { }
            return $"PBI Desktop (pid {pbiPid})";
        }

        private static int ReadPortFromFile(int msmdsrvPid)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string portDir = Path.Combine(localAppData, "Microsoft", "Power BI Desktop", "AnalysisServicesWorkspaces");

                if (!Directory.Exists(portDir)) return 0;

                foreach (var dir in Directory.GetDirectories(portDir))
                {
                    string portFile = Path.Combine(dir, "Data", "msmdsrv.port.txt");
                    if (!File.Exists(portFile)) continue;

                    string portText = File.ReadAllText(portFile, Encoding.Unicode).Trim();
                    if (string.IsNullOrWhiteSpace(portText))
                        portText = new string(portText.Where(char.IsDigit).ToArray());

                    if (int.TryParse(new string(portText.Where(char.IsDigit).ToArray()), out int port) && port > 0)
                        return port;
                }
            }
            catch { }
            return 0;
        }
    }
}
