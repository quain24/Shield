﻿using System.Collections.Generic;

namespace Shield.Helpers
{
    public interface IIdGenerator
    {
        void FlushUsedUpIdsBuffer();

        string GetNewID();

        IEnumerable<string> GetUsedUpIds();

        void MarkAsUsedUp(string id);

        void MarkAsUsedUp(string[] ids);
    }
}