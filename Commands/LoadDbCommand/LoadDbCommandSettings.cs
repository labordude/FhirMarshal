using System.ComponentModel;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class LoadDbCommandSettings : CommandSettings
{
    [CommandOption("--host <HOST>")]
    [Description("PostgreSQL Host")]
    public string? Host { get; set; }

    [CommandOption("--port <PORT>")]
    [Description("PostgreSQL Port")]
    public int Port { get; set; }

    [CommandOption("-U|--username <USERNAME>")]
    [Description("PostgreSQL Username")]
    public string? Username { get; set; }

    [CommandOption("-W|--password <PASSWORD>")]
    [Description("PostgreSQL Password")]
    public string? Password { get; set; }

    [CommandOption("-d|--db <DATABASE>")]
    [Description("PostgreSQL Database")]
    public string? Database { get; set; }

    [CommandOption("-m|--mode <MODE>")]
    [Description("Load mode (insert or copy) -- default is insert")]
    public string? Mode { get; set; }

    [CommandOption("-n|--numdl <NUMDL>")]
    [Description("Number of download workers")]
    public int NumDl { get; set; }

    [CommandOption("--fhir <FHIR>")]
    [Description("FHIR Version")]
    public string? FhirVersion { get; set; }

    [CommandOption("--input <INPUT>")]
    [Description("Input file/Url")]
    public string[] InputFiles { get; set; } = Array.Empty<string>();

    public List<string> Input
    {
        get { return InputFiles.ToList(); }
    }
}
