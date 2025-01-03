using System.ComponentModel;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class InitDbCommandSettings : CommandSettings
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

    [CommandOption("--fhir <FHIR_VERSION>")]
    [Description("FHIR Version")]
    public string? FhirVersion { get; set; }
}
