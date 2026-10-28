using System.Reflection;

[assembly: AssemblyTitle("LoafLock")]
[assembly: AssemblyDescription("Locks the keyboard so a cat lying on it can't type. The mouse keeps working.")]
[assembly: AssemblyCompany("b0r3d.org")]
[assembly: AssemblyProduct("LoafLock")]
[assembly: AssemblyCopyright("Copyright (c) 2026 b0r3d.org")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]
// "0.2+abc1234": the version, then the commit it was built from (build.cmd writes BuildInfo.cs)
[assembly: AssemblyInformationalVersion(LoafLock.Program.Version + "+" + LoafLock.BuildInfo.Commit)]

// the compiler that comes with Windows leaves this out, and without it .NET runs the exe in its
// .NET 4.0 compatibility mode. Visual Studio always writes it.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
