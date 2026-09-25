using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class PlcCrossReferenceInfo
{
    public string PlcName { get; set; } = string.Empty;

    public List<CrossReferenceSourceInfo> Sources { get; set; } = new List<CrossReferenceSourceInfo>();

    public List<string> Messages { get; set; } = new List<string>();

    public int SourceCount { get; set; }

    public int ReferenceCount { get; set; }

    public int LocationCount { get; set; }
}
