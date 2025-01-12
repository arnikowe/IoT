using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx;
using Opc.UaFx.Client;
using Newtonsoft.Json.Linq;

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

        private DeviceErrors lastCheckedErrorCode;

        public VirtualDevice(DeviceClient deviceClient, string opcServerUrl)
        {
            this.client = deviceClient;
            this.opcClient = new OpcClient(opcServerUrl);
            this.opcClient.Connect();
            this.lastCheckedErrorCode = DeviceErrors.None; 
        }

        #region OPC UA Node Mapping
        private const string ProductionStatusNode = "ns=2;s=Device 1/ProductionStatus";
        private const string WorkorderIdNode = "ns=2;s=Device 1/WorkorderId";
        private const string ProductionRateNode = "ns=2;s=Device 1/ProductionRate";
        private const string GoodCountNode = "ns=2;s=Device 1/GoodCount";
        private const string BadCountNode = "ns=2;s=Device 1/BadCount";
        private const string TemperatureNode = "ns=2;s=Device 1/Temperature";
        private const string DeviceErrorNode = "ns=2;s=Device 1/DeviceError";
        private const string EmergencyStopNode = "ns=2;s=Device 1/EmergencyStop";
        #endregion

        #region Telemetry Data Reading
        private Dictionary<string, object> ReadTelemetryData()
        {
            var telemetryData = new Dictionary<string, object>
            {
                ["ProductionStatus"] = opcClient.ReadNode(ProductionStatusNode).Value,
                ["WorkorderId"] = opcClient.ReadNode(WorkorderIdNode).Value,
                ["ProductionRate"] = opcClient.ReadNode(ProductionRateNode).Value,
                ["GoodCount"] = opcClient.ReadNode(GoodCountNode).Value,
                ["BadCount"] = opcClient.ReadNode(BadCountNode).Value,
                ["Temperature"] = opcClient.ReadNode(TemperatureNode).Value
            };

            var deviceErrorValue = opcClient.ReadNode(DeviceErrorNode).Value ?? 0;
            telemetryData["DeviceError"] = ((DeviceErrors)Convert.ToInt32(deviceErrorValue)).ToString();

            if ((int)telemetryData["ProductionStatus"] == 0)
            {
                telemetryData["WorkorderId"] = string.Empty;
            }

            return telemetryData;
        }

        public async Task ReadTelemetryAndSendToHubAsync()
        {
            var telemetryData = ReadTelemetryData();

            lastReportedProductionRate = (int)telemetryData["ProductionRate"];
            lastReportedErrorCode = (DeviceErrors)Enum.Parse(typeof(DeviceErrors), telemetryData["DeviceError"].ToString());

            await SendTelemetryDataAsync(telemetryData);

            if (lastReportedErrorCode != DeviceErrors.None)
            {
                await SendDeviceErrorEventAsync(lastReportedErrorCode);
            }

            await UpdateTwinAsync();
        }

        private async Task SendTelemetryDataAsync(Dictionary<string, object> telemetryData)
        {
            var dataString = JsonConvert.SerializeObject(telemetryData);
            var eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            Console.WriteLine($"Sending telemetry: {dataString}");
            await client.SendEventAsync(eventMessage);
        }

        private async Task SendDeviceErrorEventAsync(DeviceErrors error)
        {
            var errorEvent = new { DeviceError = error.ToString() };
            var dataString = JsonConvert.SerializeObject(errorEvent);
            var errorMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            Console.WriteLine($"Sending error event: {dataString}");
            await client.SendEventAsync(errorMessage);
        }

        private async Task MonitorDeviceErrorsAsync()
        {
            while (true) 
            {
                var deviceErrorValue = opcClient.ReadNode(DeviceErrorNode).Value ?? 0;
                var currentErrorCode = (DeviceErrors)Convert.ToInt32(deviceErrorValue);

                if (currentErrorCode != lastCheckedErrorCode)
                {
                  
                    await SendDeviceErrorEventAsync(currentErrorCode);
                    lastCheckedErrorCode = currentErrorCode; 
                }

                await Task.Delay(1000); 
            }
        }


        #endregion

        #region Device Twin Management
        public async Task InitializeHandlers()
        {
            await client.SetReceiveMessageHandlerAsync((message, userContext) =>
            {
                Console.WriteLine("C2D message received.");
                return Task.FromResult(MessageResponse.Completed);
            }, null);

            await client.SetMethodHandlerAsync("EmergencyStop", DeviceErrorHandler, client);
            await client.SetMethodHandlerAsync("ResetErrorStatus", DeviceErrorHandler, client);
            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);
            //await InitializeTwinOnStartAsync();

            _ = MonitorDeviceErrorsAsync();
        }
        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Desired properties update received.");

            bool twinUpdated = false;

            if (desiredProperties.Contains("ProductionRate"))
            {
                int rate = (int)desiredProperties["ProductionRate"];
                opcClient.WriteNode(ProductionRateNode, rate);
                lastReportedProductionRate = rate;
                Console.WriteLine($"ProductionRate updated to: {rate}");
                twinUpdated = true;
            }


            if (twinUpdated)
            {
                var reportedProperties = new TwinCollection
                {
                    ["DeviceError"] = (int)lastReportedErrorCode,
                    ["ProductionRate"] = lastReportedProductionRate
                };

                try
                {
                    await client.UpdateReportedPropertiesAsync(reportedProperties);
                    Console.WriteLine("Device Twin reported properties updated successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating Device Twin: {ex.Message}");
                }
            }
        }
        private async Task<int> ReadDesiredRateIfExistsAsync()
        {
            var twin = await client.GetTwinAsync();
            if (twin.Properties.Desired.Contains("ProductionRate"))
            {
                return (int)twin.Properties.Desired["ProductionRate"];
            }
            return 0; // Domyślna wartość, jeśli nie ma ustawionej wartości w twinie
        }

        private async Task InitializeTwinOnStartAsync()
        {
            int desiredInitialRate = await ReadDesiredRateIfExistsAsync();
            opcClient.WriteNode(ProductionRateNode, desiredInitialRate);
            var initialReportedProperties = new TwinCollection
            {
                ["DeviceError"] = DeviceErrors.None.ToString(),
                ["ProductionRate"] = desiredInitialRate
            };
            await client.UpdateReportedPropertiesAsync(initialReportedProperties);
        }
        private void UpdateLocalState()
        {
            var telemetryData = ReadTelemetryData();
            lastReportedProductionRate = (int)telemetryData["ProductionRate"];
            lastReportedErrorCode = (DeviceErrors)Enum.Parse(typeof(DeviceErrors), telemetryData["DeviceError"].ToString());
            Console.WriteLine($"Local state updated: ProductionRate={lastReportedProductionRate}, DeviceError={lastReportedErrorCode}");
        }
        public async Task UpdateTwinAsync()
        {
            UpdateLocalState();

            var reportedProperties = new TwinCollection
            {
                ["DeviceError"] = lastReportedErrorCode.ToString(),
                ["ProductionRate"] = lastReportedProductionRate
            };

            try
            {
                await client.UpdateReportedPropertiesAsync(reportedProperties);
                Console.WriteLine("Device Twin reported properties updated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Device Twin: {ex.Message}");
            }
        }
        #endregion

        #region Direct Methods
        private async Task<MethodResponse> DeviceErrorHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"Direct Method invoked: {methodRequest.Name}");

            var result = opcClient.CallMethod("ns=2;s=Device 1", $"ns=2;s=Device 1/{methodRequest.Name}");

            if (result != null)
            {
                Console.WriteLine($"{methodRequest.Name} executed successfully.");
            }
            else
            {
                Console.WriteLine($"Failed to execute {methodRequest.Name}.");
            }

            var responsePayload = new { message = $"{methodRequest.Name} executed successfully" };
            string responseJson = JsonConvert.SerializeObject(responsePayload);
            return new MethodResponse(Encoding.UTF8.GetBytes(responseJson), 200);
        }
        #endregion
    }
}
