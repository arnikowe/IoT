using DeviceSdkDemo.Device;
using Microsoft.Azure.Devices.Client;

string deviceConnectionString = "HostName=WMII.azure-devices.net;DeviceId=DeviceDemoSdk;SharedAccessKey=OKqg+bAc7sU4i1RoUoxdrsslHhak3fOmCfaKEsshX9I=";
string opcServerUrl = "opc.tcp://localhost:4840/";

using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
var device = new VirtualDevice(deviceClient, opcServerUrl);

await device.InitializeHandlers();
await device.UpdateTwinAsync();

Console.WriteLine($"Connection success!");

await device.SendMessages(10, 1000);

Console.WriteLine("Finished! Press Enter to close...");
Console.ReadLine();
