using DeviceSdkDemo.Device;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices;
using Opc.UaFx.Client;

string deviceConnectionString = ConnectionStrings.deviceConnectionString;
string opcServerUrl = ConnectionStrings.OpcUaServerUrl;

// Utworzenie klienta IoT Hub
using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
var device = new VirtualDevice(deviceClient, opcServerUrl);

// Inicjalizacja handlerów
await device.InitializeHandlers();
Console.WriteLine("Handlers initialized.");

// Uruchomienie nasłuchiwania w tle
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(1000); // Regularne opóźnienie, aby uniknąć zbyt częstego odpytywania
    }
});

while (true)
{
    Console.WriteLine("\nWybierz opcję:");
    Console.WriteLine("1. Wyślij dane telemetryczne");
    Console.WriteLine("2. Wykonaj metodę EmergencyStop");
    Console.WriteLine("3. Zaktualizuj Device Twin");
    Console.WriteLine("0. Wyjdź");

    var choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await device.ReadTelemetryAndSendToHubAsync();
            Console.WriteLine("Telemetry sent.");
            break;
        case "2":
            var method = new CloudToDeviceMethod("EmergencyStop");
            var serviceClient = ServiceClient.CreateFromConnectionString(ConnectionStrings.IoTHubConnectionString);
            var methodResponse = await serviceClient.InvokeDeviceMethodAsync("DeviceDemoSdk", method);
            Console.WriteLine($"EmergencyStop response: {methodResponse.Status}");
            break;
        case "3":
            await device.UpdateTwinAsync();
            Console.WriteLine("Device Twin updated.");
            break;
        case "0":
            Console.WriteLine("Exiting program.");
            return;
        default:
            Console.WriteLine("Nieprawidłowy wybór, spróbuj ponownie.");
            break;
    }
}
