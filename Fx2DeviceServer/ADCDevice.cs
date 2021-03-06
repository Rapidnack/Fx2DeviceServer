﻿using CyUSB;
using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Fx2DeviceServer
{
    public class ADCDevice : Fx2Device
    {
        private ushort dataPortNo = 0;
        private CyBulkEndPoint endpoint2 = null;

        public ADCDevice(CyUSBDevice usbDevice, MonoUsbProfile usbProfile)
            : base(usbDevice, usbProfile, EDeviceType.ADC)
        {
            byte[] response = ReceiveVendorResponse((byte)EVendorRequests.DeviceParam, 2);
            dataPortNo = (ushort)(response[0] + (response[1] << 8));

            Console.WriteLine($"DeviceAttached: {this}");

            if (USBDevice != null)
            {
                endpoint2 = USBDevice.EndPointOf(0x82) as CyBulkEndPoint;
            }

            var ct = Cts.Token;
            Task.Run(() =>
            {
                UdpClient udp = new UdpClient();
                try
                {
                    int maxPacketSize;
                    if (USBDevice != null)
                    {
                        maxPacketSize = endpoint2.MaxPktSize;
                    }
                    else
                    {
                        maxPacketSize = MonoUsbApi.GetMaxPacketSize(USBProfile.ProfileHandle, 0x82);
                    }
                    byte[] inData = new byte[maxPacketSize];
                    byte[] outData = null;
                    int outDataPos = 0;

                    while (!ct.IsCancellationRequested)
                    {
                        int xferLen = inData.Length;
                        bool ret = false;
                        if (USBDevice != null)
                        {
                            ret = endpoint2.XferData(ref inData, ref xferLen);
                        }
                        else
                        {
                            ret = MonoUsbApi.BulkTransfer(MonoDeviceHandle, 0x82, inData, inData.Length, out xferLen, TIMEOUT) == 0;
                        }
                        if (ret == false)
                            break;

                        int inDataPos = 0;
                        while (!ct.IsCancellationRequested && inDataPos < xferLen)
                        {
                            if (outData == null)
                            {
                                outData = new byte[1472];
                            }
                            while (outDataPos < outData.Length && inDataPos < xferLen)
                            {
                                outData[outDataPos++] = inData[inDataPos++];
                            }

                            if (outDataPos == outData.Length)
                            {
                                udp.Send(outData, outData.Length, "127.0.0.1", dataPortNo);
                                outData = null;
                                outDataPos = 0;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // nothing to do
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{DeviceType}: {ex.Message}");
                }
                finally
                {
                    udp.Close();
                }
            }, ct);
        }

        public override string ToString()
        {
            return $"{DeviceType} {dataPortNo}";
        }
    }
}
