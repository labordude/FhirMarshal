namespace FhirMarshal.Config;

public class FhirMarshalConfig
{
    private List<string> FhirVersions = new() { "3.3.0", "4.0.0" };
    public string WebHost { get; set; } = string.Empty;
    public int WebPort { get; set; } = 0;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string SslMode { get; set; } = string.Empty;

    public string FhirVersion { get; set; } = string.Empty;

    public string AcceptHeader { get; set; } = "application/fhir+json";
    public int NumDl { get; set; } = 5;
    public string Mode { get; set; } = "insert";

    public string Output { get; set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
}
