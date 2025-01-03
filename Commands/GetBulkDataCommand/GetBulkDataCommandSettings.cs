using System.ComponentModel;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class GetBulkDataCommandSettings : CommandSettings
{
    [CommandOption("-n|--num-dl <NUM_DL>")]
    [Description("Number of downloads to perform")]
    public int NumDl { get; set; } = 5;

    [CommandOption("-i|--input <INPUT>")]
    [Description("Input URL")]
    public string? Url { get; set; }

    [CommandOption("-a|--accept-header <ACCEPT_HEADER>")]
    [Description("Accept header")]
    public string? AcceptHeader { get; set; }

    [CommandOption("-o|--output <OUTPUT>")]
    [Description("Output directory")]
    public string? Output { get; set; }
}
