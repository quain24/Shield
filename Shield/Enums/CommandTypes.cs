﻿namespace Shield.Enums
{
    /// <summary>
    /// Options for a command type when, for example, transmiting command to a device
    /// </summary>
    public enum CommandType
    {
        Empty = 0,
        HandShake,
        Confirm,
        Cancel,
        Sending,
        StartSending,
        StopSending,
        Receiving,
        StartReceiving,
        StopReceiving,
        Completed,
        Correct,
        Error,
        Unknown,
        Partial,
        Data    //15
    }
}