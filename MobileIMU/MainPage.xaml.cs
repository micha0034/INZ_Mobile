using Android;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobileIMU
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void ButtonStart_Clicked(object sender, EventArgs e)
        {
            Android.App.Application.Context.StartForegroundService(new Intent(Android.App.Application.Context, typeof(ServiceIMU)));
        }

        private void ButtonStop_Clicked(object sender, EventArgs e)
        {
            Android.App.Application.Context.StopService(new Intent(Android.App.Application.Context, typeof(ServiceIMU)));
        }
    }

    [Service]
    public class ServiceIMU : Service
    {
        private CancellationTokenSource stop = null;
        private Task taskWWW = null;
        private HttpListener server = null;
        private NotificationManager manager = null;

        private const string nonImportantChannelName = "constantNameJustAStickIMUDroid", channelId = "ServiceIMUDroid", channelName = "ServiceIMUSrv";

        private readonly float[] gyro = new float[3] { 0, 0, 0 }, acc = new float[3] { 0, 0, 0 };
        private DateTime? lastReading = null;

        public ServiceIMU() : base()
        {
        }

        private void Gyroscope_ReadingChanged(object sender, GyroscopeChangedEventArgs e)
        {
            var data = e.Reading;

            var timeDiff = (DateTime.Now - lastReading).Value.TotalMilliseconds;

            // Process Angular Velocity X, Y, and Z reported in rad/s.
            gyro[0] -= (float)(data.AngularVelocity.Z * 180 / Math.PI * (timeDiff / 1000));
            gyro[1] -= (float)(data.AngularVelocity.Y * 180 / Math.PI * (timeDiff / 1000));
            gyro[2] -= (float)(data.AngularVelocity.X * 180 / Math.PI * (timeDiff / 1000));

            for (int i = 0; i < 3; i++)
            {
                while (gyro[i] > 360) gyro[i] -= 360;
                while (gyro[i] < -360) gyro[i] += 360;
            }

            lastReading = DateTime.Now;
        }

        private void Acc_ReadingChanged(object sender, AccelerometerChangedEventArgs e)
        {
            var data = e.Reading;

            // Accelerometer readings are reported back in G. A G is a unit of gravitation force equal to that exerted by the earth's gravitational field (9.81 m/s^2).
            acc[0] += data.Acceleration.Z;
            acc[1] += data.Acceleration.Y;
            acc[2] += data.Acceleration.X;

            lastReading = DateTime.Now;
        }

        private string ConcatReadings(IEnumerable<float> values) => values.Select(x => x.ToString(CultureInfo.InvariantCulture)).Aggregate((x, y) => x + "/" + y);

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            manager = (NotificationManager)GetSystemService(NotificationService);
            var channel = new NotificationChannel(nonImportantChannelName, channelName, NotificationImportance.None)
            {
                LockscreenVisibility = NotificationVisibility.Private
            };

            var warnings = new NotificationChannel(channelId, channelName, NotificationImportance.High)
            {
                LockscreenVisibility = NotificationVisibility.Private
            };
            manager = (NotificationManager)GetSystemService(NotificationService);
            manager.CreateNotificationChannel(channel);
            manager.CreateNotificationChannel(warnings);
            var notification = new Notification.Builder(this, nonImportantChannelName)
                .SetContentTitle("Just a Stick")
                .SetContentText("Mobile IMU is running.")
                .SetSmallIcon(Resource.Drawable.ButtonRadio)
                .SetOngoing(true)
                .Build();
            StartForeground(1337, notification);

            // This method executes on the main thread of the application.
            Log.Debug("ServiceIMU", "ServiceIMU started...");

            stop?.Cancel();

            stop = new CancellationTokenSource();

            if (!HttpListener.IsSupported)
            {
                Toast.MakeText(this, "WebServer is not supported!", ToastLength.Long).Show();
                stop.Cancel();
                return StartCommandResult.NotSticky;
            }

            server = new HttpListener();
            server.Prefixes.Add("http://*:2137/");
            server.Start();

            taskWWW = new Task(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        var context = await server.GetContextAsync();
                        var response = context.Response;
                        string responseString = ConcatReadings(gyro) + "/" + "0/0/0"/*ConcatReadings(acc)*/ + "/0";

                        switch (context.Request.Url.LocalPath.TrimStart('/')) {
                            case "d":
                                break;
                            case "c":
                            case "a":
                            case "m":
                                responseString = "done";
                                break;
                            case "p":
                                responseString = "0/0/0/0/0/0/0/0/0/0/0/0";
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        var buffer = Encoding.UTF8.GetBytes(responseString);
                        response.ContentLength64 = buffer.Length;
                        var output = response.OutputStream;
                        output.Write(buffer, 0, buffer.Length);

                        output.Close();

                        Log.Debug("ServiceIMU", "WWW loop executed.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ServiceIMU", ex.Message);
                    }
                }
            }, stop.Token);
            taskWWW.Start();

            return StartCommandResult.Sticky;
        }

        public override void OnCreate()
        {
            base.OnCreate();

            try
            {
                if (Gyroscope.IsMonitoring)
                    Gyroscope.Stop();
                Gyroscope.Start(SensorSpeed.Fastest);

                if (Accelerometer.IsMonitoring)
                    Accelerometer.Stop();
                Accelerometer.Start(SensorSpeed.Fastest);

                lastReading = DateTime.Now;
                Gyroscope.ReadingChanged += Gyroscope_ReadingChanged;
                Accelerometer.ReadingChanged += Acc_ReadingChanged;
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, ex.Message, ToastLength.Long).Show();
                OnDestroy();
                return;
            }

            Toast.MakeText(this, "The service has been started.", ToastLength.Long).Show();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            stop?.Cancel();
            server?.Stop();
            if (Gyroscope.IsMonitoring)
                Gyroscope.Stop();
            if (Accelerometer.IsMonitoring)
                Accelerometer.Stop();
            Gyroscope.ReadingChanged -= Gyroscope_ReadingChanged;
            Accelerometer.ReadingChanged -= Acc_ReadingChanged;
            Toast.MakeText(this, "The service has been stopped.", ToastLength.Long).Show();
        }

        public override IBinder OnBind(Intent intent)
        {
            // Started service, NOT a binded service - return null...
            return null;
        }
    }
}
