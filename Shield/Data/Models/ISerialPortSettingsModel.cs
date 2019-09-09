﻿using System.IO.Ports;
using System.Text;
using Shield.CommonInterfaces;

namespace Shield.Data.Models
{
    public interface ISerialPortSettingsModel : ICommunicationDeviceSettings
    {
        int BaudRate { get; set; }
        int DataBits { get; set; }
        Parity Parity { get; set; }
        int PortNumber { get; set; }
        StopBits StopBits { get; set; }
        int ReadTimeout { get; set; }
        int WriteTimeout { get; set; }
        int Encoding { get; set; }
    }
}