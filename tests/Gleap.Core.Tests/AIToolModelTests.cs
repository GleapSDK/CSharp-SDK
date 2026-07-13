using GleapSDK.Models;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class AIToolModelTests
{
    [Fact]
    public void AITool_Serializes_ParametersWithEnumKeyAndLowercaseType()
    {
        var tool = new AITool
        {
            Name = "getOrder",
            Description = "d",
            Response = "r",
            ExecutionType = "auto",
            Parameters = new List<AIToolParameter>
            {
                new AIToolParameter
                {
                    Name = "id",
                    Description = "pd",
                    Type = AIParamType.String,
                    Required = true,
                    Enums = new List<string> { "a", "b" }
                }
            }
        };

        var json = new SystemTextJsonSerializer().Serialize(tool);

        Assert.Contains("\"name\":\"getOrder\"", json);
        Assert.Contains("\"parameters\":", json);
        Assert.Contains("\"type\":\"string\"", json);
        Assert.Contains("\"enum\":[\"a\",\"b\"]", json);
        Assert.DoesNotContain("\"enums\"", json);
    }
}
