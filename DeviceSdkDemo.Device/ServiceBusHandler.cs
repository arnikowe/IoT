using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeviceSdkDemo.Device
{
    public class ServiceBusHandler
    {
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceClient _serviceClient;
        private readonly RegistryManager _registryManager;
        private readonly ServiceBusProcessor _productionProcessor;
        private readonly ServiceBusProcessor _errorsProcessor;

        public ServiceBusHandler(string serviceBusConnectionString, string iotHubConnectionString)
        {
            _serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
            _serviceClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString);
            _registryManager = RegistryManager.CreateFromConnectionString(iotHubConnectionString);

            var productionKpiQueueName = "productionkpiqueue";
            var deviceErrorsQueueName = "deviceerrorsqueue";

            _productionProcessor = _serviceBusClient.CreateProcessor(productionKpiQueueName);
            _errorsProcessor = _serviceBusClient.CreateProcessor(deviceErrorsQueueName);

            _productionProcessor.ProcessMessageAsync += HandleProductionMessageAsync;
            _productionProcessor.ProcessErrorAsync += ProcessErrorAsync;

            _errorsProcessor.ProcessMessageAsync += HandleErrorMessageAsync;
            _errorsProcessor.ProcessErrorAsync += ProcessErrorAsync;
        }

        public async Task StartProcessingAsync()
        {
            await _productionProcessor.StartProcessingAsync();
            await _errorsProcessor.StartProcessingAsync();
        }

        public async Task StopProcessingAsync()
        {
            await _productionProcessor.StopProcessingAsync();
            await _errorsProcessor.StopProcessingAsync();
        }

        private async Task HandleProductionMessageAsync(ProcessMessageEventArgs args)
        {
            try
            {
                var messageBody = args.Message.Body.ToString();
                var productionData = JsonSerializer.Deserialize<ProductionData>(messageBody);

                if (productionData.PercentGoodProduction < 90)
                {
                    await DecreaseProductionRateAsync(productionData.DeviceId);
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing production message: {ex.Message}");
            }
        }

        private async Task HandleErrorMessageAsync(ProcessMessageEventArgs args)
        {
            try
            {
                var messageBody = args.Message.Body.ToString();
                var errorData = JsonSerializer.Deserialize<ErrorData>(messageBody);

                if (errorData.OccuredErrors > 3)
                {
                    await TriggerEmergencyStopAsync(errorData.DeviceId);
                }

                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing error message: {ex.Message}");
            }
        }

        private async Task DecreaseProductionRateAsync(string deviceId)
        {
            try
            {
                var twin = await _registryManager.GetTwinAsync(deviceId);
                var currentRate = (int)twin.Properties.Reported["ProductionRate"];

                if (currentRate >= 10)
                {
                    var patch = new Twin();
                    patch.Properties.Desired["ProductionRate"] = currentRate - 10;

                    await _registryManager.UpdateTwinAsync(deviceId, patch, twin.ETag);
                    Console.WriteLine($"Production rate decreased for {deviceId}.");
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
                Console.WriteLine($"EmergencyStop triggered for {deviceId}. Response: {response.Status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error triggering EmergencyStop for {deviceId}: {ex.Message}");
            }
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            Console.WriteLine($"Service Bus Processor Error: {args.Exception.Message}");
            return Task.CompletedTask;
        }

        // Definicje klas danych
        public class ProductionData
        {
            public string DeviceId { get; set; }
            public float PercentGoodProduction { get; set; }
        }

        public class ErrorData
        {
            public string DeviceId { get; set; }
            public int OccuredErrors { get; set; }
        }
    }
}
