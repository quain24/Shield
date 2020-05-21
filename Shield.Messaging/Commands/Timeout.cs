﻿using System;

namespace Shield.Messaging.Commands
{
    public class Timeout
    {
        public Timeout(int timeout)
        {
            Value = timeout > 0 ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout value has to be positive");
        }

        public int Value { get; }
    }
}