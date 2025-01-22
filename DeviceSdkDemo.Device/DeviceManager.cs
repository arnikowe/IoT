using Microsoft.Azure.Devices.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeviceSdkDemo.Device
{
    public class DeviceManager
    {
        private readonly List<VirtualDevice> _devices;
        private readonly ServiceBusHandler _serviceBusHandler;

        public DeviceManager(IEnumerable<(string connectionString, string opcServerUrl, string deviceNodePrefix)> deviceConfigurations,
                     string serviceBusConnectionString, string iotHubConnectionString,
                     string emailConnectionString, string senderEmail, string recipientEmail)
        {
            _devices = new List<VirtualDevice>();
            _serviceBusHandler = new ServiceBusHandler(serviceBusConnectionString, iotHubConnectionString, emailConnectionString, senderEmail, recipientEmail);

            var emailNotificationService = new EmailNotificationService(emailConnectionString, senderEmail, recipientEmail);

            foreach (var config in deviceConfigurations)
            {
                var deviceClient = DeviceClient.CreateFromConnectionString(config.connectionString);
                _devices.Add(new VirtualDevice(deviceClient, config.opcServerUrl, config.deviceNodePrefix,
                                               emailNotificationService, _serviceBusHandler));
            }
        }



        public async Task RunAsync(CancellationToken cancellationToken)
        {


            // Start processing Service Bus messages
            await _serviceBusHandler.StartProcessingAsync();

            List<Task> tasks = new List<Task>();

            foreach (var device in _devices)
            {
                tasks.Add(Task.Run(async () => await HandleDeviceAsync(device, cancellationToken)));
            }

            await Task.WhenAll(tasks);

            // Stop processing Service Bus messages
            await _serviceBusHandler.StopProcessingAsync();
        }

        private async Task HandleDeviceAsync(VirtualDevice device, CancellationToken cancellationToken)
        {

            await device.InitializeHandlers();
            Console.WriteLine($"Handlers initialized for device {device}.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await device.ReadTelemetryAndSendToHubAsync();
                    await Task.Delay(5000, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in device {device}: {ex.Message}");
                }
            }
        }
    }
}
