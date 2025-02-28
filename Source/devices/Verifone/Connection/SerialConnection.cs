﻿using Devices.Common;
using Devices.Common.DebugDump;
using Devices.Verifone.Connection.Interfaces;
using Devices.Verifone.VIPA;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Devices.Common.Constants.LogMessage;

namespace Devices.Verifone.Connection
{
    public class SerialConnection : IDisposable
    {
#if DEBUG
        internal const bool LogSerialBytes = true;
#else
        internal const bool LogSerialBytes = false;
#endif
        internal VIPAImpl.ResponseTagsHandlerDelegate ResponseTagsHandler = null;
        internal VIPAImpl.ResponseTaglessHandlerDelegate ResponseTaglessHandler = null;
        internal VIPAImpl.ResponseCLessHandlerDelegate ResponseContactlessHandler = null;

        // optimize serial port read buffer size based on expected response
        private const int packetTxHeaderLength = 0x04;  // CLA, INS, P1, P2
        private const int packetRxHeaderLength = 0x04;  // NAD, PCB, LEN, LRC

        // The LEN byte is the length of the packet.
        // It includes the CLA, INS, P1, P2 bytes (but not for subsequent packets in Chained commands),
        // includes the Lc and data field (if present) bytes, and includes the Le byte (if present),
        // includes the SW1-SW2 bytes for responses, but excludes the LRC byte.
        private const int rawReadSizeBytes = packetTxHeaderLength + 0xFB; // 0xFF
        private const int unchainedResponseMessageSize = rawReadSizeBytes * 4;

        private const int chainedResponseMessageSize = unchainedResponseMessageSize * 10;
        private const int chainedCommandMinimumLength = 0xFE;
        private const int chainedCommandPayloadLength = 0xF8;

        private CancellationTokenSource cancellationTokenSource;
        private SerialPort serialPort;
        private readonly IVIPASerialParser serialParser;
        private bool readingSerialPort = false;
        private bool shouldStopReading;
        private bool readerThreadIsActive;
        private bool disposedValue;
        private ArrayPool<byte> arrayPool { get; set; }
        private readonly object readerThreadLock = new object();
        private readonly object portWriteLock = new object();

        // this parameter needs to be adjusted as needed and taking into consideration that Engage devices
        // have a faster processor than UX devices.
        private const int portReadIdleDelayMs = 50;

        private bool IsChainedMessageResponse { get; set; }

        // TODO: Dependency should be injected.
        internal DeviceConfig Config { get; } = new DeviceConfig().SetSerialDeviceConfig(new SerialDeviceConfig());

        private DeviceLogHandler DeviceLogHandler;

        public SerialConnection(DeviceInformation deviceInformation, DeviceLogHandler deviceLogHandler)
        {
            DeviceLogHandler = deviceLogHandler;
            serialParser = new VIPASerialParserImpl(deviceLogHandler, deviceInformation.ComPort);
            cancellationTokenSource = new CancellationTokenSource();
            arrayPool = ArrayPool<byte>.Create();

            if (deviceInformation.ComPort?.Length > 0 && !Config.SerialConfig.CommPortName.Equals(deviceInformation.ComPort, StringComparison.OrdinalIgnoreCase))
            {
                Config.SerialConfig.CommPortName = deviceInformation.ComPort;
            }
        }

        public bool Connect(bool exposeExceptions = false)
        {
            bool connected = false;

            try
            {
                // Create a new SerialPort object with default settings.
                serialPort = new SerialPort(Config.SerialConfig.CommPortName, Config.SerialConfig.CommBaudRate, Config.SerialConfig.CommParity,
                    Config.SerialConfig.CommDataBits, Config.SerialConfig.CommStopBits);

                // Update the Handshake
                serialPort.Handshake = Config.SerialConfig.CommHandshake;

                // Set the read/write timeouts
                serialPort.ReadTimeout = Config.SerialConfig.CommReadTimeout;
                serialPort.WriteTimeout = Config.SerialConfig.CommWriteTimeout;
                serialPort.DataReceived += SerialPort_DataReceived;

                serialPort.Open();

                // discard any buffered bytes
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                connected = true;
                shouldStopReading = false;
                readerThreadIsActive = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VIPA [{serialPort?.PortName}]: {ex.Message}");
                DeviceLogHandler(LogLevel.Error, $"VIPA [{serialPort?.PortName}]: {ex.Message}");

                if (exposeExceptions)
                {
                    throw;
                }

                Dispose();
            }

            return connected;
        }

        public bool IsConnected()
            => serialPort?.IsOpen ?? false;

        public void Disconnect(bool exposeExceptions = false)
        {
            Debug.WriteLine($"VIPA [{serialPort?.PortName ?? "COMXX"}]: disconnect request.");

            shouldStopReading = true;

            try
            {
                cancellationTokenSource?.Cancel();

                // discard any buffered bytes
                if (serialPort?.IsOpen ?? false)
                {
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();

                    serialPort.Close();

                    Debug.WriteLine($"VIPA [{serialPort?.PortName}]: closed port.");
                }
            }
            catch (Exception)
            {
                if (exposeExceptions)
                {
                    throw;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Disconnect();
                    serialPort?.Dispose();
                    serialPort = null;
                    cancellationTokenSource?.Dispose();
                    cancellationTokenSource = null;
                }
                disposedValue = true;

                // https://docs.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.open?view=dotnet-plat-ext-3.1#System_IO_Ports_SerialPort_Open
                // SerialPort has a quirk (aka bug) where needs time to let a worker thread exit:
                //    "The best practice for any application is to wait for some amount of time after calling the Close method before
                //     attempting to call the Open method, as the port may not be closed instantly".
                // The amount of time is unspecified and unpredictable.
                Thread.Sleep(250);
            }
        }

        private bool IsChainedResponseCommand(VIPACommand command) =>
            ((VIPACommandType)(command.cla << 8 | command.ins) == VIPACommandType.ResetDevice) ||
            ((VIPACommandType)(command.cla << 8 | command.ins) == VIPACommandType.DisplayHTML && command.data != null &&
              Encoding.UTF8.GetString(command.data).IndexOf(VIPACommand.ChainedResponseAnswerData, StringComparison.OrdinalIgnoreCase) >= 0);

        public void WriteSingleCmd(VIPAResponseHandlers responsehandlers, VIPACommand command)
        {
            if (command == null)
            {
                return;
            }

            // if command is a chained command, we don't need to process as a single packet 
            if (WriteChainedCmd(responsehandlers, command))
            {
                return;
            }

            ResponseTagsHandler = responsehandlers.responsetagshandler;
            ResponseTaglessHandler = responsehandlers.responsetaglesshandler;
            ResponseContactlessHandler = responsehandlers.responsecontactlesshandler;

            int dataLen = command.data?.Length ?? 0;
            byte lrc = 0;

            if (0 < dataLen)
            {
                dataLen++;  // Allow for Lc byte
            }

            if (command.includeLE)
            {
                dataLen++;  // Allow for Le byte
            }

            int cmdLength = 7 /*NAD, PCB, LEN, CLA, INS, P1, P2*/ + dataLen + 1 /*LRC*/;
            byte[] cmdBytes = arrayPool.Rent(cmdLength);
            int cmdIndex = 0;

            cmdBytes[cmdIndex++] = command.nad;
            lrc ^= command.nad;
            cmdBytes[cmdIndex++] = command.pcb;
            lrc ^= command.pcb;
            cmdBytes[cmdIndex++] = (byte)(packetTxHeaderLength  /*CLA, INS, P1, P2*/ + dataLen /*Lc, data.Length, Le*/);
            lrc ^= (byte)(packetTxHeaderLength                  /*CLA, INS, P1, P2*/ + dataLen /*Lc, data.Length, Le*/);
            cmdBytes[cmdIndex++] = command.cla;
            lrc ^= command.cla;
            cmdBytes[cmdIndex++] = command.ins;
            lrc ^= command.ins;
            cmdBytes[cmdIndex++] = command.p1;
            lrc ^= command.p1;
            cmdBytes[cmdIndex++] = command.p2;
            lrc ^= command.p2;

            if (0 < command.data?.Length)
            {
                cmdBytes[cmdIndex++] = (byte)command.data.Length;
                lrc ^= (byte)command.data.Length;

                foreach (byte byt in command.data)
                {
                    cmdBytes[cmdIndex++] = byt;
                    lrc ^= byt;
                }
            }

            if (command.includeLE)
            {
                cmdBytes[cmdIndex++] = command.le;
                lrc ^= command.le;
            }

            cmdBytes[cmdIndex++] = lrc;

            // chained message response
            IsChainedMessageResponse = IsChainedResponseCommand(command);

            Debug.WriteLineIf(LogSerialBytes, $"VIPA-WRITE[{serialPort?.PortName}]: {BitConverter.ToString(cmdBytes, 0, cmdLength)}");
            WriteBytes(cmdBytes, cmdLength);

            arrayPool.Return(cmdBytes);
        }

        public byte[] ReadRaw(int readOffset)
        {
            Debug.WriteLineIf(LogSerialBytes, $"VIPA-READ: ON PORT={serialPort?.PortName} - READ-OFFSET: {readOffset}");
            return ReadBytes(readOffset);
        }

        public void WriteRaw(byte[] buffer, int length)
        {
            Debug.WriteLineIf(LogSerialBytes, $"VIPA-WRITE: ON PORT={serialPort?.PortName} - {BitConverter.ToString(buffer, 0, length)}");
            WriteBytes(buffer, length);
        }

        [DebuggerNonUserCode]
        private async Task ReadExistingResponseBytes()
        {
            while (!shouldStopReading)
            {
                if (!readingSerialPort && !IsChainedMessageResponse)
                {
                    await Task.Delay(portReadIdleDelayMs);
                    continue;
                }

                byte[] buffer = arrayPool.Rent(unchainedResponseMessageSize);  //Read the whole thing if possible.

                bool moreData = serialPort?.IsOpen ?? false;
                bool firstPacket = true;

                while (moreData && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (serialPort.BytesToRead > 0)
                        {
                            bool parseBytes = true;

                            int readLength = serialPort.Read(buffer, 0, buffer.Length);
                            Debug.WriteLineIf(LogSerialBytes, string.Format("VIPA-READ [{0}](0x{1:X2}) : {2}", serialPort?.PortName, readLength, BitConverter.ToString(buffer, 0, readLength)));
                            //await DebugDump.LoggerWriter(string.Format("VIPA-READ [{0}](0x{1:X2}) : {2}", serialPort?.PortName, readLength, BitConverter.ToString(buffer, 0, readLength)));

                            // examine response for possible chained message response
                            firstPacket = ProcessChainedMessageResponseIfAppropriate(firstPacket, ref buffer, readLength,
                                ref moreData, ref parseBytes);

                            // assemble combined bytes for chained answer response
                            if (parseBytes)
                            {
                                serialParser.BytesRead(buffer, readLength);
                            }
                        }
                        else if (!IsChainedMessageResponse)
                        {
                            moreData = false;
                        }
                    }
                    catch (TimeoutException)
                    {
                        // This is acceptable as the SerialPort library might timeout and recover
                        moreData = false;
                        Debug.WriteLine($"TimedOut VIPA-READ [{serialPort.PortName}]");
                    }
                    // TODO: remove unnecessary catches after POC for multi-device is shakendown
                    catch (InvalidOperationException ioe)
                    {
                        moreData = false;
                        Debug.WriteLine($"Invalid Operation VIPA-READ [{serialPort.PortName}]: {ioe.Message}");
                    }
                    catch (OperationCanceledException oce)
                    {
                        moreData = false;
                        Debug.WriteLine($"Operation Cancelled VIPA-READ [{serialPort.PortName}]: {oce.Message}");
                    }
                    catch (Exception ex)
                    {
                        moreData = false;
                        Debug.WriteLine($"Exception VIPA-READ [{serialPort.PortName}]: {ex.Message}");
                    }
                    finally
                    {
                        arrayPool.Return(buffer);
                    }
                }

                readingSerialPort = false;

                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    serialParser.ReadAndExecute(ResponseTagsHandler, ResponseTaglessHandler, ResponseContactlessHandler, IsChainedMessageResponse);
                    serialParser.SanityCheck();
                }
            }

            readerThreadIsActive = false;
        }

        private byte[] ReadBytes(int readOffset)
        {
            byte[] buffer = new byte[rawReadSizeBytes];

            try
            {
                int length = serialPort.Read(buffer, readOffset, rawReadSizeBytes);
            }
            catch (TimeoutException)
            {
                //We aren't worried about timeouts.  All other exceptions we should allow to throw
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception VIPA-READ [{serialPort.PortName}]: {ex.Message}");
            }

            return buffer;
        }

        private bool ProcessChainedMessageResponseIfAppropriate(bool isFirstPacket, ref byte[] buffer, int readLength,
            ref bool moreData, ref bool parseBytes)
        {
            bool firstPacket = isFirstPacket;

            // examine response for possible chained message response
            if (isFirstPacket)
            {
                firstPacket = false;

                if (!IsChainedMessageResponse)
                {
                    IsChainedMessageResponse = ((buffer[1] & 0x01) == 0x01);
                }
            }

            Debug.WriteLineIf(IsChainedMessageResponse, string.Format("VIPA-READ [{0}] - CHAINED MESSAGE RESPONSE - {1}", serialPort?.PortName, BitConverter.ToString(buffer, 0, readLength)));

            if (IsChainedMessageResponse)
            {
                // SW1-SW2-LRC in trailing edge of data frame
                if (buffer[readLength - 3] == 0x90 && buffer[readLength - 2] == 0x00)
                {
                    // Setup chained-message-response buffer after chained-command response.
                    // This check is necessary as a packet could have been originated from an unsolicited event and incorrectly showing
                    // that chained packets will follow: 01-41-FE...SW1-SW2-LRC - this is a single packet in the sequence.
                    if ((buffer[1] & 0x01) == 0x00)
                    {
                        // chained command answer: expect SW1SW2=0x9000
                        serialParser.BytesRead(buffer, readLength);
                        serialParser.ReadAndExecute(ResponseTagsHandler, ResponseTaglessHandler, ResponseContactlessHandler);
                        serialParser.SanityCheck();
                        parseBytes = false;
                        // grow the buffer as signature payload is large
                        arrayPool.Return(buffer);
                        buffer = arrayPool.Rent(chainedResponseMessageSize);
                    }
                    else
                    {
                        // There's no more data to collect - unchained message response packet collection completed.
                        moreData = false;
                    }
                }
            }

            return firstPacket;
        }

        private void WriteBytes(byte[] msg, int cmdLength)
        {
            try
            {
                lock (portWriteLock)
                {
                    serialPort?.Write(msg, 0, cmdLength);
                }
            }
            catch (TimeoutException)
            {
                //We aren't worried about timeouts.  All other exceptions we should allow to throw
            }
            catch (Exception)
            {

            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!readingSerialPort)
            {
                readingSerialPort = true;

                if (!readerThreadIsActive)
                {
                    lock (readerThreadLock)
                    {
                        if (!readerThreadIsActive)
                        {
                            readerThreadIsActive = true;
                            Task.Run(ReadExistingResponseBytes, cancellationTokenSource.Token);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// PCB for chained command: bit 0 set for all packets, except last packet
        /// 2nd – nth packet : NAD PCB(bit 0 set) LEN Data… LRC
        /// Last packet      : NAD PCB(bit 0 unset) LEN Data… LRC
        /// </summary>
        /// <param name="command"></param>
        /// <param name="pcb"></param>
        /// <param name="packetOffset"></param>
        /// <param name="packetLen"></param>
        private void ProcessNextPacket(VIPACommand command, byte pcb, int packetOffset, byte packetLen)
        {
            byte[] cmdBytes = GetNextPacket(command, pcb, packetOffset, packetLen, out int cmdLength);

            Debug.WriteLineIf(LogSerialBytes, $"VIPA-WRITE[{serialPort?.PortName}][LEN={cmdLength}]: {BitConverter.ToString(cmdBytes, 0, cmdLength)}");
            WriteBytes(cmdBytes, cmdLength);
            arrayPool.Return(cmdBytes);
        }

        /// <summary>
        /// PCB for chained command: bit 0 set for all packets, except last packet
        /// 2nd – nth packet : NAD PCB(bit 0 set) LEN Data… LRC
        /// Last packet      : NAD PCB(bit 0 unset) LEN Data… LRC
        /// </summary>
        /// <param name="command"></param>
        /// <param name="pcb"></param>
        /// <param name="packetOffset"></param>
        /// <param name="packetLen"></param>
        /// <param name="cmdLength"></param>
        internal byte[] GetNextPacket(VIPACommand command, byte pcb, int packetOffset, byte packetLen, out int cmdLength)
        {
            byte[] cmdBytes = arrayPool.Rent(packetLen + 4 /*NAD, PCB, LEN, LRC*/);

            int cmdIndex = 0;
            byte lrc = 0;

            cmdBytes[cmdIndex++] = command.nad;
            lrc ^= command.nad;
            cmdBytes[cmdIndex++] = pcb;
            lrc ^= pcb;

            // data processing: command length
            cmdBytes[cmdIndex++] = packetLen;
            lrc ^= packetLen;

            // data LRC
            byte[] packet = new byte[packetLen];
            Array.Copy(command.data, packetOffset, packet, 0, packetLen);

            foreach (byte byt in packet)
            {
                cmdBytes[cmdIndex++] = byt;
                lrc ^= byt;
            }
            cmdBytes[cmdIndex++] = lrc;
            cmdLength = cmdIndex;

            return cmdBytes;
        }

        /// <summary>
        /// PCB for chained command: bit 0 set for all packets, except last packet
        /// 2nd – nth packet : NAD PCB(bit 0 set) LEN Data… LRC
        /// Last packet      : NAD PCB(bit 0 unset) LEN Data… LRC
        /// </summary>
        /// <param name="command"></param>
        /// <param name="pcb"></param>
        /// <param name="packetOffset"></param>
        /// <param name="packetLen"></param>
        /// <param name="cmdLength"></param>
        internal byte[] GetFirstPacket(VIPACommand command, out int cmdLength)
        {
            // 1st. Packet - special handling
            cmdLength = chainedCommandMinimumLength;
            byte[] cmdBytes = arrayPool.Rent(cmdLength + 4 /*CLA, INS, P1, P2*/);
            int cmdIndex = 0;
            // The Lc byte contains the length of the data field (excluding the Length Expected (Le) byte if present),
            // capped at maximum of 0xFF. Commands without a data field contain no Lc byte
            byte lrc = 0;
            cmdBytes[cmdIndex++] = command.nad;
            lrc ^= command.nad;
            cmdBytes[cmdIndex++] = 0x01;    // PCB for chained command: bit 0 set for all packets, except last packet
            lrc ^= 0x01;
            cmdBytes[cmdIndex++] = chainedCommandMinimumLength;
            lrc ^= chainedCommandMinimumLength;
            cmdBytes[cmdIndex++] = command.cla;
            lrc ^= command.cla;
            cmdBytes[cmdIndex++] = command.ins;
            lrc ^= command.ins;
            cmdBytes[cmdIndex++] = command.p1;
            lrc ^= command.p1;
            cmdBytes[cmdIndex++] = command.p2;
            lrc ^= command.p2;
            // data processing: command length
            cmdBytes[cmdIndex++] = chainedCommandMinimumLength + 1;
            lrc ^= chainedCommandMinimumLength + 1;
            // data LRC

            byte[] packet = new byte[chainedCommandPayloadLength + 1];
            Array.Copy(command.data, 0, packet, 0, chainedCommandPayloadLength + 1);

            foreach (byte byt in packet)
            {
                cmdBytes[cmdIndex++] = byt;
                lrc ^= byt;
            }

            cmdBytes[cmdIndex++] = lrc;
            cmdLength = cmdIndex;

            return cmdBytes;
        }

        // <summary>
        /// Assemble the write request in 1...N packets
        /// 1st. packet      : NAD PCB (bit 0 set) LEN CLA INS P1 P2 Lc Data… LRC
        /// 2nd – nth packet : NAD PCB(bit 0 set) LEN Data… LRC
        /// Last packet      : NAD PCB(bit 0 unset) LEN Data… LRC
        ///
        /// Packet length (LEN) byte
        /// The LEN byte is the length of the packet. It includes the CLA, INS, P1, P2 bytes (but not for subsequent
        /// packets in Chained commands), includes the Lc and data field (if present) bytes, and includes the Le
        /// byte(if present), includes the SW1 - SW2 bytes for responses, but excludes the LRC byte.
        ///
        /// </summary>
        ///
        /// <param name="responsehandlers"></param>
        /// <param name="command"></param>
        public bool WriteChainedCmd(VIPAResponseHandlers responsehandlers, VIPACommand command)
        {
            if (command == null)
            {
                return false;
            }

            int dataLen = command.data?.Length ?? 0;

            // chained command must be of minimum length: it includes the CLA, INS, P1, P2 bytes
            if ((dataLen + packetTxHeaderLength) < chainedCommandMinimumLength)
            {
                return false;
            }

            ResponseTagsHandler = responsehandlers.responsetagshandler;
            ResponseTaglessHandler = responsehandlers.responsetaglesshandler;
            ResponseContactlessHandler = responsehandlers.responsecontactlesshandler;
            byte[] cmdBytes = GetFirstPacket(command, out int cmdLength);
            int packetNumber = 1;

            // chained message response
            IsChainedMessageResponse = IsChainedResponseCommand(command);
            Debug.WriteLineIf(LogSerialBytes, $"VIPA-WRITE[{serialPort?.PortName}][LEN={cmdLength}]: {BitConverter.ToString(cmdBytes, 0, cmdLength)}");

            WriteBytes(cmdBytes, cmdLength);

            arrayPool.Return(cmdBytes);

            // process 2nd...nth packet
            int packetOffset = (chainedCommandPayloadLength + 1) * packetNumber;
            byte remaining = (byte)(command.data.Length - packetOffset);

            // Packet payload is 258 bytes maximum
            while (remaining > chainedCommandMinimumLength)
            {
                ProcessNextPacket(command, 0x01, packetOffset, remaining);
                packetNumber++;
                packetOffset = (chainedCommandPayloadLength + 1) * packetNumber;
                remaining = (byte)(command.data.Length - packetOffset);
            }

            // process last packet
            ProcessNextPacket(command, 0x00, packetOffset, remaining);

            return true;
        }
    }
}
