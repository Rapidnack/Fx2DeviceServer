using CyUSB;
using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Fx2DeviceServer
{
    public partial class Form1 : Form
    {
        private USBDeviceList usbDeviceList = null;
        private MonoUsbProfileList profileList = null;
        private CancellationTokenSource cts = null;
        private List<Fx2Device> fx2Devices = new List<Fx2Device>();

        // The first time the Session property is used it creates a new session
        // handle instance in '__sessionHandle' and returns it. Subsequent 
        // request simply return '__sessionHandle'.
        private static MonoUsbSessionHandle __sessionHandle;
        public static MonoUsbSessionHandle Session
        {
            get
            {
                if (ReferenceEquals(__sessionHandle, null))
                    __sessionHandle = new MonoUsbSessionHandle();
                return __sessionHandle;
            }
        }

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FileVersionInfo ver = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            this.Text = $"FX2 Device Server {ver.ProductMajorPart}.{ver.ProductMinorPart}.{ver.ProductPrivatePart}";

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    p.PriorityClass = ProcessPriorityClass.RealTime;
                }

                usbDeviceList = new USBDeviceList(CyConst.DEVICES_CYUSB);
                usbDeviceList.DeviceAttached += (s, evt) => SetDevice();
                usbDeviceList.DeviceRemoved += (s, evt) => SetDevice();

                SetDevice();
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    using (Process p = Process.GetCurrentProcess())
                    {
                        p.PriorityClass = ProcessPriorityClass.RealTime;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Usage: sudo mono UsbDeviceServer.exe");
                    return;
                }

                int numDevices = -1;

                // Initialize the context.
                if (Session.IsInvalid)
                    throw new Exception("Failed to initialize context.");

                MonoUsbApi.SetDebug(Session, 0);
                // Create a MonoUsbProfileList instance.
                profileList = new MonoUsbProfileList();

                cts = new CancellationTokenSource();
                var ct = cts.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            // The list is initially empty.
                            // Each time refresh is called the list contents are updated. 
                            int ret = profileList.Refresh(Session);
                            if (ret < 0) throw new Exception("Failed to retrieve device list.");

                            if (numDevices != ret)
                            {
                                numDevices = ret;
                                Console.WriteLine($"{numDevices} device(s) found.");
                                MonoSetDevice();
                            }

                            await Task.Delay(1000, ct);
                        }
                    }
                    finally
                    {
                        // Since profile list, profiles, and sessions use safe handles the
                        // code below is not required but it is considered good programming
                        // to explicitly free and close these handle when they are no longer
                        // in-use.
                        profileList.Close();
                        Session.Close();
                    }
                }, ct);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var fx2Device in fx2Devices)
            {
                fx2Device.Dispose();
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (usbDeviceList != null)
                {
                    usbDeviceList.Dispose();
                }
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                if (cts != null)
                {
                    cts.Cancel();
                }
            }
        }

        private void SetDevice()
        {
            // convert USBDeviceList to List<>
            List<CyUSBDevice> usbDevices = new List<CyUSBDevice>();
            foreach (CyUSBDevice usbDevice in usbDeviceList)
            {
                usbDevices.Add(usbDevice);
            }

            // DeviceRemoved
            foreach (var fx2Device in fx2Devices.ToArray())
            {
                if (!usbDevices.Contains(fx2Device.USBDevice))
                {
                    fx2Device.Dispose();
                    fx2Devices.Remove(fx2Device);
                }
            }

            // DeviceAttached
            foreach (CyUSBDevice usbDevice in usbDeviceList)
            {
                if (usbDevice.VendorID == 0x04b4 && usbDevice.ProductID == 0x1004)
                {
                    if (fx2Devices.Count(p => p.USBDevice == usbDevice) == 0)
                    {
                        byte[] response = Fx2Device.ReceiveVendorResponse(usbDevice, null, Fx2Device.VendorDeviceType, 1);
                        if (response == null)
                        {
                            fx2Devices.Add(new Fx2Device(usbDevice, null));
                        }
                        else
                        {
                            switch ((Fx2Device.EDeviceType)response[0])
                            {
                                case Fx2Device.EDeviceType.DAC: fx2Devices.Add(new DACDevice(usbDevice, null)); break;
                                case Fx2Device.EDeviceType.ADC: fx2Devices.Add(new ADCDevice(usbDevice, null)); break;

                                default: fx2Devices.Add(new Fx2Device(usbDevice, null)); break;
                            }
                        }
                    }
                }
            }
        }

        private void MonoSetDevice()
        {
            // convert MonoUsbProfileList to List<>
            List<MonoUsbProfile> usbProfiles = new List<MonoUsbProfile>();
            foreach (MonoUsbProfile usbProfile in profileList)
            {
                usbProfiles.Add(usbProfile);
            }

            // DeviceRemoved
            foreach (var fx2Device in fx2Devices.ToArray())
            {
                if (!usbProfiles.Contains(fx2Device.USBProfile))
                {
                    fx2Device.Dispose();
                    fx2Devices.Remove(fx2Device);
                }
            }

            // DeviceAttached
            foreach (MonoUsbProfile usbProfile in profileList)
            {
                if (usbProfile.DeviceDescriptor.VendorID == 0x04b4 && usbProfile.DeviceDescriptor.ProductID == 0x1004)
                {
                    if (fx2Devices.Count(p => p.USBProfile == usbProfile) == 0)
                    {
                        byte[] response;
                        using (var monoDeviceHandle = usbProfile.OpenDeviceHandle())
                        {
                            response = Fx2Device.ReceiveVendorResponse(null, monoDeviceHandle, Fx2Device.VendorDeviceType, 1);
                        }
                        if (response == null)
                        {
                            fx2Devices.Add(new Fx2Device(null, usbProfile));
                        }
                        else
                        {
                            switch ((Fx2Device.EDeviceType)response[0])
                            {
                                case Fx2Device.EDeviceType.DAC: fx2Devices.Add(new DACDevice(null, usbProfile)); break;
                                case Fx2Device.EDeviceType.ADC: fx2Devices.Add(new ADCDevice(null, usbProfile)); break;

                                default: fx2Devices.Add(new Fx2Device(null, usbProfile)); break;
                            }
                        }
                    }
                }
            }
        }
    }
}
