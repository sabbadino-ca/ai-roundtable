
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;
var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddUserSecrets<Program>().Build();

//var azureClient = new AzureOpenAIClient(
//    new Uri(configuration["AzureOpenAiApiUrl"]),
//    new ApiKeyCredential(apiKey));
string apiKey = configuration["AzureOpenAiApiKey"];
string deploymentName = configuration["DeploymentOrModelName"];

var chatClient = new ChatClient(
    model: deploymentName,
    credential: new ApiKeyCredential("OPENAI_API_KEY"),
    options: new OpenAIClientOptions()
    {
        Endpoint = new Uri("http://localhost:1234/v1")
    }
);
//ChatClient chatClient = azureClient.GetChatClient(deploymentName);
//using var ollamaClient = new OllamaApiClient(
//            uriString: "http://localhost:11434",
//            defaultModel: deploymentName);
//var chatClient = ollamaClient.AsChatCompletionService().AsChatClient();

string? line;

var messages = new List<ChatMessage>
{
    new UserChatMessage (configuration["SystemMessage"])
};
while ((line = Console.ReadLine()) != null)
{
    try
    {
        // File.AppendAllText(".\\actor1.log", line.Replace("\\n", Environment.NewLine));
        // File.AppendAllText(".\\actor1.log", $"{Environment.NewLine}**************{Environment.NewLine}");
        //Console.WriteLine(DateTime.Now);
        var messagesToAdd = JsonSerializer.Deserialize<List<shared.Message>>(line);
        foreach (var msg in messagesToAdd)
        {
            if (msg.Role == shared.Message.AssistantRole)
                messages.Add(new AssistantChatMessage(msg.Text));
            else
            {
                messages.Add(new UserChatMessage(msg.Text));
            }
        }
        //messages.Add(new UserChatMessage(line));
        var completion = await chatClient.CompleteChatAsync(messages);
        var response = completion.Value.Content[0].Text;
        response = response.Replace("\n", "");
        //messages.Add(new AssistantChatMessage(response));
        Console.WriteLine(response);
    }
    catch (Exception ex)
    {
        File.AppendAllText(".\\actor2.log", ex.ToString());
        await Console.Error.WriteLineAsync(ex.ToString());
        await Console.Error.FlushAsync();
    }
}