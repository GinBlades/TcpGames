using Newtonsoft.Json;
using System;

namespace TcpGames.Shared
{
    public class Packet
    {
        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        // Makes a packet
        public Packet(string command = "", string message = "")
        {
            Command = command;
            Message = message;
        }

        public override string ToString()
        {
            return $"[Packet:\n Command=`{Command}` Message =`{Message}`]";
        }

        // Serialize to JSON
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        // Deserialize
        public static Packet FromJson(string jsonData)
        {
            return JsonConvert.DeserializeObject<Packet>(jsonData);
        }
    }
}
