// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Skills.Core;
using RepoUtils;

public static class Example00_02_PlayFabGenerative
{
    public static async Task RunAsync()
    {
        var goals = new string[]
            {
                "Create a segment with name NewPlayersSegment for the players first logged in date greater than 2023-08-01?", // Working
                "Create a segment with name LegacyPlayersSegment for the players last logged in date less than 2023-05-01?", // Working
                "Create a segment with name EgyptNewPlayers for the players located in the Egypt?", // Working
                "Create a segment for china for the players logged in the last 30 days and grant them 10 virtual currency?",
                "Create a segment with name WelcomeEgyptNewPlayers for the players located in the Egypt with entered segment action of email notification?", // With entered segment action
                "Create a segment with name EgyptNewPlayers for the players located in the Egypt?" // If the segment already exist, create a segment with name appended with guid
            };
        await CreateSegmentExample(goals[0]);
    }

    private static async Task CreateSegmentExample(string goal)
    {
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
            plan.Steps[0].Parameters = plan.Parameters;

            // Execute the full plan (which is a single function)
            SKContext result = await plan.InvokeAsync(kernel2.CreateNewContext());

            // Show the result, which should match the given goal
            Console.WriteLine(result);
        }
    }
}
