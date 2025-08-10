
using Amazon.SQS;
using Amazon.SQS.Model;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net;

namespace JobManager.API.Subscribers
{
    public class JobApplicationCreatedSubscriber : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly IAmazonSQS _sqs;

        public JobApplicationCreatedSubscriber(IConfiguration configuration, IAmazonSQS sqs)
        {
            _configuration = configuration;
            _sqs = sqs;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueUrl = _configuration["AWS:SQSQueueUrl"];

            var sendGridClient = new SendGridClient(_configuration["SendGrid:ApiKey"]);

            var from = "luis.felipe@nextwave.education";
            var name = "Luis Felipe";

            while(!stoppingToken.IsCancellationRequested)
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageAttributeNames = ["All"],
                    WaitTimeSeconds = 20
                };

                var response = await _sqs.ReceiveMessageAsync(request, stoppingToken);

                if (response.HttpStatusCode == HttpStatusCode.OK)
                {
                    foreach (var message in response.Messages)
                    {
                        var splitString = message.Body.Split("|");

                        if (splitString.Length > 1)
                        {
                            var subject = splitString[0];
                            var email = splitString[1];

                            var emailMessage = new SendGridMessage
                            {
                                From = new EmailAddress(from, name),
                                Subject = subject
                            };

                            emailMessage.AddContent(MimeType.Text, message.Body);
                            emailMessage.AddTo(new EmailAddress(email));

                            var sendGridResponse = await sendGridClient.SendEmailAsync(emailMessage);
                        }


                        Console.WriteLine($"Processando mensagem {message}");

                        await _sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle);
                    }
                }
            }
        }
    }
}
