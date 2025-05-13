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
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var connectionString = Environment.GetEnvironmentVariable("CosmosDBConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("CosmosDBConnection environment variable is not set.");
            }

            _cosmosClient = new CosmosClient(connectionString) ?? throw new InvalidOperationException("Failed to initialize CosmosClient.");

            // Initialize database and container if they don't exist
            InitializeCosmosResources().GetAwaiter().GetResult();
            _container = _cosmosClient.GetContainer("FeedbackDB", "CalendarEvents") ?? throw new InvalidOperationException("Failed to access CalendarEvents container.");
        }

        private async Task InitializeCosmosResources()
        {
            try
            {
                // Create database if it doesn't exist
                Database database = await _cosmosClient.CreateDatabaseIfNotExistsAsync("FeedbackDB");
                _logger.LogInformation("Database 'FeedbackDB' ensured.");

                // Create container if it doesn't exist
                await database.CreateContainerIfNotExistsAsync(new ContainerProperties
                {
                    Id = "CalendarEvents",
                    PartitionKeyPath = "/id",
                    IndexingPolicy = new IndexingPolicy
                    {
                        IndexingMode = IndexingMode.Consistent,
                        IncludedPaths = {
                            new IncludedPath { Path = "/id/?" },
                            new IncludedPath { Path = "/date/?" },
                            new IncludedPath { Path = "/title/?" },
                            new IncludedPath { Path = "/time/?" }
                        },
                        ExcludedPaths = { new ExcludedPath { Path = "/*" } }
                    }
                }, throughput: 400);
                _logger.LogInformation("Container 'CalendarEvents' ensured with partition key '/id'.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize Cosmos DB resources: {ex.Message}");
                throw;
            }
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

                    // Use 'id' as the partition key
                    await _container.CreateItemAsync(eventItem, new PartitionKey(eventItem.id));

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
                        query = "SELECT * FROM c WHERE c.date = @date";
                        queryDefinition = new QueryDefinition(query)
                            .WithParameter("@date", date);
                    }
                    else
                    {
                        query = "SELECT * FROM c WHERE c.date >= @startDate AND c.date <= @endDate";
                        queryDefinition = new QueryDefinition(query)
                            .WithParameter("@startDate", startDate)
                            .WithParameter("@endDate", endDate);
                    }

                    _logger.LogInformation($"Executing query: {query}");

                    var iterator = _container.GetItemQueryIterator<Event>(queryDefinition);
                    var events = new System.Collections.Generic.List<Event>();

                    if (iterator == null)
                    {
                        _logger.LogWarning("Query iterator is null. Returning empty result.");
                        return new OkObjectResult(events);
                    }

                    while (iterator.HasMoreResults)
                    {
                        var response = await iterator.ReadNextAsync();
                        if (response != null)
                        {
                            events.AddRange(response);
                        }
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

                    // Use 'id' as the partition key
                    await _container.DeleteItemAsync<Event>(deleteRequest.id, new PartitionKey(deleteRequest.id));

                    return new OkObjectResult(new { message = "Event deleted successfully!" });
                }

                return new BadRequestObjectResult("Invalid request method.");
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogError($"Cosmos DB resource not found: {ex.Message}");
                return new NotFoundObjectResult("Database or container not found. Please ensure FeedbackDB and CalendarEvents container exist.");
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