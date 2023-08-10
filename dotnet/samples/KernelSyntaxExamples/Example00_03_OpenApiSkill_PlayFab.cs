// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Planning.Stepwise;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.OpenAPI.Authentication;
using Microsoft.SemanticKernel.Skills.OpenAPI.Extensions;
using Newtonsoft.Json;
using RepoUtils;

/// <summary>
/// This example shows how to import PlayFab APIs as skills.
/// </summary>
// ReSharper disable once InconsistentNaming
public static class Example_00_03_OpenApiSkill_PlayFab
{
    public static async Task RunAsync()
    {
        // Simple example
        await SkillImportExample();

        // Example semantic skill for generating PlayFab segments
        string[] questions = new string[]
        {
             "How do I create a segment for Android players in Canada?",
             "How do I create a segment for players with high risk of churn who have spent over $100?"
        };

        foreach (string q in questions)
        {
            try
            {
                Console.WriteLine("Question: " + q);
                await JsonExample(q);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }


        // Examples for native skill calling PlayFab APIs
        questions = new string[]
        {
             "Get the details for all my PlayFab segments",
             "Do I have a segment that filters for Canadian players?",
        };

        foreach (string q in questions)
        {
            try
            {
                Console.WriteLine("Question: " + q);
                await PlannerExample(q);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private static async Task SkillImportExample()
    {
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Logger).Build();
        var contextVariables = new ContextVariables();

        contextVariables.Set("server_url", TestConfiguration.PlayFab.Endpoint);

        using HttpClient httpClient = new();

        var playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);

        // GetSegments skill
        {
            // Set properties for the Get Segments operation in the openAPI.swagger.json
            contextVariables.Set("content_type", "application/json");
            contextVariables.Set("payload", "{ \"SegmentIds\": [] }");

            // Run operation via the semantic kernel
            var result = await kernel.RunAsync(contextVariables, playfabApiSkills["GetSegments"]);

            Console.WriteLine("\n\n\n");
            var formattedContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(result.Result), Formatting.Indented);
            Console.WriteLine("GetSegments playfabApiSkills response: \n{0}", formattedContent);
        }
    }

    private static async Task PlannerExample(string question)
    {
        var kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Logger)
            .WithAzureChatCompletionService(TestConfiguration.AzureOpenAI.ChatDeploymentName, TestConfiguration.AzureOpenAI.Endpoint, TestConfiguration.AzureOpenAI.ApiKey, alsoAsTextCompletion: true, setAsDefault: true)
            .Build();

        SKContext context = kernel.CreateNewContext();

        using HttpClient httpClient = new();

        var playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);

        var plannerConfig = new StepwisePlannerConfig();
        plannerConfig.ExcludedFunctions.Add("TranslateMathProblem");
        plannerConfig.MinIterationTimeMs = 1500;
        plannerConfig.MaxTokens = 1000;
        plannerConfig.MaxIterations = 10;

        var settings = new CompleteRequestSettings { Temperature = 0.1, MaxTokens = 500 };

        StepwisePlanner planner = new(kernel, plannerConfig);
        Plan plan = planner.CreatePlan(question);

        SKContext? result = await plan.InvokeAsync(context, settings);

        Console.WriteLine("Result: " + result);
        if (result.Variables.TryGetValue("stepCount", out string? stepCount))
        {
            Console.WriteLine("Steps Taken: " + stepCount);
        }

        if (result.Variables.TryGetValue("skillCount", out string? skillCount))
        {
            Console.WriteLine("Skills Used: " + skillCount);
        }
    }

    private static async Task JsonExample(string question)
    {
        var kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Logger)
            .WithAzureChatCompletionService(TestConfiguration.AzureOpenAI.ChatDeploymentName, TestConfiguration.AzureOpenAI.Endpoint, TestConfiguration.AzureOpenAI.ApiKey, alsoAsTextCompletion: true, setAsDefault: true)
            .Build();

        SKContext context = kernel.CreateNewContext();

        string miniJson = await GetMinifiedOpenApiJson();

        string FunctionDefinition = @"
You are an AI assistant for generating PlayFab segment API requests. You have access to the full OpenAPI 3.0.1 specification.
If you do not know how to answer the question, reply with 'I cannot answer this'.

Api Spec:
{{$apiSpec}}

Example: How do I create a segment for Canadian players?
Answer: To create a segment for Canadian players, you would use the `CreateSegment` API and include a LocationSegmentFilter definition with the country set to Canada.

Question:
{{$input}}".Replace("{{$apiSpec}}", miniJson, StringComparison.OrdinalIgnoreCase);

        var playfabJsonFunction = kernel.CreateSemanticFunction(FunctionDefinition, temperature: 0.1, topP: 1);

        var result = await playfabJsonFunction.InvokeAsync(question);

        Console.WriteLine("Result: " + result);
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
            var playfabApiFile = "../../../Skills/PlayFabApiSkill/openapi_updated.json";
            var parameters = new OpenApiSkillExecutionParameters(httpClient, authCallback: titleSecretKeyProvider.AuthenticateRequestAsync, serverUrlOverride: new Uri(TestConfiguration.PlayFab.Endpoint));

            playfabApiSkills = await kernel.ImportAIPluginAsync("PlayFabApiSkill", playfabApiFile, parameters);
        }
        else
        {
            var playfabApiRawFileUrl = new Uri(TestConfiguration.PlayFab.SwaggerEndpoint);
            var parameters = new OpenApiSkillExecutionParameters(httpClient, authCallback: titleSecretKeyProvider.AuthenticateRequestAsync, serverUrlOverride: new Uri(TestConfiguration.PlayFab.Endpoint));

            playfabApiSkills = await kernel.ImportAIPluginAsync("PlayFabApiSkill", playfabApiRawFileUrl, parameters);
        }

        return playfabApiSkills;
    }

    private static async Task<string> GetMinifiedOpenApiJson()
    {
        var playfabApiFile = "../../../Skills/PlayFabApiSkill/openapi_updated.json";

        var pluginJson = string.Empty;

        if (!File.Exists(playfabApiFile))
        {
            throw new FileNotFoundException($"Invalid URI. The specified path '{playfabApiFile}' does not exist.");
        }

        using (var sr = File.OpenText(playfabApiFile))
        {
            pluginJson = await sr.ReadToEndAsync().ConfigureAwait(false); //must await here to avoid stream reader being disposed before the string is read
        }

        return JsonConvert.SerializeObject(JsonConvert.DeserializeObject(pluginJson), Formatting.None);
    }
}
