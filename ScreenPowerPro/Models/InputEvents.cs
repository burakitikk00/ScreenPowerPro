using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ScreenPowerPro.Models;

public class MouseClickEvent
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "left_down"; // left_down, left_up, right_down, right_up

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public class MouseMoveEvent
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public class KeystrokeEvent
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("modifiers")]
    public List<string> Modifiers { get; set; } = new();
}
