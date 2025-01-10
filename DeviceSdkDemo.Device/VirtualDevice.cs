using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx;
using Opc.UaFx.Client;

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

        public VirtualDevice(DeviceClient deviceClient, string opcServerUrl)
        {
            this.client = deviceClient;
            this.opcClient = new OpcClient(opcServerUrl);
            this.opcClient.Connect();
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

            return telemetryData;
        }

        public async Task ReadTelemetryAndSendToHubAsync()
        {
            var telemetryData = ReadTelemetryData();

            lastReportedProductionRate = (int)telemetryData["ProductionRate"];
            lastReportedErrorCode = (DeviceErrors)Enum.Parse(typeof(DeviceErrors), telemetryData["DeviceError"].ToString());

            var dataString = JsonConvert.SerializeObject(telemetryData);
            Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            Console.WriteLine($"Sending telemetry: {dataString}");
            await client.SendEventAsync(eventMessage);

            await UpdateTwinAsync();
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

            await client.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, null);
            await client.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, null);

            // Poprawne nasłuchiwanie na zmiany w Device Twin
            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);
            await InitializeTwinOnStartAsync();
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
            UpdateLocalState(); // Upewnienie się, że stan jest aktualny przed aktualizacją Device Twin

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

        private async Task<int> ReadDesiredRateIfExistsAsync()
        {
            var desired = await client.GetTwinAsync();
            return desired.Properties.Desired.Contains("ProductionRate")
                ? (int)desired.Properties.Desired["ProductionRate"]
                : 0;
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

    if (desiredProperties.Contains("DeviceError"))
    {
        try
        {
            // Obsługa wartości binarnych dla DeviceErrors
            string errorString = desiredProperties["DeviceError"].ToString();
            var errors = errorString.Split(',')
                .Select(e => (DeviceErrors)Enum.Parse(typeof(DeviceErrors), e.Trim()))
                .Aggregate(DeviceErrors.None, (current, next) => current | next);

            int binaryErrorValue = (int)errors;  // Konwersja na wartość binarną
            lastReportedErrorCode = errors;
            opcClient.WriteNode(DeviceErrorNode, binaryErrorValue);  // Zapisanie jako wartość binarna
            Console.WriteLine($"DeviceError updated to binary value: {binaryErrorValue}");
            twinUpdated = true;

            // Wyślij komunikat D2C po zmianie błędu
            var errorMessage = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                DeviceError = binaryErrorValue
            })))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };
            await client.SendEventAsync(errorMessage);
            Console.WriteLine("Sent D2C message for DeviceError change.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing DeviceError: {ex.Message}");
        }
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



        #endregion

        #region Direct Methods
        private async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
        {
            opcClient.WriteNode(EmergencyStopNode, true);
            return new MethodResponse(Encoding.UTF8.GetBytes("{\"status\":\"Emergency Stop activated\"}"), 200);
        }

        private async Task<MethodResponse> ResetErrorStatusHandler(MethodRequest methodRequest, object userContext)
        {
            opcClient.WriteNode(DeviceErrorNode, 0);
            return new MethodResponse(Encoding.UTF8.GetBytes("{\"status\":\"Error Status Reset\"}"), 200);
        }
        #endregion
    }
}
