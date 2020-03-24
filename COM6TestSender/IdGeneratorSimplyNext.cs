﻿using Shield.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace COM6TestSender
{
    class IdGeneratorSimplyNext : IIdGenerator
    {
        int id = 0;

        public void FlushUsedUpIdsBuffer()
        {
            throw new NotImplementedException();
        }

        public string GetNewID()
        {            
            string a = id.ToString().PadLeft(4,'0');
            id++;
            return a;
        }

        public IEnumerable<string> GetUsedUpIds()
        {
            throw new NotImplementedException();
        }

        public void MarkAsUsedUp(string id)
        {
            return;
        }

        public void MarkAsUsedUp(string[] ids)
        {
            return;
        }
    }
}