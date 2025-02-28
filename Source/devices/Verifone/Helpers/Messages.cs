﻿using Devices.Common.Helpers;

namespace Devices.Verifone.Helpers
{
    class Messages
    {
        public enum ConsoleMessages
        {
            [StringValue("VIPA: GET DEVICE INFO")]
            GetDeviceInfo,
            [StringValue("VIPA: GET DEVICE HEALTH")]
            GetDeviceHealth,
            [StringValue("VIPA: LOCK DEVICE UPDATE")]
            LockDeviceUpdate,
            [StringValue("VIPA: UNLOCK DEVICE UPDATE")]
            UnlockDeviceUpdate,
            [StringValue("VIPA: UPDATE DEVICE CONFIGURATION")]
            UpdateDeviceUpdate,
            [StringValue("VIPA: UPDATE HMAC KEYS")]
            UpdateHMACKeys,
            [StringValue("VIPA: GENERATE HMAC")]
            GenerateHMAC,
            [StringValue("VIPA: RESET")]
            DeviceReset,
            [StringValue("VIPA: EXTENDED RESET")]
            DeviceExtendedReset,
            [StringValue("VIPA: ABORT COMMAND")]
            AbortCommand,
            [StringValue("VIPA: RESTART")]
            VIPARestart,
            [StringValue("VIPA: REBOOT DEVICE")]
            RebootDevice,
            [StringValue("VIPA: GET CARD STATUS")]
            GetCardStatus,
            [StringValue("VIPA: GET CARD INFO")]
            GetCardInfo,
            [StringValue("VIPA: ENTER CARD TYPE")]
            GetCardType,
            [StringValue("VIPA: ENTER ZIP")]
            GetZipCode,
            [StringValue("VIPA: ENTER PIN")]
            GetPIN,
            [StringValue("VIPA: ENTER ADA MODE")]
            StartADA,
            [StringValue("VIPA: CLESS READER CLOSED")]
            DeviceCLessReaderClosed,
            [StringValue("VIPA: GET SECURITY CONFIGURATION")]
            GetSecurityConfiguration,
            [StringValue("VIPA: GET SIGNATURE")]
            GetSignature,
            [StringValue("VIPA: SET KEYBOARD STATUS")]
            KeyboardStatus,
            [StringValue("VIPA: UPDATE IDLE SCREEN")]
            UpdateIdleScreen,
            [StringValue("VIPA: DISPLAY CUSTOM SCREEN")]
            DisplayCustomScreen,
            [StringValue("VIPA: DISPLAY CUSTOM SCREEN HTML")]
            DisplayCustomScreenHTML,
            [StringValue("VIPA: VERSIONS")]
            VIPAVersions,
            [StringValue("VIPA: LOG CONFIGURATION")] 
            LogConfiguration
        }
    }
}
