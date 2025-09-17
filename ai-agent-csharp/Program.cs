using Agent;
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

        // Load configuration from appsettings.json or environment variables
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var projectEndpoint = configuration["PROJECT_ENDPOINT"];
        var modelDeployment = configuration["MODEL_DEPLOYMENT_NAME"];

        // Create Azure OpenAI client
        var azureOpenAIClient = new AzureOpenAIClient(
            new Uri(projectEndpoint),
            new ApiKeyCredential("AZURE_AGENT_API_KEY")
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

            var checkStatusTool = new FunctionToolDefinition("check_ticket_status")
            {
                Description = "Get the current status for a given ticket",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"ticket_id\":{\"type\":\"string\"}},\"required\":[\"ticket_id\"]}")
            };

            var addCommentTool = new FunctionToolDefinition("add_ticket_comment")
            {
                Description = "Add a comment to a ticket",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"ticket_id\":{\"type\":\"string\"},\"comment\":{\"type\":\"string\"}},\"required\":[\"ticket_id\",\"comment\"]}")
            };

            var setPriorityTool = new FunctionToolDefinition("set_ticket_priority")
            {
                Description = "Set ticket priority (1=high,5=low)",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"ticket_id\":{\"type\":\"string\"},\"priority\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}},\"required\":[\"ticket_id\",\"priority\"]}")
            };

            var escalateTool = new FunctionToolDefinition("escalate_ticket")
            {
                Description = "Escalate a ticket with a reason",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"ticket_id\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\"}},\"required\":[\"ticket_id\",\"reason\"]}")
            };

            var listTicketsTool = new FunctionToolDefinition("list_user_tickets")
            {
                Description = "List recent tickets for a user",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"email_address\":{\"type\":\"string\"}},\"required\":[\"email_address\"]}")
            };

            var searchKbTool = new FunctionToolDefinition("search_kb")
            {
                Description = "Search internal KB for answers",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"]}")
            };

            var sendEmailTool = new FunctionToolDefinition("send_email_notification")
            {
                Description = "Send an email notification (demo)",
                Parameters = BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"to\":{\"type\":\"string\"},\"subject\":{\"type\":\"string\"},\"body\":{\"type\":\"string\"}},\"required\":[\"to\",\"subject\",\"body\"]}")
            };

            // Create the assistant with tools
            var assistantOptions = new AssistantCreationOptions()
            {
                Name = "support-agent",
                Instructions = """
                        You are a technical support agent.

                        Workflow:
                        1) If a user reports an issue, collect a valid email and a short description, then call submit_support_ticket.
                        2) If a user asks about a ticket, call check_ticket_status.
                        3) If a user wants to add details, call add_ticket_comment.
                        4) For urgency, call set_ticket_priority (1..5) and optionally escalate_ticket with a reason.
                        5) If a user asks what tickets they have, call list_user_tickets with their email.
                        6) If the user asks how to fix something, try search_kb; summarize top 1–3 hits.
                        7) If the user asks to notify someone, call send_email_notification.

                        Rules:
                        - Confirm critical fields back to the user before calling a tool.
                        - Never invent ticket IDs. Ask for them if missing.
                        - Only escalate with a clear reason.
                        """,
                Tools = { submitTicketTool }
            };

            assistantOptions.Tools.Add(checkStatusTool);
            assistantOptions.Tools.Add(addCommentTool);
            assistantOptions.Tools.Add(setPriorityTool);
            assistantOptions.Tools.Add(escalateTool);
            assistantOptions.Tools.Add(listTicketsTool);
            assistantOptions.Tools.Add(searchKbTool);
            assistantOptions.Tools.Add(sendEmailTool);

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
                                try
                                {
                                    var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolCall.FunctionArguments);

                                    string result = toolCall.FunctionName switch
                                    {
                                        "submit_support_ticket" => UserFunctions.SubmitSupportTicket(
                                            args["email_address"].GetString()!, args["description"].GetString()!),

                                        "check_ticket_status" => UserFunctions.CheckTicketStatus(
                                            args["ticket_id"].GetString()!),

                                        "add_ticket_comment" => UserFunctions.AddTicketComment(
                                            args["ticket_id"].GetString()!, args["comment"].GetString()!),

                                        "set_ticket_priority" => UserFunctions.SetTicketPriority(
                                            args["ticket_id"].GetString()!, args["priority"].GetInt32()),

                                        "escalate_ticket" => UserFunctions.EscalateTicket(
                                            args["ticket_id"].GetString()!, args["reason"].GetString()!),

                                        "list_user_tickets" => UserFunctions.ListUserTickets(
                                            args["email_address"].GetString()!),

                                        "search_kb" => UserFunctions.SearchKb(
                                            args["query"].GetString()!),

                                        "send_email_notification" => UserFunctions.SendEmailNotification(
                                            args["to"].GetString()!, args["subject"].GetString()!, args["body"].GetString()!),

                                        _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolCall.FunctionName}" })
                                    };

                                    toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, result));
                                }
                                catch (Exception ex)
                                {
                                    toolOutputs.Add(new ToolOutput(toolCall.ToolCallId, $"Error: {ex.Message}"));
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
