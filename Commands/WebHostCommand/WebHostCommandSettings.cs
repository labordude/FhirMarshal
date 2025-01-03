using System.ComponentModel;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class WebHostCommandSettings : CommandSettings
{
    [CommandOption("--webhost <WEBHOST>")]
    [Description("The host for the web server")]
    public string? WebHost { get; set; } = string.Empty;

    [CommandOption("--webport <PORT>")]
    [Description("The port for the web server")]
    public int? WebPort { get; set; } = 0;
}
