using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ScreenPowerPro.Core.Tracking;

namespace ScreenPowerPro.Models;

public class MouseClickEvent
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    [JsonPropertyName("timestamp_ms")]
    public double TimestampMs
    {
        get => Math.Round(Timestamp * 1000.0, 3);
        set
        {
            if (value > 0 && Timestamp <= 0)
                Timestamp = value / 1000.0;
        }
    }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "left_down"; // left_down, left_up, right_down, right_up

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    public MouseFrameData ToFrameData()
    {
        var evtType = Type switch
        {
            "left_up" => MouseEventType.LeftUp,
            "right_down" => MouseEventType.RightDown,
            "right_up" => MouseEventType.RightUp,
            "double_click" => MouseEventType.DoubleClick,
            _ => MouseEventType.LeftDown
        };
        return new MouseFrameData(TimestampMs > 0 ? TimestampMs : Timestamp * 1000.0, X, Y, evtType);
    }

    public static MouseClickEvent FromFrameData(MouseFrameData frame)
    {
        string typeStr = frame.EventType switch
        {
            MouseEventType.LeftUp => "left_up",
            MouseEventType.RightDown => "right_down",
            MouseEventType.RightUp => "right_up",
            MouseEventType.DoubleClick => "double_click",
            _ => "left_down"
        };
        return new MouseClickEvent
        {
            Timestamp = Math.Round(frame.TimestampMs / 1000.0, 4),
            Type = typeStr,
            X = (int)Math.Round(frame.X),
            Y = (int)Math.Round(frame.Y)
        };
    }
}

public class MouseMoveEvent
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    [JsonPropertyName("timestamp_ms")]
    public double TimestampMs
    {
        get => Math.Round(Timestamp * 1000.0, 3);
        set
        {
            if (value > 0 && Timestamp <= 0)
                Timestamp = value / 1000.0;
        }
    }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    public MouseFrameData ToFrameData()
    {
        return new MouseFrameData(TimestampMs > 0 ? TimestampMs : Timestamp * 1000.0, X, Y, MouseEventType.Move);
    }

    public static MouseMoveEvent FromFrameData(MouseFrameData frame)
    {
        return new MouseMoveEvent
        {
            Timestamp = Math.Round(frame.TimestampMs / 1000.0, 4),
            X = (int)Math.Round(frame.X),
            Y = (int)Math.Round(frame.Y)
        };
    }
}

public class KeystrokeEvent
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    [JsonPropertyName("timestamp_ms")]
    public double TimestampMs
    {
        get => Math.Round(Timestamp * 1000.0, 3);
        set
        {
            if (value > 0 && Timestamp <= 0)
                Timestamp = value / 1000.0;
        }
    }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("modifiers")]
    public List<string> Modifiers { get; set; } = new();
}

/// <summary>
/// Milisaniye tabanlı birleşik fare olay modeli (MouseEventData).
/// </summary>
public class MouseEventData
{
    [JsonPropertyName("timestamp_ms")]
    public double TimestampMs { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("event_type")]
    public MouseEventType EventType { get; set; } = MouseEventType.Move;

    public MouseEventData() { }

    public MouseEventData(double timestampMs, float x, float y, MouseEventType eventType = MouseEventType.Move)
    {
        TimestampMs = timestampMs;
        X = x;
        Y = y;
        EventType = eventType;
    }

    public MouseFrameData ToFrameData() => new(TimestampMs, X, Y, EventType);

    public static MouseEventData FromFrameData(MouseFrameData frame) => new(frame.TimestampMs, frame.X, frame.Y, frame.EventType);
}
