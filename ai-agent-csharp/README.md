# AI Agent - C# .NET 8 Version

This is a C# .NET Core 8 version of the AI Agent application that analyzes data using Azure AI Projects SDK.

## Features

- **Azure AI Integration**: Uses Azure AI Projects SDK to create and interact with AI agents
- **File Upload**: Uploads data files for analysis by the AI agent
- **Code Interpreter**: Uses code interpreter tool to analyze data with Python
- **Interactive Chat**: Provides an interactive console interface to chat with the AI agent
- **Environment Configuration**: Loads configuration from environment variables
- **Conversation History**: Displays full conversation log at the end

## Prerequisites

- .NET 8.0 SDK
- Azure AI Project with appropriate permissions
- Azure credentials configured (using DefaultAzureCredential)

## Setup

1. Set the following environment variables:
   ```
   PROJECT_ENDPOINT=your_azure_ai_project_endpoint
   MODEL_DEPLOYMENT_NAME=gpt-4o
   ```

2. Ensure you have Azure credentials configured (via Azure CLI, Visual Studio, etc.)

## Usage

1. Build and run the application:
   ```bash
   dotnet run
   ```

2. The application will:
   - Display the data from `data.txt` 
   - Upload the file to Azure AI
   - Create an AI agent with code interpreter capabilities
   - Start an interactive chat session

3. Enter prompts to analyze the data or type 'quit' to exit

4. At the end, the full conversation history will be displayed

## Project Structure

- `Program.cs` - Main application logic
- `Agent.csproj` - Project file with dependencies
- `data.txt` - Sample data file (travel expenses)
- `appsettings.json` - Configuration template
- `.env` - Environment variables template

## Dependencies

- `Azure.AI.Projects` (1.0.0-beta.2) - Azure AI Projects SDK
- `Azure.Identity` (1.15.0) - Azure authentication
- `Microsoft.Extensions.Configuration.EnvironmentVariables` (9.0.8) - Configuration support

## Comparison with Python Version

This C# version provides the same functionality as the Python `agent.py`:

| Feature | Python | C# .NET 8 |
|---------|--------|-----------|
| Azure AI SDK | `azure-ai-agents` | `Azure.AI.Projects` |
| Environment Variables | `python-dotenv` | `Microsoft.Extensions.Configuration` |
| Authentication | `DefaultAzureCredential` | `DefaultAzureCredential` |
| File Operations | `pathlib.Path` | `System.IO.Path` |
| Async Operations | `async/await` | `async/await Task` |
| Interactive Console | `input()` | `Console.ReadLine()` |

The C# version follows .NET conventions and uses strongly-typed APIs throughout.