// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Azure;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.OpenAPI.Authentication;
using Microsoft.SemanticKernel.Skills.OpenAPI.Extensions;
using Newtonsoft.Json;
using RepoUtils;
using System.Linq;

namespace Microsoft.SemanticKernel.Skills.Core;

/// <summary>
/// Read and write from a file.
/// </summary>
/// <example>
/// Usage: kernel.ImportSkill("file", new FileIOSkill());
/// Examples:
/// {{file.readAsync $path }} => "hello world"
/// {{file.writeAsync}}
/// </example>
public sealed class SegmentSkill
{
    ContextVariables contextVariables = new ContextVariables();

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
        [Description("Value of the segment comparison. Some of the examples are 2023-08-01, India, Australia, Kenya.")] string segmentcomparisonvalue
        )
    {
        //ToDo: Create payload json using Playfab dlls/sdk
        // Set properties to create a Segment using swagger.json
        contextVariables.Set("content_type", "application/json");
        contextVariables.Set("server_url", TestConfiguration.PlayFab.Endpoint);
        string segmentPayload = GetSegmentPayload(segmentname, segmentdefinition, segmentcomparison, ref segmentcomparisonvalue);

        contextVariables.Set("content_type", "application/json");
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

    private static string GetSegmentPayload(string segmentname, string segmentdefinition, string segmentcomparison, ref string segmentcomparisonvalue)
    {
        string segmentPayload = "{\n  \"SegmentModel\": {\n \"Name\": \"<SegmentName>\",\n \"SegmentOrDefinitions\": [\n {\n \"SegmentAndDefinitions\": [\n {\n \"<SegmentDefinition>\": {\n \"LogInDate\": \"<SegmentComparisonValue>T00:00:00Z\",\n \"Comparison\": \"<SegmentComparison>\"\n }\n }\n ]\n }\n ]\n }\n  }";
        string locationPayload = "{\n  \"SegmentModel\": {\n \"Name\": \"<SegmentName>\",\n \"SegmentOrDefinitions\": [\n {\n \"SegmentAndDefinitions\": [\n {\n \"<SegmentDefinition>\": {\n \"CountryCode\": \"<SegmentComparisonValue>\",\n \"Comparison\": \"<SegmentComparison>\"\n }\n }\n ]\n }\n ]\n }\n  }";

        if (segmentdefinition == "LocationFilter")
        {
            segmentcomparisonvalue = GetCountryCode(segmentcomparisonvalue);
            segmentPayload = locationPayload;
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

    private static string GetCountryCode(string country)
    {
        StringDictionary countryCodes = new StringDictionary();
        countryCodes.Add("India", "IN");
        countryCodes.Add("Israel", "IL");
        countryCodes.Add("Australia", "AU");
        countryCodes.Add("Kenya", "KE");
        countryCodes.Add("Egypt", "EG");

        if(countryCodes.ContainsKey(country))
        {
            return countryCodes[country];
        }

        return string.Empty;

    }
}
