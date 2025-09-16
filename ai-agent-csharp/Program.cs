using Agent;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Assistants;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static IConfiguration? _configuration;

    static async Task Main(string[] args)
    {
#pragma warning disable OPENAI001
        // Clear the console
        Console.Clear();

        // Load configuration from appsettings.json or environment variables
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var projectEndpoint = configuration["PROJECT_ENDPOINT"];
        var modelDeployment = configuration["MODEL_DEPLOYMENT_NAME"];

        // Create credentials (excluding environment and managed identity)
        //var credential = new DefaultAzureCredential();

        // Create Azure OpenAI client
        var azureOpenAIClient = new AzureOpenAIClient(
            new Uri(projectEndpoint),
            new ApiKeyCredential("API_KEY")
        );

        // Get the Assistance client
        var assistantClient = azureOpenAIClient.GetAssistantClient();

        try
        {
            // Define the function tool
            var submitTicketTool = new FunctionToolDefinition("submit_support_ticket");
            submitTicketTool.Description = "Submit a support ticket with email and description";
            submitTicketTool.Parameters = BinaryData.FromString("""
                {
                    "type": "object",
                    "properties": {
                        "email_address": {
                            "type": "string",
                            "description": "The email address of the user"
                        },
                        "description": {
                            "type": "string",
                            "description": "Description of the issue"
                        }
                    },
                    "required": ["email_address", "description"]
                }
                """);

            // Create the assistant with tools
            var assistantOptions = new AssistantCreationOptions()
            {
                Name = "support-agent",
                Instructions = """
                        You are a technical support agent.
                        When a user has a technical issue, you get their email address and a description of the issue.
                        Then you use those values to submit a support ticket using the function available to you.
                        If a file is saved, tell the user the file name.
                    """,
                Tools = { submitTicketTool }
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
                        // Handle function calls
                        var requiredActions = run.Value.RequiredActions;
                        if (requiredActions is not null)
                        {
                            var toolOutputs = new List<ToolOutput>();

                            foreach (var toolCall in requiredActions)
                            {
                                if (toolCall.FunctionName == "submit_support_ticket")
                                {
                                    try
                                    {
                                        var arguments = JsonSerializer.Deserialize<Dictionary<string, string>>(toolCall.FunctionArguments);
                                        var result = UserFunctions.SubmitSupportTicket(
                                            arguments["email_address"],
                                            arguments["description"]
                                        );

                                        toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, result));
                                    }
                                    catch (Exception ex)
                                    {
                                        toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, $"Error: {ex.Message}"));
                                    }
                                }
                            }

                            if (toolOutputs.Any())
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
