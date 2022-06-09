# GenerateDotNetBindingRedirects

You may find this project useful if all of this is true:
1. You have a lot of .NET Framework projects across multiple solutions, all using PackageReference for referencing the NuGet packages.
1. You have a process that enforces the consistent versioning of NuGet packages your project files reference directly.
1. You have almost no ad hoc dlls. (If you do many - better arrange them as NuGet packages on your internal NuGet repo).
1. You are overwhelmed with the burden to maintain the assembly binding redirects.

This project aims to produce a tool that can be used to auto generate all the binding redirects determinstically after the restore phase, but before the build.

Unfortunately, I cannot share the inputs and expected outputs for the unit tests, since they reflect the structure of our actual application and contain actual project files. This repository was created by passing our internal repository through `filter-branch` git command in order to remove the respective folders as explained here - http://link-intersystems.com/blog/2014/07/17/remove-directories-and-files-permanently-from-git/#

The commands I used to filter out the unit test inputs/outputs are:
```
git filter-branch --index-filter 'git rm -rf --cached --ignore-unmatch src/GenerateBindingRedirectsTests/Input src/GenerateBindingRedirectsTests/Expected src/Tests/Input src/Tests/Expected' --prune-empty --tag-name-filter cat -- --all
git for-each-ref --format="%(refname)" refs/original/ | while read ref; do git update-ref -d $ref; done
git reflog expire --expire=now --all
git gc --prune=now
```

I am planning to sync our internal repository with this one periodically by following the aforementioned process from the last commit.
