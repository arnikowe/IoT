using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Net.Sockets;
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
        private Dictionary<string, object> ReadTelemetryData()
        {
            var telemetryData = new Dictionary<string, object>();

            OpcValue productionStatus = opcClient.ReadNode("ns=2;s=Device 1/ProductionStatus");
            OpcValue productionRate = opcClient.ReadNode("ns=2;s=Device 1/ProductionRate");
            OpcValue temperature = opcClient.ReadNode("ns=2;s=Device 1/Temperature");
            OpcValue goodCount = opcClient.ReadNode("ns=2;s=Device 1/GoodCount");
            OpcValue badCount = opcClient.ReadNode("ns=2;s=Device 1/BadCount");

            telemetryData["ProductionStatus"] = productionStatus.Value;
            telemetryData["ProductionRate"] = productionRate.Value;
            telemetryData["Temperature"] = temperature.Value;
            telemetryData["GoodCount"] = goodCount.Value;
            telemetryData["BadCount"] = badCount.Value;

            return telemetryData;
        }

        #region Sending Messages

        public async Task SendMessages(int nrOfMessages, int delay)
        {
            Console.WriteLine($"Device sending {nrOfMessages} messages to IoTHub...\n");

            for (int count = 0; count < nrOfMessages; count++)
            {
                var telemetryData = ReadTelemetryData();

                var dataString = JsonConvert.SerializeObject(telemetryData);
                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
                eventMessage.ContentType = MediaTypeNames.Application.Json;
                eventMessage.ContentEncoding = "utf-8";

                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Data: [{dataString}]");

                await client.SendEventAsync(eventMessage);

                if (count < nrOfMessages - 1)
                    await Task.Delay(delay);
            }
        }


        #endregion Sending Messages

        #region Receiving Messages

        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
        {
            Console.WriteLine($"\t{DateTime.Now}> C2D message callback - message received with Id={receivedMessage.MessageId}.");
            PrintMessage(receivedMessage);

            await client.CompleteAsync(receivedMessage);
            Console.WriteLine($"\t{DateTime.Now}> Completed C2D message with Id={receivedMessage.MessageId}.");

            receivedMessage.Dispose();
        }

        private void PrintMessage(Message receivedMessage)
        {
            string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
            Console.WriteLine($"\t\tReceived message: {messageData}");

            int propCount = 0;
            foreach (var prop in receivedMessage.Properties)
            {
                Console.WriteLine($"\t\tProperty[{propCount++}> Key={prop.Key} : Value={prop.Value}");
            }
        }

        #endregion Receiving Messages

        #region Direct Methods

        private async Task<MethodResponse> SendMessagesHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");

            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { nrOfMessages = default(int), delay = default(int) });

            await SendMessages(payload.nrOfMessages, payload.delay);

            return new MethodResponse(0);
        }

        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");

            await Task.Delay(1000);

            return new MethodResponse(0);
        }

        #endregion Direct Methods

        #region Device Twin

        public async Task UpdateTwinAsync()
        {
            var twin = await client.GetTwinAsync();

            Console.WriteLine($"\nInitial twin value received: \n{JsonConvert.SerializeObject(twin, Formatting.Indented)}");
            Console.WriteLine();

            var reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastAppLaunch"] = DateTime.Now;

            await client.UpdateReportedPropertiesAsync(reportedProperties);
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine($"\tDesired property change:\n\t{JsonConvert.SerializeObject(desiredProperties)}");
            Console.WriteLine("\tSending current time as reported property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;

            await client.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
        }

        #endregion Device Twin

        public async Task InitializeHandlers()
        {
            await client.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, client);

            await client.SetMethodHandlerAsync("SendMessages", SendMessagesHandler, client);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, client);

            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, client);
        }
    }
}
