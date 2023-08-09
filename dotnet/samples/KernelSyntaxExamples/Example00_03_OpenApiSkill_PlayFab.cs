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
        await SkillImportExample();
    }

    private static async Task SkillImportExample2()
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

    private static async Task SkillImportExample()
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
            //kernel2.ImportSemanticSkillFromDirectory(folder, "WriterSkill");
            //var playfabApiSkills = await GetPlayFabSkill(kernel2, httpClient);
            //kernel2.ImportSkill(playfabApiSkills);

            // Create an instance of ActionPlanner.
            // The ActionPlanner takes one goal and returns a single function to execute.
            var planner = new ActionPlanner(kernel2);

            // We're going to ask the planner to find a function to achieve this goal.
            var goal = "Create a segment with name MySegment for the players first logged in date greater than 2023-05-01?";

            // The planner returns a plan, consisting of a single function
            // to execute and achieve the goal requested.
            var plan = await planner.CreatePlanAsync(goal);

            // Execute the full plan (which is a single function)
            SKContext result = await plan.InvokeAsync();

            // Show the result, which should match the given goal
            Console.WriteLine(result);

            /* Output:
             *
             * Cleopatra was a queen
             * But she didn't act like one
             * She was more like a teen

             * She was always on the scene
             * And she loved to be seen
             * But she didn't have a queenly bone in her body
             */




            // Set properties to create a Segment using swagger.json
            //contextVariables.Set("content_type", "application/json");
            //Guid guid = Guid.NewGuid();
            //string segmentPayload = "{\n  \"SegmentModel\": {\n \"Name\": \"<SegmentName>\",\n \"SegmentOrDefinitions\": [\n {\n \"SegmentAndDefinitions\": [\n {\n \"FirstLoginDateFilter\": {\n \"LogInDate\": \"2011-12-31T00:00:00Z\",\n \"Comparison\": \"GreaterThan\"\n }\n }\n ]\n }\n ]\n }\n  }";
            //segmentPayload = segmentPayload.Replace("<SegmentName>", Guid.NewGuid().ToString());
            //contextVariables.Set("payload", segmentPayload);
            //
            //// Run operation via the semantic kernel
            //var result2 = await kernel.RunAsync(contextVariables, playfabApiSkills["CreateSegment"]);
            //
            //Console.WriteLine("\n\n\n");
            //var formattedContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(result2.Result), Formatting.Indented);
            //Console.WriteLine("CreateSegment playfabApiSkills response: \n{0}", formattedContent);
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
