using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MyAtasIndicator.Shared
{
    public static class SignalBus
    {
        public static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATAS", "SignalBus");

        public static readonly string State100Path = Path.Combine(BaseDir, "state_100rx.json");
        public static readonly string State10Path  = Path.Combine(BaseDir, "state_10rx.json");
        public static readonly string Req10Path    = Path.Combine(BaseDir, "request_10rx.json");

        static SignalBus() { Directory.CreateDirectory(BaseDir); }

        public static void WriteJson<T>(string path, T obj)
        {
            var tmp = path + ".tmp";
            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                ser.WriteObject(ms, obj);
                File.WriteAllText(tmp, Encoding.UTF8.GetString(ms.ToArray()));
            }
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        public static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path, Encoding.UTF8);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var ser = new DataContractJsonSerializer(typeof(T));
                    return ser.ReadObject(ms) as T;
                }
            }
            catch { return null; }
        }
    }

    public enum Trend { None, Up, Down }
    public enum AbsorptionMode { DeltaFavorTrendOrAbsorption, RequireAbsorptionAlways, Disabled }

    [DataContract] public class State100
    {
        [DataMember] public DateTime Timestamp { get; set; }
        [DataMember] public long BarId { get; set; }
        [DataMember] public Trend Trend { get; set; }
        [DataMember] public decimal Hh { get; set; }
        [DataMember] public decimal Hl { get; set; }
    }

    [DataContract] public class Request10
    {
        [DataMember] public DateTime Timestamp { get; set; }
        [DataMember] public decimal ZoneLow { get; set; }
        [DataMember] public decimal ZoneHigh { get; set; }
        [DataMember] public int ExpiresSec { get; set; } = 60;
    }

    [DataContract] public class State10
    {
        [DataMember] public DateTime Timestamp { get; set; }
        [DataMember] public bool PullbackOk { get; set; }
        [DataMember] public bool Absorption { get; set; }
        [DataMember] public decimal ZoneLow { get; set; }
        [DataMember] public decimal ZoneHigh { get; set; }
        [DataMember] public decimal DeltaInPullback { get; set; }
        [DataMember] public int BigTradesInZone { get; set; }
    }
}

