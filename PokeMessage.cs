using System;
using System.Collections.Generic;

[Serializable]
public class PokeMessage
{
    public string message_type;
    public int sequence_number;
    public int ack_number; // Only used if message_type == "ACK"
    public Dictionary<string, string> data;

    public PokeMessage() {
        data = new Dictionary<string, string>();
    }

    // Helper to serialize to the RFC's "key: value" newline format
    public string Serialize()
    {
        string output = $"message_type: {message_type}\n";
        if (message_type == "ACK")
        {
            output += $"ack_number: {ack_number}\n";
        }
        else
        {
            output += $"sequence_number: {sequence_number}\n";
            foreach (var kvp in data)
            {
                output += $"{kvp.Key}: {kvp.Value}\n";
            }
        }
        return output;
    }

    // Helper to parse from string back to object
    public static PokeMessage Deserialize(string rawData)
    {
        PokeMessage msg = new PokeMessage();
        string[] lines = rawData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex == -1) continue;

            string key = line.Substring(0, colonIndex).Trim();
            string value = line.Substring(colonIndex + 1).Trim();

            switch (key)
            {
                case "message_type": msg.message_type = value; break;
                case "sequence_number": int.TryParse(value, out msg.sequence_number); break;
                case "ack_number": int.TryParse(value, out msg.ack_number); break;
                default: msg.data[key] = value; break;
            }
        }
        return msg;
    }
}