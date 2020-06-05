using CyUSB;
using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Threading;

namespace Fx2DeviceServer
{
    public class Fx2Device : IDisposable
    {
        public enum EDeviceType
        {
            Unknown = 0,
            DAC = 1,
            ADC = 2,
        }

		public enum EVendorRequests
		{
			DeviceType = 0xc0,
			DeviceParam = 0xc1,
		}

        protected const int TIMEOUT = 3000;

        protected EDeviceType DeviceType { get; private set; } = EDeviceType.Unknown;
        protected CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        protected MonoUsbDeviceHandle MonoDeviceHandle { get; private set; } = null;

        public CyUSBDevice USBDevice { get; private set; } = null;

        private MonoUsbProfile _usbProfile = null;
        public MonoUsbProfile USBProfile
        {
            get
            {
                return _usbProfile;
            }
            set
            {
                _usbProfile = value;
                MonoDeviceHandle = _usbProfile.OpenDeviceHandle();
            }
        }

        public Fx2Device(CyUSBDevice usbDevice, MonoUsbProfile usbProfile, EDeviceType deviceType = EDeviceType.Unknown)
        {
            if (usbDevice != null)
            {
                USBDevice = usbDevice;
            }
            if (usbProfile != null)
            {
                USBProfile = usbProfile;
            }
            DeviceType = deviceType;

            if (deviceType == EDeviceType.Unknown)
            {
                Console.WriteLine($"DeviceAttached: {this}");
            }
        }

        private bool disposed = false;
        public void Dispose()
        {
            Dispose(true);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (Cts != null)
                    {
                        Cts.Cancel();
                    }
                    Console.WriteLine($"DeviceRemoved: {this}");
                }

                disposed = true;
            }
        }

        public override string ToString()
        {
            return $"{DeviceType}";
        }

        public static byte[] ReceiveVendorResponse(CyUSBDevice usbDevice, MonoUsbDeviceHandle monoDeviceHandle, byte reqCode, int length, ushort value = 0, ushort index = 0)
        {
            if (usbDevice != null)
            {
                CyControlEndPoint ctrlEpt = usbDevice.ControlEndPt;
                ctrlEpt.TimeOut = TIMEOUT;
                ctrlEpt.Direction = CyConst.DIR_FROM_DEVICE;
                ctrlEpt.ReqType = CyConst.REQ_VENDOR;
                ctrlEpt.Target = CyConst.TGT_DEVICE;
                ctrlEpt.ReqCode = reqCode;
                ctrlEpt.Value = value;
                ctrlEpt.Index = index;

                int bytes = length;
                byte[] buffer = new byte[bytes];
                ctrlEpt.XferData(ref buffer, ref bytes);
                if (bytes == buffer.Length)
                {
                    return buffer;
                }
            }
            else
            {
                short bytes = (short)length;
                byte[] data = new byte[bytes];
                byte requestType = CyConst.DIR_FROM_DEVICE + CyConst.REQ_VENDOR + CyConst.TGT_DEVICE;
                int ret = MonoUsbApi.ControlTransfer(monoDeviceHandle, requestType, reqCode, (short)value, (short)index, data, bytes, TIMEOUT);
                if (ret == data.Length)
                {
                    return data;
                }
            }

            return null;
        }

        public static bool SendVendorRequest(CyUSBDevice usbDevice, MonoUsbDeviceHandle monoDeviceHandle, byte reqCode, byte[] data, ushort value = 0, ushort index = 0)
        {
            if (data == null)
            {
                data = new byte[0];
            }

            if (usbDevice != null)
            {
                CyControlEndPoint ctrlEpt = usbDevice.ControlEndPt;
                ctrlEpt.TimeOut = TIMEOUT;
                ctrlEpt.Direction = CyConst.DIR_TO_DEVICE;
                ctrlEpt.ReqType = CyConst.REQ_VENDOR;
                ctrlEpt.Target = CyConst.TGT_DEVICE;
                ctrlEpt.ReqCode = reqCode;
                ctrlEpt.Value = value;
                ctrlEpt.Index = index;

                int bytes = data.Length;
                ctrlEpt.XferData(ref data, ref bytes);
                return bytes == data.Length;
            }
            else
            {
                short bytes = (short)data.Length;
                byte requestType = CyConst.DIR_TO_DEVICE + CyConst.REQ_VENDOR + CyConst.TGT_DEVICE;
                int ret = MonoUsbApi.ControlTransfer(monoDeviceHandle, requestType, reqCode, (short)value, (short)index, data, bytes, TIMEOUT);
                return ret == data.Length;
            }
        }

        protected byte[] ReceiveVendorResponse(byte reqCode, int length, ushort value = 0, ushort index = 0)
        {
            return ReceiveVendorResponse(USBDevice, MonoDeviceHandle, reqCode, length, value, index);
        }

        protected bool SendVendorRequest(byte reqCode, byte[] data, ushort value = 0, ushort index = 0)
        {
            return SendVendorRequest(USBDevice, MonoDeviceHandle, reqCode, data, value, index);
        }
    }
}
