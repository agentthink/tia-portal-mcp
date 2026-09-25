using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class PlcCompileInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    public List<CompileMessageInfo> Messages { get; set; } = new List<CompileMessageInfo>();

    public List<string> DiagnosticNotes { get; set; } = new List<string>();
}
