// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Text.RegularExpressions;
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
        // await SkillImportExample();

        string[] questions = new string[]
        {
            // "Get the details for PlayFab segment 968016902A4DC242",
            "Get all my PlayFab segments",
            // "Create a segment named 'My Favorite Segment' for players who last logged in over 1 day ago",
        };

        foreach (string q in questions)
        {
            try
            {
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

        context.Variables.Set("content_type", "application/json");

        using HttpClient httpClient = new();

        var playfabApiSkills = await GetPlayFabSkill(kernel, httpClient);
        //kernel.ImportSkill(new ExtractHexIdSkill(), "ExtractHexadecimalId");

        var plannerConfig = new StepwisePlannerConfig();
        plannerConfig.ExcludedFunctions.Add("TranslateMathProblem");
        plannerConfig.MinIterationTimeMs = 1500;
        plannerConfig.MaxTokens = 1000;
        plannerConfig.MaxIterations = 5;

        StepwisePlanner planner = new(kernel, plannerConfig);
        Plan plan = planner.CreatePlan(question);

        var settings = new CompleteRequestSettings { Temperature = 0.1, MaxTokens = 250 };
        var result = await plan.InvokeAsync(context, settings);

        Console.WriteLine("Question: " + question);

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
            var playfabApiFile = "../../../Skills/PlayFabApiSkill/openapi.json";
            var parameters = new OpenApiSkillExecutionParameters(httpClient, authCallback: titleSecretKeyProvider.AuthenticateRequestAsync, serverUrlOverride: new Uri(TestConfiguration.PlayFab.Endpoint));

            playfabApiSkills = await kernel.ImportOpenApiSkillFromFileAsync("PlayFabApiSkill", playfabApiFile, parameters); ;
        }
        else
        {
            var playfabApiRawFileUrl = new Uri(TestConfiguration.PlayFab.SwaggerEndpoint);
            playfabApiSkills = await kernel.ImportOpenApiSkillFromUrlAsync("PlayFabApiSkill", playfabApiRawFileUrl, new OpenApiSkillExecutionParameters(httpClient, authCallback: titleSecretKeyProvider.AuthenticateRequestAsync)); ;
        }

        return playfabApiSkills;
    }
}
