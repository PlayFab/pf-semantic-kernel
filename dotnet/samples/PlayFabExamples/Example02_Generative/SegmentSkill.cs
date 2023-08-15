// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.OpenAPI.Authentication;
using Microsoft.SemanticKernel.Skills.OpenAPI.Extensions;
using Newtonsoft.Json;
using PlayFabExamples.Common.Configuration;
using PlayFabExamples.Common.Logging;

namespace PlayFabExamples.Example02_Generative;

/// <summary>
/// Create a segment with given information.
/// </summary>
public sealed class SegmentSkill
{
    /// <summary>
    /// Create a segment using playfab open api json content.
    /// </summary>
    /// <param name="inputPrompt">Input prompt.</param>
    /// <returns>Status of segment creation.</returns>
    public async Task<string> CreateSegmentUsingOpenAPI(string inputPrompt)
    {
        // Step 1: Generate payload content to create a segment.
        Console.WriteLine(inputPrompt);
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Logger)
            .WithAzureChatCompletionService(TestConfiguration.AzureOpenAI.ChatDeploymentName, TestConfiguration.AzureOpenAI.Endpoint, TestConfiguration.AzureOpenAI.ApiKey, alsoAsTextCompletion: true, setAsDefault: true)
            .Build();
        SKContext context = kernel.CreateNewContext();
        string miniJson = await GetMinifiedOpenApiJson();

        string FunctionDefinition = @"
You are an AI assistant for generating PlayFab input payload for given api. You have access to the full OpenAPI 3.0.1 specification.

Api Spec:
{{$apiSpec}}

The CreateSegment operation in PlayFab Admin API requires a CreateSegmentRequest payload input.
For FirstLoginFilter and LastLoginFilter, if the input value is days, convert value into minutes.
Segment model name should be meaningful name from the input question.
Don't provide any description about the answer. Only provide json payload content.
Don't provide notes like below.
Note: 30 days converted to minutes is 43200

Example:
Question: Create a segment for the players first logged in date greater than 2023-05-01?
Answer: 
{
  ""SegmentModel"": {
    ""Name"": ""FirstLoggedInPlayers"",
    ""SegmentOrDefinitions"": [
      {
        ""SegmentAndDefinitions"": [
          {
            ""FirstLoginDateFilter"": {
              ""LogInDate"": ""2023-05-01T00:00:00Z"",
              ""Comparison"": ""GreaterThan""
            }
          }
        ]
      }
    ]
  }
}

Question:
{{$input}}"
.Replace("{{$apiSpec}}", miniJson, StringComparison.OrdinalIgnoreCase);

        var playfabJsonFunction = kernel.CreateSemanticFunction(FunctionDefinition, temperature: 0.1, topP: 0.1);
        var result = await playfabJsonFunction.InvokeAsync(inputPrompt);
        var payload = result.Result.Substring(result.Result.IndexOf('{'), result.Result.LastIndexOf('}') - result.Result.IndexOf('{') + 1);
        Console.WriteLine(payload);

        // Step 2: Create a segment using above generated payload.
        ContextVariables contextVariables = new();
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("server_url", TestConfiguration.PlayFab.Endpoint);
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("payload", payload);

        using HttpClient httpClient = new();
        var playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);
        var result2 = await kernel.RunAsync(contextVariables, playfabApiSkills["CreateSegment"]);

        var formattedContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(result2.Result), Formatting.Indented);
        Console.WriteLine("\nCreateSegment playfabApiSkills response: \n{0}", formattedContent);

        return $"Segment created successfully";
    }

    /// <summary>
    /// Minimizing jsob by removing spaces and new lines.
    /// </summary>
    /// <returns>Minimized json content.</returns>
    /// <exception cref="FileNotFoundException">File not found exception.</exception>
    private static Task<string> GetMinifiedOpenApiJson()
    {
        var playfabApiFile = "./Example02_Generative/SegmentOpenAPIs.json";

        if (!File.Exists(playfabApiFile))
        {
            throw new FileNotFoundException($"Invalid URI. The specified path '{playfabApiFile}' does not exist.");
        }

        var playfabOpenAPIContent = File.ReadAllText(playfabApiFile);
        return Task.FromResult(JsonConvert.SerializeObject(JsonConvert.DeserializeObject(playfabOpenAPIContent), Formatting.None));
    }

    /// <summary>
    /// Read a file
    /// </summary>
    /// <example>
    /// {{file.readAsync $path }} => "hello world"
    /// </example>
    /// <param name="path"> Source file </param>
    /// <returns> File content </returns>
    [SKFunction, Description("Create a segment using prompt and parsing prompt")]
    public async Task<string> CreateSegment([Description("Name of the segment.")] string segmentname,
        [Description("Name of the segment definition. Some of the examples are FirstLoginDateFilter, LastLoginDateFilter, LocationFilter.")] string segmentdefinition,
        [Description("Name of the segment comparison. Some of the examples are GreaterThan, LessThan, Equals.")] string segmentcomparison,
        [Description("Value of the segment comparison. Some of the examples are 2023-08-01, India, Australia, Kenya. For country get 2 letter country code instead of country name.")] string segmentcomparisonvalue,
        [Description("Name of the segment action. Examples are GrantVirtualCurrencyAction, EmailNotificationAction. This is empty if there is no segment action.")] string segmentaction,
        [Description("Name of the segment action key or code. Examples are virtual currency code, email template id. This is empty if there is no segment action.")] string segmentactioncode,
        [Description("Name of the segment action key value or code value. Examples are template name, virtual currency amount. This is empty if there is no segment action.")] string segmentactionvalue
        )
    {
        //ToDo: Create payload json using Playfab dlls/sdk
        // Set properties to create a Segment using swagger.json
        ContextVariables contextVariables = new();
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("server_url", TestConfiguration.PlayFab.Endpoint);
        string segmentPayload = GetSegmentPayload(segmentname, segmentdefinition, segmentcomparison, segmentcomparisonvalue, segmentaction, segmentactioncode, segmentactionvalue);

        contextVariables.Set("payload", segmentPayload);
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Logger).Build();
        using HttpClient httpClient = new();
        var playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);

        // Run operation via the semantic kernel
        var result2 = await kernel.RunAsync(contextVariables, playfabApiSkills["CreateSegment"]);

        Console.WriteLine("\n\n\n");
        var formattedContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(result2.Result), Formatting.Indented);
        Console.WriteLine("CreateSegment playfabApiSkills response: \n{0}", formattedContent);

        return $"Segment {segmentname} created with segment definition {segmentdefinition}";
    }

    private static string GetSegmentPayload(string segmentname, string segmentdefinition, string segmentcomparison, string segmentcomparisonvalue, string segmentaction, string segmentactioncode, string segmentactionvalue)
    {
        //ToDo: Need to explore and replace this logic with open ai chat.
        string segmentPayload = "{\n  \"SegmentModel\": {\n \"Name\": \"<SegmentName>\",\n \"SegmentOrDefinitions\": [\n {\n \"SegmentAndDefinitions\": [\n {\n \"<SegmentDefinition>\": {\n \"LogInDate\": \"<SegmentComparisonValue>T00:00:00Z\",\n \"Comparison\": \"<SegmentComparison>\"\n }\n }\n ]\n }\n ]\n <EnteredSegmentAction> }\n }";
        string locationPayload = "{\n  \"SegmentModel\": {\n \"Name\": \"<SegmentName>\",\n \"SegmentOrDefinitions\": [\n {\n \"SegmentAndDefinitions\": [\n {\n \"<SegmentDefinition>\": {\n \"CountryCode\": \"<SegmentComparisonValue>\",\n \"Comparison\": \"<SegmentComparison>\"\n }\n }\n ]\n }\n ]\n <EnteredSegmentAction> }\n }";

        if (segmentdefinition == "LocationFilter")
        {
            segmentPayload = locationPayload;
        }

        if (!string.IsNullOrEmpty(segmentaction) && segmentaction == "GrantVirtualCurrencyAction")
        {
            string enteredSegmentAction = $", \"EnteredSegmentActions\": [\n {{\n \"GrantVirtualCurrencyAction\": {{\n \"CurrencyCode\": \"{segmentactioncode}\",\n \"Amount\": {segmentactionvalue}\n }}\n }}\n ]";
            segmentPayload = segmentPayload.Replace("<EnteredSegmentAction>", enteredSegmentAction);
        }
        else
        {
            segmentPayload = segmentPayload.Replace("<EnteredSegmentAction>", string.Empty);
        }

        segmentPayload = segmentPayload.Replace("<SegmentName>", segmentname);
        segmentPayload = segmentPayload.Replace("<SegmentDefinition>", segmentdefinition);
        segmentPayload = segmentPayload.Replace("<SegmentComparison>", segmentcomparison);
        segmentPayload = segmentPayload.Replace("<SegmentComparisonValue>", segmentcomparisonvalue);
        return segmentPayload;
    }

    private static async Task<IDictionary<string, ISKFunction>> GetPlayFabSkill(IKernel kernel, HttpClient httpClient)
    {
        IDictionary<string, ISKFunction> playfabApiSkills;

        var titleSecretKeyProvider = new PlayFabAuthenticationProvider(() =>
        {
            string s = TestConfiguration.PlayFab.TitleSecretKey;
            return Task.FromResult(s);
        });

        bool useLocalFile = true;
        if (useLocalFile)
        {
            var playfabApiFile = "./Example02_Generative/SegmentOpenAPIs.json";
            playfabApiSkills = await kernel.ImportOpenApiSkillFromFileAsync("PlayFabApiSkill", playfabApiFile, new OpenApiSkillExecutionParameters(httpClient, authCallback: titleSecretKeyProvider.AuthenticateRequestAsync));
        }
        else
        {
            var playfabApiRawFileUrl = new Uri(TestConfiguration.PlayFab.SwaggerEndpoint);
            playfabApiSkills = await kernel.ImportOpenApiSkillFromUrlAsync("PlayFabApiSkill", playfabApiRawFileUrl, new OpenApiSkillExecutionParameters(httpClient, authCallback: titleSecretKeyProvider.AuthenticateRequestAsync));
        }

        return playfabApiSkills;
    }
}
