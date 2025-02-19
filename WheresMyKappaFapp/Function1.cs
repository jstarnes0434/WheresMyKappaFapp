using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace WheresMyKappaFapp
{
    public class Function1
    {
        private readonly ILogger<Function1> _logger;

        public Function1(ILogger<Function1> logger)
        {
            _logger = logger;
        }

        [Function("Function1")]
        public async Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Function, "get", "post", "options")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);

            // Handle OPTIONS request (Preflight)
            if (req.Method == "OPTIONS")
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*"); // Change "*" to "http://localhost:5173" for more security
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                return response;
            }

            response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5173"); // Adjust for production later
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync("{\"message\": \"Hello from Azure Functions!\"}");

            return response;
        }
    }
}
