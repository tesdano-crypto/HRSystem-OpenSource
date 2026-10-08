# Dependency and license review

Reviewed 2026-10-08. Evidence: restored project.assets.json graphs and each exact
cached NuGet package .nuspec; embedded license files inspected where declared.
100 distinct package/version pairs: 90 MIT expressions, 8 Apache-2.0 expressions,
1 custom license file and 1 legacy license URL without a license expression.
There are 14 centrally declared direct packages plus one SDK-injected direct asset
package. The local dotnet-ef tool is listed separately below, outside that graph.
No dependency versions or business code were changed.

The full machine-readable inventory records project membership and embedded notice
file names. Membership in a production project can include build/design-only assets;
it is not proof that every package ships in published runtime output.

## Important findings

- **Microsoft.Data.SqlClient.SNI.runtime 6.0.2**: .nuspec declares a license FILE,
  LICENSE.txt, headed Microsoft Software License Terms. Do not classify it as MIT.
  Section 3 permits object-code redistribution within applications under conditions:
  protection of the terms for downstream recipients, distribution-related indemnity,
  trademark restrictions, and an Excluded License restriction. Preserve its notices.
  Source-only NuGet references do not bundle this binary; installers/containers or
  published Windows app distributions need a specific compliance review. GPL-3.0
  compatibility must not be presumed with this native dependency.
- **xunit.abstractions 2.0.3**: metadata has no SPDX expression or embedded license;
  it links to a moving upstream license URL. That URL currently identifies Apache-2.0
  with additional source notices, but it is not immutable version-specific evidence.
  Keep this as REVIEW for redistribution of test tools; do not silently fill in MIT.
- MIT packages require preservation of copyright/permission notices when distributing
  copies. Apache-2.0 packages require the license and applicable notices, and notices
  of modifications where relevant. A new project license does not replace these terms.
- Embedded THIRD-PARTY-NOTICES files can contain additional component terms. This is
  a package-metadata review, not a complete binary SBOM or blanket redistribution
  clearance. Build outputs and package payloads must not be committed as source.

Sources: [SNI package](https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/6.0.2),
[xunit.abstractions package](https://www.nuget.org/packages/xunit.abstractions/2.0.3),
[legacy xUnit license URL](https://raw.githubusercontent.com/xunit/xunit/master/license.txt).
The custom SNI license text was inspected in the restored package, not inferred from
Microsoft.Data.SqlClient's separate MIT declaration.

## Exact resolved package metadata

| Package | Version | Metadata type | Declared license | Reference kind | Evidence |
| --- | --- | --- | --- | --- | --- |
| AngleSharp | 1.5.2 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/AngleSharp/1.5.2) |
| AngleSharp.Css | 1.0.0-beta.224 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/AngleSharp.Css/1.0.0-beta.224) |
| AngleSharp.Diffing | 1.1.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/AngleSharp.Diffing/1.1.1) |
| Azure.Core | 1.47.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Azure.Core/1.47.1) |
| Azure.Identity | 1.14.2 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Azure.Identity/1.14.2) |
| bunit | 2.8.6 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/bunit/2.8.6) |
| ClosedXML | 0.105.1 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/ClosedXML/0.105.1) |
| ClosedXML.Parser | 2.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/ClosedXML.Parser/2.0.0) |
| coverlet.collector | 10.0.1 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/coverlet.collector/10.0.1) |
| DocumentFormat.OpenXml | 3.1.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/DocumentFormat.OpenXml/3.1.1) |
| DocumentFormat.OpenXml.Framework | 3.1.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/DocumentFormat.OpenXml.Framework/3.1.1) |
| ExcelNumberFormat | 1.1.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/ExcelNumberFormat/1.1.0) |
| Humanizer.Core | 2.14.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Humanizer.Core/2.14.1) |
| Microsoft.AspNetCore.App.Internal.Assets | 10.0.10 | expression | MIT | Direct (SDK) | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.App.Internal.Assets/10.0.10) |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebAssembly/10.0.10) |
| Microsoft.AspNetCore.Components.WebAssembly.Authentication | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebAssembly.Authentication/10.0.10) |
| Microsoft.AspNetCore.Cryptography.Internal | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Cryptography.Internal/10.0.10) |
| Microsoft.AspNetCore.Cryptography.KeyDerivation | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Cryptography.KeyDerivation/10.0.10) |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.10 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore/10.0.10) |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.10 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.10) |
| Microsoft.AspNetCore.TestHost | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost/10.0.10) |
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Bcl.AsyncInterfaces/8.0.0) |
| Microsoft.Bcl.Cryptography | 9.0.4 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Bcl.Cryptography/9.0.4) |
| Microsoft.Build.Framework | 18.0.2 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Build.Framework/18.0.2) |
| Microsoft.CodeAnalysis.Analyzers | 3.11.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Analyzers/3.11.0) |
| Microsoft.CodeAnalysis.Common | 5.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Common/5.0.0) |
| Microsoft.CodeAnalysis.CSharp | 5.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/5.0.0) |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Workspaces/5.0.0) |
| Microsoft.CodeAnalysis.Workspaces.Common | 5.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.Common/5.0.0) |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.0.0) |
| Microsoft.CodeCoverage | 18.8.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.CodeCoverage/18.8.1) |
| Microsoft.Data.SqlClient | 6.1.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient/6.1.1) |
| Microsoft.Data.SqlClient.SNI.runtime | 6.0.2 | file | LICENSE.txt | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/6.0.2) |
| Microsoft.EntityFrameworkCore | 10.0.10 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.10) |
| Microsoft.EntityFrameworkCore.Abstractions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Abstractions/10.0.10) |
| Microsoft.EntityFrameworkCore.Analyzers | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Analyzers/10.0.10) |
| Microsoft.EntityFrameworkCore.Design | 10.0.10 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.10) |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.10 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.InMemory/10.0.10) |
| Microsoft.EntityFrameworkCore.Relational | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational/10.0.10) |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.10 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer/10.0.10) |
| Microsoft.Extensions.Caching.Abstractions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Abstractions/10.0.10) |
| Microsoft.Extensions.Caching.Memory | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/10.0.10) |
| Microsoft.Extensions.Configuration | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/10.0.10) |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/10.0.10) |
| Microsoft.Extensions.Configuration.Binder | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/10.0.10) |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.10) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.10) |
| Microsoft.Extensions.DependencyModel | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyModel/10.0.10) |
| Microsoft.Extensions.Diagnostics | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics/10.0.10) |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/10.0.10) |
| Microsoft.Extensions.Identity.Core | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Identity.Core/10.0.10) |
| Microsoft.Extensions.Identity.Stores | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Identity.Stores/10.0.10) |
| Microsoft.Extensions.Logging | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.10) |
| Microsoft.Extensions.Logging.Abstractions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.10) |
| Microsoft.Extensions.Options | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Options/10.0.10) |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/10.0.10) |
| Microsoft.Extensions.Primitives | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/10.0.10) |
| Microsoft.FluentUI.AspNetCore.Components | 4.14.3 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components/4.14.3) |
| Microsoft.FluentUI.AspNetCore.Components.Icons | 4.14.3 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.FluentUI.AspNetCore.Components.Icons/4.14.3) |
| Microsoft.Identity.Client | 4.73.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Identity.Client/4.73.1) |
| Microsoft.Identity.Client.Extensions.Msal | 4.73.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.Identity.Client.Extensions.Msal/4.73.1) |
| Microsoft.IdentityModel.Abstractions | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.IdentityModel.Abstractions/7.7.1) |
| Microsoft.IdentityModel.JsonWebTokens | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.IdentityModel.JsonWebTokens/7.7.1) |
| Microsoft.IdentityModel.Logging | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.IdentityModel.Logging/7.7.1) |
| Microsoft.IdentityModel.Protocols | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.IdentityModel.Protocols/7.7.1) |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.IdentityModel.Protocols.OpenIdConnect/7.7.1) |
| Microsoft.IdentityModel.Tokens | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.IdentityModel.Tokens/7.7.1) |
| Microsoft.JSInterop.WebAssembly | 10.0.10 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.JSInterop.WebAssembly/10.0.10) |
| Microsoft.NET.Test.Sdk | 18.8.1 | expression | MIT | Direct | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.8.1) |
| Microsoft.SqlServer.Server | 1.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.SqlServer.Server/1.0.0) |
| Microsoft.TestPlatform.ObjectModel | 18.8.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/18.8.1) |
| Microsoft.TestPlatform.TestHost | 18.8.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/18.8.1) |
| Microsoft.VisualStudio.SolutionPersistence | 1.0.52 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Microsoft.VisualStudio.SolutionPersistence/1.0.52) |
| Mono.TextTemplating | 3.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Mono.TextTemplating/3.0.0) |
| Newtonsoft.Json | 13.0.3 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) |
| RBush.Signed | 4.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/RBush.Signed/4.0.0) |
| SixLabors.Fonts | 1.0.0 | expression | Apache-2.0 | Transitive | [NuGet](https://www.nuget.org/packages/SixLabors.Fonts/1.0.0) |
| System.ClientModel | 1.5.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.ClientModel/1.5.1) |
| System.CodeDom | 6.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.CodeDom/6.0.0) |
| System.Composition | 9.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Composition/9.0.0) |
| System.Composition.AttributedModel | 9.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Composition.AttributedModel/9.0.0) |
| System.Composition.Convention | 9.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Composition.Convention/9.0.0) |
| System.Composition.Hosting | 9.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Composition.Hosting/9.0.0) |
| System.Composition.Runtime | 9.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Composition.Runtime/9.0.0) |
| System.Composition.TypedParts | 9.0.0 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Composition.TypedParts/9.0.0) |
| System.Configuration.ConfigurationManager | 9.0.4 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Configuration.ConfigurationManager/9.0.4) |
| System.Diagnostics.EventLog | 9.0.4 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Diagnostics.EventLog/9.0.4) |
| System.IdentityModel.Tokens.Jwt | 7.7.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.IdentityModel.Tokens.Jwt/7.7.1) |
| System.IO.Packaging | 8.0.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.IO.Packaging/8.0.1) |
| System.Memory.Data | 8.0.1 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Memory.Data/8.0.1) |
| System.Security.Cryptography.Pkcs | 9.0.4 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/9.0.4) |
| System.Security.Cryptography.ProtectedData | 9.0.4 | expression | MIT | Transitive | [NuGet](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/9.0.4) |
| xunit | 2.9.3 | expression | Apache-2.0 | Direct | [NuGet](https://www.nuget.org/packages/xunit/2.9.3) |
| xunit.abstractions | 2.0.3 | missing | UNKNOWN | Transitive | [NuGet](https://www.nuget.org/packages/xunit.abstractions/2.0.3) |
| xunit.analyzers | 1.18.0 | expression | Apache-2.0 | Transitive | [NuGet](https://www.nuget.org/packages/xunit.analyzers/1.18.0) |
| xunit.assert | 2.9.3 | expression | Apache-2.0 | Transitive | [NuGet](https://www.nuget.org/packages/xunit.assert/2.9.3) |
| xunit.core | 2.9.3 | expression | Apache-2.0 | Transitive | [NuGet](https://www.nuget.org/packages/xunit.core/2.9.3) |
| xunit.extensibility.core | 2.9.3 | expression | Apache-2.0 | Transitive | [NuGet](https://www.nuget.org/packages/xunit.extensibility.core/2.9.3) |
| xunit.extensibility.execution | 2.9.3 | expression | Apache-2.0 | Transitive | [NuGet](https://www.nuget.org/packages/xunit.extensibility.execution/2.9.3) |
| xunit.runner.visualstudio | 3.1.5 | expression | Apache-2.0 | Direct | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) |

## Tools, frontend assets and data

- dotnet-ef **10.0.10**, local tool manifest: NuGet metadata declares MIT;
  [exact version metadata](https://www.nuget.org/packages/dotnet-ef/10.0.10).
- .NET SDK/shared runtime and SQL Server are separately installed prerequisites;
  SQL Server is not relicensed by a project license. Do not bundle it without its terms.
- wwwroot contains application CSS and a download helper. Blazor/Fluent UI static
  assets come from framework/NuGet dependencies, not a separate vendored JS package.
- Calendar JSON contains a transformed DGPA public-calendar dataset, with authority,
  title, source URL, document identifier and hash retained. The
  [DGPA open-information declaration](https://www.dgpa.gov.tw/archive?uid=75)
  permits reuse within its scope with attribution and exclusions. Keep source
  attribution, do not imply endorsement, and check for any attachment-specific
  exception before a data release. This review does not revalidate calendar accuracy.
- Before shipping binaries, assemble a release-specific dependency/notice bundle
  from the actual published output. No binary redistribution approval is implied here.

## Project license comparison (Apache-2.0 selected by the owner)

| Choice | Practical effect for this project | Obligations and tradeoffs |
| --- | --- | --- |
| MIT | Low-friction adoption, including proprietary HR customizations and commercial redistribution | Preserve copyright/permission notice; no express patent grant; modified source need not be published |
| Apache-2.0 | Also permits proprietary and commercial use; more explicit patent provisions for a multi-contributor business system | License/attribution retention, applicable NOTICE and changed-file notices; express contributor patent grant with termination provisions; does not relicense SNI |
| GPL-3.0 | Distributed covered derivatives must provide corresponding source under GPL terms; suitable if reciprocal distribution is the goal | Higher integration/distribution obligations; assess SNI restrictions before choosing; ordinary hosted access alone is not the AGPL network-source trigger; choose only/or-later explicitly |

Recommendation: **Apache-2.0**, if the owner wants permissive commercial adoption
with explicit contributor patent terms. MIT is a simpler alternative. GPL-3.0 is
not recommended without a deliberate copyleft goal and a native SQL dependency
compatibility review. The owner subsequently authorized Apache-2.0; see ../LICENSE
and ../NOTICE. This selection does not override dependency licenses.

Primary texts: [MIT](https://opensource.org/license/mit),
[Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0),
[GPL-3.0](https://opensource.org/license/gpl-3.0).

Apache-2.0 was added under the owner's explicit authorization. Package metadata
alone cannot establish rights to third-party code; separate terms remain applicable.
