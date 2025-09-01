
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;

using System.ClientModel;
var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddUserSecrets<Program>().Build();
//string apiKey = configuration["AzureOpenAiApiKey"];
string deploymentName = configuration["DeploymentOrModelName"];

//var azureClient = new AzureOpenAIClient(
//    new Uri(configuration["AzureOpenAiApiUrl"]),
//    new ApiKeyCredential(apiKey));

//ChatClient chatClient = azureClient.GetChatClient(deploymentName);
using var ollamaClient = new OllamaApiClient(
            uriString: "http://localhost:11434",
            defaultModel: deploymentName);
var chatClient = ollamaClient.AsChatCompletionService().AsChatClient();

string? line;

var messages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, configuration["SystemMessage"])
};
while ((line = Console.ReadLine()) != null)
{
    try
    {
       // File.AppendAllText(".\\actor1.log", line.Replace("\\n", Environment.NewLine));
       // File.AppendAllText(".\\actor1.log", $"{Environment.NewLine}**************{Environment.NewLine}");
        //Console.WriteLine(DateTime.Now);
        messages.Add(new ChatMessage(ChatRole.User,line));
        var completion = await chatClient.GetResponseAsync(messages);
        var response = completion.Text;
        response = response.Replace("\n", "");
        messages.Add(new ChatMessage(ChatRole.Assistant, response));
        Console.WriteLine(response);
    }
    catch (Exception ex)
    {
        File.AppendAllText(".\\actor1.log", ex.ToString());
        await Console.Error.WriteLineAsync(ex.ToString());
        await Console.Error.FlushAsync();
    }
}