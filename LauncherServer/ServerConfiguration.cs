
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PangyaLauncherServer
{
    public class ServerProcessInfo
    {
        public string Name { get; set; }
        public bool Run { get; set; }
        public int Delay { get; set; }
        public string Path { get; set; }
        public string Parameters { get; set; }
    }

    public static class ServerConfig
    {
        public static List<ServerProcessInfo> LoadProcesses(string xmlFile)
        {
            if (!File.Exists(xmlFile))
                throw new FileNotFoundException("Arquivo de configuração não encontrado", xmlFile);

            XDocument doc = XDocument.Load(xmlFile);

            return doc.Descendants("Process")
                .Select(x => new ServerProcessInfo
                {
                    Name = (string)x.Attribute("Name"),
                    Run = (bool)x.Attribute("Run"),
                    Delay = (int)x.Attribute("Delay"),
                    Path = (string)x.Attribute("Path"),
                    Parameters = (string)x.Attribute("Parameters")
                })
                .ToList();
        }
    }

}
