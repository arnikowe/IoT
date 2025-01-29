# Industrial IoT

Zaprojektowanie i opracowanie systemu monitorowania produkcji, który będzie zbierał dane z dostępnych linii produkcyjnych i wykonywał podstawowe analizy, obliczenia i logikę biznesową na podstawie tych danych.

## Instalacja

Aby uruchomić projekt:

1. Upewnij się, że masz zainstalowane środowisko Visual Studio 2019 lub nowsze.
2. Sklonuj repozytorium projektu za pomocą Visual Studio lub narzędzia Git w wierszu poleceń.
3. Otwórz plik rozwiązania projektu `DeviceSdkDemo.sln` w Visual Studio.
4. Zweryfikuj, czy wszystkie wymagane pakiety NuGet zostały zainstalowane. Jeśli nie, skorzystaj z opcji `Restore NuGet Packages`.
5. Wybierz DeviceSdkDemo.Console jako projekt startowy.
6. Uruchom projekt, klikając przycisk Start w Visual Studio.


## Połączenie z serwerem OPC UA
By połączyć się z serwerem musisz podać adres URL swojego serwera OPC UA, który zazwyczaj wygląda tak: `opc.tcp://localhost:4840/`
Aby to zrobić znajdź plik `ConnectionStrings.json` w folderze `/bin/Debug/net6.0`  i wpisz swój adres URL obok właściwości `"OpcUaServerUrl"`w następujący sposób: `"OpcUaServerUrl": "przykładowy_adres_URL"`

## Konfiguracja ustawień aplikacji
Aby poprawnie skonfigurować agenta, należy otworzyć plik konfiguracyjny `ConnectionStrings.json` i wypełnić klucze odpowiednimi wartościami. Na przykład:
```javascript
{
  "IoTHubConnectionString": "HostName=IoTHuBWMII.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=",
  "OpcUaServerUrl": "opc.tcp://localhost:4840/",
  "serviceBusConnectionString": "Endpoint=sb://servicebusiot.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=",
  "DeviceConnectionStrings": {
    "Device1": "HostName=IoTHuBWMII.azure-devices.net;DeviceId=Device1;SharedAccessKey=",
    "Device2": "HostName=IoTHuBWMII.azure-devices.net;DeviceId=Device2;SharedAccessKey=",
    "Device3": "HostName=IoTHuBWMII.azure-devices.net;DeviceId=Device3;SharedAccessKey="
  },
  "Email": {
    "ConnectionString": "endpoint=https://productionerrors.europe.communication.azure.com/;accesskey=",
    "SenderEmail": "DoNotReply@f4771d41-efe3-4bb2-9150-be214a59f828.azurecomm.net",
    "RecipientEmail": "example@gamil.com"
  }
}

```


## D2C Messages

Istnieją dwa typy wiadomości D2C (device to cloud)
1. Telemtria
2. Wiadomości o błędzie

### Telemetria
Wiadomość z telemetrią wysyła się co 1 sekunde do każdego urządzenia. Każda wiadomość telemetryczna ma następujące właściwości pochodzące z serwera: ProductionStatus, WorkorderId, GoodCount, BadCount i Temperature

Przykładowa telemetria na urządzeniu w IoTHub:
```java
{
  "body": {
    "ProductionStatus": 1,
    "WorkorderId": "4ee77890-b782-4606-8e72-70585c8f7945",
    "GoodCount": 29,
    "BadCount": 0,
    "Temperature": 60.58839660720343
  },
  "enqueuedTime": "Wed Jan 22 2025 21:34:25 GMT+0100 (czas środkowoeuropejski standardowy)"
}
```
Przykładowa wiadomość na konsoli:

```java
Telemetry from Device Device 1: {"ProductionStatus":1,"WorkorderId":"87e10bd8-fd95-405f-8776-6d2bab7b7292","GoodCount":39,"BadCount":3,"Temperature":79.23403544173846}
```

### Wiadomości o błędzie
Komunikaty o błędach są wysyłane do IoT Hub tylko wtedy, gdy wystąpi błąd. Agent sprawdza każde urządzenie pod kątem nowych błędów co 1 sekundę.

Przykładowa wiadomości o błędzie na urządzeniu w IoTHub:
```java
{
  "body": {
    "DeviceError": [
      "PowerFailure"
    ],
    "newErrors": 1
  },
  "enqueuedTime": "Wed Jan 22 2025 22:25:38 GMT+0100 (czas środkowoeuropejski standardowy)"
}
```

Przykładowa wiadomość na konsoli:

```java
Sending error event: {"DeviceError":["PowerFailure"],"newErrors":1} from Device Device 3
```

Ponadto, jeśli wystąpi nowy błąd,  powiadomienie zostanie wysłane na wstępnie zdefiniowane adresy e-mail (zaimplementowane przy użyciu usług Azure Communication Services).

## Device Twin
Device Twin ma dwa rodzaje właściwości: `desired` i `reported`.

Oto przykład Device Twin
```java
{
	"deviceId": "Device1",
	"etag": "AAAAAAAAAAE=",
	"deviceEtag": "MzgyODQyMzEw",
	"status": "enabled",
	"statusUpdateTime": "0001-01-01T00:00:00Z",
	"connectionState": "Disconnected",
	"lastActivityTime": "2025-01-22T21:22:53.4503122Z",
	"cloudToDeviceMessageCount": 0,
	"authenticationType": "sas",
	"x509Thumbprint": {
		"primaryThumbprint": null,
		"secondaryThumbprint": null
	},
	"modelId": "",
	"version": 5,
	"properties": {
		"desired": {
			"$metadata": {
				"$lastUpdated": "2025-01-22T20:02:20.5578083Z"
			},
			"$version": 1
		},
		"reported": {
			"ProductionRate": 30,
			"DeviceError": "PowerFailure",
			"$metadata": {
				"$lastUpdated": "2025-01-22T21:25:44.3067256Z",
				"ProductionRate": {
					"$lastUpdated": "2025-01-22T21:22:46.4192792Z"
				},
				"DeviceError": {
					"$lastUpdated": "2025-01-22T21:25:44.3067256Z"
				}
			},
			"$version": 4
		}
	},
	"capabilities": {
		"iotEdge": false
	}
}
```
### Desired properties
Właściwość desired można ustawić tylko z IoT Hub, a urządzenie natychmiast ją odczyta. 
 
Przykładowa wiadomość po zmianie właściwości desired device twin
```java
ProductionRate updated to: 40 on device Device 1
```

A tak wygląda desired device twin w IoTHub
```java
"properties": {
		"desired": {
			"ProductionRate": 40,
			"$metadata": {
				"$lastUpdated": "2025-01-22T21:37:58.5819904Z",
				"$lastUpdatedVersion": 3,
				"ProductionRate": {
					"$lastUpdated": "2025-01-22T21:37:58.5819904Z",
					"$lastUpdatedVersion": 3
				}
			},
			"$version": 3
		},
```

### Reported properties
Istnieją dwie reported właściwości: `ProductionRate` and `DeviceError`. Reported właściwości są wysyłane przez urządzenie. Co sekundę sprawdzane są błędy urządzenia i szybkość produkcji. Jeśli nie pasują do zgłoszonych odpowiedników, nowa wartość jest zgłaszana do Device Twin.

A tak wygląda reported device twin w IoTHub
```java
"reported": {
			"ProductionRate": 0,
			"DeviceError": "PowerFailure",
			"$metadata": {
				"$lastUpdated": "2025-01-22T21:34:24.2765296Z",
				"ProductionRate": {
					"$lastUpdated": "2025-01-22T21:34:24.2765296Z"
				},
				"DeviceError": {
					"$lastUpdated": "2025-01-22T21:25:44.3067256Z"
				}
			},
			"$version": 5
		}
```

Przykładowa wiadomość po zmianie właściwości reported device twin
```java
Device Device 1:Device Twin updated: {"ProductionRate":60}
```

## Direct methods
Na każdym urządzeniu możesz wywołać 2 metody bezpośrednie: `Emergency Stop` i `Reset Error Status`

### EmergencyStop
Produkcja na linii produkcyjnej zostaje zatrzymana, a wszystkie wyzwalacze błędów są odznaczone, ale wyzwalacz EmergencyStop jest ustawiony. Aby odznaczyć flagę EmergencyStop, należy wywołać bezpośrednią metodę Reset Error Status.
Flagi błędów urządzenia w aplikacji symulatora przed wywołaniem Emergency Stop:

![1](https://github.com/user-attachments/assets/b0cc5cf3-c52c-473a-969c-7d19f9079d3c)

Wywołanie metody bezpośredniej EmergencyStop z poziomu IoT Explorer:

![2](https://github.com/user-attachments/assets/2c1e2d90-177f-4485-b526-f91f4d961dae)

Przykład wiadomości z konsoli po wywołaniu: 
```java
EmergencyStop executed successfully on Device Device 2.
```
Komunikat programu IoT Explorer o pomyślnym wywołaniu:
```java
Successfully invoked method 'EmergencyStop' on device 'Device2' with response {"status":200,"payload":{"message":"EmergencyStop executed successfully"}}
```
Flagi błędów urządzenia w aplikacji symulatora po wywołaniu Emergency Stop:

![3](https://github.com/user-attachments/assets/2c976511-fbca-443a-9f45-a003692bf2b6)

### Reset Error Status
Usuwa wszystkie flagi błędów, w tym flagę Emergency Stop.
Podobnie jak w przypadku metody bezpośredniej Emergency Stop, modyfikuje dane błędów na serwerze.
Flagi błędów urządzenia w aplikacji symulatora przed wywołaniem Reset Error Status:

![3](https://github.com/user-attachments/assets/f8bb6aab-7798-46fa-9807-9b402974b691)


Wywołanie metody bezpośredniej ResetErrorStatus z poziomu IoT Explorer:

![4](https://github.com/user-attachments/assets/83587345-7147-4c15-a49f-1a6ee33ec9ad)


Przykład wiadomości z konsoli po wywołaniu: 
```java
ResetErrorStatus executed successfully on Device Device 2.
```
Komunikat programu IoT Explorer o pomyślnym wywołaniu:
```java
Successfully invoked method 'EmergencyStop' on device 'Device2' with response {"status":200,"payload":{"message":"EmergencyStop executed successfully"}}
```
Flagi błędów urządzenia w aplikacji symulatora po wywołaniu Emergency Stop:

![5](https://github.com/user-attachments/assets/0dd9dff4-55c8-41c0-9c71-8cce4451fe6c)


### Metoda domyślna
Jeśli wywołasz metodę, która nie istnieje lub popełnisz błąd w słowie kluczowym jednej z metod wymienionych powyżej, zostanie wykonana Metoda domyślna. Jej celem jest napisanie w konsoli, że wywołano nieznaną metodę.

Przykład wiadomości z konsoli po wywołaniu: 
```java
 Unknown method executed: test on Device 2
```

## Kalkulacje
W projekcie istnieją 3 typy obliczeń, które działają z danymi z IoT Hub:

1. KPI produkcji
Podaje procent dobrej produkcji w całkowitej objętości, pogrupowane według urządzenia w 5-minutowych oknach.

Wykorzystane zapytanie w Stream Analytics Job:

```java
SELECT
  System.Timestamp() AS WindowEndTime,
  IoTHub.ConnectionDeviceId AS DeviceId, 
  COUNT(*) AS TotalMessages, 
  AVG(
    CASE 
      WHEN (GoodCount + BadCount) > 0 
      THEN CAST(GoodCount AS FLOAT) / (GoodCount + BadCount) 
      ELSE 0 
    END
  ) * 100 AS PercentGoodProduction 
INTO
  [productionkpiqueue] 
FROM
  [IoTHuBWMII] 
GROUP BY
  TumblingWindow(minute, 5),
  IoTHub.ConnectionDeviceId;
```

Wyniki są przechowywane w blobach kontenera `production-kpi` a także w `productionkpiqueue` .

Oto przykład zawartości blobu:
```java
{"WindowEndTime":"2025-01-22T19:30:00.0000000Z","DeviceId":"Device1","TotalMessages":6,"PercentGoodProduction":91.31766978541171}
{"WindowEndTime":"2025-01-22T19:30:00.0000000Z","DeviceId":"Device2","TotalMessages":6,"PercentGoodProduction":92.12336184110377}
{"WindowEndTime":"2025-01-22T19:30:00.0000000Z","DeviceId":"Device3","TotalMessages":6,"PercentGoodProduction":74.94772176975567}
```

Właściwości GoodCount i BadCount każdego urządzenia są przesyłane jako dane telemetryczne do IoTHub w Azure. Za pomocą specjalnego zapytania w usłudze ASA wartości te są wykorzystywane do obliczenia procentowego udziału dobrej produkcji w stosunku do całkowitej produkcji w 5-minutowych oknach czasowych. Jeśli obliczony wynik spadnie poniżej 90%, dane te są przesyłane i zapisywane w kolejce ServiceBus Queue.

2. Temperatura
Co 1 minutę podaje średnią, minimalną i maksymalną temperaturę w ciągu ostatnich 5 minut (grupowane według urządzenia).

Wykorzystane zapytanie w Stream Analytics Job:

```java
SELECT
  System.Timestamp() AS WindowEndTime,
  IoTHub.ConnectionDeviceId AS DeviceId,
  MIN(Temperature) AS MinTemperature,
  MAX(Temperature) AS MaxTemperature,
  AVG(Temperature) AS AvgTemperature
INTO
  [temperature]
FROM
  [IoTHuBWMII]
GROUP BY
HoppingWindow(minute,5,1), IoTHub.ConnectionDeviceId;
Wyniki są przechowywane w blobach kontenera pomiarów `temperature`.
```

Oto przykład zawartości bloba:
```java
{"WindowEndTime":"2025-01-21T09:29:00.0000000Z","DeviceId":"Device1","MinTemperature":68.23337832886705,"MaxTemperature":77.00643535766953,"AvgTemperature":71.19181742742353}
{"WindowEndTime":"2025-01-21T09:29:00.0000000Z","DeviceId":"Device2","MinTemperature":62.4528465397645,"MaxTemperature":80.44664282969374,"AvgTemperature":71.39820708310359}
{"WindowEndTime":"2025-01-21T09:29:00.0000000Z","DeviceId":"Device3","MinTemperature":79.12201401206633,"MaxTemperature":95.17524191969831,"AvgTemperature":84.2363935359985}
{"WindowEndTime":"2025-01-21T09:30:00.0000000Z","DeviceId":"Device1","MinTemperature":60.04030317615731,"MaxTemperature":79.17764881180128,"AvgTemperature":68.58347116230834}
{"WindowEndTime":"2025-01-21T09:30:00.0000000Z","DeviceId":"Device2","MinTemperature":62.4528465397645,"MaxTemperature":83.6449143838678,"AvgTemperature":72.38075257744214}
{"WindowEndTime":"2025-01-21T09:30:00.0000000Z","DeviceId":"Device3","MinTemperature":78.16535713871596,"MaxTemperature":104.77312475669194,"AvgTemperature":85.7434256608729}
{"WindowEndTime":"2025-01-21T09:31:00.0000000Z","DeviceId":"Device1","MinTemperature":60.04030317615731,"MaxTemperature":80.97473024841192,"AvgTemperature":70.3524841859567}
{"WindowEndTime":"2025-01-21T09:31:00.0000000Z","DeviceId":"Device2","MinTemperature":60.99078764064969,"MaxTemperature":85.16475782974366,"AvgTemperature":71.90486001467175}
{"WindowEndTime":"2025-01-21T09:31:00.0000000Z","DeviceId":"Device3","MinTemperature":24.252472512486502,"MaxTemperature":106.07351842328333,"AvgTemperature":78.5757932241112}
{"WindowEndTime":"2025-01-21T09:32:00.0000000Z","DeviceId":"Device1","MinTemperature":60.04030317615731,"MaxTemperature":80.97473024841192,"AvgTemperature":70.97393708283586}
{"WindowEndTime":"2025-01-21T09:32:00.0000000Z","DeviceId":"Device2","MinTemperature":60.99078764064969,"MaxTemperature":85.16475782974366,"AvgTemperature":72.00405079626837}
```

Wartości właściwości Temperature dla każdego urządzenia są przesyłane jako dane telemetryczne do IoTHub w Azure. Co minutę, specjalne zapytanie w usłudze ASA wykorzystuje te dane do obliczenia średniej, minimalnej oraz maksymalnej temperatury z ostatnich pięciu minut.

3. Błędy urządzenia
Przechowuje sytuacje, gdy urządzenie doświadczyło więcej niż 3 błędów w czasie krótszym niż 1 minuta.

Wykorzystane zapytanie w Stream Analytics Job:

```java
SELECT
  System.Timestamp() AS WindowEndTime, 
  IoTHub.ConnectionDeviceId AS DeviceId, 
  CAST(SUM(newErrors) AS BIGINT) AS ErrorCount 
INTO
  [deviceerrorsqueue] 
FROM
  [IoTHuBWMII]
WHERE
   newErrors IS NOT NULL 
GROUP BY
  SlidingWindow(minute, 1),
  IoTHub.ConnectionDeviceId 
HAVING SUM(newErrors) >= 3;
```

Wyniki są przechowywane w blobach kontenera `device-error` i `deviceerrorsqueue`.

Oto przykład zawartości blobu:
```java
{"WindowEndTime":"2025-01-22T20:09:09.8200000Z","DeviceId":"Device3","ErrorCount":4}
{"WindowEndTime":"2025-01-22T20:09:59.0850000Z","DeviceId":"Device3","ErrorCount":3}
{"WindowEndTime":"2025-01-22T20:10:26.2320000Z","DeviceId":"Device2","ErrorCount":4}
{"WindowEndTime":"2025-01-22T20:11:15.4810000Z","DeviceId":"Device2","ErrorCount":3}
{"WindowEndTime":"2025-01-22T20:12:16.8180000Z","DeviceId":"Device3","ErrorCount":3}
{"WindowEndTime":"2025-01-22T20:12:59.1190000Z","DeviceId":"Device3","ErrorCount":6}
{"WindowEndTime":"2025-01-22T20:13:16.8180000Z","DeviceId":"Device3","ErrorCount":3}
```

Gdy na urządzeniu pojawi się nowy błąd, do IoTHub wysyłana jest pojedyncza wiadomość. Następnie program wykorzystuje specjalne zapytanie w usłudze ASA do zliczenia liczby takich wiadomości w ciągu ostatniej minuty. Jeśli suma przekroczy wartość 3, dane zostają przesłane i zapisane w kolejce ServiceBus Queue.

## Logika biznesowa
### Obniżenie wartości ProductionRate

Na podstawie danych znajdujących się w kolejce ServiceBus Queue, które zawierają informacje o kalkulacji KPI produkcji (gdy KPI spada poniżej 90%), program wywołuje funkcję zmniejszającą wartość ProductionRate o 10 dla danego urządzenia.

Przykładowy komunikat wyświetlany na konsoli:
```java
Production rate decreased for Device1. New desired rate: 30
```

### Wywołanie metody EmergencyStop

Na podstawie danych zapisanych w kolejce ServiceBus Queue (device-errors-queue), dotyczących liczby błędów występujących w ciągu ostatniej minuty (gdy przekracza 3 błędy), program wywołuje metodę bezpośrednią EmergencyStop, która zatrzymuje działanie urządzenia.

Przykładowy komunikat wyświetlany na konsoli:
```java
EmergencyStop invoked for Device1
```

### Wysłanie maila z informacją o pojawieniu się nowego błędu

W momencie wykrycia nowego błędu na urządzeniu program wysyła wiadomość e-mail do odbiorcy wskazanego w pliku konfiguracyjnym  E-mail zawiera informacje o rodzaju błędu i urządzeniu, na którym wystąpił.
Pojawienie się nowego błędu powoduje nawiązanie połączenia z usługami Azure Communication Services i Email Communication Services, które umożliwiają przesłanie wiadomości e-mail na zdefiniowany adres.


Przykładowa treść e-maila:

![6](https://github.com/user-attachments/assets/a8773848-5f3d-49a5-855d-b6738507e7be)


Wiadomość o wysłaniu maila wyświetlana na konsoli:
```java
Email sent successfully.
```
