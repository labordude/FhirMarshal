<!--
Hey, thanks for using the awesome-readme-template template.
If you have any enhancements, then fork this project and create a pull request
or just open an issue with the label "enhancement".

Don't forget to give this project a star for additional support ;)
Maybe you can mention me or this repo in the acknowledgements too
-->
<div align="center">

  <img src="Assets/logo.png" alt="logo" width="200" height="auto" />
  <h1>FhirMarshal</h1>

  <p>
    A robust .NET-based solution for efficiently FHIR (Fast Healthcare Interoperability Resources) data.
  </p>

<!-- <h4>
    <a href="https://github.com/Louis3797/awesome-readme-template/">View Demo</a>
  <span> · </span>
    <a href="https://github.com/Louis3797/awesome-readme-template">Documentation</a>
  <span> · </span>
    <a href="https://github.com/Louis3797/awesome-readme-template/issues/">Report Bug</a>
  <span> · </span>
    <a href="https://github.com/Louis3797/awesome-readme-template/issues/">Request Feature</a>
  </h4> -->
</div>

<br />

<!-- Table of Contents -->

Table of Contents

- [About the Project](#star2-about-the-project)
  - [Screenshots](#camera-screenshots)
  - [Tech Stack](#space_invader-tech-stack)
  - [Features](#dart-features)
  - [Environment Variables](#key-environment-variables)
- [Getting Started](#toolbox-getting-started)
  - [Prerequisites](#bangbang-prerequisites)
  - [Installation](#gear-installation)
  - [Run Locally](#running-run-locally)
- [Roadmap](#compass-roadmap)
- [License](#warning-license)
- [Acknowledgements](#gem-acknowledgements)

<!-- About the Project -->

## :star2: About the Project

FhirMarshal is a port of [fhirbase](https://github.com/fhirbase/fhirbase),
reimagined for .NET. It simplifies the process of handling FHIR data by
integrating with FHIR Bulk APIs, enabling data extraction, transformation, and
loading with a focus on configurability and performance.

fhirbase was originally built in Go for managing FHIR resources in PostgreSQL.
FhirMarshal builds on this foundation, introducing OAuth 2.0 capabilities for
secure integration with FHIR servers and a sleek, interactive CLI.

### :question::question: Why did I build this project?

To challenge myself learning a new language and framework, I decided to port
fhirbase to .NET. I wanted to create a tool that would make it easier to work
with FHIR data, and I saw an opportunity to improve on the original fhirbase
project by adding OAuth 2.0 support and modernizing the codebase with .NET 9.

I also submitted a PR to the original fhirbase project fixing some of the issues
that had been reported by the community. I also updated the project's
dependencies and refactored the code into a new structure. Not sure if they
still look at their repo, but I hope my contributions help the community.

<!-- Screenshots -->

### :camera: Screenshots

<div align="center"> 
  <p><img src="https://ik.imagekit.io/callofdadduty/main.png?updatedAt=1735931674749" alt="screenshot" width="420" height="auto" /></p>
  <p><img src="https://ik.imagekit.io/callofdadduty/configuration.png?updatedAt=1735931674812" alt="screenshot" width="420" height="auto" /></p>
   <p><img src="https://ik.imagekit.io/callofdadduty/init_db.png?updatedAt=1735931674664" alt="screenshot" width="420" height="auto" /></p>
    <p><img src="https://ik.imagekit.io/callofdadduty/load_db.png?updatedAt=1735931674842" alt="screenshot" width="420" height="auto" /></p>
     <p><img src="https://ik.imagekit.io/callofdadduty/web%20server.png?updatedAt=1735931674653" alt="screenshot" width="420" height="auto" /></p>
      <p><img src="https://ik.imagekit.io/callofdadduty/start%20bulk.png?updatedAt=1735931774202" alt="screenshot" width="420" height="auto" /></p>
        <p><img src="https://ik.imagekit.io/callofdadduty/retries.png?updatedAt=1735931773910" alt="screenshot" width="420" height="auto" /></p>
          <p><img src="https://ik.imagekit.io/callofdadduty/all%20neat.png?updatedAt=1735931773886" alt="screenshot" width="420" height="auto" /></p>
</div>

<!-- TechStack -->

### :space_invader: Tech Stack

  <ul>
    <li>C#</li>
    <li>.NET 9 (Blazor)</li>
    <li>PostgreSQL</li>
    <li>Dapper</li>
    <li>Spectre.Console</li>
    <li>MudBlazor</li>
  </ul>

<!-- Features -->

### :dart: Features

- Port of fhirbase: Combines the strengths of fhirbase with an interactive .NET
  CLI.
- OAuth: Authenticate securely with FHIR servers using OAuth 2.0.
- FHIR Bulk Data Handling: Extract and transform NDJSON data from FHIR servers.
  Will run multiple threads to speed up the process and handle retries with
  exponential backoff.
- Configurable Runtime Settings: Modify app settings dynamically using
  runtimeSettings.json.
- Web API Support: Do some testing and handle basic queries with the built-in
  web server.
- Integration with Postgres: Built-in support for PostgreSQL databases for data
  storage and analysis.
- Customizable: Use the appsettings.json file or programmatic configuration for
  flexibility.
- Modern Architecture: Built with .NET 9, Dapper, MudBlazor, and
  Spectre.Console.

<!-- Env Variables -->

### :key: Environment Setup

The appsettings.json file contains the configuration settings for FhirMarshal.
If the program doesn't find one, it will create it. You can update the settings
in this file to match your environment and also change configuration while
running the app. The file is structured as follows and contains fairly sane
defaults:

```json
{
	"FhirMarshalConfig": {
		"WebHost": "localhost",
		"WebPort": 3000,
		"Host": "localhost",
		"Port": 5432,
		"Database": "fhirbase",
		"Username": "postgres",
		"Password": "",
		"SslMode": "Prefer",
		"FhirVersion": "4.0.0",
		"AcceptHeader": "application/fhir\u002Bjson",
		"NumDl": 5,
		"Mode": "insert",
		"Output": ""
	},
	"AuthConfig": {
		"TokenEndpoint": "",
		"CertPath": "",
		"ClientId": "",
		"Audience": "",
		"GrantType": "client_credentials",
		"AssertionType": "jwt",
		"ClientAssertionType": "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
	}
}
```

<!-- Getting Started -->

## :toolbox: Getting Started

<!-- Prerequisites -->

### :bangbang: Prerequisites

Before you begin, ensure you have met the following requirements:

- You have installed the latest version of .NET SDK.
- PostgreSQL is installed on your machine.
- A FHIR server is available for testing.

To check for your .NET SDK, run the following command:

```bash
  dotnet --version
```

<!-- Run Locally -->

### :running: Run Locally

Clone the project

```bash
  git clone https://github.com/labordude/FhirMarshal.git
```

Go to the project directory

```bash
  cd FhirMarshal
```

Build the project

```bash
  dotnet build

```

Run the project

```bash
  dotnet run
```

<!-- Roadmap -->

## :compass: Roadmap

- [ ] Add additional OAuth methods for FHIR server authentication.
- [ ] Update transformations to support FHIR R4B and R5.
- [ ] Automate the process of updating the FHIR version.

<!-- License -->

## :warning: License

Distributed under the MIT License. See LICENSE for more information.

<!-- Contact -->

<!-- ## :handshake: Contact -->

<!-- Acknowledgments -->

## :gem: Acknowledgements

Use this section to mention useful resources and libraries that you have used in
your projects.

- [fhirbase](https://github.com/fhirbase/fhirbase)
- [Awesome README template](https://github.com/Louis3797/awesome-readme-template)
- [Spectre.Console](https://spectreconsole.net/)
- [MudBlazor](https://mudblazor.com/)
