using Azure;
using Azure.Communication.Email;
using System;
using System.Threading.Tasks;

namespace DeviceSdkDemo.Device
{
    public class EmailNotificationService
    {
        private readonly EmailClient _emailClient;
        private readonly string _senderEmail;
        private readonly string _recipientEmail;

        public EmailNotificationService(string connectionString, string senderEmail, string recipientEmail)
        {
            // Inicjalizacja właściwej klasy EmailClient z Azure Communication Services
            _emailClient = new EmailClient(connectionString);
            _senderEmail = senderEmail;
            _recipientEmail = recipientEmail;
        }

        public async Task SendErrorNotificationAsync(string deviceId, string errorType)
        {
            try
            {
                var subject = $"Device Error Notification - {deviceId}";
                var body = $"An error of type '{errorType}' occurred on device {deviceId}.";

                // Tworzenie treści wiadomości e-mail
                var emailContent = new EmailContent(subject)
                {
                    PlainText = body
                };

                // Konfiguracja wiadomości e-mail
                var emailMessage = new EmailMessage(_senderEmail, _recipientEmail, emailContent);

                // Wysyłanie wiadomości e-mail za pomocą Azure Communication Services
                var response = await _emailClient.SendAsync(WaitUntil.Completed, emailMessage);

                Console.WriteLine($"Email sent successfully. Status: {response.UpdateStatus}");
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to send email notification: {ex.Message}");
            }
        }
    }
}
