// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Skills.PlayFabApiSkill;

internal class CreateSegmentInfoSkill
{
    [SKFunction, Description("Get information to create a segment")]
    public static string GetCreateSegmentInfo([Description("Text to uppercase")] string input) =>
        input.ToUpperInvariant();
}
