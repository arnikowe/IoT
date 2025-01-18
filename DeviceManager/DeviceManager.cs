using DeviceSdkDemo.Device;
using Microsoft.Azure.Devices.Client;
using Opc.UaFx.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class DeviceManager
{
    private readonly Dictionary<string, VirtualDevice> devices;
    private readonly string opcServerUrl;

    public DeviceManager(string opcServerUrl)
    {
        this.devices = new Dictionary<string, VirtualDevice>();
        this.opcServerUrl = opcServerUrl;
    }

    // Dodanie urządzenia do managera
    public async Task AddDeviceAsync(string deviceId, string deviceConnectionString)
    {
        var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
        var device = new VirtualDevice(deviceId, deviceClient, opcServerUrl);
        await device.InitializeHandlers();
        devices.Add(deviceId, device);
        Console.WriteLine($"Device {deviceId} added.");
    }

    // Usunięcie urządzenia z managera
    public void RemoveDevice(string deviceId)
    {
        if (devices.ContainsKey(deviceId))
        {
            devices.Remove(deviceId);
            Console.WriteLine($"Device {deviceId} removed.");
        }
        else
        {
            Console.WriteLine($"Device {deviceId} not found.");
        }
    }

    // Wysłanie danych telemetrycznych do wszystkich urządzeń
    public async Task SendTelemetryToAllDevicesAsync()
    {
        foreach (var device in devices.Values)
        {
            await device.ReadTelemetryAndSendToHubAsync();
        }
    }

    // Aktualizacja Device Twin we wszystkich urządzeniach
    public async Task UpdateAllDeviceTwinsAsync()
    {
        foreach (var device in devices.Values)
        {
            await device.UpdateTwinAsync();
        }
    }
}
