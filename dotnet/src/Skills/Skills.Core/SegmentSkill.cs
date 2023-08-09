// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.SkillDefinition;

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
    /// <summary>
    /// Read a file
    /// </summary>
    /// <example>
    /// {{file.readAsync $path }} => "hello world"
    /// </example>
    /// <param name="path"> Source file </param>
    /// <returns> File content </returns>
    [SKFunction, Description("Read a file")]
    public async Task<string> CreateSegment([Description("Name of the segment.")] string segmentname,
        [Description("Name of the segment definition. Some of the examples are FirstLoginDateFilter, LastLoginDateFilter.")] string segmentdefinition,
        [Description("Name of the segment comparison. Some of the examples are GreaterThan, LessThan.")] string segmentcomparison,
        [Description("Value of the segment comparison. Some of the examples are 2023-08-01.")] string segmentcomparisonvalue)
    {        
        return $"Segment {segmentname} created with segment definition {segmentdefinition}";
    }
}
