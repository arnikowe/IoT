using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx;
using Opc.UaFx.Client;

namespace DeviceSdkDemo.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient client;
        private OpcClient opcClient;

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
            return new Dictionary<string, object>
            {
                ["ProductionStatus"] = opcClient.ReadNode(ProductionStatusNode).Value,
                ["WorkorderId"] = opcClient.ReadNode(WorkorderIdNode).Value,
                ["ProductionRate"] = opcClient.ReadNode(ProductionRateNode).Value,
                ["GoodCount"] = opcClient.ReadNode(GoodCountNode).Value,
                ["BadCount"] = opcClient.ReadNode(BadCountNode).Value,
                ["Temperature"] = opcClient.ReadNode(TemperatureNode).Value
            };
        }
        #endregion

        #region Sending Messages

        public async Task SendMessages(int nrOfMessages, int delay)
        {
            for (int count = 0; count < nrOfMessages; count++)
            {
                var telemetryData = ReadTelemetryData();
                var dataString = JsonConvert.SerializeObject(telemetryData);
                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
                {
                    ContentType = MediaTypeNames.Application.Json,
                    ContentEncoding = "utf-8"
                };

                Console.WriteLine($"Sending message: {count}, Data: {dataString}");
                await client.SendEventAsync(eventMessage);
                await Task.Delay(delay);
            }
        }

        #endregion

        #region Receiving Messages (C2D)
        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
        {
            Console.WriteLine($"Received C2D message with Id={receivedMessage.MessageId}.");
            string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
            Console.WriteLine($"Message Data: {messageData}");
            await client.CompleteAsync(receivedMessage);
        }
        #endregion

        #region Direct Methods

        private async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Emergency Stop triggered.");
            opcClient.WriteNode(EmergencyStopNode, true);
            return new MethodResponse(Encoding.UTF8.GetBytes("{\"status\":\"Emergency Stop activated\"}"), 200);
        }

        private async Task<MethodResponse> ResetErrorStatusHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Reset Error Status triggered.");
            opcClient.WriteNode(DeviceErrorNode, 0); // Reset flag
            return new MethodResponse(Encoding.UTF8.GetBytes("{\"status\":\"Error Status Reset\"}"), 200);
        }

        #endregion

        #region Device Twin Management

        public async Task UpdateTwinAsync()
        {
            var twin = await client.GetTwinAsync();
            Console.WriteLine($"Initial Twin Properties: {JsonConvert.SerializeObject(twin, Formatting.Indented)}");

            var reportedProperties = new TwinCollection
            {
                ["ProductionRate"] = opcClient.ReadNode(ProductionRateNode).Value,
                ["DeviceError"] = opcClient.ReadNode(DeviceErrorNode).Value
            };

            await client.UpdateReportedPropertiesAsync(reportedProperties);
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine($"Desired property change received: {JsonConvert.SerializeObject(desiredProperties)}");

            if (desiredProperties.Contains("ProductionRate"))
            {
                var desiredRate = (int)desiredProperties["ProductionRate"];
                opcClient.WriteNode(ProductionRateNode, desiredRate);
                Console.WriteLine($"ProductionRate updated to: {desiredRate}");

                var reportedProperties = new TwinCollection
                {
                    ["ProductionRate"] = desiredRate
                };

                await client.UpdateReportedPropertiesAsync(reportedProperties);
            }
        }

        #endregion

        #region Handlers Initialization

        public async Task InitializeHandlers()
        {
            await client.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, null);
            await client.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, null);
            await client.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, null);
            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);
        }

        #endregion
    }
}
