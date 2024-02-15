// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

using ILoggerFactory loggerFactory = LoggerFactory.Create(Builder =>
{
    Builder
        .SetMinimumLevel(0)
        .AddDebug();
});

// Create kernel
var builder = Kernel.CreateBuilder();
// Add a text or chat completion service using either:
// builder.Services.AddAzureOpenAIChatCompletion()
// builder.Services.AddAzureOpenAITextGeneration()
// builder.Services.AddOpenAIChatCompletion()
// builder.Services.AddOpenAITextGeneration()
builder.WithCompletionService();
builder.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Trace));

var kernel = builder.Build();

// Create chat history
ChatHistory history = [];

// Create choices
List<string> choices = ["ContinueConversation", "EndConversation"];

// Create few-shot examples
List<ChatHistory> fewShotExamples = [
    [
        new ChatMessageContent(AuthorRole.User, "Can you send a very quick approval to the marketing team?"),
        new ChatMessageContent(AuthorRole.System, "Intent:"),
        new ChatMessageContent(AuthorRole.Assistant, "ContinueConversation")
    ],
    [
        new ChatMessageContent(AuthorRole.User, "Thanks, I'm done for now"),
        new ChatMessageContent(AuthorRole.System, "Intent:"),
        new ChatMessageContent(AuthorRole.Assistant, "EndConversation")
    ]
];

// Create handlebars template for intent
var getIntent = kernel.CreateFunctionFromPrompt(
    new()
    {
        Template = @"
        <message role=""system"">Instructions: What is the intent of this request?
        Do not explain the reasoning, just reply back with the intent. If you are unsure, reply with {{choices[0]}}.
        Choices: {{choices}}.</message>

        {{#each fewShotExamples}}
            {{#each this}}
                <message role=""{{role}}"">{{content}}</message>
            {{/each}}
        {{/each}}

        {{#each history}}
            <message role=""{{role}}"">{{content}}</message>
        {{/each}}

        <message role=""user"">{{request}}</message>
        <message role=""system"">Intent:</message>",
        TemplateFormat = "handlebars"
    },
    new HandlebarsPromptTemplateFactory()
);

// Create a Semantic Kernel template for chat
var chat = kernel.CreateFunctionFromPrompt(
    @"{{$history}}
    User: {{$request}}
    Assistant1: "
);

// Start the chat loop
// Create another kernel for the third agent
var builder2 = Kernel.CreateBuilder();
builder2.WithCompletionService();
builder2.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Trace));

var kernel2 = builder2.Build();

// Create a Semantic Kernel template for chat for the third agent
var chat2 = kernel2.CreateFunctionFromPrompt(
    @"{{$history}}
    User: {{$request}}
    Assistant2: "
);

// Start the chat loop
while (true)
{
    // Get user input
    Console.Write("User > ");
    var request = Console.ReadLine();

    // Invoke prompt
    var intent = await kernel.InvokeAsync(
        getIntent,
        new() {
            { "request", request },
            { "choices", choices },
            { "history", history },
            { "fewShotExamples", fewShotExamples }
        }
    );

    // End the chat if the intent is "Stop"
    if (intent.ToString() == "EndConversation")
    {
        break;
    }

    // Get chat response
    var chatResult = kernel.InvokeStreamingAsync<StreamingChatMessageContent>(
        chat,
        new() {
            { "request", request },
            { "history", string.Join("\n", history.Select(x => x.Role + ": " + x.Content)) }
        }
    );

    // Get chat response for the third agent
    var chatResult2 = kernel2.InvokeStreamingAsync<StreamingChatMessageContent>(
        chat2,
        new() {
            { "request", request },
            { "history", string.Join("\n", history.Select(x => x.Role + ": " + x.Content)) }
        }
    );

    // Stream the response
    string message = "";
    await foreach (var chunk in chatResult)
    {
        if (chunk.Role.HasValue) Console.Write(chunk.Role + " > ");
        message += chunk;
        Console.Write(chunk);
    }
    Console.WriteLine();

    // Stream the response for the third agent
    string message2 = "";
    await foreach (var chunk in chatResult2)
    {
        if (chunk.Role.HasValue) Console.Write(chunk.Role + " > ");
        message2 += chunk;
        Console.Write(chunk);
    }
    Console.WriteLine();

    // Append to history
    history.AddUserMessage(request!);
    history.AddAssistantMessage(message);
    history.AddAssistantMessage(message2); // Add the third agent's message to the history
}
