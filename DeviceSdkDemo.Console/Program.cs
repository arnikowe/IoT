using DeviceSdkDemo.Device;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices;
using TransportType = Microsoft.Azure.Devices.TransportType;

string deviceConnectionString = "HostName=WMII.azure-devices.net;DeviceId=DeviceDemoSdk;SharedAccessKey=OKqg+bAc7sU4i1RoUoxdrsslHhak3fOmCfaKEsshX9I=";
string opcServerUrl = "opc.tcp://localhost:4840/";

// Utworzenie klienta IoT Hub
using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, (Microsoft.Azure.Devices.Client.TransportType)TransportType.Amqp);
var device = new VirtualDevice(deviceClient, opcServerUrl);

// Inicjalizacja handlerów
await device.InitializeHandlers();
Console.WriteLine("Handlers initialized.");

// Sprawdzenie aktualizacji stanu urządzenia Twin
await device.ReadTelemetryAndSendToHubAsync();
Console.WriteLine("Initial telemetry sent.");

// Wysłanie testowych wiadomości telemetrycznych
Console.WriteLine("Sending test telemetry messages...");
for (int i = 0; i < 5; i++)
{
    await device.ReadTelemetryAndSendToHubAsync();
    await Task.Delay(1000);
}

// Testowanie metody bezpośredniej EmergencyStop
/*var method = new CloudToDeviceMethod("EmergencyStop");
var serviceClient = ServiceClient.CreateFromConnectionString(deviceConnectionString);
var methodResponse = await serviceClient.InvokeDeviceMethodAsync("DeviceDemoSdk", method);
Console.WriteLine($"EmergencyStop response: {methodResponse.Status}");*/

// Testowanie aktualizacji właściwości urządzenia
await device.UpdateTwinAsync();
Console.WriteLine("Device Twin updated.");

Console.WriteLine("Test completed. Press Enter to exit.");
Console.ReadLine();
