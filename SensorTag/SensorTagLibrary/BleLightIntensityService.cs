using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Security.Cryptography.Certificates;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Windows.Web.Http.Headers;

namespace SensorTag
{
    /// <summary>
    /// This class provides access to the SensorTag light intensity (LUX) BLE data.  The driver for this sensor is using a state machine 
    /// so when the enable command is issued, the sensor starts to perform one measurements and the data is stored. To obtain 
    /// the data either use notifications or read the data directly.  The optical sensor used on the SensorTag is OPT3001 from Texas Instruments. 
    /// </summary>
    public class BleLightIntensityService : BleGenericGattService
    {

        public BleLightIntensityService()
        {
            values.CollectionChanged += OnCollectionChanged;
        }

        /// <summary>
        /// The version of the SensorTag device.  1=CC2541, 2=CC2650.
        /// </summary>
        public int Version { get; set; }

        static Guid LightIntensityServiceUuid = Guid.Parse("f000aa70-0451-4000-b000-000000000000");
        static Guid LightIntensityCharacteristicUuid = Guid.Parse("f000aa71-0451-4000-b000-000000000000");
        static Guid LightIntensityCharacteristicConfigUuid = Guid.Parse("f000aa72-0451-4000-b000-000000000000");
        static Guid LightIntensityCharacteristicPeriodUuid = Guid.Parse("f000aa73-0451-4000-b000-000000000000");

        static public ObservableCollection<Tuple<DateTimeOffset, Double>> values = new ObservableCollection<Tuple<DateTimeOffset, double>>();

        Delegate _lightValueChanged;

        public event EventHandler<LightIntensityMeasurementEventArgs> LightMeasurementValueChanged
        {
            add
            {
                if (_lightValueChanged != null)
                {
                    _lightValueChanged = Delegate.Combine(_lightValueChanged, value);
                }
                else
                {
                    _lightValueChanged = value;
                    RegisterForValueChangeEvents(LightIntensityCharacteristicUuid);
                }
            }
            remove
            {
                if (_lightValueChanged != null)
                {
                    _lightValueChanged = Delegate.Remove(_lightValueChanged, value);
                    if (_lightValueChanged == null)
                    {
                        UnregisterForValueChangeEvents(LightIntensityCharacteristicUuid);
                    }
                }
            }
        }

        private async Task<int> GetConfig()
        {
            var ch = GetCharacteristic(LightIntensityCharacteristicConfigUuid);
            if (ch != null)
            {
                var properties = ch.CharacteristicProperties;

                if ((properties & GattCharacteristicProperties.Read) != 0)
                {
                    byte value = await ReadCharacteristicByte(LightIntensityCharacteristicConfigUuid, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
                    Debug.WriteLine("Light intensity config = " + value);
                    return (int)value;
                }
            }
            return -1;
        }

        bool isReading;

        public async Task StartReading()
        {
            if (!isReading)
            {
                await WriteCharacteristicByte(LightIntensityCharacteristicConfigUuid, 1);
                isReading = true;
            }
        }

        public async Task StopReading()
        {
            if (isReading)
            {
                isReading = false;
                await WriteCharacteristicByte(LightIntensityCharacteristicConfigUuid, 0);
            }
        }

        /// <summary>
        /// Get the rate at which sensor is being polled, in milliseconds.  
        /// </summary>
        /// <returns>Returns the value read from the sensor or -1 if something goes wrong.</returns>
        public async Task<int> GetPeriod()
        {
            byte v = await ReadCharacteristicByte(LightIntensityCharacteristicPeriodUuid, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
            return (int)(v * 10);
        }

        /// <summary>
        /// Set the rate at which sensor is being polled.
        /// The period ranges for 100 ms to 2.55 seconds, resolution 10 ms.
        /// </summary>
        /// <param name="milliseconds">The delay between updates, accurate only to 10ms intervals. </param>
        public async Task SetPeriod(int milliseconds)
        {

            int delay = milliseconds / 10;
            if (delay < 0)
            {
                delay = 1;
            }
            await WriteCharacteristicByte(LightIntensityCharacteristicPeriodUuid, (byte)delay);
        }

        private void OnLightIntensityMeasurementValueChanged(LightIntensityMeasurementEventArgs args)
        {
            if (_lightValueChanged != null)
            {
                ((EventHandler<LightIntensityMeasurementEventArgs>)_lightValueChanged)(this, args);
            }
        }

        public async Task<bool> ConnectAsync(string deviceContainerId)
        {
            if (Version == 1)
            {
                throw new NotSupportedException();
            }
            return await this.ConnectAsync(LightIntensityServiceUuid, deviceContainerId);
        }


        public static string CSVfromValues(ObservableCollection<Tuple<DateTimeOffset, Double>> values)
        {
            string header = "#40:11,lux";
            string body = "";
            foreach (var value in values)
            {
                var line = value.Item1.ToString("yyyyMMddHHmmssf") + "," + value.Item2.ToString();
                body += Environment.NewLine + line;
            }
            return header + body;
        }
        public static void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && values.Count > 5)
            {
                var csv = CSVfromValues(values);
                values.Clear();
                SendHttpsDataPUT(csv);
            }
        }


        static async void SendHttpsDataPUT(string data)
        {
            var _url = "https://JLEFEBVRENEW:5486/connectordata/sensortag/";
            var httpBaseProtocolFilter = new HttpBaseProtocolFilter();
            httpBaseProtocolFilter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
            using (var client = new HttpClient(httpBaseProtocolFilter))
            {
                var byteArray = Encoding.UTF8.GetBytes("a:a");
                client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Basic", Convert.ToBase64String(byteArray));

                var stringContent = new HttpStringContent(data);
                var response = await client.PutAsync(new Uri(_url), stringContent);
                var a = await response.Content.ReadAsStringAsync();
            }
        }

        protected override void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            if (sender.Uuid == LightIntensityCharacteristicUuid)
            {
                if (_lightValueChanged != null)
                {
                    uint dataLength = eventArgs.CharacteristicValue.Length;
                    using (DataReader reader = DataReader.FromBuffer(eventArgs.CharacteristicValue))
                    {
                        if (dataLength == 2)
                        {
                            ushort rawData = ReadBigEndianU16bit(reader);

                            uint m = (uint)(rawData & 0x0FFF);
                            uint e = ((uint)(rawData & 0xF000)) >> 12;

                            double lux = (double)m * (0.01 * Math.Pow(2.0, e));

                            var measurement = new LightIntensityMeasurement();
                            measurement.Lux = lux;

                            values.Add(new Tuple<DateTimeOffset, double>(eventArgs.Timestamp, lux));
                            //SendHttpsDataPUT(data);
                            OnLightIntensityMeasurementValueChanged(new LightIntensityMeasurementEventArgs(measurement, eventArgs.Timestamp));
                        }
                    }
                }
            }
        }
    }


    public class LightIntensityMeasurement
    {
        /// <summary>
        /// Light intensity (LUX). See https://en.wikipedia.org/wiki/Lux
        /// </summary>
        public double Lux { get; set; }

        public LightIntensityMeasurement()
        {
        }

    }

    public class LightIntensityMeasurementEventArgs : EventArgs
    {
        public LightIntensityMeasurementEventArgs(LightIntensityMeasurement measurement, DateTimeOffset timestamp)
        {
            Measurement = measurement;
            Timestamp = timestamp;
        }

        public LightIntensityMeasurement Measurement
        {
            get;
            private set;
        }

        public DateTimeOffset Timestamp
        {
            get;
            private set;
        }
    }

}
