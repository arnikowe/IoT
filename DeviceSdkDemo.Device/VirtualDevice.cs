using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx;
using Opc.UaFx.Client;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;

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
        private readonly string deviceNodePrefix;

        public VirtualDevice(DeviceClient deviceClient, string opcServerUrl, string deviceNodePrefix)
        {
            client = deviceClient;
            opcClient = new OpcClient(opcServerUrl);
            opcClient.Connect();
            this.deviceNodePrefix = deviceNodePrefix;
        }



        #region Telemetry Data Reading
        private Dictionary<string, object> ReadTelemetryData()
        {
            var telemetryData = new Dictionary<string, object>();

            try
            {
                telemetryData["ProductionStatus"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionStatus")?.Value ?? 0;
                telemetryData["WorkorderId"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/WorkorderId")?.Value ?? string.Empty;
                telemetryData["ProductionRate"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionRate")?.Value ?? 0;
                telemetryData["GoodCount"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/GoodCount")?.Value ?? 0;
                telemetryData["BadCount"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/BadCount")?.Value ?? 0;
                telemetryData["Temperature"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/Temperature")?.Value ?? 0.0;

                var deviceErrorValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/DeviceError")?.Value ?? 0;
                telemetryData["DeviceError"] = ((DeviceErrors)Convert.ToInt32(deviceErrorValue)).ToString();

                if ((int)telemetryData["ProductionStatus"] == 0)
                {
                    telemetryData["WorkorderId"] = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading telemetry data: {ex.Message}");
                // Assign default values in case of failure
                telemetryData["ProductionStatus"] = 0;
                telemetryData["WorkorderId"] = string.Empty;
                telemetryData["ProductionRate"] = 0;
                telemetryData["GoodCount"] = 0;
                telemetryData["BadCount"] = 0;
                telemetryData["Temperature"] = 0.0;
                telemetryData["DeviceError"] = DeviceErrors.Unknown.ToString();
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

            Console.WriteLine($"Telemetry JSON: {dataString}");
            var eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            try
            {
                Console.WriteLine("Sending telemetry...");
                await client.SendEventAsync(eventMessage);
                Console.WriteLine("Telemetry sent successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending telemetry: {ex.Message}");
            }
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
                var deviceErrorValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/DeviceError").Value ?? 0;
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
                opcClient.WriteNode($"ns=2;s={deviceNodePrefix}/ProductionRate", rate);
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
            opcClient.WriteNode($"ns=2;s={deviceNodePrefix}/ProductionRate", desiredInitialRate);
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

            var result = opcClient.CallMethod($"ns=2;s={deviceNodePrefix}", $"ns=2;s={deviceNodePrefix}/{methodRequest.Name}");

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
