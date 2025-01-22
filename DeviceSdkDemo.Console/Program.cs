using DeviceSdkDemo.Device;
using Microsoft.Extensions.Configuration;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("ConnectionStrings.json", optional: false, reloadOnChange: true)
            .Build();

        var iotHubConnectionString = configuration["IoTHubConnectionString"];
        var opcUaServerUrl = configuration["OpcUaServerUrl"];
        var serviceBusConnectionString = configuration["serviceBusConnectionString"];
        var emailConnectionString = configuration["Email:ConnectionString"];
        var senderEmail = configuration["Email:SenderEmail"];
        var recipientEmail = configuration["Email:RecipientEmail"];


        var deviceConfigurations = new[]
        {
            (
                configuration["DeviceConnectionStrings:Device1"],
                opcUaServerUrl,
                "Device 1"
            ),
            (
                configuration["DeviceConnectionStrings:Device2"],
                opcUaServerUrl,
                "Device 2"
            ),
            (
                configuration["DeviceConnectionStrings:Device3"],
                opcUaServerUrl,
                "Device 3"
            )
        };


        var deviceManager = new DeviceManager(deviceConfigurations, serviceBusConnectionString, iotHubConnectionString,emailConnectionString,senderEmail,recipientEmail);
        var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        await deviceManager.RunAsync(cancellationTokenSource.Token);
    }
}
