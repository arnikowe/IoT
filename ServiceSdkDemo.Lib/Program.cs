using Microsoft.Azure.Devices;
using Newtonsoft.Json;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Devices;
using Newtonsoft.Json;
namespace ServiceSdkDemo.Lib
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            string serviceBusConnectionString = "<ServiceBusConnectionString>";
            string iotHubConnectionString = "<IoTHubConnectionString>";
            string queueName = "<QueueName>";

            ServiceBusClient serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
            ServiceBusProcessor processor = serviceBusClient.CreateProcessor(queueName);

            processor.ProcessMessageAsync += async (args) =>
            {
                var messageBody = args.Message.Body.ToString();
                var data = JsonConvert.DeserializeObject<dynamic>(messageBody);

                if (data.Errors > 3)
                {
                    var serviceClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString);
                    var method = new CloudToDeviceMethod("EmergencyStop");
                    await serviceClient.InvokeDeviceMethodAsync(data.DeviceId, method);
                    Console.WriteLine("Emergency stop triggered for device: " + data.DeviceId);
                }

                await args.CompleteMessageAsync(args.Message);
            };

            processor.ProcessErrorAsync += async (args) =>
            {
                Console.WriteLine("Error: " + args.Exception.Message);
            };

            await processor.StartProcessingAsync();

            Console.WriteLine("Press Enter to stop.");
            Console.ReadLine();

            await processor.StopProcessingAsync();
        }
    }
}
