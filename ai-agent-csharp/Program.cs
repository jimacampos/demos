using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

class Program
{
    private static IConfiguration? _configuration;
    
    static async Task Main(string[] args)
    {
        // Clear the console
        Console.Clear();
        
        // Load configuration from environment variables
        _configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
            
        string? projectEndpoint = _configuration["PROJECT_ENDPOINT"];
        string? modelDeploymentName = _configuration["MODEL_DEPLOYMENT_NAME"];
        
        if (string.IsNullOrEmpty(projectEndpoint))
        {
            Console.WriteLine("Please set PROJECT_ENDPOINT environment variable");
            return;
        }
        
        if (string.IsNullOrEmpty(modelDeploymentName))
        {
            modelDeploymentName = "gpt-4o";
        }
        
        // Display the data to be analyzed
        string dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.txt");
        
        if (!File.Exists(dataFilePath))
        {
            Console.WriteLine($"Data file not found: {dataFilePath}");
            return;
        }
        
        string data = await File.ReadAllTextAsync(dataFilePath);
        Console.WriteLine(data);
        Console.WriteLine();
        
        // Connect to the Agent client
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = true,
            ExcludeManagedIdentityCredential = true
        });
        
        var agentsClient = new AgentsClient(projectEndpoint, credential);
        
        try
        {
            // Upload the data file
            var uploadedFile = await agentsClient.UploadFileAsync(
                filePath: dataFilePath,
                purpose: AgentFilePurpose.Agents);
            Console.WriteLine($"Uploaded {Path.GetFileName(dataFilePath)}");
            
            // Create a code interpreter tool definition
            var codeInterpreterTool = new CodeInterpreterToolDefinition();
            
            // Create tool resources with the uploaded file
            var toolResources = new ToolResources()
            {
                CodeInterpreter = new CodeInterpreterToolResource()
            };
            toolResources.CodeInterpreter.FileIds.Add(uploadedFile.Value.Id);
            
            // Define an agent that uses the CodeInterpreterTool
            var agent = await agentsClient.CreateAgentAsync(
                model: modelDeploymentName,
                name: "data-agent", 
                description: "A helpful AI agent for data analysis",
                instructions: "You are an AI agent that analyzes the data in the file that has been uploaded. Use Python to calculate statistical metrics as necessary.",
                tools: new[] { codeInterpreterTool },
                toolResources: toolResources);
                
            Console.WriteLine($"Using agent: {agent.Value.Name}");
            Console.WriteLine();
            
            // Create a thread for the conversation
            var thread = await agentsClient.CreateThreadAsync();
            
            // Loop until the user types 'quit'
            while (true)
            {
                // Get input text
                Console.Write("Enter a prompt (or type 'quit' to exit): ");
                string? userPrompt = Console.ReadLine();
                
                if (string.IsNullOrEmpty(userPrompt) || userPrompt.ToLower() == "quit")
                {
                    break;
                }
                
                // Send a prompt to the agent
                await agentsClient.CreateMessageAsync(
                    threadId: thread.Value.Id,
                    role: MessageRole.User,
                    content: userPrompt);
                
                // Create a run and poll for completion
                var run = await agentsClient.CreateRunAsync(thread.Value, agent.Value);
                
                // Poll for run completion
                do
                {
                    await Task.Delay(1000);
                    run = await agentsClient.GetRunAsync(thread.Value.Id, run.Value.Id);
                } while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress);
                
                // Check the run status for failures
                if (run.Value.Status == RunStatus.Failed)
                {
                    Console.WriteLine($"Run failed: {run.Value.LastError?.Message}");
                    continue;
                }
                
                // Show the latest response from the agent
                var messages = await agentsClient.GetMessagesAsync(thread.Value.Id);
                var lastMessage = messages.Value.FirstOrDefault(m => m.Role == MessageRole.Agent);
                
                if (lastMessage?.ContentItems?.FirstOrDefault() is MessageTextContent textContent)
                {
                    Console.WriteLine($"Agent: {textContent.Text}");
                    Console.WriteLine();
                }
            }
            
            // Get the conversation history
            Console.WriteLine("\nConversation Log:\n");
            var allMessages = await agentsClient.GetMessagesAsync(
                threadId: thread.Value.Id, 
                order: ListSortOrder.Ascending);
            
            foreach (var message in allMessages.Value)
            {
                var role = message.Role == MessageRole.User ? "User" : "Agent";
                if (message.ContentItems?.FirstOrDefault() is MessageTextContent content)
                {
                    Console.WriteLine($"{role}: {content.Text}\n");
                }
            }
            
            // Clean up
            await agentsClient.DeleteAgentAsync(agent.Value.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }
}
