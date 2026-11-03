# Making missing assemblies available to the editor

Your site's `web.config` lists an assembly, such as `MySql.Data`, that is in neither the
site's `Bin` folder nor the .NET Framework. On the Windows server, IIS loads it from the
Global Assembly Cache (GAC). Linux has no GAC, so the editor can't find it, and code that
uses it shows errors such as `Type 'MySqlConnection' is not defined`. The site itself is
fine.

Give the editor a copy of the DLL in a folder of your own, then tell it where that folder
is.

## 1. Get the DLL

Use the version your `web.config` names (for example `MySql.Data, Version=6.10.6.0`).

**From NuGet** (easiest). Download the package and extract the .NET Framework build:

```sh
mkdir -p ~/.local/share/aspx-vb-assemblies
cd /tmp
curl -L -o MySql.Data.zip https://www.nuget.org/api/v2/package/MySql.Data/6.10.6
unzip -j -o MySql.Data.zip 'lib/net452/*' -d ~/.local/share/aspx-vb-assemblies
```

For another assembly or version, change the name and version in the URL. Inside the
package, pick the `lib/net4…` folder closest to the site's framework version.

**Or from the Windows server's GAC.** Copy it from

```
C:\Windows\Microsoft.NET\assembly\GAC_MSIL\MySql.Data\v4.0_6.10.6.0__c5687fc88969c44d\MySql.Data.dll
```

The folder name is `v4.0_<version>__<public key token>`, both taken from the
`web.config` line.

Put the DLL in a folder **outside the site**. Don't put it in the site's `Bin`: `Bin` is
deployed with the site, and the server would then load that copy instead of its own.

## 2. Point the editor at the folder

In VS Code settings (JSON):

```json
"aspxVb.assemblyFolders": ["~/.local/share/aspx-vb-assemblies"]
```

The language server restarts by itself, and the errors go away. One folder can hold
assemblies for every site.

An older site may name an older version (for example `MySql.Data, Version=6.3.4.0`).
A newer DLL from the same family is usually close enough for editing. Errors that remain
then point at members that really did change between versions.
