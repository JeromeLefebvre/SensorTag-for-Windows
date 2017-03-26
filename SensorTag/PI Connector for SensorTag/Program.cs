using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Configuration;

/*
 * TODO: Add a timer to re-configure the sensor tags once every few minutes
 * TODO: Support multiple sensortags
 * 
 */
namespace SensorTag
{
    class Program
    {
        static string uriAddress = ConfigurationManager.AppSettings["UFLDataSource"];
        static string user = "a";
        static string password = "a";
        static private string timeformat = "yyyyMMddHHmmssf";

        public static ObservableCollection<Tuple<String, DateTimeOffset, Double>> values = new ObservableCollection<Tuple<String, DateTimeOffset, double>>();


        public static List<string> connectedDevice = new List<string>() { };

        static void Main(string[] args)
        {

            values.CollectionChanged += OnCollectionChanged;

            while (true)
            {
                configureSensorTags();
                Console.WriteLine("looking for new sensors");
                Thread.Sleep(100000);
            }
        }

        public static void configureSensorTags()
        {
            var sensortags = SensorTag.FindAllDevices();
            sensortags.Wait();
            foreach (var sensor in sensortags.Result)
            {
                if (connectedDevice.Contains(sensor.DeviceAddress))
                    continue;
                Console.WriteLine($"About to connect to {sensor.DeviceAddress}");
                var connect = sensor.ConnectAsync();

                connectedDevice.Add(sensor.DeviceAddress);
                connect.Wait();
                SensorTag.SelectedSensor = sensor;
                Console.WriteLine($"Configuring the light sensor for {sensor.DeviceAddress}");
                sensor.LightIntensity.SetPeriod(250).Wait();
                sensor.LightIntensity.mac = sensor.DeviceAddress;

                sensor.LightIntensity.StartReading().Wait();
                sensor.LightIntensity.LightMeasurementValueChanged += OnLightMeasurementValueChanged;

                Console.WriteLine("Configuring the Motion sensors");
                sensor.Movement.SetPeriod(100).Wait();
                sensor.Movement.mac = sensor.DeviceAddress;
                sensor.Movement.StartReading(MovementFlags.Mag | MovementFlags.GyroX | MovementFlags.GyroY | MovementFlags.GyroZ).Wait();
                sensor.Movement.MovementMeasurementValueChanged += OnMovementMeasurementValueChanged;

            }
        }

        public static string CSVfromValues(ObservableCollection<Tuple<String, DateTimeOffset, Double>> values)
        {
            string body = "";

            lock (values)
            {
                foreach (var value in values)
                {
                    var line = value.Item1 + "," + value.Item2.ToString(timeformat) + "," + value.Item3.ToString();
                    body += line + Environment.NewLine;
                }
            }
            return body;
        }
        public static void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            lock (values)
            {
                if (e.Action == NotifyCollectionChangedAction.Add && values.Count > 5)
                {
                    var csv = CSVfromValues(values);
                    values.Clear();
                    SendHttpsDataPUT(csv);
                }
            }
        }

        static void SendHttpsDataPUT(string data)
        {
            byte[] fileToSend = Encoding.UTF8.GetBytes(data);

            System.Net.ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(
            (object sender, X509Certificate certification, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; });
            using (var wb = new WebClient())
            {
                string svcCredentials = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(user + ":" + password));
                wb.Headers[HttpRequestHeader.Authorization] = string.Format("Basic {0}", svcCredentials);
                try
                {
                    var response = wb.UploadData(uriAddress, "PUT", fileToSend);
                    string sResponse = Encoding.ASCII.GetString(response);
                    //Console.WriteLine(DateTime.Now.ToString("h:mm:ss tt"), sResponse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
        static public void OnLightMeasurementValueChanged(object sender, LightIntensityMeasurementEventArgs e)
        {
            lock (values)
            {
                values.Add(new Tuple<string, DateTimeOffset, double>($"{e.Mac}.lux", e.Timestamp, e.Measurement.Lux));
            }
        }

        static public void OnMovementMeasurementValueChanged(object sender, MovementEventArgs e)
        {
            lock (values)
            {
                values.Add(new Tuple<string, DateTimeOffset, double>($"{e.Measurement.mac}.GyroX", e.Timestamp, e.Measurement.GyroX));
                values.Add(new Tuple<string, DateTimeOffset, double>($"{e.Measurement.mac}.GyroY", e.Timestamp, e.Measurement.GyroY));
                values.Add(new Tuple<string, DateTimeOffset, double>($"{e.Measurement.mac}.GyroZ", e.Timestamp, e.Measurement.GyroZ));
            }
        }
    }
}
