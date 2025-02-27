﻿using Azure;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using System.Text.Json;

namespace DeviceSdkDemo.Device
{
    public class ServiceBusHandler
    {
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceClient _serviceClient;
        private readonly RegistryManager _registryManager;
        private readonly ServiceBusProcessor _productionProcessor;
        private readonly ServiceBusProcessor _errorsProcessor;
        private readonly EmailNotificationService _emailNotificationService;

        public ServiceBusHandler(string serviceBusConnectionString, string iotHubConnectionString, string emailConnectionString, string senderEmail, string recipientEmail)
        {
            _serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
            _serviceClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString);
            _registryManager = RegistryManager.CreateFromConnectionString(iotHubConnectionString);

            _emailNotificationService = new EmailNotificationService(emailConnectionString, senderEmail, recipientEmail);

            _productionProcessor = _serviceBusClient.CreateProcessor("productionkpiqueue");
            _errorsProcessor = _serviceBusClient.CreateProcessor("deviceerrorsqueue");

            _productionProcessor.ProcessMessageAsync += HandleProductionMessageAsync;
            _productionProcessor.ProcessErrorAsync += ProcessErrorAsync;

            _errorsProcessor.ProcessMessageAsync += HandleErrorMessageAsync;
            _errorsProcessor.ProcessErrorAsync += ProcessErrorAsync;
        }

        public async Task StartProcessingAsync()
        {
            await ClearQueueAsync("productionkpiqueue");
            await ClearQueueAsync("deviceerrorsqueue");

            await _productionProcessor.StartProcessingAsync();
            await _errorsProcessor.StartProcessingAsync();
        }

        public async Task StopProcessingAsync()
        {
            await _productionProcessor.StopProcessingAsync();
            await _errorsProcessor.StopProcessingAsync();
        }

        public async Task ClearQueueAsync(string queueName)
        {
            try
            {
                var receiver = _serviceBusClient.CreateReceiver(queueName);
                while (true)
                {
                    var messages = await receiver.ReceiveMessagesAsync(maxMessages: 10, maxWaitTime: TimeSpan.FromSeconds(1));
                    if (messages.Count == 0) break;

                    foreach (var message in messages)
                    {
                        await receiver.CompleteMessageAsync(message);
                    }
                }

                await receiver.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing queue {queueName}: {ex.Message}");
            }
        }

        private async Task HandleProductionMessageAsync(ProcessMessageEventArgs args)
        {
            try
            {
                var messageBody = args.Message.Body.ToString();
                var productionData = JsonSerializer.Deserialize<ProductionData>(messageBody);

                if (productionData.PercentGoodProduction < 90)
                {
                    Console.WriteLine($"Good production rate below threshold for device {productionData.DeviceId}. Decreasing desired production rate.");
                    await DecreaseProductionRateAsync(productionData.DeviceId);
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError processing production message: {ex.Message}\n");
            }
        }

        private async Task HandleErrorMessageAsync(ProcessMessageEventArgs args)
        {
            try
            {
                var messageBody = args.Message.Body.ToString();

                var errorData = JsonSerializer.Deserialize<ErrorData>(messageBody);


                if (errorData.ErrorCount > 3)
                {
                    Console.WriteLine($"Error count exceeds threshold. Triggering Emergency Stop for Device ID: {errorData.DeviceId}.");
                    await TriggerEmergencyStopAsync(errorData.DeviceId);
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError processing error message: {ex.Message}\nMessage: {args.Message.Body}");
            }
        }



        private async Task DecreaseProductionRateAsync(string deviceId)
        {
            try
            {
                var twin = await _registryManager.GetTwinAsync(deviceId);

                if (twin.Properties.Reported.Contains("DeviceError"))
                {
                    string deviceError = twin.Properties.Reported["DeviceError"];
                    if (deviceError == "EmergencyStop")
                    {
                        Console.WriteLine($"Device {deviceId} is in Emergency Stop state. Skipping production rate decrease.");
                        return;
                    }
                }

                var currentRate = (int)twin.Properties.Reported["ProductionRate"];
                if (currentRate >= 10)
                {
                    var patch = new Twin();
                    patch.Properties.Desired["ProductionRate"] = currentRate - 10;

                    await _registryManager.UpdateTwinAsync(deviceId, patch, twin.ETag);
                    Console.WriteLine($"Production rate decreased for {deviceId}. New desired rate: {currentRate - 10}%.");
                }
                else
                {
                    Console.WriteLine($"Production rate for device {deviceId} is already at minimum. Skipping decrease.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decreasing production rate for {deviceId}: {ex.Message}");
            }
        }

        private async Task TriggerEmergencyStopAsync(string deviceId)
        {
            try
            {
                var methodInvocation = new CloudToDeviceMethod("EmergencyStop")
                {
                    ResponseTimeout = TimeSpan.FromSeconds(30)
                };

                var response = await _serviceClient.InvokeDeviceMethodAsync(deviceId, methodInvocation);

                Console.WriteLine($"EmergencyStop invoked for {deviceId}. Response: {response.Status}");

                try
                {
                    await _emailNotificationService.SendErrorNotificationAsync(deviceId, "EmergencyStop");
                }
                catch (RequestFailedException emailEx)
                {
                    Console.WriteLine($"Error sending email notification for device {deviceId}: {emailEx.Message}");
                }

                await ClearQueueAsync("deviceerrorsqueue");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error invoking EmergencyStop for {deviceId}: {ex.Message}");
            }
        }


        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            Console.WriteLine($"Service Bus Processor Error: {args.Exception.Message}");
            return Task.CompletedTask;
        }

        public class ProductionData
        {
            public string DeviceId { get; set; }
            public float PercentGoodProduction { get; set; }
        }

        public class ErrorData
        {
            public string DeviceId { get; set; }
            public int ErrorCount { get; set; }
        }
    }
}
