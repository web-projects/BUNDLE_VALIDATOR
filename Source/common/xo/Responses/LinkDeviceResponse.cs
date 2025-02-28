﻿using System.Collections.Generic;
using Common.XO.Device;

namespace Common.XO.Responses
{
    public class LinkDeviceResponse
    {
        public List<LinkErrorValue> Errors { get; set; }

        public LinkDevicePowerOnNotification PowerOnNotification { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string TerminalId { get; set; }
        public string SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public string Port { get; set; }
        public List<string> Features { get; set; }
        public List<string> Configurations { get; set; }
        public string EMVL2KernelVersion { get; set; }
        public string ContactlessKernelInformation { get; set; }
        //CardWorkflowControls only used when request Action = 'DALStatus'; can be null
        //public LinkCardWorkflowControls CardWorkflowControls { get; set; }
    }
}
