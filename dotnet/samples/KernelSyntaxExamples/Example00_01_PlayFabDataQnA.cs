// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;
using Google.Apis.CustomSearchAPI.v1.Data;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Reliability;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.Core;
using Microsoft.SemanticKernel.Skills.Web;
using Microsoft.SemanticKernel.Skills.Web.Bing;
using NCalcSkills;
using NRedisStack.Graph;
using RepoUtils;
using Skills;
using static Azure.Core.HttpHeader;

/**
 * This example shows how to use Stepwise Planner to create a plan for a given goal.
 */

// ReSharper disable once InconsistentNaming
public static partial class Example00_01_PlayFabDataQnA
{
    public static async Task RunAsync()
    {
        string[] questions = new string[]
        {
            "If my number of players increases 30% overall in France, would be the impact over the overall number of monthly active players? Explain how you calculated that",
            "How many players played my game yesterday?",
            "What is the average number of players I had last week excluding Friday and Monday?",
        };

        foreach (var question in questions)
        {
            IKernel kernel = GetKernel(true);

           // var func = kernel.ImportSkill(new GameQuestionsAnsweringSkill(kernel), "AnswerQuestionAboutGameAndPlayers");
            // SKContext result = await kernel.RunAsync(question);
            // await Console.Out.WriteLineAsync(result.Result);
            /*try
            {
                await RunTextCompletion(question);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            try
            {
                await RunBaseChatCompletion(question);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }*/

            try
            {
                await RunNewChatCompletion(question);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private static async Task RunTextCompletion(string question)
    {
        Console.WriteLine("RunTextCompletion");
        var kernel = GetKernel();
        await RunWithQuestion(kernel, question, false);
    }

    private static async Task RunBaseChatCompletion(string question)
    {
        Console.WriteLine("RunBaseChatCompletion");
        var kernel = GetKernel(true);
        await RunWithQuestion(kernel, question, false);
    }

    private static async Task RunNewChatCompletion(string question)
    {
        Console.WriteLine("RunNewChatCompletion");
        var kernel = GetKernel(true);
        await RunWithQuestion(kernel, question, true);
    }

    private static async Task RunWithQuestion(IKernel kernel, string question, bool useChatStepwisePlanner)
    {
        var bingConnector = new BingConnector(TestConfiguration.Bing.ApiKey);
        var webSearchEngineSkill = new WebSearchEngineSkill(bingConnector);

        // kernel.ImportSkill(webSearchEngineSkill, "WebSearch");
        kernel.ImportSkill(new GameReportFetcherSkill(), "GameReportFetcher");
        kernel.ImportSkill(new InlineDataProcessorSkill(kernel), "InlineDataProcessor");

        kernel.ImportSkill(new LanguageCalculatorSkill(kernel), "advancedCalculator");
        // kernel.ImportSkill(new SimpleCalculatorSkill(kernel), "basicCalculator");
        kernel.ImportSkill(new TimeSkill(), "time");

        Console.WriteLine("*****************************************************");
        Console.WriteLine("Question: " + question);
        Plan plan;

        Stopwatch sw = Stopwatch.StartNew();
        if (useChatStepwisePlanner)
        {
            var plannerConfig = new Microsoft.SemanticKernel.Planning.Stepwise.StepwisePlannerConfig();
            plannerConfig.ExcludedFunctions.Add("TranslateMathProblem");
            plannerConfig.MinIterationTimeMs = 1500;
            plannerConfig.MaxTokens = 12000;

            ChatStepwisePlanner planner = new(kernel, plannerConfig);

            plan = planner.CreatePlan(question);
        }
        else
        {
            var plannerConfig = new Microsoft.SemanticKernel.Planning.Stepwise.StepwisePlannerConfig();
            plannerConfig.ExcludedFunctions.Add("TranslateMathProblem");
            plannerConfig.MinIterationTimeMs = 1500;
            plannerConfig.MaxTokens = 12000;

            StepwisePlanner planner = new(kernel, plannerConfig);

            plan = planner.CreatePlan(question);
        }
        var result = await plan.InvokeAsync(kernel.CreateNewContext());
        Console.WriteLine("Result: " + result);
        if (result.Variables.TryGetValue("stepCount", out string? stepCount))
        {
            Console.WriteLine("Steps Taken: " + stepCount);
        }

        if (result.Variables.TryGetValue("skillCount", out string? skillCount))
        {
            Console.WriteLine("Skills Used: " + skillCount);
        }

        Console.WriteLine("Time Taken: " + sw.Elapsed);
        Console.WriteLine("*****************************************************");
    }

    private static IKernel GetKernel(bool useChat = false)
    {
        var builder = new KernelBuilder();
        if (useChat)
        {
            builder.WithAzureChatCompletionService(
                TestConfiguration.AzureOpenAI.ChatDeploymentName,
                TestConfiguration.AzureOpenAI.Endpoint,
                TestConfiguration.AzureOpenAI.ApiKey,
                alsoAsTextCompletion: true,
                setAsDefault: true);
        }
        else
        {
            builder.WithAzureTextCompletionService(
                TestConfiguration.AzureOpenAI.DeploymentName,
                TestConfiguration.AzureOpenAI.Endpoint,
                TestConfiguration.AzureOpenAI.ApiKey);
        }

        var kernel = builder
            .WithLogger(ConsoleLogger.Logger)
            .WithAzureTextEmbeddingGenerationService(
                deploymentName: "text-embedding-ada-002",
                endpoint: TestConfiguration.AzureOpenAI.Endpoint,
                apiKey: TestConfiguration.AzureOpenAI.ApiKey)
            .WithMemoryStorage(new VolatileMemoryStore())
            .Configure(c => c.SetDefaultHttpRetryConfig(new HttpRetryConfig
            {
                MaxRetryCount = 3,
                UseExponentialBackoff = true,
                MinRetryDelay = TimeSpan.FromSeconds(3),
            }))
            .Build();

        return kernel;
    }
    public class GameQnaSkill
    {
    }
    public class GameReportFetcherSkill
    {
        [SKFunction,
            SKName("FetchGameReport"),
            Description("Fetches the relevant comma-separated report about a game based on the provided question. This method takes a question about a game as input and retrieves the corresponding comma-separated report with relevant information about the game. The method internally processes the question to identify the appropriate report and returns it as a string")]
        public Task<string> FetchGameReportAsync(
        [Description("he question related to the game report.")]
        string question,
            SKContext context)
        {
            DateTime today = DateTime.UtcNow;
            string ret = @$"
The average Game Monthly and Daily active users, new users and their retention. All averaged to a weekly time window.
Date, Region, MonthlyActiveUsers, DailyActiveUsers, NewPlayers, Retention1Day, Retention7Day
{today:yyyy/MM/dd}, Greater China, 2059256, 292000, 37733, 5.4, 3.03
{today:yyyy/MM/dd}, France, 975300, 302497, 5029, 13.69, 8.26
{today:yyyy/MM/dd}, All, 24916479, 5772155, 251214, 9.5, 5.51
{today:yyyy/MM/dd}, Japan, 965110, 321652, 6394, 10.83, 7.21
{today:yyyy/MM/dd}, United Kingdom, 802591, 239125, 4885, 12.37, 7.29
{today:yyyy/MM/dd}, United States, 4624050, 1454545, 30046, 13.66, 8.21
{today:yyyy/MM/dd}, Latin America, 2910856, 417127, 38295, 9.25, 5.51
{today:yyyy/MM/dd}, India, 1257550, 115146, 21236, 7.09, 3.37
{today:yyyy/MM/dd}, Middle East & Africa, 2247499, 281922, 29776, 9.42, 4.98
{today:yyyy/MM/dd}, Germany, 1380647, 456723, 6222, 14.19, 8.72
{today:yyyy/MM/dd}, Canada, 600845, 208641, 3344, 14.33, 8.8
{today:yyyy/MM/dd}, Western Europe, 2451426, 720703, 15013, 12.28, 7.37
{today:yyyy/MM/dd}, Asia Pacific, 2234259, 344618, 31704, 7.94, 4.5
{today:yyyy/MM/dd}, Central & Eastern Europe, 2674562, 625276, 21676, 10.12, 6.16
";

            return Task.FromResult(ret);
        }
    }

    public class InlineDataProcessorSkill
    {
        private readonly IKernel _kernel;

        public InlineDataProcessorSkill(IKernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }


        [SKFunction,
            SKName("GetAnswerFromInlineData"),
            Description("Processes inline comma-separated data and returns an answer to the given question. The inlineData should be properly formatted so that it can be parsed and used to derive the answer. The method does not read from files\n or external sources; instead, it expects the data to be directly passed as a string.")]
        public async Task<string> GetAnswerFromInlineDataAsync(
            [Description("The question related to the provided inline data.")]
            string question,
            [Description("Comma-separated data as a string. The data should be in a format suitable for processing the question.")]
            string inlineData,
            SKContext context)
        {
            DateTime today = DateTime.UtcNow;

            const string FunctionDefinition = @"
Create a Python script that loads comma-separated (CSV) data inline (within the script) into a dataframe.
The CSV data should not be assumed to be available in any external file.
The script should attempt to answer the provided question and print the output to the console.
Import any necessary modules within the script (e.g., import datetime if used).
simply output the final script below without any additional explanations.

[Question]
{{$question}}

[Input CSV]
{{$inlineData}}

[Result Python Script]
";

            var csvAnalyzeFunction = _kernel.CreateSemanticFunction(FunctionDefinition, maxTokens: 3000, temperature: 0.3);

            var result = await csvAnalyzeFunction.InvokeAsync(context);

            // Path to the Python executable
            string pythonPath = "python"; // Use "python3" if on a Unix-like system

            // Inline Python script
            string pythonScript = result.Result
                .Replace('"', '\'')
                .Trim("```python")
                .Trim("```");

            // Create a ProcessStartInfo and set the required properties
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-c \"" + pythonScript + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Create a new Process
            using Process process = new() { StartInfo = startInfo };

            // Start the Python process
            process.Start();

            // Read the Python process output
            string output = process.StandardOutput.ReadToEnd().Trim();

            // Wait for the process to finish
            process.WaitForExit();

            return output;
        }
    }

    public class GameQuestionsAnsweringSkill
    {
        private readonly IKernel _kernel;

        public GameQuestionsAnsweringSkill(IKernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }


        [SKFunction,
            SKName("AnswerQuestionAboutGameAndPlayers"),
            Description("Answers for questions about the game or about its players")]
        public async Task<string> AnswerQuestionAboutGameAndPlayersAsync(
        [Description("The question related to the game report.")]
        string question,
            SKContext context)
        {
            DateTime today = DateTime.UtcNow;
            string inlineData = @$"
The average Game Monthly and Daily active users, new users and their retention. All averaged to a weekly time window.
Date, Region, MonthlyActiveUsers, DailyActiveUsers, NewPlayers, Retention1Day, Retention7Day
{today:yyyy/MM/dd}, Greater China, 2059256, 292000, 37733, 5.4, 3.03
{today:yyyy/MM/dd}, France, 975300, 302497, 5029, 13.69, 8.26
{today:yyyy/MM/dd}, All, 24916479, 5772155, 251214, 9.5, 5.51
{today:yyyy/MM/dd}, Japan, 965110, 321652, 6394, 10.83, 7.21
{today:yyyy/MM/dd}, United Kingdom, 802591, 239125, 4885, 12.37, 7.29
{today:yyyy/MM/dd}, United States, 4624050, 1454545, 30046, 13.66, 8.21
{today:yyyy/MM/dd}, Latin America, 2910856, 417127, 38295, 9.25, 5.51
{today:yyyy/MM/dd}, India, 1257550, 115146, 21236, 7.09, 3.37
{today:yyyy/MM/dd}, Middle East & Africa, 2247499, 281922, 29776, 9.42, 4.98
{today:yyyy/MM/dd}, Germany, 1380647, 456723, 6222, 14.19, 8.72
{today:yyyy/MM/dd}, Canada, 600845, 208641, 3344, 14.33, 8.8
{today:yyyy/MM/dd}, Western Europe, 2451426, 720703, 15013, 12.28, 7.37
{today:yyyy/MM/dd}, Asia Pacific, 2234259, 344618, 31704, 7.94, 4.5
{today:yyyy/MM/dd}, Central & Eastern Europe, 2674562, 625276, 21676, 10.12, 6.16
";

            const string FunctionDefinition = @"
Create a Python script that loads comma-separated (CSV) data inline (within the script) into a dataframe.
The CSV data should not be assumed to be available in any external file.
The script should attempt to answer the provided question and print the output to the console.
Import any necessary modules within the script (e.g., import datetime if used). Avoid using the pandas module;
simply output the final script below without any additional explanations.

[Question]
{{$question}}

[Input CSV]
{{$inlineData}}

[Result Python Script]
";

            var csvAnalyzeFunction = _kernel.CreateSemanticFunction(FunctionDefinition, maxTokens: 3000, temperature: 0.1);

            var result = await csvAnalyzeFunction.InvokeAsync(context);

            // Path to the Python executable
            string pythonPath = "python"; // Use "python3" if on a Unix-like system

            // Inline Python script
            string pythonScript = result.Result
                .Replace('"', '\'')
                .Trim("```python")
                .Trim("```");

            // Create a ProcessStartInfo and set the required properties
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-c \"" + pythonScript + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Create a new Process
            using Process process = new() { StartInfo = startInfo };

            // Start the Python process
            process.Start();

            // Read the Python process output
            string output = process.StandardOutput.ReadToEnd().Trim();

            // Wait for the process to finish
            process.WaitForExit();

            return output;
        }
    }
}

public static class StringHelper
{
    public static string Trim(this string source, string wordToRemove)
    {
        // Remove from the beginning
        while (source.StartsWith(wordToRemove, StringComparison.OrdinalIgnoreCase))
        {
            source = source.Substring(wordToRemove.Length);
        }

        // Remove from the end
        while (source.EndsWith(wordToRemove, StringComparison.OrdinalIgnoreCase))
        {
            source = source.Substring(0, source.Length - wordToRemove.Length);
        }

        return source;
    }
}
