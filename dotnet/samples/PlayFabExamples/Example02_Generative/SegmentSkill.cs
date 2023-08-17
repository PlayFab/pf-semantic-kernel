// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.OpenAPI.Authentication;
using Microsoft.SemanticKernel.Skills.OpenAPI.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFabExamples.Common.Configuration;
using PlayFabExamples.Common.Logging;

namespace PlayFabExamples.Example02_Generative;

/// <summary>
/// Create a segment for given input prompt / question.
/// </summary>
public sealed class SegmentSkill
{
    /// <summary>
    /// Create segment function for given input prompt / question.
    /// </summary>
    /// <param name="inputPrompt">Input prompt for create segment skill.</param>
    /// <returns>Status of segment creation.</returns>
    [SKFunction, Description("Create segment for given input prompt / question.")]
    public async Task<string> CreateSegment(string inputPrompt)
    {
        // Step 1: Generate payload content to create a segment.
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

        ISKFunction playfabJsonFunction = kernel.CreateSemanticFunction(FunctionDefinition, temperature: 0.1, topP: 0.1);
        SKContext result = await playfabJsonFunction.InvokeAsync(inputPrompt);
        string payload = result.Result.Substring(result.Result.IndexOf('{'), result.Result.LastIndexOf('}') - result.Result.IndexOf('{') + 1);
        string newPayload = await GenerateDistinctSegmentName(payload);
        Console.WriteLine(newPayload);

        // Step 2: Create a segment using above generated payload.
        ContextVariables contextVariables = new();
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("server_url", TestConfiguration.PlayFab.Endpoint);
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("payload", newPayload);

        using HttpClient httpClient = new();
        IDictionary<string, ISKFunction> playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);
        SKContext result2 = await kernel.RunAsync(contextVariables, playfabApiSkills["CreateSegment"]);

        string formattedContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(result2.Result), Formatting.Indented);
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
    /// Get current title segments.
    /// </summary>
    /// <returns>List of segments.</returns>
    private async Task<List<Segment>> GetSegments()
    {
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Logger).Build();
        ContextVariables contextVariables = new();
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("server_url", TestConfiguration.PlayFab.Endpoint);
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("payload", "{ \"SegmentIds\": [] }");

        using HttpClient httpClient = new();
        IDictionary<string, ISKFunction> playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);
        SKContext result = await kernel.RunAsync(contextVariables, playfabApiSkills["GetSegments"]);

        string formattedContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(result.Result), Formatting.Indented);
        JObject segmentsDataObject = JObject.Parse(formattedContent);
        string? content = ((Newtonsoft.Json.Linq.JValue)segmentsDataObject.GetValue("content")).Value.ToString();
        JObject segmentsDataObject2 = JObject.Parse(content);
        string segmentsArrayContent = segmentsDataObject2.GetValue("data").SelectTokens("$.Segments").First().ToString();

        return System.Text.Json.JsonSerializer.Deserialize<List<Segment>>(segmentsArrayContent);
    }

    /// <summary>
    /// To create a segment, segment name should be disitinct.
    /// </summary>
    /// <param name="payload">Segment creation payload.</param>
    /// <returns>Updated payload.</returns>
    private async Task<string> GenerateDistinctSegmentName(string payload)
    {
        List<Segment> segments = await GetSegments();
        string segmentsString = string.Join("\n ", segments.Select(segment => segment.Name.Trim()));

        string FunctionDefinition = @"Update the payload segment model name in the below payload json with unique name and name should not be in the below list.
{{segmentList}}

Payload:
{{payload}}

{{$input}}"
.Replace("{{payload}}", payload, StringComparison.OrdinalIgnoreCase)
.Replace("{{segmentList}}", segmentsString, StringComparison.OrdinalIgnoreCase);

        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Logger)
            .WithAzureChatCompletionService(TestConfiguration.AzureOpenAI.ChatDeploymentName, TestConfiguration.AzureOpenAI.Endpoint, TestConfiguration.AzureOpenAI.ApiKey, alsoAsTextCompletion: true, setAsDefault: true)
            .Build();
        ISKFunction playfabJsonFunction = kernel.CreateSemanticFunction(FunctionDefinition, temperature: 0.1, topP: 0.1);
        SKContext result = await playfabJsonFunction.InvokeAsync("Update payload segment model json by distinct segment name.");
        string newPayload = result.Result.Substring(result.Result.IndexOf('{'), result.Result.LastIndexOf('}') - result.Result.IndexOf('{') + 1);

        return newPayload;
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
