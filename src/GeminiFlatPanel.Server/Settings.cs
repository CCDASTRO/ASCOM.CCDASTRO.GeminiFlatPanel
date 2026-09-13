using System;
using System.IO;
using System.Xml.Serialization;
namespace GeminiFlatPanel.Server
{
    public class Settings
    {
        public string ExternalSwitchProgId { get; set; } = "";
        public short ExternalSwitchCount { get; set; }
        public int? VerifiedClosedLimit { get; set; }
        public int? VerifiedOpenLimit { get; set; }
        public string VerifiedFirmware { get; set; }
        public bool MotionSafetyLock { get; set; } = true;
        public string Port { get; set; } = "COM9";
        public string HeaterName { get; set; } = "Dew heater";
        public int? ClosedPosition { get; set; }
        public int? OpenPosition { get; set; }
        public int Tolerance { get; set; } = 5;
        internal static string TestDirectory; public static string DirectoryPath => TestDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCDASTRO", "GeminiFlatPanel");
        private static readonly object gate = new object();
        public static Settings Load()
        {
            lock(gate)
            {
                string file = Path.Combine(DirectoryPath, "settings.xml");
                if(!File.Exists(file)) return new Settings();
                using(var reader = File.OpenRead(file)) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
            }
        }
        public void Save()
        {
            lock(gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                string file = Path.Combine(DirectoryPath, "settings.xml"), temp = file + ".tmp";
                using(var writer = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(writer, this);
                if(File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
        }
    }
}
