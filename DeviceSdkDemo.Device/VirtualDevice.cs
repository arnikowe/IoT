using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx.Client;
using Opc.Ua;

namespace DeviceSdkDemo.Device
{
    [Flags]
    public enum DeviceErrors
    {
        None = 0,
        EmergencyStop = 1,
        PowerFailure = 2,
        SensorFailure = 4,
        Unknown = 8
    }
    public class VirtualDevice
    {
        private readonly DeviceClient client;
        private readonly OpcClient opcClient;

        private DeviceErrors lastReportedErrorCode;
        private int lastReportedProductionRate;

        private readonly string deviceNodePrefix;
        private readonly EmailNotificationService _emailNotificationService;

        private int lastErrorCount = 0;
        private readonly ServiceBusHandler _serviceBusHandler;

        private DeviceErrors previousReportedErrorCode = DeviceErrors.None;
        private int previousReportedProductionRate;


        public VirtualDevice(DeviceClient deviceClient, string opcServerUrl, string deviceNodePrefix,
                             EmailNotificationService emailNotificationService, ServiceBusHandler serviceBusHandler)
        {
            client = deviceClient;
            opcClient = new OpcClient(opcServerUrl);
            opcClient.Connect();
            this.deviceNodePrefix = deviceNodePrefix;
            _emailNotificationService = emailNotificationService;
            _serviceBusHandler = serviceBusHandler;
            _ = InitializePreviousProductionRateAsync();
        }





        #region Telemetry Data Reading
        private Dictionary<string, object> ReadTelemetryData()
        {
            var telemetryData = new Dictionary<string, object>();

            try
            {
                telemetryData["ProductionStatus"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionStatus")?.Value ?? 0;
                telemetryData["WorkorderId"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/WorkorderId")?.Value ?? string.Empty;
                telemetryData["GoodCount"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/GoodCount")?.Value ?? 0;
                telemetryData["BadCount"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/BadCount")?.Value ?? 0;
                telemetryData["Temperature"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/Temperature")?.Value ?? 0.0;


                if ((int)telemetryData["ProductionStatus"] == 0)
                {
                    telemetryData["WorkorderId"] = string.Empty;
                }
            }
            catch 
            {
                telemetryData["ProductionStatus"] = 0;
                telemetryData["WorkorderId"] = string.Empty;
                telemetryData["GoodCount"] = 0;
                telemetryData["BadCount"] = 0;
                telemetryData["Temperature"] = 0.0;
            }

            return telemetryData;
        }
        private DeviceErrors ReadDeviceError()
        {
            try
            {
                var deviceErrorValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/DeviceError")?.Value ?? 0;
                return (DeviceErrors)Convert.ToInt32(deviceErrorValue);
            }
            catch 
            {
                return DeviceErrors.Unknown; 
            }
        }
        private int ReadProductionRate()
        {
            try
            {
                var productionRateValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionRate")?.Value ?? 0;
                return Convert.ToInt32(productionRateValue);
            }
            catch
            {
                return previousReportedProductionRate; 
            }
        }

        private async Task InitializePreviousProductionRateAsync()
        {
            var twin = await client.GetTwinAsync();
            if (twin.Properties.Reported.Contains("ProductionRate"))
            {
                previousReportedProductionRate = (int)twin.Properties.Reported["ProductionRate"];
            }
            else
            {
                previousReportedProductionRate = 0;
            }
        }


        public async Task ReadTelemetryAndSendToHubAsync()
        {
            var telemetryData = ReadTelemetryData(); 
            await SendTelemetryDataAsync(telemetryData);
            
            var currentError = ReadDeviceError();
            await HandleErrorEvents(currentError);

            var productionRate = ReadProductionRate();
            await UpdateTwinAsync(productionRate);
        }


        private async Task HandleErrorEvents(DeviceErrors currentError)
        {
            int currentErrorCount = Enum.GetValues(typeof(DeviceErrors))
                .Cast<DeviceErrors>()
                .Where(error => error != DeviceErrors.None && (currentError & error) != 0)
                .Count();

          
            if (currentErrorCount > lastErrorCount)
            {
                await SendDeviceErrorEventAsync(currentError);
            }
       

            lastErrorCount = currentErrorCount;
            lastReportedErrorCode = currentError;
        }



        private async Task SendTelemetryDataAsync(Dictionary<string, object> telemetryData)
        {
            var dataString = JsonConvert.SerializeObject(telemetryData);

            Console.WriteLine($"Telemetry from Device {deviceNodePrefix}: {dataString}");
            var eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            try
            {
                await client.SendEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending telemetry: {ex.Message}");
            }
        }


        private async Task SendDeviceErrorEventAsync(DeviceErrors newError)
        {
            var activeErrors = Enum.GetValues(typeof(DeviceErrors))
                .Cast<DeviceErrors>()
                .Where(error => error != DeviceErrors.None && (newError & error) != 0)
                .Select(error => error.ToString())
                .ToList();

            var errorEvent = new
            {
                DeviceError = activeErrors, 
                newErrors = activeErrors.Count
            };

            var dataString = JsonConvert.SerializeObject(errorEvent);
            var errorMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            Console.WriteLine($"Sending error event: {dataString} from Device {deviceNodePrefix}");
            try
            {
                await client.SendEventAsync(errorMessage);
                UpdateTwinAsync(ReadProductionRate());
                foreach (var error in activeErrors)
                {
                    await _emailNotificationService.SendErrorNotificationAsync(deviceNodePrefix, error);
                    
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending error event: {ex.Message}");
            }
        }

        #endregion

        #region Device Twin Management
        public async Task InitializeHandlers()
        {
            await client.SetReceiveMessageHandlerAsync((message, userContext) =>
            {
                return Task.FromResult(MessageResponse.Completed);
            }, null);

            await client.SetMethodHandlerAsync("EmergencyStop", DeviceErrorHandler, client);
            await client.SetMethodHandlerAsync("ResetErrorStatus", DeviceErrorHandler, client);
            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, client);

        }
        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
           
            bool twinUpdated = false;
            var reportedProperties = new TwinCollection();

            if (desiredProperties.Contains("ProductionRate"))
            {
                int desiredRate = (int)desiredProperties["ProductionRate"];
                opcClient.WriteNode($"ns=2;s={deviceNodePrefix}/ProductionRate", desiredRate);
                lastReportedProductionRate = desiredRate;

                reportedProperties["ProductionRate"] = desiredRate;
                twinUpdated = true;
                Console.WriteLine($"ProductionRate updated to: {desiredRate} on device {deviceNodePrefix}");
            }

            if (twinUpdated)
            {
                try
                {
                    await client.UpdateReportedPropertiesAsync(reportedProperties);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating Device Twin OnDesiredPropertyChanged: {ex.Message}");
                }
            }
        }


        public async Task UpdateTwinAsync(int productionRate)
        {
            var updatedProperties = new TwinCollection();

            var activeErrors = Enum.GetValues(typeof(DeviceErrors))
                .Cast<DeviceErrors>()
                .Where(error => error != DeviceErrors.None && (lastReportedErrorCode & error) != 0)
                .Select(error => error.ToString())
                .ToList();

            var newDeviceErrorState = activeErrors.Count > 0
                ? string.Join(", ", activeErrors)
                : "None";

            if (newDeviceErrorState != previousReportedErrorCode.ToString())
            {
                updatedProperties["DeviceError"] = newDeviceErrorState;
                previousReportedErrorCode = lastReportedErrorCode; 
            }

            if (Math.Abs(productionRate - previousReportedProductionRate) > 0)
            {
                updatedProperties["ProductionRate"] = productionRate;
                previousReportedProductionRate = productionRate;
            }


            if (updatedProperties.Count > 0)
            {
                try
                {
                    await client.UpdateReportedPropertiesAsync(updatedProperties);
                    Console.WriteLine($"Device {deviceNodePrefix}:Device Twin updated: {JsonConvert.SerializeObject(updatedProperties)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating Device Twin: {ex.Message}");
                }
            }
            
        }



        #endregion

        #region Direct Methods
        private async Task<MethodResponse> DeviceErrorHandler(MethodRequest methodRequest, object userContext)
        {

            var result = opcClient.CallMethod($"ns=2;s={deviceNodePrefix}", $"ns=2;s={deviceNodePrefix}/{methodRequest.Name}");


            if (result != null)
            {
                Console.WriteLine($"{methodRequest.Name} executed successfully on Device {deviceNodePrefix}.");
            }
            else
            {
                Console.WriteLine($"Failed to execute {methodRequest.Name}.");
            }
             await _serviceBusHandler.ClearQueueAsync("deviceerrorsqueue");
            var responsePayload = new { message = $"{methodRequest.Name} executed successfully" };
            string responseJson = JsonConvert.SerializeObject(responsePayload);
            return new MethodResponse(Encoding.UTF8.GetBytes(responseJson), 200);
        }
        private async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($" Unknown method executed: {methodRequest.Name} on {deviceNodePrefix}");
            return new MethodResponse(0);
        }


        #endregion
    }
}