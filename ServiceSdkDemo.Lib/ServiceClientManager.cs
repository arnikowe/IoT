using Microsoft.Azure.Devices;
using Newtonsoft.Json;
using System.Text;

namespace ServiceSdkDemo.Lib
{
    public class ServiceClientManager
    {
        private readonly ServiceClient serviceClient;

        public ServiceClientManager(string connectionString)
        {
            serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
        }

        public async Task SendCloudToDeviceMessageAsync(string deviceId, string messageText)
        {
            var messageBody = new { text = messageText };
            var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(messageBody)));
            message.MessageId = Guid.NewGuid().ToString();
            await serviceClient.SendAsync(deviceId, message);
        }

        public async Task<int> InvokeDirectMethodAsync(string deviceId, string methodName, object payload)
        {
            var method = new CloudToDeviceMethod(methodName)
            {
                ResponseTimeout = TimeSpan.FromSeconds(30)
            };
            method.SetPayloadJson(JsonConvert.SerializeObject(payload));

            var result = await serviceClient.InvokeDeviceMethodAsync(deviceId, method);
            return result.Status;
        }

        public async Task CloseAsync()
        {
            await serviceClient.CloseAsync();
        }
    }
}
