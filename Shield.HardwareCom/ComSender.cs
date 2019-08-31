﻿using Shield.HardwareCom.Models;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shield.HardwareCom.Enums;
using System.Diagnostics;
using Shield.HardwareCom.CommonInterfaces;

namespace Shield.HardwareCom
{
    public class ComSender : IComSender
    {
        // private SerialPort _port;
        private ICommunicationDevice _port;
        private ICommandModel _command;
              
        public void Setup(ICommunicationDevice port)
        {
            _port = port;
        }

        public void Command(ICommandModel command)
        {
            _command = command;
        }        

        public bool Send(ICommandModel command)
        {  
            try
            {
                if (command.CommandType == CommandType.Data)
                    _port.Write(command.Data);
                else
                    _port.Write(command.CommandTypeString);

                return true;
            }
            catch(Exception ex)
            {
                if(ex is TimeoutException)
                {
                    Debug.WriteLine("--- ComSender --- Timeout exception: " + ex.Message);
                }
                else
                {
                    Debug.WriteLine("--- ComSender --- Other exception: " + ex.Message);                   
                }
                return false;
            }
        }

    }
}
