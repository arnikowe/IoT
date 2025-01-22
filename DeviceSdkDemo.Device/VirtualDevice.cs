using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Opc.UaFx;
using Opc.UaFx.Client;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;

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

        private DeviceErrors lastCheckedErrorCode;
        private readonly string deviceNodePrefix;
        private readonly EmailNotificationService _emailNotificationService;

        private int lastErrorCount = 0;
        private DateTime _lastResetTime = DateTime.MinValue;
        private readonly ServiceBusHandler _serviceBusHandler;

        private DeviceErrors previousReportedErrorCode = DeviceErrors.None;
        private int previousReportedProductionRate = 0;


        public VirtualDevice(DeviceClient deviceClient, string opcServerUrl, string deviceNodePrefix,
                             EmailNotificationService emailNotificationService, ServiceBusHandler serviceBusHandler)
        {
            client = deviceClient;
            opcClient = new OpcClient(opcServerUrl);
            opcClient.Connect();
            this.deviceNodePrefix = deviceNodePrefix;
            _emailNotificationService = emailNotificationService;
            _serviceBusHandler = serviceBusHandler;
        }





        #region Telemetry Data Reading
        private Dictionary<string, object> ReadTelemetryData()
        {
            var telemetryData = new Dictionary<string, object>();

            try
            {
                telemetryData["ProductionStatus"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionStatus")?.Value ?? 0;
                telemetryData["WorkorderId"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/WorkorderId")?.Value ?? string.Empty;
                //telemetryData["ProductionRate"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionRate")?.Value ?? 0;
                telemetryData["GoodCount"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/GoodCount")?.Value ?? 0;
                telemetryData["BadCount"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/BadCount")?.Value ?? 0;
                telemetryData["Temperature"] = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/Temperature")?.Value ?? 0.0;

                //var deviceErrorValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/DeviceError")?.Value ?? 0;
                //telemetryData["DeviceError"] = ((DeviceErrors)Convert.ToInt32(deviceErrorValue)).ToString();

                if ((int)telemetryData["ProductionStatus"] == 0)
                {
                    telemetryData["WorkorderId"] = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading telemetry data: {ex.Message}");
                // Assign default values in case of failure
                telemetryData["ProductionStatus"] = 0;
                telemetryData["WorkorderId"] = string.Empty;
                //telemetryData["ProductionRate"] = 0;
                telemetryData["GoodCount"] = 0;
                telemetryData["BadCount"] = 0;
                telemetryData["Temperature"] = 0.0;
                //telemetryData["DeviceError"] = DeviceErrors.Unknown.ToString();
            }

            return telemetryData;
        }
        private DeviceErrors ReadDeviceError()
        {
            try
            {
                // Odczyt wartości błędu z OPC UA
                var deviceErrorValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/DeviceError")?.Value ?? 0;
                return (DeviceErrors)Convert.ToInt32(deviceErrorValue);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading DeviceError: {ex.Message}");
                return DeviceErrors.Unknown; // Domyślna wartość w przypadku błędu
            }
        }
        private int ReadProductionRate()
        {
            try
            {
                // Odczyt wartości ProductionRate z OPC UA
                var productionRateValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/ProductionRate")?.Value ?? 0;
                return Convert.ToInt32(productionRateValue);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading ProductionRate: {ex.Message}");
                return previousReportedProductionRate; // Zwróć poprzednią wartość w przypadku błędu
            }
        }



        public async Task ReadTelemetryAndSendToHubAsync()
        {
            var telemetryData = ReadTelemetryData(); // Odczyt danych telemetrycznych
            await SendTelemetryDataAsync(telemetryData);
            // Osobny odczyt DeviceError
            var currentError = ReadDeviceError();
            var productionRate = ReadProductionRate();
            // Wysyłanie telemetrii (bez DeviceError)
            

            // Obsługa zdarzeń błędów
            await HandleErrorEvents(currentError);

            // Aktualizacja Device Twin
            await UpdateTwinAsync(productionRate);
        }


        private async Task HandleErrorEvents(DeviceErrors currentError)
        {
            // Liczba aktywnych błędów
            int currentErrorCount = Enum.GetValues(typeof(DeviceErrors))
                .Cast<DeviceErrors>()
                .Where(error => error != DeviceErrors.None && (currentError & error) != 0)
                .Count();

            // Aktualizuj Device Twin tylko raz
            await UpdateTwinAsync(lastReportedProductionRate);

            // Jeśli liczba błędów wzrosła, wyślij zdarzenie
            if (currentErrorCount > lastErrorCount)
            {
                Console.WriteLine($"Error count increased. Sending error event.");
                await SendDeviceErrorEventAsync(currentError);
            }
            else if (currentErrorCount < lastErrorCount)
            {
                Console.WriteLine($"Error count decreased (from {lastErrorCount} to {currentErrorCount}).");
            }

            // Aktualizacja liczby błędów
            lastErrorCount = currentErrorCount;

            // Aktualizacja `lastReportedErrorCode` na podstawie aktualnych błędów
            lastReportedErrorCode = currentError;
        }






        private async Task SendTelemetryDataAsync(Dictionary<string, object> telemetryData)
        {
            var dataString = JsonConvert.SerializeObject(telemetryData);

            Console.WriteLine($"Telemetry JSON: {dataString}");
            var eventMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            try
            {
                await client.SendEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending telemetry: {ex.Message}");
            }
        }


        private async Task SendDeviceErrorEventAsync(DeviceErrors newError)
        {
            // Wyodrębnij nazwy aktywnych błędów jako listę
            var activeErrors = Enum.GetValues(typeof(DeviceErrors))
                .Cast<DeviceErrors>()
                .Where(error => error != DeviceErrors.None && (newError & error) != 0)
                .Select(error => error.ToString())
                .ToList();

            var errorEvent = new
            {
                DeviceError = activeErrors, // Lista błędów
                newErrors = activeErrors.Count
            };

            var dataString = JsonConvert.SerializeObject(errorEvent);
            var errorMessage = new Message(Encoding.UTF8.GetBytes(dataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            Console.WriteLine($"Sending error event: {dataString}");
            try
            {
                await client.SendEventAsync(errorMessage);
                Console.WriteLine("Error event sent successfully.");

                // Wyślij powiadomienie e-mail dla błędów
                foreach (var error in activeErrors)
                {
                    await _emailNotificationService.SendErrorNotificationAsync(deviceNodePrefix, error);
                    Console.WriteLine($"Error notification sent for: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending error event: {ex.Message}");
            }
        }




        /* private async Task MonitorDeviceErrorsAsync()
         {
             while (true)
             {
                 var deviceErrorValue = opcClient.ReadNode($"ns=2;s={deviceNodePrefix}/DeviceError").Value ?? 0;
                 var currentErrorCode = (DeviceErrors)Convert.ToInt32(deviceErrorValue);

                 // Sprawdzamy tylko zmiany w stanie błędów
                 if (currentErrorCode != lastCheckedErrorCode)
                 {
                     if (currentErrorCode > DeviceErrors.None)
                     {
                         await SendDeviceErrorEventAsync(currentErrorCode);
                     }
                     else
                     {
                         Console.WriteLine("Device errors cleared. No further action required.");
                     }

                     lastCheckedErrorCode = currentErrorCode;
                 }

                 await Task.Delay(1000);
             }
         }*/



        #endregion

        #region Device Twin Management
        public async Task InitializeHandlers()
        {
            await client.SetReceiveMessageHandlerAsync((message, userContext) =>
            {
                Console.WriteLine("C2D message received.");
                return Task.FromResult(MessageResponse.Completed);
            }, null);

            await client.SetMethodHandlerAsync("EmergencyStop", DeviceErrorHandler, client);
            await client.SetMethodHandlerAsync("ResetErrorStatus", DeviceErrorHandler, client);
            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);
            //await InitializeTwinOnStartAsync();

            //_ = MonitorDeviceErrorsAsync();
        }
        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Desired properties update received.");

            bool twinUpdated = false;
            var reportedProperties = new TwinCollection();

            // Obsługa zmiany ProductionRate
            if (desiredProperties.Contains("ProductionRate"))
            {
                int desiredRate = (int)desiredProperties["ProductionRate"];
                opcClient.WriteNode($"ns=2;s={deviceNodePrefix}/ProductionRate", desiredRate);
                lastReportedProductionRate = desiredRate;

                reportedProperties["ProductionRate"] = desiredRate;
                twinUpdated = true;
                Console.WriteLine($"ProductionRate updated to: {desiredRate}");
            }

            if (twinUpdated)
            {
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

        private async Task<int> ReadDesiredRateIfExistsAsync()
        {
            var twin = await client.GetTwinAsync();
            if (twin.Properties.Desired.Contains("ProductionRate"))
            {
                return (int)twin.Properties.Desired["ProductionRate"];
            }
            return 0; // Domyślna wartość, jeśli nie ma ustawionej wartości w twinie
        }

        private async Task InitializeTwinOnStartAsync()
        {
            int desiredInitialRate = await ReadDesiredRateIfExistsAsync();
            opcClient.WriteNode($"ns=2;s={deviceNodePrefix}/ProductionRate", desiredInitialRate);

            // Pobierz istniejący ProductionRate z Device Twin
            var twin = await client.GetTwinAsync();
            if (twin.Properties.Reported.Contains("ProductionRate"))
            {
                previousReportedProductionRate = (int)twin.Properties.Reported["ProductionRate"];
            }
            else
            {
                previousReportedProductionRate = desiredInitialRate;
            }

            var initialReportedProperties = new TwinCollection
            {
                ["DeviceError"] = DeviceErrors.None.ToString(),
                ["ProductionRate"] = desiredInitialRate
            };
            await client.UpdateReportedPropertiesAsync(initialReportedProperties);

            Console.WriteLine($"Initialized Twin - ProductionRate: {desiredInitialRate}, DeviceError: None");
        }

        private void UpdateLocalState()
        {
            // Odczyt danych telemetrycznych
            var telemetryData = ReadTelemetryData();

            // Pobranie ProductionRate w sposób niezależny
            lastReportedProductionRate = ReadProductionRate();

            // Pobranie DeviceError w sposób niezależny
            lastReportedErrorCode = ReadDeviceError();

            Console.WriteLine($"Local state updated: ProductionRate={lastReportedProductionRate}, DeviceError={lastReportedErrorCode}");
        }

        public async Task UpdateTwinAsync(int productionRate)
        {
            var updatedProperties = new TwinCollection();

            // Wyodrębnij listę aktywnych błędów
            var activeErrors = Enum.GetValues(typeof(DeviceErrors))
                .Cast<DeviceErrors>()
                .Where(error => error != DeviceErrors.None && (lastReportedErrorCode & error) != 0)
                .Select(error => error.ToString())
                .ToList();

            var newDeviceErrorState = activeErrors.Count > 0
                ? string.Join(", ", activeErrors)
                : "None";

            // Aktualizacja DeviceError tylko przy zmianie
            if (newDeviceErrorState != previousReportedErrorCode.ToString())
            {
                updatedProperties["DeviceError"] = newDeviceErrorState;
                previousReportedErrorCode = lastReportedErrorCode; // Aktualizuj enum
                Console.WriteLine($"Updating Device Twin - DeviceError: {newDeviceErrorState}");
            }

            // Aktualizacja ProductionRate tylko przy zmianie
            if (productionRate != previousReportedProductionRate)
            {
                updatedProperties["ProductionRate"] = productionRate;
                previousReportedProductionRate = productionRate;
                Console.WriteLine($"Updating Device Twin - ProductionRate: {productionRate}");
            }

            // Aktualizacja Device Twin tylko przy zmianach
            if (updatedProperties.Count > 0)
            {
                try
                {
                    await client.UpdateReportedPropertiesAsync(updatedProperties);
                    Console.WriteLine($"Device Twin updated: {JsonConvert.SerializeObject(updatedProperties)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating Device Twin: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("No changes detected. Skipping Device Twin update.");
            }
        }




        #endregion

        #region Direct Methods
        private async Task<MethodResponse> DeviceErrorHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"Direct Method invoked: {methodRequest.Name}");

            var result = opcClient.CallMethod($"ns=2;s={deviceNodePrefix}", $"ns=2;s={deviceNodePrefix}/{methodRequest.Name}");

            if (methodRequest.Equals("ResetErrorStatus"))
            {
                opcClient.WriteNode($"ns=2;s={deviceNodePrefix}/DeviceError", (int)DeviceErrors.None);
                lastReportedErrorCode = DeviceErrors.None;

                var reportedProperties = new TwinCollection
                {
                    ["DeviceError"] = DeviceErrors.None.ToString(),
                    ["ProductionRate"] = lastReportedProductionRate
                };
                await client.UpdateReportedPropertiesAsync(reportedProperties);
                Console.WriteLine("Device Twin updated with cleared error status.");

                //_lastResetTime = DateTime.UtcNow;

                await Task.Delay(2000);

                //await new ServiceBusHandler("<ServiceBusConnectionString>", "<IoTHubConnectionString>")
                //.ClearQueueAsync("deviceerrorsqueue");

            }

            if (result != null)
            {
                Console.WriteLine($"{methodRequest.Name} executed successfully.");
            }
            else
            {
                Console.WriteLine($"Failed to execute {methodRequest.Name}.");
            }
             await _serviceBusHandler.ClearQueueAsync("deviceerrorsqueue");
            Console.WriteLine("Error queue cleared successfully.");
            var responsePayload = new { message = $"{methodRequest.Name} executed successfully" };
            string responseJson = JsonConvert.SerializeObject(responsePayload);
            return new MethodResponse(Encoding.UTF8.GetBytes(responseJson), 200);
        }




        #endregion
    }
}