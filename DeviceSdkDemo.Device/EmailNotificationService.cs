using Azure;
using Azure.Communication.Email;


namespace DeviceSdkDemo.Device
{
    public class EmailNotificationService
    {
        private readonly EmailClient _emailClient;
        private readonly string _senderEmail;
        private readonly string _recipientEmail;

        public EmailNotificationService(string connectionString, string senderEmail, string recipientEmail)
        {
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

                var emailContent = new EmailContent(subject)
                {
                    PlainText = body
                };

                var emailMessage = new EmailMessage(_senderEmail, _recipientEmail, emailContent);

                var response = await _emailClient.SendAsync(WaitUntil.Completed, emailMessage);

                Console.WriteLine($"Email sent successfully. ");
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to send email notification: {ex.Message}");
            }
        }
    }
}
