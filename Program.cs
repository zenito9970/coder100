using OpenAI;
using OpenAI.Chat;

class Program
{
    static void Main(string[] args)
    {
        var credential = new System.ClientModel.ApiKeyCredential("lm-studio");
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri("http://localhost:1234/v1") };
        var client = new OpenAIClient(credential, clientOptions);
        var chat = client.GetChatClient("openai/gpt-oss-20b");

        var camelCaseOption = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var readTool = ChatTool.CreateFunctionTool("Read", "Read contents of specified file.", BinaryData.FromObjectAsJson(new { Type = "object", Properties = new { Path = new { Type = "string", Description = "relative filepath, e.g. ./src/main.c" } } }, camelCaseOption));
        var writeTool = ChatTool.CreateFunctionTool("Write", "Writes the content to the file. If the file does not exist, it will be newly created.", BinaryData.FromObjectAsJson(new { Type = "object", Properties = new { Type = "object", Properties = new { Path = new { Type = "string", Description = "relative filepath, e.g. ./src/main.c", }, Content = new { Type = "string", Description = "The content to write to the file. Any existing content will be discarded and overwritten with this content." } } } }, camelCaseOption));
        var listTool = ChatTool.CreateFunctionTool("List", "Get a list of files and directories in the specified directory.", BinaryData.FromObjectAsJson(new { Type = "object", Properties = new { Path = new { Type = "string", Description = "relative directory path, e.g. ./src/internal" } } }, camelCaseOption));
        var completionOptions = new ChatCompletionOptions { AllowParallelToolCalls = false, ToolChoice = ChatToolChoice.CreateAutoChoice(), Tools = { readTool, writeTool, listTool } };

        var context = new List<ChatMessage>();
        context.Add(ChatMessage.CreateSystemMessage("You are an interactive CLI tool that helps users with software engineering tasks. Use the instructions below and the tools available to you to assist the user."));

        var skipUserInput = false;
        while (true)
        {
            if (!skipUserInput)
            {
                Console.Write("\n> ");
                var prompt = Console.ReadLine();
                if (prompt?.StartsWith("/exit") ?? true)
                {
                    break;
                }
                context.Add(ChatMessage.CreateUserMessage(prompt));
            }
            skipUserInput = false;

            var result = chat.CompleteChat(context, completionOptions).Value;
            if (result.FinishReason == ChatFinishReason.Stop)
            {
                var msg = string.Join('\n', result.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text.TrimEnd()));
                context.Add(ChatMessage.CreateAssistantMessage(msg));
                Console.WriteLine(msg);
            }
            else if (result.FinishReason == ChatFinishReason.ToolCalls)
            {
                skipUserInput = true;
                context.Add(ChatMessage.CreateAssistantMessage(result));
                foreach (var toolCall in result.ToolCalls)
                {
                    string toolResult;
                    try
                    {
                        if (toolCall.FunctionName == "Read")
                        {
                            var arg = System.Text.Json.JsonSerializer.Deserialize<ReadArguments>(toolCall.FunctionArguments, camelCaseOption);
                            Console.WriteLine($"Read({arg?.Path ?? "null"})");
                            toolResult = File.ReadAllText(arg?.Path ?? "");
                        }
                        else if (toolCall.FunctionName == "Write")
                        {
                            var arg = System.Text.Json.JsonSerializer.Deserialize<WriteArguments>(toolCall.FunctionArguments, camelCaseOption);
                            Console.WriteLine($"Write({arg?.Path ?? "null"})");
                            Console.Write("allow? [y/N]: ");
                            var allow = Console.ReadLine()?.ToLower().StartsWith('y') ?? false;
                            toolResult = allow ? "complete" : "denied by user";
                            if (allow)
                            {
                                File.WriteAllText(arg?.Path ?? "", arg?.Content ?? "");
                            }
                        }
                        else if (toolCall.FunctionName == "List")
                        {
                            var arg = System.Text.Json.JsonSerializer.Deserialize<ListArguments>(toolCall.FunctionArguments, camelCaseOption);
                            Console.WriteLine($"List({arg?.Path ?? "null"})");
                            toolResult = string.Join('\n', string.Join("/\n", Directory.GetDirectories(arg?.Path ?? "")), string.Join('\n', Directory.GetFiles(arg?.Path ?? "")));
                        }
                        else
                        {
                            toolResult = "error: undefined tool";
                        }
                    }
                    catch (Exception e)
                    {
                        toolResult = e.Message;
                    }

                    context.Add(ChatMessage.CreateToolMessage(toolCall.Id, toolResult));
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(toolResult.TrimEnd());
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(result.FinishReason);
                Console.ResetColor();
            }
        }
    }

    record ReadArguments(string Path);
    record WriteArguments(string Path, string Content);
    record ListArguments(string Path);
}
