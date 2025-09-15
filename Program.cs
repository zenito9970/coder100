using OpenAI;
using OpenAI.Chat;

class Program
{
    static void Main(string[] args)
    {
        var credential = new System.ClientModel.ApiKeyCredential("lm-studio");
        var option = new OpenAIClientOptions();
        option.Endpoint = new Uri("http://localhost:1234/v1");
        var client = new OpenAIClient(credential, option);
        var chat = client.GetChatClient("openai/gpt-oss-20b");

        var camelCaseOption = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var readTool = ChatTool.CreateFunctionTool(
            functionName: nameof(Read),
            functionDescription: "read contents of specified file.",
            functionParameters: BinaryData.FromObjectAsJson(new
                {
                    Type = "object",
                    Properties = new
                    {
                        Path = new
                        {
                            Type = "string",
                            Description = "relative filepath, e.g. ./src/main.c",
                        },
                    },
                },
                camelCaseOption));

        var writeTool = ChatTool.CreateFunctionTool(
            functionName: nameof(Write),
            functionDescription:
            "Writes the content to the file. If the file does not exist, it will be newly created.",
            functionParameters: BinaryData.FromObjectAsJson(new
                {
                    Type = "object",
                    Properties = new
                    {
                        Type = "object",
                        Properties = new
                        {
                            Path = new
                            {
                                Type = "string",
                                Description = "relative filepath, e.g. ./src/main.c",
                            },
                            Content = new
                            {
                                Type = "string",
                                Description = "The content to write to the file. Any existing content will be discarded and overwritten with this content.",
                            },
                        },
                    },
                },
                camelCaseOption));

        var listTool = ChatTool.CreateFunctionTool(
            functionName: nameof(List),
            functionDescription: "Get a list of files and directories in the specified directory.",
            functionParameters: BinaryData.FromObjectAsJson(new
                {
                    Type = "object",
                    Properties = new
                    {
                        Path = new
                        {
                            Type = "string",
                            Description = "relative directory path, e.g. ./src/internal",
                        },
                    },
                },
                camelCaseOption));

        var options = new ChatCompletionOptions()
        {
            AllowParallelToolCalls = false,
            ToolChoice = ChatToolChoice.CreateAutoChoice(),
            Tools = { readTool, writeTool, listTool },
        };

        var context = new List<ChatMessage>();
        context.Add(ChatMessage.CreateSystemMessage("You are an interactive CLI tool that helps users with software engineering tasks. Use the instructions below and the tools available to you to assist the user."));

        var skipUserInput = false;
        while (true)
        {
            if (!skipUserInput)
            {
                Console.Write("> ");
                var prompt = Console.ReadLine();
                if (prompt.StartsWith("/exit"))
                {
                    break;
                }

                context.Add(ChatMessage.CreateUserMessage(prompt));
            }
            skipUserInput = false;

            var result = chat.CompleteChat(context, options);
            switch (result.Value.FinishReason)
            {
                case ChatFinishReason.Stop:
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var content in result.Value.Content)
                    {
                        switch (content.Kind)
                        {
                            case ChatMessageContentPartKind.Text:
                            {
                                sb.AppendLine(content.Text);
                                break;
                            }
                            default:
                            {
                                break;
                            }
                        }
                    }
                    var message = sb.ToString();
                    context.Add(ChatMessage.CreateAssistantMessage(message));
                    Console.WriteLine(message);
                    break;
                }
                case ChatFinishReason.ToolCalls:
                {
                    skipUserInput = true;
                    context.Add(new AssistantChatMessage(result.Value));

                    foreach (var toolCall in result.Value.ToolCalls)
                    {
                        switch (toolCall.FunctionName)
                        {
                            case nameof(Read):
                            {
                                var arg = System.Text.Json.JsonSerializer.Deserialize<ReadArguments>(toolCall.FunctionArguments, camelCaseOption);
                                Console.WriteLine($"Read({arg?.Path ?? "null"})");
                                if (arg != null)
                                {
                                    var content = Read(arg.Path);
                                    context.Add(ChatMessage.CreateToolMessage(toolCall.Id, content));

                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.WriteLine(content);
                                    Console.ResetColor();
                                }
                                break;
                            }
                            case nameof(Write):
                            {
                                var arg = System.Text.Json.JsonSerializer.Deserialize<WriteArguments>(toolCall.FunctionArguments, camelCaseOption);
                                Console.WriteLine($"Write({arg?.Path ?? "null"})");
                                if (arg != null)
                                {
                                    //
                                }
                                break;
                            }
                            case nameof(List):
                            {
                                var arg = System.Text.Json.JsonSerializer.Deserialize<ListArguments>(toolCall.FunctionArguments, camelCaseOption);
                                Console.WriteLine($"List({arg?.Path ?? "null"})");
                                if (arg != null)
                                {
                                    var list = List(arg.Path);
                                    context.Add(ChatMessage.CreateToolMessage(toolCall.Id, list));

                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.WriteLine(list);
                                    Console.ResetColor();
                                }
                                break;
                            }
                        }
                    }

                    break;
                }
                default:
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(result.Value.FinishReason);
                    Console.ResetColor();
                    break;
                }
            }
        }
    }

    record ReadArguments(string Path);
    record WriteArguments(string Path,  string Content);
    record ListArguments(string Path);

    static string Read(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    static string Write(string path, string content)
    {
        return null;
    }

    static string List(string path)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            var dirs = Directory.GetDirectories(path);
            var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var dir in dirs) sb.AppendLine($"{dir}/");
            foreach (var file in files) sb.AppendLine($"{file}");
            return sb.ToString();
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
