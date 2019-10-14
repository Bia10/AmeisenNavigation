﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AmeisenNavigation.Server.Objects;
using AmeisenNavigationWrapper;
using Newtonsoft.Json;

namespace AmeisenNavigation.Server
{
    internal static class Program
    {
        private static readonly string ErrorPath = AppDomain.CurrentDomain.BaseDirectory + "errors.txt";
        private static readonly string SettingsPath = AppDomain.CurrentDomain.BaseDirectory + "config.json";
        private static int clientCount = 0;

        private static volatile bool stopServer = false;

        private static TcpListener TcpListener { get; set; }

        private static AmeisenNav AmeisenNav { get; set; }

        private static Thread LoggingThread { get; set; }

        private static Queue<LogEntry> LogQueue { get; set; }

        public static void Main()
        {
            SetupLogging();

            UpdateConnectedClientCount();
            PrintHeader();

            LogQueue.Enqueue(new LogEntry(BuildLog($"Loading: {SettingsPath}..."), ConsoleColor.White));
            Settings settings = LoadConfigFile();

            if (settings == null)
            {
                Console.ReadKey();
            }
            else if (!Directory.Exists(settings.MmapsFolder))
            {
                LogQueue.Enqueue(new LogEntry(BuildLog($"MMAP folder missing, edit folder in config.json..."), ConsoleColor.Red));
                Console.ReadKey();
            }
            else
            {
                AmeisenNav = new AmeisenNav(settings.MmapsFolder);

                if (settings.PreloadMaps.Length > 0)
                {
                    PreloadMaps(settings);
                }

                TcpListener = new TcpListener(IPAddress.Parse(settings.IpAddress), settings.Port);
                TcpListener.Start();

                LogQueue.Enqueue(new LogEntry(BuildLog($"{settings.IpAddress}:{settings.Port} press Ctrl + C to exit..."), ConsoleColor.Green));

                EnterServerLoop();

                // cleanup after server stopped
                AmeisenNav.Dispose();
            }
        }

        private static void SetupLogging()
        {
            LogQueue = new Queue<LogEntry>();
            LoggingThread = new Thread(() => LoggingThreadRoutine());

            LoggingThread.Start();
        }

        public static List<Vector3> GetPath(Vector3 start, Vector3 end, int mapId)
        {
            int pathSize;
            List<Vector3> path = new List<Vector3>();

            unsafe
            {
                fixed (float* pointerStart = start.ToArray())
                fixed (float* pointerEnd = end.ToArray())
                {
                    float* path_raw = AmeisenNav.GetPath(mapId, pointerStart, pointerEnd, &pathSize);

                    // postprocess the raw path to a list of Vector3
                    // the raw path looks like this:
                    // [ x1, y1, z1, x2, y2, z2, ...]
                    for (int i = 0; i < pathSize * 3; i += 3)
                    {
                        path.Add(new Vector3(path_raw[i], path_raw[i + 1], path_raw[i + 2]));
                    }
                }
            }

            return path;
        }

        public static void EnterServerLoop()
        {
            while (!stopServer)
            {
                TcpClient newClient = TcpListener.AcceptTcpClient();
                Thread userThread = new Thread(new ThreadStart(() => HandleClient(newClient)));
                userThread.Start();
            }
        }

        public static void HandleClient(TcpClient client)
        {
            LogQueue.Enqueue(new LogEntry(BuildLog($"New Client: {client.Client.RemoteEndPoint}"), ConsoleColor.Green));

            using (StreamReader reader = new StreamReader(client.GetStream(), Encoding.ASCII))
            using (StreamWriter writer = new StreamWriter(client.GetStream(), Encoding.ASCII))
            {
                bool isClientConnected = true;
                Interlocked.Increment(ref clientCount);
                UpdateConnectedClientCount();

                while (isClientConnected)
                {
                    try
                    {
                        string rawData = reader.ReadLine()?.Replace("&gt;", string.Empty);
                        if (!string.IsNullOrEmpty(rawData))
                        {
                            PathRequest pathRequest = JsonConvert.DeserializeObject<PathRequest>(rawData);

                            List<Vector3> path = GetPath(pathRequest.A, pathRequest.B, pathRequest.MapId);

                            writer.WriteLine(JsonConvert.SerializeObject(path) + " &gt;");
                            writer.Flush();
                        }
                    }
                    catch (Exception e)
                    {
                        string errorMsg = BuildLog($"{e.GetType()} occured at client ");
                        LogQueue.Enqueue(new LogEntry(errorMsg, ConsoleColor.Red, $"{client.Client.RemoteEndPoint}"));

                        try
                        {
                            File.AppendAllText(ErrorPath, $"{errorMsg} \n{e}\n");
                        }
                        catch
                        {
                            // ignored, if we cant write to the log what should we do?
                        }

                        isClientConnected = false;
                    }
                }

                Interlocked.Decrement(ref clientCount);
                UpdateConnectedClientCount();
            }
        }

        public static void ColoredPrint(string s, ConsoleColor color, string uncoloredOutput = "")
        {
            Console.ForegroundColor = color;
            Console.Write(s);
            Console.ResetColor();
            Console.WriteLine(uncoloredOutput);
        }

        public static string BuildLog(string s)
            => $"[{DateTime.Now.ToLongTimeString()}] >> {s}";

        public static void LoggingThreadRoutine()
        {
            while (!stopServer || LogQueue.Count > 0)
            {
                if (LogQueue.Count > 0)
                {
                    LogEntry logEntry = LogQueue.Dequeue();
                    ColoredPrint(logEntry.ColoredPart, logEntry.Color, logEntry.UncoloredPart);
                }

                Thread.Sleep(1);
            }
        }

        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.White;
            string version = System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString();
            Console.WriteLine(@"    ___                   _                 _   __           ");
            Console.WriteLine(@"   /   |  ____ ___  ___  (_)_______  ____  / | / /___ __   __");
            Console.WriteLine(@"  / /| | / __ `__ \/ _ \/ / ___/ _ \/ __ \/  |/ / __ `/ | / /");
            Console.WriteLine(@" / ___ |/ / / / / /  __/ (__  )  __/ / / / /|  / /_/ /| |/ / ");
            Console.WriteLine(@"/_/  |_/_/ /_/ /_/\___/_/____/\___/_/ /_/_/ |_/\__,_/ |___/  ");
            Console.Write($"                                        Server ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(version);
            Console.WriteLine();
            Console.ResetColor();
        }

        private static void PreloadMaps(Settings settings)
        {
            Console.WriteLine(BuildLog($"Preloading Maps..."));
            foreach (int i in settings.PreloadMaps)
            {
                AmeisenNav.LoadMap(i);
            }

            LogQueue.Enqueue(new LogEntry(BuildLog($"Preloaded {settings.PreloadMaps.Length} Maps"), ConsoleColor.Green));
        }

        private static Settings LoadConfigFile()
        {
            Settings settings = null;

            try
            {
                if (File.Exists(SettingsPath))
                {
                    settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath));

                    if (!settings.MmapsFolder.EndsWith("/") && !settings.MmapsFolder.EndsWith("\\"))
                    {
                        settings.MmapsFolder += "/";
                    }

                    LogQueue.Enqueue(new LogEntry(BuildLog($"Loaded config file"), ConsoleColor.Green));
                }
                else
                {
                    settings = new Settings();
                    File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings));
                    LogQueue.Enqueue(new LogEntry(BuildLog($"Created default config file"), ConsoleColor.White));
                }
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue(new LogEntry(BuildLog($"Failed to parse config.json...\n"), ConsoleColor.Red, ex.ToString()));
            }

            return settings;
        }

        private static void UpdateConnectedClientCount()
        {
            Console.Title = $"AmeisenNavigation Server - Connected Clients: [{clientCount}]";
        }
    }
}
