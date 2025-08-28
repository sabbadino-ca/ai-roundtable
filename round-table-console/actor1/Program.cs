using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using System.ClientModel;
var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddUserSecrets<Program>().Build();
string apiKey = configuration["AzureOpenAiApiKey"];
string deploymentName = configuration["DeploymentOrModelName"];

var azureClient = new AzureOpenAIClient(
    new Uri(configuration["AzureOpenAiApiUrl"]),
    new ApiKeyCredential(apiKey));

ChatClient chatClient = azureClient.GetChatClient(deploymentName);
string? line;
var messages = new List<ChatMessage>
{
    new SystemChatMessage(configuration["SystemMessage"])
};
while ((line = Console.ReadLine()) != null)
{
    try
    {
        File.AppendAllText(".\\actor1.log", line.Replace("\\n", Environment.NewLine));
        File.AppendAllText(".\\actor1.log", $"{Environment.NewLine}**************{Environment.NewLine}");
        //Console.WriteLine(DateTime.Now);
        messages.Add(new UserChatMessage(line));
        var completion = await chatClient.CompleteChatAsync(messages);
        var response = completion.Value.Content[0].Text;
        response = response.Replace("\n", "");
        messages.Add(new AssistantChatMessage(response));
        Console.WriteLine(response);
    }
    catch (Exception ex)
    {
        File.AppendAllText(".\\actor2.log", ex.ToString());
        await Console.Error.WriteLineAsync(ex.ToString());
        await Console.Error.FlushAsync();
    }
}