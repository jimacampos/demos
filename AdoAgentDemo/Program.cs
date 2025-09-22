using AdoAgentDemo;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Assistants;
using System.ClientModel;
using System.Text.Json;

class Program
{
    private static IConfiguration? _configuration;

    static async Task Main(string[] arguments)
    {
#pragma warning disable OPENAI001
        // Clear the console
        Console.Clear();

        // what are the last 5 builds from pipeline id 15174

        // Load configuration from appsettings.json or environment variables
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var organizationUrl = configuration["AZURE_DEVOPS:ORGANIZATION"];
        var project = configuration["AZURE_DEVOPS:PROJECT"];
        var personalAccessToken = configuration["AZURE_DEVOPS:PAT"];

        await using var ado = new AzureDevOpsClient(organizationUrl, project, personalAccessToken!);

        var projectEndpoint = configuration["AZURE_AI:PROJECT_ENDPOINT"];
        var modelDeployment = configuration["AZURE_AI:MODEL_DEPLOYMENT_NAME"];

        // Create Azure OpenAI client
        var azureOpenAIClient = new AzureOpenAIClient(
            new Uri(projectEndpoint),
            new ApiKeyCredential(configuration["AZURE_AI:API_KEY"])
        );

        // Get the Assistance client
        var assistantClient = azureOpenAIClient.GetAssistantClient();

        try
        {
            var getBuildDefinitionsTool = new FunctionToolDefinition("get_build_definitions");
            getBuildDefinitionsTool.Description = "List build (YAML) pipeline definitions in an Azure DevOps project.";
            getBuildDefinitionsTool.Parameters = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "name_filter": {
                  "type": "string",
                  "description": "Optional name filter for the pipeline definition"
                },
                "organization_url": {
                  "type": "string",
                  "format": "uri",
                  "description": "Azure DevOps org URL (e.g., https://dev.azure.com/<org>). If omitted, uses ADO_ORG_URL."
                },
                "project": {
                  "type": "string",
                  "description": "Project name or ID. If omitted, uses ADO_PROJECT."
                },
                "personal_access_token": {
                  "type": "string",
                  "description": "PAT with Build (Read) and Release (Read). If omitted, uses ADO_PAT."
                }
              },
              "additionalProperties": false
            }
            """);

            var getLatestBuildsTool = new FunctionToolDefinition("get_latest_builds");
            getLatestBuildsTool.Description = "Get latest builds (pipeline runs). Optionally filter by definition and result.";
            getLatestBuildsTool.Parameters = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "top": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "Max number of builds to return. Default: 20."
                },
                "definition_id": {
                  "type": "integer",
                  "description": "Build definition ID to filter by."
                },
                "result_filter": {
                  "type": "string",
                  "description": "Filter by build result.",
                  "enum": ["Succeeded", "Failed", "PartiallySucceeded", "Canceled"]
                },
                "organization_url": {
                  "type": "string",
                  "format": "uri",
                  "description": "Azure DevOps org URL. If omitted, uses ADO_ORG_URL."
                },
                "project": {
                  "type": "string",
                  "description": "Project name or ID. If omitted, uses ADO_PROJECT."
                },
                "personal_access_token": {
                  "type": "string",
                  "description": "PAT. If omitted, uses ADO_PAT."
                }
              },
              "additionalProperties": false
            }
            """);

            var getBuildTool = new FunctionToolDefinition("get_build");
            getBuildTool.Description = "Get a single build (pipeline run) by ID.";
            getBuildTool.Parameters = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "build_id": {
                  "type": "integer",
                  "description": "The numeric build ID."
                },
                "organization_url": {
                  "type": "string",
                  "format": "uri",
                  "description": "Azure DevOps org URL. If omitted, uses ADO_ORG_URL."
                },
                "project": {
                  "type": "string",
                  "description": "Project name or ID. If omitted, uses ADO_PROJECT."
                },
                "personal_access_token": {
                  "type": "string",
                  "description": "PAT. If omitted, uses ADO_PAT."
                }
              },
              "required": ["build_id"],
              "additionalProperties": false
            }
            """);

            var getLatestBuildStatusTool = new FunctionToolDefinition("get_latest_build_status");
            getLatestBuildStatusTool.Description = "Get the most recent completed build status for a definition.";
            getLatestBuildStatusTool.Parameters = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "definition_id": {
                  "type": "integer",
                  "description": "Build definition ID."
                },
                "organization_url": {
                  "type": "string",
                  "format": "uri",
                  "description": "Azure DevOps org URL. If omitted, uses ADO_ORG_URL."
                },
                "project": {
                  "type": "string",
                  "description": "Project name or ID. If omitted, uses ADO_PROJECT."
                },
                "personal_access_token": {
                  "type": "string",
                  "description": "PAT. If omitted, uses ADO_PAT."
                }
              },
              "required": ["definition_id"],
              "additionalProperties": false
            }
            """);

            var getReleasesTool = new FunctionToolDefinition("get_releases");
            getReleasesTool.Description = "List classic Release pipeline runs (releases).";
            getReleasesTool.Parameters = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "top": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "Max number of releases to return. Default: 20."
                },
                "definition_id": {
                  "type": "integer",
                  "description": "Release definition ID to filter by."
                },
                "organization_url": {
                  "type": "string",
                  "format": "uri",
                  "description": "Azure DevOps org URL. If omitted, uses ADO_ORG_URL."
                },
                "project": {
                  "type": "string",
                  "description": "Project name or ID. If omitted, uses ADO_PROJECT."
                },
                "personal_access_token": {
                  "type": "string",
                  "description": "PAT. If omitted, uses ADO_PAT."
                }
              },
              "additionalProperties": false
            }
            """);

            var getDeploymentsTool = new FunctionToolDefinition("get_deployments");
            getDeploymentsTool.Description = "List classic Release deployments. Filter by definition, environment, or status.";
            getDeploymentsTool.Parameters = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "definition_id": {
                  "type": "integer",
                  "description": "Release definition ID."
                },
                "environment_id": {
                  "type": "integer",
                  "description": "Definition environment ID."
                },
                "status_filter": {
                  "type": "string",
                  "description": "Deployment status filter.",
                  "enum": ["NotDeployed", "InProgress", "Succeeded", "PartialSucceeded", "Failed", "All"]
                },
                "top": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "Max number of deployments to return. Default: 50."
                },
                "organization_url": {
                  "type": "string",
                  "format": "uri",
                  "description": "Azure DevOps org URL. If omitted, uses ADO_ORG_URL."
                },
                "project": {
                  "type": "string",
                  "description": "Project name or ID. If omitted, uses ADO_PROJECT."
                },
                "personal_access_token": {
                  "type": "string",
                  "description": "PAT. If omitted, uses ADO_PAT."
                }
              },
              "additionalProperties": false
            }
            """);



            // Create the assistant with tools
            var assistantOptions = new AssistantCreationOptions()
            {
                Name = "support-agent",
                Instructions = """
                        ROLE
                        You are an Azure DevOps assistant. Fetch and summarize pipeline/build/release/deployment status by calling tools, then answer crisply.

                        TOOLS
                        get_build_definitions • get_latest_builds • get_build • get_latest_build_status • get_releases • get_deployments

                        WORKFLOW
                        1) Detect intent:
                           - “List pipelines” → get_build_definitions
                           - “Status of <pipeline>” → resolve name → get_latest_build_status
                           - “Latest builds (succeeded/failed)” → resolve name → get_latest_builds
                           - “Build #<id> details” → get_build
                           - “Releases/Deployments (e.g., Prod)” → resolve release def + environment → get_deployments
                        2) Resolve names → IDs:
                           - Pipelines: get_build_definitions(name_filter). If multiple, show short disambiguation and ask the user to choose. Never guess.
                           - Environments (Prod/Stage): get_releases(top=50) to find environment IDs under the chosen release definition.
                        3) Filters:
                           - Build results: Succeeded | Failed | PartiallySucceeded | Canceled.
                           - If user asks for branch/date filters, call the tool and filter client-side; say you filtered client-side.
                        4) Defaults: If “top” missing, use 10–20. Org/project/PAT are provided by the host; never print secrets.

                        RULES
                        - Never invent IDs. Confirm ambiguous names before calling a tool.
                        - Handle tool errors (`ok:false`) briefly and propose the next step (e.g., show nearest matches).
                        - One tool call at a time unless resolution requires a second call.
                        - Show times in user local time when known; otherwise include “UTC”.

                        OUTPUT
                        - Start with a one-sentence answer.
                        - Then a compact list/table including: Name/ID → Result/Status → When → Link (if available).
                        - Always include the IDs used (definitionId, buildId, releaseId, environmentId).
                        
                        """,
                Tools = { getBuildDefinitionsTool, getLatestBuildsTool, getBuildTool, getLatestBuildStatusTool, getReleasesTool, getDeploymentsTool }
            };

            var assistant = await assistantClient.CreateAssistantAsync(
                model: modelDeployment,
                assistantOptions
            );

            Console.WriteLine($"You're chatting with: {assistant.Value.Name} ({assistant.Value.Id})");

            // Create a thread
            var thread = await assistantClient.CreateThreadAsync();
            var threadId = thread.Value.Id;

            // Main interaction loop
            while (true)
            {
                Console.Write("Enter a prompt (or type 'quit' to exit): ");
                var userPrompt = Console.ReadLine();

                if (string.IsNullOrEmpty(userPrompt))
                {
                    Console.WriteLine("Please enter a prompt.");
                    continue;
                }

                if (userPrompt.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var messageContent = MessageContent.FromText(userPrompt);

                await assistantClient.CreateMessageAsync(threadId, MessageRole.User, [messageContent]);

                var run = await assistantClient.CreateRunAsync(threadId, assistant.Value.Id);
                var runId = run.Value.Id;

                // Wait for the run to complete
                while (true)
                {
                    await Task.Delay(1000);
                    run = await assistantClient.GetRunAsync(threadId, runId);

                    if (run.Value.Status == RunStatus.Completed)
                    {
                        break;
                    }
                    else if (run.Value.Status == RunStatus.Failed)
                    {
                        Console.WriteLine($"Run failed: {run.Value.LastError?.Message}");
                        break;
                    }
                    else if (run.Value.Status == RunStatus.RequiresAction)
                    {
                        var requiredActions = run.Value.RequiredActions;
                        if (requiredActions is not null)
                        {
                            var toolOutputs = new List<ToolOutput>();

                            foreach (var toolCall in requiredActions)
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(toolCall.FunctionArguments);
                                    var args = doc.RootElement;

                                    // Helpers for extracting optional/required args
                                    string? S(string name) => args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                                    int? I(string name) => args.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : (int?)null;
                                    int IReq(string name) => args.GetProperty(name).GetInt32(); // will throw if missing
                                    int IOr(string name, int fallback) => I(name) ?? fallback;

                                    string result = toolCall.FunctionName switch
                                    {
                                        // Pipelines (Builds)
                                        "get_build_definitions" => DevOpsTools.GetBuildDefinitions(
                                            nameFilter: S("name_filter"),
                                            organizationUrl: organizationUrl,
                                            project: project,
                                            personalAccessToken: personalAccessToken),

                                        "get_latest_builds" => DevOpsTools.GetLatestBuilds(
                                            top: IOr("top", 20),
                                            definitionId: I("definition_id"),
                                            resultFilter: S("result_filter"),
                                            organizationUrl: organizationUrl,
                                            project: project,
                                            personalAccessToken: personalAccessToken),

                                        "get_build" => DevOpsTools.GetBuild(
                                            buildId: IReq("build_id"),
                                            organizationUrl: organizationUrl,
                                            project: project,
                                            personalAccessToken: personalAccessToken),

                                        "get_latest_build_status" => DevOpsTools.GetLatestBuildStatus(
                                            definitionId: IReq("definition_id"),
                                            organizationUrl: organizationUrl,
                                            project: project,
                                            personalAccessToken: personalAccessToken),

                                        // Releases & Deployments (Classic)
                                        "get_releases" => DevOpsTools.GetReleases(
                                            top: IOr("top", 20),
                                            definitionId: I("definition_id"),
                                            organizationUrl: organizationUrl,
                                            project: project,
                                            personalAccessToken: personalAccessToken),

                                        "get_deployments" => DevOpsTools.GetDeployments(
                                            definitionId: I("definition_id"),
                                            environmentId: I("environment_id"),
                                            statusFilter: S("status_filter"),
                                            top: IOr("top", 50),
                                            organizationUrl: organizationUrl,
                                            project: project,
                                            personalAccessToken: personalAccessToken),

                                        _ => JsonSerializer.Serialize(new
                                        {
                                            ok = false,
                                            error = new { type = "UnknownTool", message = $"Unknown tool: {toolCall.FunctionName}" }
                                        })
                                    };

                                    toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, result));
                                }
                                catch (Exception ex)
                                {
                                    var errorJson = JsonSerializer.Serialize(new
                                    {
                                        ok = false,
                                        error = new { type = "DispatcherError", message = ex.Message }
                                    });
                                    toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, errorJson));
                                }
                            }

                            if (toolOutputs.Count > 0)
                            {
                                await assistantClient.SubmitToolOutputsToRunAsync(threadId, runId, toolOutputs);
                            }
                        }
                    }
                }

                if (run.Value.Status == RunStatus.Failed)
                {
                    continue;
                }

                // Get the latest messages from the thread
                var messages = assistantClient.GetMessagesAsync(threadId, new MessageCollectionOptions()
                {
                    Order = MessageCollectionOrder.Descending
                });

                // Get the latest assistant message
                await foreach (var message in messages)
                {
                    if (message.Role == MessageRole.Assistant)
                    {
                        foreach (var content in message.Content)
                        {
                            if (content is MessageContent textContent)
                            {
                                Console.WriteLine($"Last Message: {textContent.Text}");
                            }
                        }
                        break; // Only show the latest assistant message
                    }
                }
            }

            // Display conversation history
            Console.WriteLine("\nConversation Log:\n");
            var allMessages = assistantClient.GetMessagesAsync(threadId, new MessageCollectionOptions()
            {
                Order = MessageCollectionOrder.Ascending
            });

            await foreach (var message in allMessages)
            {
                foreach (var content in message.Content)
                {
                    if (content is MessageContent textContent)
                    {
                        Console.WriteLine($"{message.Role}: {textContent.Text}\n");
                    }
                }
            }

            // Clean up
            await assistantClient.DeleteAssistantAsync(assistant.Value.Id);
            Console.WriteLine("Deleted agent");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
#pragma warning restore OPENAI001
    }
}
