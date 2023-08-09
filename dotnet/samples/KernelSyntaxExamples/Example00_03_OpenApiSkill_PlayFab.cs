// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.OpenAPI.Authentication;
using Microsoft.SemanticKernel.Skills.OpenAPI.Extensions;
using RepoUtils;
using Microsoft.SemanticKernel.Planning;
using Newtonsoft.Json;
using Microsoft.SemanticKernel.Skills.Core;

/// <summary>
/// This example shows how to import PlayFab APIs as skills.
/// </summary>
// ReSharper disable once InconsistentNaming
public static class Example_00_03_OpenApiSkill_PlayFab
{
    public static async Task RunAsync()
    {
        var goals = new string[]
            {
                "Create a segment with name NewPlayersSegment for the players first logged in date greater than 2023-08-01?",
                "Create a segment with name LegacyPlayersSegment for the players last logged in date less than 2023-05-01?",
                "Create a segment with name EgyptNewPlayers for the players located in the Egypt?",
                "Create a segment with name EgyptNewPlayers for the players located in the Egypt?" // If the segment already exist, create a segment with name appended with guid
            };
        await CreateSegmentExample(goals[2]);
        //await GetSegmentsExample();
    }

    private static async Task GetSegmentsExample()
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

    private static async Task CreateSegmentExample(string goal)
    {
        using HttpClient httpClient = new();

        // Create a segment skill
        {
            Console.WriteLine("======== Action Planner ========");
            var kernel2 = new KernelBuilder()
                .WithLogger(ConsoleLogger.Logger)
                .WithAzureTextCompletionService("text-davinci-003", TestConfiguration.AzureOpenAI.Endpoint, TestConfiguration.AzureOpenAI.ApiKey) // Note: Action Planner works with old models like text-davinci-002
                .Build();

            string folder = RepoFiles.SampleSkillsPath();
            kernel2.ImportSkill(new SegmentSkill(), "SegmentSkill");

            // Create an instance of ActionPlanner.
            // The ActionPlanner takes one goal and returns a single function to execute.
            var planner = new ActionPlanner(kernel2);

            // We're going to ask the planner to find a function to achieve this goal.
            //var goal = "Create a segment with name NewPlayersSegment for the players first logged in date greater than 2023-08-01?";
            Console.WriteLine("Goal: " + goal);

            // The planner returns a plan, consisting of a single function
            // to execute and achieve the goal requested.
            var plan = await planner.CreatePlanAsync(goal);

            // Execute the full plan (which is a single function)
            SKContext result = await plan.InvokeAsync();

            // Show the result, which should match the given goal
            Console.WriteLine(result);
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
