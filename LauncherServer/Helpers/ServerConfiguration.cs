using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using PangyaLauncherServer.Models;

namespace PangyaLauncherServer.Helpers
{
    /// <summary>
    /// Loads and saves server process configuration from/to an XML file.
    /// </summary>
    public static class ServerConfiguration
    {
        // ------------------------------------------------------------------ //
        //  Load                                                               //
        // ------------------------------------------------------------------ //

        public static List<ServerEntry> Load(string xmlFile)
        {
            if (!File.Exists(xmlFile))
                throw new FileNotFoundException("Configuration file not found.", xmlFile);

            var doc  = XDocument.Load(xmlFile);
            var list = new List<ServerEntry>();

            foreach (var x in doc.Descendants("Process"))
            {
                list.Add(new ServerEntry
                {
                    Name        = Require(x, "Name"),
                    Path        = Require(x, "Path"),
                    Parameters  = (string?)x.Attribute("Parameters") ?? string.Empty,
                    Delay       = ParseInt(x,  "Delay",       0),
                    AutoRun     = ParseBool(x, "Run",         false),
                    AutoRestart = ParseBool(x, "AutoRestart", true),
                    MaxRestarts = ParseInt(x,  "MaxRestarts", 3)
                });
            }

            if (list.Count == 0)
                throw new InvalidDataException("No <Process> entries found in the configuration file.");

            return list;
        }

        // ------------------------------------------------------------------ //
        //  Save (updates existing file or creates new)                        //
        // ------------------------------------------------------------------ //

        public static void Save(string xmlFile, List<ServerEntry> entries)
        {
            var root = new XElement("Processes");
            foreach (var e in entries)
            {
                root.Add(new XElement("Process",
                    new XAttribute("Name",        e.Name),
                    new XAttribute("Path",        e.Path),
                    new XAttribute("Parameters",  e.Parameters ?? string.Empty),
                    new XAttribute("Delay",       e.Delay),
                    new XAttribute("Run",         e.AutoRun),
                    new XAttribute("AutoRestart", e.AutoRestart),
                    new XAttribute("MaxRestarts", e.MaxRestarts)
                ));
            }

            new XDocument(new XDeclaration("1.0", "utf-8", null), root)
                .Save(xmlFile);
        }

        // ------------------------------------------------------------------ //
        //  Generate default startup.xml if missing                            //
        // ------------------------------------------------------------------ //

        public static void GenerateDefault(string xmlFile)
        {
            if (File.Exists(xmlFile)) return;

            var defaults = new List<ServerEntry>
            {
                new() { Name="Auth",      Path="servers/AuthServer.exe",      Delay=0,    AutoRun=true,  MaxRestarts=5 },
                new() { Name="Login",     Path="servers/LoginServer.exe",     Delay=500,  AutoRun=true,  MaxRestarts=5 },
                new() { Name="Game",      Path="servers/GameServer.exe",      Delay=1000, AutoRun=true,  MaxRestarts=5 },
                new() { Name="Rank",      Path="servers/RankServer.exe",      Delay=1500, AutoRun=false, MaxRestarts=3 },
                new() { Name="Messenger", Path="servers/MessengerServer.exe", Delay=2000, AutoRun=false, MaxRestarts=3 }
            };

            Save(xmlFile, defaults);
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                             //
        // ------------------------------------------------------------------ //

        private static string Require(XElement x, string attr)
        {
            var v = (string?)x.Attribute(attr);
            if (string.IsNullOrWhiteSpace(v))
                throw new InvalidDataException($"Required attribute '{attr}' is missing or empty.");
            return v;
        }

        private static int ParseInt(XElement x, string attr, int def)
        {
            var v = (string?)x.Attribute(attr);
            return int.TryParse(v, out var n) ? n : def;
        }

        private static bool ParseBool(XElement x, string attr, bool def)
        {
            var v = (string?)x.Attribute(attr);
            return bool.TryParse(v, out var b) ? b : def;
        }
    }
}
