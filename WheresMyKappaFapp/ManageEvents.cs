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
    public class ManageEvents
    {
        private readonly ILogger<ManageEvents> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;

        public ManageEvents(ILogger<ManageEvents> logger)
        {
            _logger = logger;
            _cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("CosmosDBConnection"));
            _container = _cosmosClient.GetContainer("CalendarDB", "Events");
        }

        [Function("ManageEvents")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", "delete", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("Processing event request.");

            try
            {
                if (req.Method == "POST")
                {
                    // Handle POST: Create a new event
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    var eventItem = JsonSerializer.Deserialize<Event>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (eventItem == null || string.IsNullOrEmpty(eventItem.title) || string.IsNullOrEmpty(eventItem.date))
                    {
                        return new BadRequestObjectResult("Invalid event data. Title and date are required.");
                    }

                    // Ensure id is set (use provided id or generate a new one)
                    eventItem.id = eventItem.id ?? DateTime.UtcNow.Ticks.ToString();

                    _logger.LogInformation($"Creating event: {JsonSerializer.Serialize(eventItem)}");

                    // Use 'date' as the partition key
                    await _container.CreateItemAsync(eventItem, new PartitionKey(eventItem.date));

                    return new OkObjectResult(new { message = "Event created successfully!", eventId = eventItem.id });
                }
                else if (req.Method == "GET")
                {
                    // Handle GET: Retrieve events by date or date range
                    string date = req.Query["date"];
                    string startDate = req.Query["startDate"];
                    string endDate = req.Query["endDate"];

                    if (string.IsNullOrEmpty(date) && (string.IsNullOrEmpty(startDate) || string.IsNullOrEmpty(endDate)))
                    {
                        return new BadRequestObjectResult("Query parameter 'date' or both 'startDate' and 'endDate' are required.");
                    }

                    string query;
                    QueryDefinition queryDefinition;

                    if (!string.IsNullOrEmpty(date))
                    {
                        // Query for a specific date
                        query = "SELECT * FROM c WHERE c.date = @date";
                        queryDefinition = new QueryDefinition(query)
                            .WithParameter("@date", date);
                    }
                    else
                    {
                        // Query for a date range
                        query = "SELECT * FROM c WHERE c.date >= @startDate AND c.date <= @endDate";
                        queryDefinition = new QueryDefinition(query)
                            .WithParameter("@startDate", startDate)
                            .WithParameter("@endDate", endDate);
                    }

                    _logger.LogInformation($"Executing query: {query}");

                    var iterator = _container.GetItemQueryIterator<Event>(queryDefinition);
                    var events = new System.Collections.Generic.List<Event>();

                    while (iterator.HasMoreResults)
                    {
                        var response = await iterator.ReadNextAsync();
                        events.AddRange(response);
                    }

                    return new OkObjectResult(events);
                }
                else if (req.Method == "DELETE")
                {
                    // Handle DELETE: Remove an event by id and date
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    var deleteRequest = JsonSerializer.Deserialize<DeleteEventRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (deleteRequest == null || string.IsNullOrEmpty(deleteRequest.id) || string.IsNullOrEmpty(deleteRequest.date))
                    {
                        return new BadRequestObjectResult("Invalid delete request. 'id' and 'date' are required.");
                    }

                    _logger.LogInformation($"Deleting event with id: {deleteRequest.id}, date: {deleteRequest.date}");

                    // Delete the item using id and partition key (date)
                    await _container.DeleteItemAsync<Event>(deleteRequest.id, new PartitionKey(deleteRequest.date));

                    return new OkObjectResult(new { message = "Event deleted successfully!" });
                }

                return new BadRequestObjectResult("Invalid request method.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing event request: {ex.Message}");
                return new StatusCodeResult(500);
            }
        }

        private class DeleteEventRequest
        {
            public string id { get; set; }
            public string date { get; set; }
        }
    }
}