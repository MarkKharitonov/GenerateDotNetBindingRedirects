# GenerateDotNetBindingRedirects

You may find this project useful if all of this is true:
1. You have a lot of .NET Framework projects across multiple solutions, all using PackageReference for referencing the NuGet packages.
1. You have a process that enforces the consistent versioning of NuGet packages your project files reference directly.
1. You have almost no ad hoc dlls. (If you do many - better arrange them as NuGet packages on your internal NuGet repo).
1. You are overwhelmed with the burden to maintain the assembly binding redirects.

This project aims to produce a tool that can be used to auto generate all the binding redirects determinstically after the restore phase, but before the build.
