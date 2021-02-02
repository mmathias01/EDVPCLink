using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace EDVPCLink
{
    class Program
    {
        private static readonly Regex JsonRegex = new Regex(@"^{.*}$");

        static void Main(string[] args)
        {
            Console.WriteLine("ED -> VPC Link Starting Up!");

            using (FileSystemWatcher watcher = new FileSystemWatcher())
            {
                var path = $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\Saved Games\\Frontier Developments\\Elite Dangerous";
                watcher.Path = path;

                // Watch for changes in LastAccess and LastWrite times, and
                // the renaming of files or directories.
                watcher.NotifyFilter = NotifyFilters.LastAccess
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.FileName
                                     | NotifyFilters.DirectoryName;

                // Only watch JSON files.
                watcher.Filter = "*.json";

                // Add event handlers.
                watcher.Changed += OnChanged;
                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;
                watcher.Renamed += OnRenamed;

                // Begin watching.
                watcher.EnableRaisingEvents = true;

                // Wait for the user to quit the program.
                Console.WriteLine("Press 'q <ENTER>' or close this window to quit this utility at any time.");
                while (Console.Read() != 'q') ;
            }

        }

        private static void ReadStatusFile(string statusFilePath)
        {

            using (FileStream fs = new FileStream(statusFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
            {
                string _json = string.Empty;
                try
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    _json = reader.ReadLine() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(_json))
                    {
                        Status status = ParseStatusEntry(_json);
                        Console.WriteLine($"    [Thread {Thread.CurrentThread.ManagedThreadId}] Parsed Update ({status.timestamp})");

                        //TODO: explore benefits / overhead to opening a new connection every time vs maintaining a connection. For now this is easiest because it should be fairly fault tolerant. 
                        var ipAddress = "127.0.0.1";
                        var portNumber = 4123;
                        var ipEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), portNumber);
                        try
                        {
                            var client = new UdpClient();
                            client.Connect(ipEndPoint);

                            if (client != null)
                            {
                                var _json_data = JsonConvert.SerializeObject(status);
                                var data = Encoding.ASCII.GetBytes(_json_data);

                                client.Send(data, data.Length);
                                Console.WriteLine($"    [Thread {Thread.CurrentThread.ManagedThreadId}] Sent Updated Status to VPC Link Tool");
                                client.Close();
                            }
                        }
                        catch {
                            Console.WriteLine($"    [Thread {Thread.CurrentThread.ManagedThreadId}] Could Not Communicate with VPC Link Tool, is VPC Link Tool running, and did you press 'Start'?");
                        }
                        

                    }
                    else
                    {
                        Console.WriteLine($"    [Thread {Thread.CurrentThread.ManagedThreadId}] Parsed Update(Empty File)");
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine($"    [Thread {Thread.CurrentThread.ManagedThreadId}] Could Not Parse Update - {e.Message}");
                }
            }
        }

        public static Status ParseStatusEntry(string line)
        {
            Status status = new Status();
            try
            {
                Match match = JsonRegex.Match(line);
                if (match.Success)
                {
                    IDictionary<string, object> data = Deserializtion.DeserializeData(line);
                    status.timestamp = DateTime.UtcNow;
                    try
                    {
                        status.timestamp = JsonParsing.getDateTime("timestamp", data);
                    }
                    catch
                    {
                        Console.WriteLine($"Status update had no timestamp, using current timestamp");
                    }

                    status.flags = (Status.Flags)(JsonParsing.getOptionalLong(data, "Flags") ?? 0);
                    if (status.flags == Status.Flags.None)
                    {
                        return status;
                    }

                    data.TryGetValue("Pips", out object val);
                    List<long> pips = ((List<object>)val)?.Cast<long>()?.ToList(); // The 'TryGetValue' function returns these values as type 'object<long>'
                    status.pips_sys = pips != null ? ((decimal?)pips[0] / 2) : null; // Set system pips (converting from half pips)
                    status.pips_eng = pips != null ? ((decimal?)pips[1] / 2) : null; // Set engine pips (converting from half pips)
                    status.pips_wea = pips != null ? ((decimal?)pips[2] / 2) : null; // Set weapon pips (converting from half pips)

                    status.firegroup = JsonParsing.getOptionalInt(data, "FireGroup");
                    int? gui_focus = JsonParsing.getOptionalInt(data, "GuiFocus");
                    switch (gui_focus)
                    {
                        case 0: // No focus
                            {
                                status.gui_focus = "main";
                                break;
                            }
                        case 1: // InternalPanel (right hand side)
                            {
                                status.gui_focus = "internal";
                                break;
                            }
                        case 2: // ExternalPanel (left hand side)
                            {
                                status.gui_focus = "external";
                                break;
                            }
                        case 3: // CommsPanel (top)
                            {
                                status.gui_focus = "communications";
                                break;
                            }
                        case 4: // RolePanel (bottom)
                            {
                                status.gui_focus = "role";
                                break;
                            }
                        case 5: // StationServices
                            {
                                status.gui_focus = "station";
                                break;
                            }
                        case 6: // GalaxyMap
                            {
                                status.gui_focus = "galaxy_map";
                                break;
                            }
                        case 7: // SystemMap
                            {
                                status.gui_focus = "system_map";
                                break;
                            }
                        case 8: // Orrery
                            {
                                status.gui_focus = "orrery";
                                break;
                            }
                        case 9: // FSS mode
                            {
                                status.gui_focus = "fss";
                                break;
                            }
                        case 10: // SAA mode
                            {
                                status.gui_focus = "saa";
                                break;
                            }
                        case 11: // Codex
                            {
                                status.gui_focus = "codex";
                                break;
                            }
                    }
                    status.latitude = JsonParsing.getOptionalDecimal(data, "Latitude");
                    status.longitude = JsonParsing.getOptionalDecimal(data, "Longitude");
                    status.altitude = JsonParsing.getOptionalDecimal(data, "Altitude");
                    status.heading = JsonParsing.getOptionalDecimal(data, "Heading");
                    if (data.TryGetValue("Fuel", out object fuelData))
                    {
                        if (fuelData is IDictionary<string, object> fuelInfo)
                        {
                            status.fuelInTanks = JsonParsing.getOptionalDecimal(fuelInfo, "FuelMain");
                            status.fuelInReservoir = JsonParsing.getOptionalDecimal(fuelInfo, "FuelReservoir");
                        }
                    }
                    status.cargo_carried = (int?)JsonParsing.getOptionalDecimal(data, "Cargo");
                    status.legal_status = JsonParsing.getString(data, "LegalState");
                    status.bodyname = JsonParsing.getString(data, "BodyName");
                    status.planetradius = JsonParsing.getOptionalDecimal(data, "PlanetRadius");

                    return status;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse Status.json line: {ex.Message}");
            }
            return null;
        }

        private static void OnChanged(object source, FileSystemEventArgs e)
        {   
            if (e.Name.Equals("Status.json"))
            {
                Console.WriteLine($"File: {e.FullPath} {e.ChangeType}");
                Thread updateThread = new Thread(() => ReadStatusFile(e.FullPath))
                {
                    IsBackground = true
                };
                updateThread.Start();
            }
        }

        private static void OnCreated(object source, FileSystemEventArgs e) =>
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.FullPath} created");

        private static void OnDeleted(object source, FileSystemEventArgs e) =>
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.FullPath} deleted");

        private static void OnRenamed(object source, RenamedEventArgs e) =>
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.OldFullPath} renamed to {e.FullPath}");
    }
}
