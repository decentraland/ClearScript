# Description
This is a fork of [ClearScript](https://github.com/microsoft/ClearScript) that was made for [Decentraland](https://decentraland.org/), a [Unity](https://unity.com/) game. ClearScript is a library that makes it easy to add scripting to your .NET applications. It currently supports JavaScript (via [V8](https://developers.google.com/v8/) and [JScript](https://docs.microsoft.com/en-us/previous-versions//hbxc2t98(v=vs.85))) and [VBScript](https://docs.microsoft.com/en-us/previous-versions//t0aew7h6(v=vs.85)).

# Differences from Upstream
* Additional unsafe APIs unfit for upstream
* A tool to generate a Unity package, and the package itself
* Only JavaScript is supported
* x86 is not supported
* [COM](https://learn.microsoft.com/en-us/windows/win32/com/the-component-object-model) is not supported
* The Linux native library is built with glibc 2.31 (Ubuntu 20.04)

# Installation
Add this line to your `manifest.json`:
```Json
"org.decentraland.clearscript": "https://github.com/decentraland/ClearScript.git?path=/Unity/Package",
```

# Notes for Maintiners
After you make a change, you must run `PackageBuilder.exe`, copy the new native libraries, if any, to `Unity/Package/Plugins` and also commit their debug symbols in the same commit.

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
