using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace DurableFunctionDatabase
{
    public static class Database
    {
        public class WriteOperation
        {
            public string Key { get; set; }

            public string Workflow { get; set; }

            public string Operation { get; set; }

            public int Value { get; set; }

            public WriteOperation(string key, string workflow, string operation, int value)
            {
                Key = key;
                Workflow = workflow;
                Operation = operation;
                Value = value;
            }
        }

        [FunctionName("Yeast")]
        public static void Yeast(
            [EntityTrigger] IDurableEntityContext ctx)
        {
            var activities = new List<string> { "Batch Samples", "Tx Yeast", "PoolPrep", "Pick & Grow Colonies", "QC", "Tx Ecoli/Agro" };
            Workflow(activities, ctx);
        }

        [FunctionName("Gateway")]
        public static void Gateway(
            [EntityTrigger] IDurableEntityContext ctx)
        {
            var activities = new List<string> { "Batch Samples", "Gateway RXN", "E.Coli Tx", "Pick & Grow Colonies", "Miniprep", "QC", "Tx Ecoli/Agro" };
            Workflow(activities, ctx);
        }

        private static void Workflow(List<string> activities,
            IDurableEntityContext ctx)
        {
            int currentValue = 0;
            try
            {
                currentValue = ctx.GetState<int>();

            }
            catch
            {

            }

            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "advance":

                    if (currentValue < activities.Count - 1)
                    {
                        currentValue += 1;
                    }
                    ctx.SetState(currentValue);
                    ctx.Return(activities[currentValue]);
                    break;
                case "set":
                    int value = ctx.GetInput<int>();
                    if (value < activities.Count)
                    {
                        currentValue = value;
                        ctx.SetState(currentValue);
                    }
                    break;
                case "activities":
                    ctx.Return(string.Join(";", activities));
                    break;
                case "get":
                    ctx.Return(activities[currentValue]);
                    break;
            }
        }

        [FunctionName("Workflow_GET_Orchestrator")]
        public static async Task<string> DatabaseGetOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var operation = context.GetInput<WriteOperation>();

            //EntityId id = new EntityId(nameof(Workflow), operation.Key);
            EntityId id = new EntityId(operation.Workflow, operation.Key);

            return await context.CallEntityAsync<string>(id, operation.Operation);
        }

        [FunctionName("Workflow_PUT_Orchestrator")]
        public static async Task<string> DatabasePutOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var operation = context.GetInput<WriteOperation>();

            //EntityId id = new EntityId(nameof(Workflow), operation.Key);
            EntityId id = new EntityId(operation.Workflow, operation.Key);

            return await context.CallEntityAsync<string>(id, "advance", operation.Value);
        }

        [FunctionName("Workflow_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", Route = "Workflow/{workflow}/{operation}/{key}")] HttpRequestMessage req,
            string workflow,
            string operation,
            string key,
            [OrchestrationClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string instanceId;
            log.LogInformation(workflow);
            // GET request
            if (req.Method == HttpMethod.Get)
            {
                int value = 0;
                instanceId = await starter.StartNewAsync("Workflow_GET_Orchestrator", new WriteOperation(key, workflow, operation, value));
                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
                return await starter.WaitForCompletionOrCreateCheckStatusResponseAsync(req, instanceId, System.TimeSpan.MaxValue);
            }

            // PUT request
            else if(req.Method == HttpMethod.Put)
            {
                var content = req.Content;
                int value = int.Parse(content.ReadAsStringAsync().Result);
                instanceId = await starter.StartNewAsync("Workflow_PUT_Orchestrator", new WriteOperation(key, workflow, operation, value));
                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
                return await starter.WaitForCompletionOrCreateCheckStatusResponseAsync(req, instanceId, System.TimeSpan.MaxValue);
            }

            // Otherwise.
            else
            {
                return req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            }
        }
    }
}