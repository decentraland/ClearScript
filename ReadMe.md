# Description

This is a fork of [ClearScript](https://github.com/microsoft/ClearScript) that was made for [Decentraland](https://decentraland.org/), a [Unity](https://unity.com/) game. ClearScript is a library that makes it easy to add scripting to your .NET applications. It currently supports JavaScript (via [V8](https://developers.google.com/v8/) and [JScript](https://docs.microsoft.com/en-us/previous-versions//hbxc2t98(v=vs.85))) and [VBScript](https://docs.microsoft.com/en-us/previous-versions//t0aew7h6(v=vs.85)).

# Differences from Upstream

* Additional unsafe APIs unfit for upstream
* A tool to generate a Unity package, and the package itself
* Only JavaScript is supported
* x86 is not supported
* [COM](https://learn.microsoft.com/en-us/windows/win32/com/the-component-object-model) is not supported
* The Linux native library is built with glibc 2.31 (Ubuntu 22.04)

# Installation

Add this line to your `manifest.json`:

```Json
"org.decentraland.clearscript": "https://github.com/decentraland/ClearScript.git?path=/Unity/Package",
```

# Notes for Maintiners

After you make a change, you must run the [Build Unity package](https://github.com/decentraland/ClearScript/actions/workflows/build.yaml) job, commit the Package part of its output, and upload the Symbols part of it to [Google Drive](https://drive.google.com/drive/folders/1lwCteTXEM0d77EGCagt5jluh299a8Mg_) to a subfolder named after the commit hash of the squash merge commit to the master branch. TODO: Automate this step away.

If you accidentally have [the Decentraland client](https://github.com/decentraland/unity-explorer) depend on a commit in a branch that is not the master branch, add that branch to [the branch protection rule](https://github.com/decentraland/ClearScript/settings/rules/11158242) to ensure it is never deleted or force pushed, else we will have commits in the client's history that cannot be built.

# Documentation

* [Main Site / Blog](https://microsoft.github.io/ClearScript/)
* [Examples](https://microsoft.github.io/ClearScript/Examples/Examples.html)
* [Tutorial](https://microsoft.github.io/ClearScript/Tutorial/FAQtorial.html)
* [API reference](https://microsoft.github.io/ClearScript/Reference/index.html)
* [Building, integrating, and deploying ClearScript](https://microsoft.github.io/ClearScript/Details/Build.html)

# Acknowledgments

We'd like to thank:

* [The Microsoft team](https://github.com/microsoft/ClearScript).
* [The V8 team](https://code.google.com/p/v8/people/list).
* [The Jekyll team](https://jekyllrb.com/team/).
* [Kenneth Reitz](http://kennethreitz.org/) for generously providing the [`Httpbin`](http://httpbin.org/) service.
* [Michael Rose](https://mademistakes.com/) for generously providing the [So Simple](https://mmistakes.github.io/so-simple-theme/) Jekyll theme.
* [Toptal](https://www.toptal.com/) for generously providing the [Toptal JavaScript Minifier](https://www.toptal.com/developers/javascript-minifier).
