using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using WheresMyKappaFapp.Models;

namespace WheresMyKappaFapp
{
    public class SubmitFeedback
    {
        private readonly ILogger<SubmitFeedback> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;

        public SubmitFeedback(ILogger<SubmitFeedback> logger)
        {
            _logger = logger;
            _cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("CosmosDBConnection"));
            _container = _cosmosClient.GetContainer("FeedbackDB", "FeedbackContainer"); 
        }

        [Function("SubmitFeedback")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            _logger.LogInformation("Processing feedback submission.");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var feedback = JsonSerializer.Deserialize<Feedback>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (feedback == null || string.IsNullOrEmpty(feedback.FeedbackArea) || string.IsNullOrEmpty(feedback.FeedbackText) || string.IsNullOrEmpty(feedback.FeedbackType))
                {
                    return new BadRequestObjectResult("Invalid feedback data.");
                }

                feedback.id = Guid.NewGuid().ToString(); // Generate a unique ID

                _logger.LogInformation($"Feedback Data: {JsonSerializer.Serialize(feedback)}");

                // Use 'id' as the partition key
                await _container.CreateItemAsync(feedback, new PartitionKey(feedback.id));

                return new OkObjectResult(new { message = "Feedback submitted successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving feedback: {ex.Message}");
                return new StatusCodeResult(500);
            }
        }

    }
}
