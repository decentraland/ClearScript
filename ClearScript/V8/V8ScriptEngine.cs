// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.Util;
using Newtonsoft.Json;

namespace Microsoft.ClearScript.V8
{
    // ReSharper disable once PartialTypeWithSinglePart

    /// <summary>
    /// Represents an instance of the V8 JavaScript engine.
    /// </summary>
    /// <remarks>
    /// Unlike <c>WindowsScriptEngine</c> instances, V8ScriptEngine instances do not have
    /// thread affinity. The underlying script engine is not thread-safe, however, so this class
    /// uses internal locks to automatically serialize all script code execution for a given
    /// instance. Script delegates and event handlers are invoked on the calling thread without
    /// marshaling.
    /// </remarks>
    public sealed partial class V8ScriptEngine : ScriptEngine, IJavaScriptEngine
    {
        #region data

        private static readonly DocumentInfo initScriptInfo = new DocumentInfo(MiscHelpers.FormatInvariant("{0} [internal]", nameof(V8ScriptEngine)));

        private readonly V8Runtime runtime;
        private readonly bool usingPrivateRuntime;

        private readonly V8ScriptEngineFlags engineFlags;
        private readonly V8ContextProxy proxy;
        private readonly V8ScriptItem script;
        private readonly InterlockedOneWayFlag disposedFlag = new InterlockedOneWayFlag();

        private const int continuationInterval = 2000;
        private bool inContinuationTimerScope;
        private bool? awaitDebuggerAndPause;

        private List<string> documentNames;
        private bool suppressInstanceMethodEnumeration;
        private bool suppressExtensionMethodEnumeration;

        private CommonJSManager commonJSManager;
        private JsonModuleManager jsonDocumentManager;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new V8 script engine instance.
        /// </summary>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified name.
        /// </summary>
        /// <param name="name">A name to associate with the instance. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(string name)
            : this(name, null)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified resource constraints.
        /// </summary>
        /// <param name="constraints">Resource constraints for the V8 runtime (see remarks).</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(V8RuntimeConstraints constraints)
            : this(null, constraints)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified name and resource constraints.
        /// </summary>
        /// <param name="name">A name to associate with the instance. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="constraints">Resource constraints for the V8 runtime (see remarks).</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(string name, V8RuntimeConstraints constraints)
            : this(name, constraints, V8ScriptEngineFlags.None)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified options.
        /// </summary>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(V8ScriptEngineFlags flags)
            : this(flags, 0)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified options and debug port.
        /// </summary>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <param name="debugPort">A TCP port on which to listen for a debugger connection.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(V8ScriptEngineFlags flags, int debugPort)
            : this(null, null, flags, debugPort)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified name and options.
        /// </summary>
        /// <param name="name">A name to associate with the instance. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(string name, V8ScriptEngineFlags flags)
            : this(name, flags, 0)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified name, options, and debug port.
        /// </summary>
        /// <param name="name">A name to associate with the instance. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <param name="debugPort">A TCP port on which to listen for a debugger connection.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(string name, V8ScriptEngineFlags flags, int debugPort)
            : this(name, null, flags, debugPort)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified resource constraints and options.
        /// </summary>
        /// <param name="constraints">Resource constraints for the V8 runtime (see remarks).</param>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(V8RuntimeConstraints constraints, V8ScriptEngineFlags flags)
            : this(constraints, flags, 0)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified resource constraints, options, and debug port.
        /// </summary>
        /// <param name="constraints">Resource constraints for the V8 runtime (see remarks).</param>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <param name="debugPort">A TCP port on which to listen for a debugger connection.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(V8RuntimeConstraints constraints, V8ScriptEngineFlags flags, int debugPort)
            : this(null, constraints, flags, debugPort)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified name, resource constraints, and options.
        /// </summary>
        /// <param name="name">A name to associate with the instance. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="constraints">Resource constraints for the V8 runtime (see remarks).</param>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(string name, V8RuntimeConstraints constraints, V8ScriptEngineFlags flags)
            : this(name, constraints, flags, 0)
        {
        }

        /// <summary>
        /// Initializes a new V8 script engine instance with the specified name, resource constraints, options, and debug port.
        /// </summary>
        /// <param name="name">A name to associate with the instance. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="constraints">Resource constraints for the V8 runtime (see remarks).</param>
        /// <param name="flags">A value that selects options for the operation.</param>
        /// <param name="debugPort">A TCP port on which to listen for a debugger connection.</param>
        /// <remarks>
        /// A separate V8 runtime is created for the new script engine instance.
        /// </remarks>
        public V8ScriptEngine(string name, V8RuntimeConstraints constraints, V8ScriptEngineFlags flags, int debugPort)
            : this(null, name, constraints, flags, debugPort)
        {
        }

        internal V8ScriptEngine(V8Runtime runtime, string name, V8RuntimeConstraints constraints, V8ScriptEngineFlags flags, int debugPort)
            : base((runtime != null) ? runtime.Name + ":" + name : name, "js")
        {
            if (runtime != null)
            {
                this.runtime = runtime;
            }
            else
            {
                this.runtime = runtime = new V8Runtime(name, constraints);
                usingPrivateRuntime = true;
            }

            DocumentNameManager = runtime.DocumentNameManager;
            HostItemCollateral = runtime.HostItemCollateral;

            engineFlags = flags;
            proxy = V8ContextProxy.Create(runtime.IsolateProxy, Name, flags, debugPort);
            script = (V8ScriptItem)GetRootItem();

            if (flags.HasFlag(V8ScriptEngineFlags.EnableStringifyEnhancements))
            {
                script.SetProperty("toJson", new Func<object, object, string>(new JsonHelper(this).ToJson));
            }

            Execute(initScriptInfo, initScript);

            if (flags.HasFlag(V8ScriptEngineFlags.EnableDebugging | V8ScriptEngineFlags.AwaitDebuggerAndPauseOnStart))
            {
                awaitDebuggerAndPause = true;
            }
        }

        #endregion

        #region public members

        /// <summary>
        /// Resumes script execution if the script engine is waiting for a debugger connection.
        /// </summary>
        /// <remarks>
        /// This method can be called safely from any thread.
        /// </remarks>
        public void CancelAwaitDebugger()
        {
            VerifyNotDisposed();
            proxy.CancelAwaitDebugger();
        }

        /// <summary>
        /// Gets or sets a soft limit for the size of the V8 runtime's heap.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property is specified in bytes. When it is set to the default value, heap size
        /// monitoring is disabled, and scripts with memory leaks or excessive memory usage
        /// can cause unrecoverable errors and process termination.
        /// </para>
        /// <para>
        /// A V8 runtime unconditionally terminates the process when it exceeds its resource
        /// constraints (see <c><see cref="V8RuntimeConstraints"/></c>). This property enables external
        /// heap size monitoring that can prevent termination in some scenarios. To be effective,
        /// it should be set to a value that is significantly lower than
        /// <c><see cref="V8RuntimeConstraints.MaxOldSpaceSize"/></c>. Note that enabling heap size
        /// monitoring results in slower script execution.
        /// </para>
        /// <para>
        /// Exceeding this limit causes the V8 runtime to behave in accordance with
        /// <c><see cref="RuntimeHeapSizeViolationPolicy"/></c>.
        /// </para>
        /// <para>
        /// Note that
        /// <c><see href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/ArrayBuffer">ArrayBuffer</see></c>
        /// memory is allocated outside the runtime's heap and is therefore not tracked by heap
        /// size monitoring. See <c><see cref="V8RuntimeConstraints.MaxArrayBufferAllocation"/></c> for
        /// additional information.
        /// </para>
        /// </remarks>
        public UIntPtr MaxRuntimeHeapSize
        {
            get
            {
                VerifyNotDisposed();
                return proxy.MaxIsolateHeapSize;
            }

            set
            {
                VerifyNotDisposed();
                proxy.MaxIsolateHeapSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the minimum time interval between consecutive heap size samples.
        /// </summary>
        /// <remarks>
        /// This property is effective only when heap size monitoring is enabled (see
        /// <c><see cref="MaxRuntimeHeapSize"/></c>).
        /// </remarks>
        public TimeSpan RuntimeHeapSizeSampleInterval
        {
            get
            {
                VerifyNotDisposed();
                return proxy.IsolateHeapSizeSampleInterval;
            }

            set
            {
                VerifyNotDisposed();
                proxy.IsolateHeapSizeSampleInterval = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum amount by which the V8 runtime is permitted to grow the stack during script execution.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This property is specified in bytes. When it is set to the default value, no stack
        /// usage limit is enforced, and scripts with unchecked recursion or other excessive stack
        /// usage can cause unrecoverable errors and process termination.
        /// </para>
        /// <para>
        /// Note that the V8 runtime does not monitor stack usage while a host call is in progress.
        /// Monitoring is resumed when control returns to the runtime.
        /// </para>
        /// </remarks>
        public UIntPtr MaxRuntimeStackUsage
        {
            get
            {
                VerifyNotDisposed();
                return proxy.MaxIsolateStackUsage;
            }

            set
            {
                VerifyNotDisposed();
                proxy.MaxIsolateStackUsage = value;
            }
        }

        /// <summary>
        /// Enables or disables instance method enumeration.
        /// </summary>
        /// <remarks>
        /// By default, a host object's instance methods are exposed as enumerable properties.
        /// Setting this property to <c>true</c> causes instance methods to be excluded from
        /// property enumeration. This affects all host objects exposed in the current script
        /// engine. Note that instance methods remain both retrievable and invocable regardless of
        /// this property's value.
        /// </remarks>
        public bool SuppressInstanceMethodEnumeration
        {
            get => suppressInstanceMethodEnumeration;

            set
            {
                suppressInstanceMethodEnumeration = value;
                OnEnumerationSettingsChanged();
            }
        }

        /// <summary>
        /// Enables or disables extension method enumeration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, all exposed extension methods appear as enumerable properties of all host
        /// objects, regardless of type. Setting this property to <c>true</c> causes extension
        /// methods to be excluded from property enumeration. This affects all host objects exposed
        /// in the current script engine. Note that extension methods remain both retrievable and
        /// invocable regardless of this property's value.
        /// </para>
        /// <para>
        /// This property has no effect if <c><see cref="SuppressInstanceMethodEnumeration"/></c> is set
        /// to <c>true</c>.
        /// </para>
        /// </remarks>
        public bool SuppressExtensionMethodEnumeration
        {
            get => suppressExtensionMethodEnumeration;

            set
            {
                suppressExtensionMethodEnumeration = value;
                RebuildExtensionMethodSummary();
            }
        }

        /// <summary>
        /// Enables or disables interrupt propagation in the V8 runtime.
        /// </summary>
        /// <remarks>
        /// By default, when nested script execution is interrupted via <c><see cref="Interrupt"/></c>, an
        /// instance of <c><see cref="ScriptInterruptedException"/></c>, if not handled by the host, is
        /// wrapped and delivered to the parent script frame as a normal exception that JavaScript
        /// code can catch. Setting this property to <c>true</c> causes the V8 runtime to remain in
        /// the interrupted state until its outermost script frame has been processed.
        /// </remarks>
        public bool EnableRuntimeInterruptPropagation
        {
            get
            {
                VerifyNotDisposed();
                return proxy.EnableIsolateInterruptPropagation;
            }

            set
            {
                VerifyNotDisposed();
                proxy.EnableIsolateInterruptPropagation = value;
            }
        }

        /// <summary>
        /// Gets or sets the V8 runtime's behavior in response to a violation of the maximum heap size.
        /// </summary>
        public V8RuntimeViolationPolicy RuntimeHeapSizeViolationPolicy
        {
            get
            {
                VerifyNotDisposed();
                return proxy.DisableIsolateHeapSizeViolationInterrupt ? V8RuntimeViolationPolicy.Exception : V8RuntimeViolationPolicy.Interrupt;
            }

            set
            {
                VerifyNotDisposed();
                switch (value)
                {
                    case V8RuntimeViolationPolicy.Interrupt:
                        proxy.DisableIsolateHeapSizeViolationInterrupt = false;
                        return;

                    case V8RuntimeViolationPolicy.Exception:
                        proxy.DisableIsolateHeapSizeViolationInterrupt = true;
                        return;

                    default:
                        throw new ArgumentException(MiscHelpers.FormatInvariant("Invalid {0} value", nameof(V8RuntimeViolationPolicy)), nameof(value));
                }
            }
        }

        /// <summary>
        /// Creates a compiled script.
        /// </summary>
        /// <param name="code">The script code to compile.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        public V8Script Compile(string code)
        {
            return Compile(null, code);
        }

        /// <summary>
        /// Creates a compiled script with an associated document name.
        /// </summary>
        /// <param name="documentName">A document name for the compiled script. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="code">The script code to compile.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        public V8Script Compile(string documentName, string code)
        {
            return Compile(new DocumentInfo(documentName), code);
        }

        /// <summary>
        /// Creates a compiled script with the specified document meta-information.
        /// </summary>
        /// <param name="documentInfo">A structure containing meta-information for the script document.</param>
        /// <param name="code">The script code to compile.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        public V8Script Compile(DocumentInfo documentInfo, string code)
        {
            VerifyNotDisposed();
            return ScriptInvoke(() => CompileInternal(documentInfo.MakeUnique(this), code));
        }

        /// <summary>
        /// Creates a compiled script, generating cache data for accelerated recompilation.
        /// </summary>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be generated.</param>
        /// <param name="cacheBytes">Cache data for accelerated recompilation.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// The generated cache data can be stored externally and is usable in other V8 script
        /// engines and application processes.
        /// </remarks>
        /// <c><seealso cref="Compile(string, V8CacheKind, byte[], out bool)"/></c>
        public V8Script Compile(string code, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            return Compile(null, code, cacheKind, out cacheBytes);
        }

        /// <summary>
        /// Creates a compiled script with an associated document name, generating cache data for accelerated recompilation.
        /// </summary>
        /// <param name="documentName">A document name for the compiled script. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be generated.</param>
        /// <param name="cacheBytes">Cache data for accelerated recompilation.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// The generated cache data can be stored externally and is usable in other V8 script
        /// engines and application processes.
        /// </remarks>
        /// <c><seealso cref="Compile(string, string, V8CacheKind, byte[], out bool)"/></c>
        public V8Script Compile(string documentName, string code, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            return Compile(new DocumentInfo(documentName), code, cacheKind, out cacheBytes);
        }

        /// <summary>
        /// Creates a compiled script with the specified document meta-information, generating cache data for accelerated recompilation.
        /// </summary>
        /// <param name="documentInfo">A structure containing meta-information for the script document.</param>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be generated.</param>
        /// <param name="cacheBytes">Cache data for accelerated recompilation.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// The generated cache data can be stored externally and is usable in other V8 script
        /// engines and application processes.
        /// </remarks>
        /// <c><seealso cref="Compile(DocumentInfo, string, V8CacheKind, byte[], out bool)"/></c>
        public V8Script Compile(DocumentInfo documentInfo, string code, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            VerifyNotDisposed();

            V8Script tempScript = null;
            cacheBytes = ScriptInvoke(() =>
            {
                tempScript = CompileInternal(documentInfo.MakeUnique(this), code, cacheKind, out var tempCacheBytes);
                return tempCacheBytes;
            });

            return tempScript;
        }

        /// <summary>
        /// Creates a compiled script, consuming previously generated cache data.
        /// </summary>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be consumed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheAccepted"><c>True</c> if <paramref name="cacheBytes"/> was accepted and used to accelerate script compilation, <c>false</c> otherwise.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. Note that script compilation may be bypassed if a suitable compiled
        /// script already exists in the V8 runtime's memory. In that case, the cache data is
        /// ignored and <paramref name="cacheAccepted"/> is set to <c>false</c>.
        /// </remarks>
        /// <c><seealso cref="Compile(string, V8CacheKind, out byte[])"/></c>
        public V8Script Compile(string code, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            return Compile(null, code, cacheKind, cacheBytes, out cacheAccepted);
        }

        /// <summary>
        /// Creates a compiled script with an associated document name, consuming previously generated cache data.
        /// </summary>
        /// <param name="documentName">A document name for the compiled script. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be consumed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheAccepted"><c>True</c> if <paramref name="cacheBytes"/> was accepted and used to accelerate script compilation, <c>false</c> otherwise.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. Note that script compilation may be bypassed if a suitable compiled
        /// script already exists in the V8 runtime's memory. In that case, the cache data is
        /// ignored and <paramref name="cacheAccepted"/> is set to <c>false</c>.
        /// </remarks>
        /// <c><seealso cref="Compile(string, string, V8CacheKind, out byte[])"/></c>
        public V8Script Compile(string documentName, string code, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            return Compile(new DocumentInfo(documentName), code, cacheKind, cacheBytes, out cacheAccepted);
        }

        /// <summary>
        /// Creates a compiled script with an associated document name, consuming previously generated cache data.
        /// </summary>
        /// <param name="documentInfo">A structure containing meta-information for the script document.</param>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be consumed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheAccepted"><c>True</c> if <paramref name="cacheBytes"/> was accepted and used to accelerate script compilation, <c>false</c> otherwise.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. Note that script compilation may be bypassed if a suitable compiled
        /// script already exists in the V8 runtime's memory. In that case, the cache data is
        /// ignored and <paramref name="cacheAccepted"/> is set to <c>false</c>.
        /// </remarks>
        /// <c><seealso cref="Compile(DocumentInfo, string, V8CacheKind, out byte[])"/></c>
        public V8Script Compile(DocumentInfo documentInfo, string code, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            VerifyNotDisposed();

            V8Script tempScript = null;
            cacheAccepted = ScriptInvoke(() =>
            {
                tempScript = CompileInternal(documentInfo.MakeUnique(this), code, cacheKind, cacheBytes, out var tempCacheAccepted);
                return tempCacheAccepted;
            });

            return tempScript;
        }

        /// <summary>
        /// Creates a compiled script, consuming previously generated cache data and updating it if necessary.
        /// </summary>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be processed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheResult">The cache data processing result for the operation.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. If returned, the updated cache data can be stored externally and is
        /// usable in other V8 script engines and application processes.
        /// </remarks>
        public V8Script Compile(string code, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            return Compile(null, code, cacheKind, ref cacheBytes, out cacheResult);
        }

        /// <summary>
        /// Creates a compiled script with an associated document name, consuming previously generated cache data and updating it if necessary.
        /// </summary>
        /// <param name="documentName">A document name for the compiled script. Currently, this name is used only as a label in presentation contexts such as debugger user interfaces.</param>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be processed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheResult">The cache data processing result for the operation.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. If returned, the updated cache data can be stored externally and is
        /// usable in other V8 script engines and application processes.
        /// </remarks>
        public V8Script Compile(string documentName, string code, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            return Compile(new DocumentInfo(documentName), code, cacheKind, ref cacheBytes, out cacheResult);
        }

        /// <summary>
        /// Creates a compiled script with the specified document meta-information, consuming previously generated cache data and updating it if necessary.
        /// </summary>
        /// <param name="documentInfo">A structure containing meta-information for the script document.</param>
        /// <param name="code">The script code to compile.</param>
        /// <param name="cacheKind">The kind of cache data to be processed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheResult">The cache data processing result for the operation.</param>
        /// <returns>A compiled script that can be executed multiple times without recompilation.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. If returned, the updated cache data can be stored externally and is
        /// usable in other V8 script engines and application processes.
        /// </remarks>
        public V8Script Compile(DocumentInfo documentInfo, string code, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            VerifyNotDisposed();

            V8Script tempScript = null;
            var tempCacheBytes = cacheBytes;
            cacheResult = ScriptInvoke(() =>
            {
                tempScript = CompileInternal(documentInfo.MakeUnique(this), code, cacheKind, ref tempCacheBytes, out var tempCacheUpdated);
                return tempCacheUpdated;
            });

            if (cacheResult == V8CacheResult.Updated)
            {
                cacheBytes = tempCacheBytes;
            }

            return tempScript;
        }

        /// <summary>
        /// Loads and compiles a script document.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        public V8Script CompileDocument(string specifier)
        {
            return CompileDocument(specifier, null);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        public V8Script CompileDocument(string specifier, DocumentCategory category)
        {
            return CompileDocument(specifier, category, null);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category and context callback.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="contextCallback">An optional context callback for the requested document.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        public V8Script CompileDocument(string specifier, DocumentCategory category, DocumentContextCallback contextCallback)
        {
            MiscHelpers.VerifyNonBlankArgument(specifier, nameof(specifier), "Invalid document specifier");
            var document = DocumentSettings.LoadDocument(null, specifier, category, contextCallback);
            return Compile(document.Info, document.GetTextContents());
        }

        /// <summary>
        /// Loads and compiles a script document, generating cache data for accelerated recompilation.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="cacheKind">The kind of cache data to be generated.</param>
        /// <param name="cacheBytes">Cache data for accelerated recompilation.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// The generated cache data can be stored externally and is usable in other V8 runtimes
        /// and application processes.
        /// </remarks>
        public V8Script CompileDocument(string specifier, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            return CompileDocument(specifier, null, cacheKind, out cacheBytes);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category, generating cache data for accelerated recompilation.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="cacheKind">The kind of cache data to be generated.</param>
        /// <param name="cacheBytes">Cache data for accelerated recompilation.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// The generated cache data can be stored externally and is usable in other V8 runtimes
        /// and application processes.
        /// </remarks>
        public V8Script CompileDocument(string specifier, DocumentCategory category, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            return CompileDocument(specifier, category, null, cacheKind, out cacheBytes);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category and context callback, generating cache data for accelerated recompilation.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="contextCallback">An optional context callback for the requested document.</param>
        /// <param name="cacheKind">The kind of cache data to be generated.</param>
        /// <param name="cacheBytes">Cache data for accelerated recompilation.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// The generated cache data can be stored externally and is usable in other V8 runtimes
        /// and application processes.
        /// </remarks>
        public V8Script CompileDocument(string specifier, DocumentCategory category, DocumentContextCallback contextCallback, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            MiscHelpers.VerifyNonBlankArgument(specifier, nameof(specifier), "Invalid document specifier");
            var document = DocumentSettings.LoadDocument(null, specifier, category, contextCallback);
            return Compile(document.Info, document.GetTextContents(), cacheKind, out cacheBytes);
        }

        /// <summary>
        /// Loads and compiles a script document, consuming previously generated cache data.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="cacheKind">The kind of cache data to be consumed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheAccepted"><c>True</c> if <paramref name="cacheBytes"/> was accepted and used to accelerate script compilation, <c>false</c> otherwise.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. Note that script compilation may be bypassed if a suitable compiled
        /// script already exists in the V8 runtime's memory. In that case, the cache data is
        /// ignored and <paramref name="cacheAccepted"/> is set to <c>false</c>.
        /// </remarks>
        public V8Script CompileDocument(string specifier, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            return CompileDocument(specifier, null, cacheKind, cacheBytes, out cacheAccepted);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category, consuming previously generated cache data.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="cacheKind">The kind of cache data to be consumed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheAccepted"><c>True</c> if <paramref name="cacheBytes"/> was accepted and used to accelerate script compilation, <c>false</c> otherwise.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. Note that script compilation may be bypassed if a suitable compiled
        /// script already exists in the V8 runtime's memory. In that case, the cache data is
        /// ignored and <paramref name="cacheAccepted"/> is set to <c>false</c>.
        /// </remarks>
        public V8Script CompileDocument(string specifier, DocumentCategory category, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            return CompileDocument(specifier, category, null, cacheKind, cacheBytes, out cacheAccepted);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category and context callback, consuming previously generated cache data.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="contextCallback">An optional context callback for the requested document.</param>
        /// <param name="cacheKind">The kind of cache data to be consumed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheAccepted"><c>True</c> if <paramref name="cacheBytes"/> was accepted and used to accelerate script compilation, <c>false</c> otherwise.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. Note that script compilation may be bypassed if a suitable compiled
        /// script already exists in the V8 runtime's memory. In that case, the cache data is
        /// ignored and <paramref name="cacheAccepted"/> is set to <c>false</c>.
        /// </remarks>
        public V8Script CompileDocument(string specifier, DocumentCategory category, DocumentContextCallback contextCallback, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            MiscHelpers.VerifyNonBlankArgument(specifier, nameof(specifier), "Invalid document specifier");
            var document = DocumentSettings.LoadDocument(null, specifier, category, contextCallback);
            return Compile(document.Info, document.GetTextContents(), cacheKind, cacheBytes, out cacheAccepted);
        }

        /// <summary>
        /// Loads and compiles a script document, consuming previously generated cache data and updating it if necessary.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="cacheKind">The kind of cache data to be processed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheResult">The cache data processing result for the operation.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. If returned, the updated cache data can be stored externally and is
        /// usable in other V8 script engines and application processes.
        /// </remarks>
        public V8Script CompileDocument(string specifier, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            return CompileDocument(specifier, null, cacheKind, ref cacheBytes, out cacheResult);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category, consuming previously generated cache data and updating it if necessary.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="cacheKind">The kind of cache data to be processed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheResult">The cache data processing result for the operation.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. If returned, the updated cache data can be stored externally and is
        /// usable in other V8 script engines and application processes.
        /// </remarks>
        public V8Script CompileDocument(string specifier, DocumentCategory category, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            return CompileDocument(specifier, category, null, cacheKind, ref cacheBytes, out cacheResult);
        }

        /// <summary>
        /// Loads and compiles a document with the specified category and context callback, consuming previously generated cache data and updating it if necessary.
        /// </summary>
        /// <param name="specifier">A string specifying the document to be loaded and compiled.</param>
        /// <param name="category">An optional category for the requested document.</param>
        /// <param name="contextCallback">An optional context callback for the requested document.</param>
        /// <param name="cacheKind">The kind of cache data to be processed.</param>
        /// <param name="cacheBytes">Cache data for accelerated compilation.</param>
        /// <param name="cacheResult">The cache data processing result for the operation.</param>
        /// <returns>A compiled script that can be executed by multiple V8 script engine instances.</returns>
        /// <remarks>
        /// To be accepted, the cache data must have been generated for identical script code by
        /// the same V8 build. If returned, the updated cache data can be stored externally and is
        /// usable in other V8 script engines and application processes.
        /// </remarks>
        public V8Script CompileDocument(string specifier, DocumentCategory category, DocumentContextCallback contextCallback, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            MiscHelpers.VerifyNonBlankArgument(specifier, nameof(specifier), "Invalid document specifier");
            var document = DocumentSettings.LoadDocument(null, specifier, category, contextCallback);
            return Compile(document.Info, document.GetTextContents(), cacheKind, ref cacheBytes, out cacheResult);
        }

        // ReSharper disable ParameterHidesMember

        /// <summary>
        /// Evaluates a compiled script.
        /// </summary>
        /// <param name="script">The compiled script to evaluate.</param>
        /// <returns>The result value.</returns>
        /// <remarks>
        /// For information about the types of result values that script code can return, see
        /// <c><see cref="ScriptEngine.Evaluate(string, bool, string)"/></c>.
        /// </remarks>
        public object Evaluate(V8Script script)
        {
            return Execute(script, true);
        }

        /// <summary>
        /// Executes a compiled script.
        /// </summary>
        /// <param name="script">The compiled script to execute.</param>
        /// <remarks>
        /// This method is similar to <c><see cref="Evaluate(V8Script)"/></c> with the exception that it
        /// does not marshal a result value to the host. It can provide a performance advantage
        /// when the result value is not needed.
        /// </remarks>
        public void Execute(V8Script script)
        {
            Execute(script, false);
        }

        // ReSharper restore ParameterHidesMember

        /// <summary>
        /// Cancels any pending request to interrupt script execution.
        /// </summary>
        /// <remarks>
        /// This method can be called safely from any thread.
        /// </remarks>
        /// <c><seealso cref="Interrupt"/></c>
        public void CancelInterrupt()
        {
            VerifyNotDisposed();
            proxy.CancelInterrupt();
        }

        /// <summary>
        /// Returns memory usage information for the V8 runtime.
        /// </summary>
        /// <returns>A <c><see cref="V8RuntimeHeapInfo"/></c> object containing memory usage information for the V8 runtime.</returns>
        public V8RuntimeHeapInfo GetRuntimeHeapInfo()
        {
            VerifyNotDisposed();
            return proxy.GetIsolateHeapInfo();
        }

        /// <summary>
        /// Begins collecting a new CPU profile.
        /// </summary>
        /// <param name="name">A name for the profile.</param>
        /// <returns><c>True</c> if the profile was created successfully, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// A V8 script engine can collect multiple CPU profiles simultaneously.
        /// </remarks>
        public bool BeginCpuProfile(string name)
        {
            return BeginCpuProfile(name, V8CpuProfileFlags.None);
        }

        /// <summary>
        /// Begins collecting a new CPU profile with the specified options.
        /// </summary>
        /// <param name="name">A name for the profile.</param>
        /// <param name="flags">Options for creating the profile.</param>
        /// <returns><c>True</c> if the profile was created successfully, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// A V8 script engine can collect multiple CPU profiles simultaneously.
        /// </remarks>
        public bool BeginCpuProfile(string name, V8CpuProfileFlags flags)
        {
            VerifyNotDisposed();
            return proxy.BeginCpuProfile(Name + ':' + name, flags);
        }

        /// <summary>
        /// Completes and returns a CPU profile.
        /// </summary>
        /// <param name="name">The name of the profile.</param>
        /// <returns>The profile if it was found and completed successfully, <c>null</c> otherwise.</returns>
        /// <remarks>
        /// An empty <paramref name="name"/> argument selects the most recently created CPU profile.
        /// </remarks>
        public V8CpuProfile EndCpuProfile(string name)
        {
            VerifyNotDisposed();
            return proxy.EndCpuProfile(Name + ':' + name);
        }

        /// <summary>
        /// Collects a sample in all CPU profiles active in the V8 runtime.
        /// </summary>
        public void CollectCpuProfileSample()
        {
            VerifyNotDisposed();
            proxy.CollectCpuProfileSample();
        }

        /// <summary>
        /// Gets or sets the time interval between automatic CPU profile samples, in microseconds.
        /// </summary>
        /// <remarks>
        /// Assigning this property has no effect on CPU profiles already active in the V8 runtime.
        /// The default value is 1000.
        /// </remarks>
        public uint CpuProfileSampleInterval
        {
            get
            {
                VerifyNotDisposed();
                return proxy.CpuProfileSampleInterval;
            }

            set
            {
                VerifyNotDisposed();
                proxy.CpuProfileSampleInterval = value;
            }
        }

        /// <summary>
        /// Writes a snapshot of the V8 runtime's heap to the given stream.
        /// </summary>
        /// <param name="stream">The stream to which to write the heap snapshot.</param>
        /// <remarks>
        /// This method generates a heap snapshot in JSON format with ASCII encoding.
        /// </remarks>
        public void WriteRuntimeHeapSnapshot(Stream stream)
        {
            MiscHelpers.VerifyNonNullArgument(stream, nameof(stream));
            VerifyNotDisposed();

            ScriptInvoke(() => proxy.WriteIsolateHeapSnapshot(stream));
        }

        #endregion

        #region internal members

        internal V8ContextProxy ContextProxy => proxy;

        internal V8Runtime.Statistics GetRuntimeStatistics()
        {
            VerifyNotDisposed();
            return proxy.GetIsolateStatistics();
        }

        internal Statistics GetStatistics()
        {
            VerifyNotDisposed();
            return ScriptInvoke(() =>
            {
                var statistics = proxy.GetStatistics();

                if (commonJSManager != null)
                {
                    statistics.CommonJSModuleCacheSize = CommonJSManager.ModuleCacheSize;
                }

                return statistics;
            });
        }

        internal bool Equals(V8ScriptItem left, V8ScriptItem right)
        {
            if ((left.Engine is V8ScriptEngine leftEngine) && (right.Engine is V8ScriptEngine rightEngine) && (leftEngine.runtime == rightEngine.runtime) && (left.GetHashCode() == right.GetHashCode()))
            {
                var engineInternal = (ScriptObject)script.GetProperty("EngineInternal");
                return (bool)engineInternal.InvokeMethod("strictEquals", left, right);
            }

            return false;
        }

        internal CommonJSManager CommonJSManager => commonJSManager ?? (commonJSManager = new CommonJSManager(this));

        internal JsonModuleManager JsonModuleManager => jsonDocumentManager ?? (jsonDocumentManager = new JsonModuleManager(this));

        private object GetRootItem()
        {
            return MarshalToHost(ScriptInvoke(() => proxy.GetRootItem()), false);
        }

        private void VerifyNotDisposed()
        {
            if (disposedFlag.IsSet)
            {
                throw new ObjectDisposedException(ToString());
            }
        }

        // ReSharper disable ParameterHidesMember

        private object Execute(V8Script script, bool evaluate)
        {
            MiscHelpers.VerifyNonNullArgument(script, nameof(script));
            VerifyNotDisposed();

            return MarshalToHost(ScriptInvoke(() =>
            {
                if (inContinuationTimerScope || (ContinuationCallback == null))
                {
                    if (ShouldAwaitDebuggerAndPause(script.DocumentInfo))
                    {
                        proxy.AwaitDebuggerAndPause();
                    }

                    return ExecuteInternal(script, evaluate);
                }

                var state = new Timer[] { null };
                using (state[0] = new Timer(_ => OnContinuationTimer(state[0]), null, Timeout.Infinite, Timeout.Infinite))
                {
                    inContinuationTimerScope = true;
                    try
                    {
                        state[0].Change(continuationInterval, Timeout.Infinite);

                        if (ShouldAwaitDebuggerAndPause(script.DocumentInfo))
                        {
                            proxy.AwaitDebuggerAndPause();
                        }

                        return ExecuteInternal(script, evaluate);
                    }
                    finally
                    {
                        inContinuationTimerScope = false;
                    }
                }
            }), false);
        }

        // ReSharper restore ParameterHidesMember

        private V8Script CompileInternal(UniqueDocumentInfo documentInfo, string code)
        {
            if (FormatCode)
            {
                code = MiscHelpers.FormatCode(code);
            }

            CommonJSManager.Module module = null;
            if (documentInfo.Category == ModuleCategory.CommonJS)
            {
                module = CommonJSManager.GetOrCreateModule(documentInfo, code);
                code = CommonJSManager.Module.GetAugmentedCode(code);
            }
            else if ((documentInfo.Category != DocumentCategory.Script) && (documentInfo.Category != ModuleCategory.Standard))
            {
                throw new NotSupportedException("The script engine cannot compile documents of type '" + documentInfo.Category + "'");
            }

            // ReSharper disable once LocalVariableHidesMember
            var script = proxy.Compile(documentInfo, code);

            if (module != null)
            {
                module.Evaluator = () => proxy.Execute(script, true);
            }

            return script;
        }

        private V8Script CompileInternal(UniqueDocumentInfo documentInfo, string code, V8CacheKind cacheKind, out byte[] cacheBytes)
        {
            if (FormatCode)
            {
                code = MiscHelpers.FormatCode(code);
            }

            CommonJSManager.Module module = null;
            if (documentInfo.Category == ModuleCategory.CommonJS)
            {
                module = CommonJSManager.GetOrCreateModule(documentInfo, code);
                code = CommonJSManager.Module.GetAugmentedCode(code);
            }
            else if ((documentInfo.Category != DocumentCategory.Script) && (documentInfo.Category != ModuleCategory.Standard))
            {
                throw new NotSupportedException("The script engine cannot compile documents of type '" + documentInfo.Category + "'");
            }

            // ReSharper disable once LocalVariableHidesMember
            var script = proxy.Compile(documentInfo, code, cacheKind, out cacheBytes);

            if (module != null)
            {
                module.Evaluator = () => proxy.Execute(script, true);
            }

            return script;
        }

        private V8Script CompileInternal(UniqueDocumentInfo documentInfo, string code, V8CacheKind cacheKind, byte[] cacheBytes, out bool cacheAccepted)
        {
            if (FormatCode)
            {
                code = MiscHelpers.FormatCode(code);
            }

            CommonJSManager.Module module = null;
            if (documentInfo.Category == ModuleCategory.CommonJS)
            {
                module = CommonJSManager.GetOrCreateModule(documentInfo, code);
                code = CommonJSManager.Module.GetAugmentedCode(code);
            }
            else if ((documentInfo.Category != DocumentCategory.Script) && (documentInfo.Category != ModuleCategory.Standard))
            {
                throw new NotSupportedException("The script engine cannot compile documents of type '" + documentInfo.Category + "'");
            }

            // ReSharper disable once LocalVariableHidesMember
            var script = proxy.Compile(documentInfo, code, cacheKind, cacheBytes, out cacheAccepted);

            if (module != null)
            {
                module.Evaluator = () => proxy.Execute(script, true);
            }

            return script;
        }

        private V8Script CompileInternal(UniqueDocumentInfo documentInfo, string code, V8CacheKind cacheKind, ref byte[] cacheBytes, out V8CacheResult cacheResult)
        {
            if (FormatCode)
            {
                code = MiscHelpers.FormatCode(code);
            }

            CommonJSManager.Module module = null;
            if (documentInfo.Category == ModuleCategory.CommonJS)
            {
                module = CommonJSManager.GetOrCreateModule(documentInfo, code);
                code = CommonJSManager.Module.GetAugmentedCode(code);
            }
            else if ((documentInfo.Category != DocumentCategory.Script) && (documentInfo.Category != ModuleCategory.Standard))
            {
                throw new NotSupportedException("The script engine cannot compile documents of type '" + documentInfo.Category + "'");
            }

            // ReSharper disable once LocalVariableHidesMember
            var script = proxy.Compile(documentInfo, code, cacheKind, ref cacheBytes, out cacheResult);

            if (module != null)
            {
                module.Evaluator = () => proxy.Execute(script, true);
            }

            return script;
        }

        private object ExecuteInternal(UniqueDocumentInfo documentInfo, string code, bool evaluate)
        {
            if (FormatCode)
            {
                code = MiscHelpers.FormatCode(code);
            }

            if (documentInfo.Category == ModuleCategory.CommonJS)
            {
                var module = CommonJSManager.GetOrCreateModule(documentInfo, code);
                return module.Process();
            }

            if ((documentInfo.Category != DocumentCategory.Script) && (documentInfo.Category != ModuleCategory.Standard))
            {
                throw new NotSupportedException("The script engine cannot execute documents of type '" + documentInfo.Category + "'");
            }

            return ExecuteRaw(documentInfo, code, evaluate);
        }

        // ReSharper disable ParameterHidesMember

        private object ExecuteInternal(V8Script script, bool evaluate)
        {
            if (script.UniqueDocumentInfo.Category == ModuleCategory.CommonJS)
            {
                var module = CommonJSManager.GetOrCreateModule(script.UniqueDocumentInfo, script.CodeDigest, () => proxy.Execute(script, evaluate));
                return module.Process();
            }

            return proxy.Execute(script, evaluate);
        }

        // ReSharper restore ParameterHidesMember

        private bool ShouldAwaitDebuggerAndPause(DocumentInfo documentInfo)
        {
            if (!awaitDebuggerAndPause.HasValue)
            {
                if (documentInfo.Flags.GetValueOrDefault().HasFlag(DocumentFlags.AwaitDebuggerAndPause))
                {
                    awaitDebuggerAndPause = false;
                    return true;
                }

                return false;
            }

            if (awaitDebuggerAndPause.Value)
            {
                awaitDebuggerAndPause = false;
                return true;
            }

            return false;
        }

        private void OnContinuationTimer(Timer timer)
        {
            try
            {
                var callback = ContinuationCallback;
                if ((callback != null) && !callback())
                {
                    Interrupt();
                }
                else
                {
                    timer.Change(continuationInterval, Timeout.Infinite);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private object CreatePromise(Action<object, object> executor)
        {
            VerifyNotDisposed();
            var v8Internal = (V8ScriptItem)script.GetProperty("EngineInternal");
            return V8ScriptItem.Wrap(this, v8Internal.InvokeMethod(false, "createPromise", executor));
        }

        private void CompletePromise<T>(Task<T> task, object resolve, object reject)
        {
            Func<T> getResult = () => task.Result;
            var engineInternal = (ScriptObject)script.GetProperty("EngineInternal");
            engineInternal.InvokeMethod("completePromiseWithResult", getResult, resolve, reject);
        }

        private void CompletePromise(Task task, object resolve, object reject)
        {
            Action wait = task.Wait;
            var engineInternal = (ScriptObject)script.GetProperty("EngineInternal");
            engineInternal.InvokeMethod("completePromise", wait, resolve, reject);
        }

        partial void TryConvertValueTaskToPromise(object obj, Action<object> setResult);

        #endregion

        #region ScriptEngine overrides (public members)

        /// <summary>
        /// Gets the script engine's recommended file name extension for script files.
        /// </summary>
        /// <remarks>
        /// <c><see cref="V8ScriptEngine"/></c> instances return "js" for this property.
        /// </remarks>
        public override string FileNameExtension => "js";

        /// <inheritdoc/>
        public override dynamic Script
        {
            get
            {
                VerifyNotDisposed();
                return script;
            }
        }

        /// <inheritdoc/>
        public override ScriptObject Global
        {
            get
            {
                VerifyNotDisposed();
                return script;
            }
        }

        /// <summary>
        /// Executes script code as a command.
        /// </summary>
        /// <param name="command">The script command to execute.</param>
        /// <returns>The command output.</returns>
        /// <remarks>
        /// <para>
        /// This method is similar to <c><see cref="ScriptEngine.Evaluate(string)"/></c> but optimized for
        /// command consoles. The specified command must be limited to a single expression or
        /// statement. Script engines can override this method to customize command execution as
        /// well as the process of converting the result to a string for console output.
        /// </para>
        /// <para>
        /// The <c><see cref="V8ScriptEngine"/></c> version of this method attempts to use
        /// <c><see href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Object/tostring">toString</see></c>
        /// to convert the return value.
        /// </para>
        /// </remarks>
        public override string ExecuteCommand(string command)
        {
            return ScriptInvoke(() =>
            {
                var engineInternal = (ScriptObject)script.GetProperty("EngineInternal");
                var commandHolder = (ScriptObject)engineInternal.GetProperty("commandHolder");
                commandHolder.SetProperty("command", command);
                return base.ExecuteCommand("EngineInternal.getCommandResult(eval(EngineInternal.commandHolder.command))");
            });
        }

        /// <inheritdoc/>
        public override string GetStackTrace()
        {
            var engineInternal = (ScriptObject)script.GetProperty("EngineInternal");
            var stackTrace = (string)engineInternal.InvokeMethod("getStackTrace");
            var lines = stackTrace.Split('\n');
            return string.Join("\n", lines.Skip(2));
        }

        /// <summary>
        /// Interrupts script execution and causes the script engine to throw an exception.
        /// </summary>
        /// <remarks>
        /// This method can be called safely from any thread.
        /// </remarks>
        /// <c><seealso cref="CancelInterrupt"/></c>
        public override void Interrupt()
        {
            VerifyNotDisposed();
            proxy.Interrupt();
        }

        /// <inheritdoc/>
        public override void CollectGarbage(bool exhaustive)
        {
            VerifyNotDisposed();
            proxy.CollectGarbage(exhaustive);
        }

        #endregion

        #region ScriptEngine overrides (internal members)

        internal override IUniqueNameManager DocumentNameManager { get; }

        internal override bool EnumerateInstanceMethods => base.EnumerateInstanceMethods && !SuppressInstanceMethodEnumeration;

        internal override bool EnumerateExtensionMethods => base.EnumerateExtensionMethods && !SuppressExtensionMethodEnumeration;

        internal override bool UseCaseInsensitiveMemberBinding => engineFlags.HasFlag(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding);

        internal override void AddHostItem(string itemName, HostItemFlags flags, object item)
        {
            VerifyNotDisposed();

            var globalMembers = flags.HasFlag(HostItemFlags.GlobalMembers);
            if (globalMembers && engineFlags.HasFlag(V8ScriptEngineFlags.DisableGlobalMembers))
            {
                throw new InvalidOperationException("GlobalMembers support is disabled in this script engine");
            }

            MiscHelpers.VerifyNonNullArgument(itemName, nameof(itemName));
            Debug.Assert(item != null);

            ScriptInvoke(() =>
            {
                object marshaledItem;

#if NETCOREAPP || NETSTANDARD
                if (item is SplitProxy.IV8HostObject || item is SplitProxy.InvokeHostObject)
                {
                    marshaledItem = item;
                }
                else
#endif
                {
                    marshaledItem = MarshalToScript(item, flags);
                    if (!(marshaledItem is HostItem))
                    {
                        throw new InvalidOperationException("Invalid host item");
                    }
                }

                proxy.AddGlobalItem(itemName, marshaledItem, globalMembers);
            });
        }

        internal override object MarshalToScript(object obj, HostItemFlags flags)
        {
            const long maxIntInDouble = (1L << 53) - 1;

            if (obj == null)
            {
                obj = NullExportValue;
            }

            if (obj == null)
            {
                return DBNull.Value;
            }

            if (obj is DBNull)
            {
                return obj;
            }

            if (obj is Undefined)
            {
                return null;
            }

            if (obj is Nonexistent)
            {
                return obj;
            }

            if (obj is INothingTag)
            {
                return null;
            }

            if (obj is BigInteger)
            {
                return obj;
            }

            if (obj is long longValue)
            {
                if (engineFlags.HasFlag(V8ScriptEngineFlags.MarshalAllLongAsBigInt))
                {
                    return new BigInteger(longValue);
                }

                if (engineFlags.HasFlag(V8ScriptEngineFlags.MarshalUnsafeLongAsBigInt) && (Math.Abs(longValue) > maxIntInDouble))
                {
                    return new BigInteger(longValue);
                }
            }

            if (obj is ulong ulongValue)
            {
                if (engineFlags.HasFlag(V8ScriptEngineFlags.MarshalAllLongAsBigInt))
                {
                    return new BigInteger(ulongValue);
                }

                if (engineFlags.HasFlag(V8ScriptEngineFlags.MarshalUnsafeLongAsBigInt) && (ulongValue > maxIntInDouble))
                {
                    return new BigInteger(ulongValue);
                }
            }

            if (engineFlags.HasFlag(V8ScriptEngineFlags.EnableDateTimeConversion) && (obj is DateTime))
            {
                return obj;
            }

            if (engineFlags.HasFlag(V8ScriptEngineFlags.EnableTaskPromiseConversion))
            {
                // .NET Core async functions return Task subclass instances that trigger result wrapping

                var testObject = obj;
                if (testObject is HostObject testHostObject)
                {
                    testObject = testHostObject.Target;
                }

                if (testObject != null)
                {
                    if (testObject.GetType().IsAssignableToGenericType(typeof(Task<>), out var typeArgs))
                    {
                        obj = typeof(TaskConverter<>).MakeSpecificType(typeArgs).InvokeMember("ToPromise", BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static, null, null, new[] { testObject, this });
                    }
                    else if (testObject is Task task)
                    {
                        obj = task.ToPromise(this);
                    }
                    else if (engineFlags.HasFlag(V8ScriptEngineFlags.EnableValueTaskPromiseConversion))
                    {
                        TryConvertValueTaskToPromise(testObject, result => obj = result);
                    }
                }
            }

            if (obj is HostItem hostItem)
            {
                if ((hostItem.Engine == this) && (hostItem.Flags == flags))
                {
                    return obj;
                }

                obj = hostItem.Target;
            }

            var hostTarget = obj as HostTarget;
            if ((hostTarget != null) && !(hostTarget is IHostVariable))
            {
                obj = hostTarget.Target;
            }

            if (obj is ScriptItem scriptItem)
            {
                if ((scriptItem.Engine is V8ScriptEngine that) && (that.runtime == runtime))
                {
                    return scriptItem.Unwrap();
                }

                if ((scriptItem is V8ScriptItem v8ScriptItem) && v8ScriptItem.IsShared)
                {
                    return scriptItem.Unwrap();
                }
            }

            return HostItem.Wrap(this, hostTarget ?? obj, flags);
        }

        internal override object MarshalToHost(object obj, bool preserveHostTarget)
        {
            if (obj == null)
            {
                return UndefinedImportValue;
            }

            if (obj is DBNull)
            {
                return null;
            }

            if (MiscHelpers.TryMarshalPrimitiveToHost(obj, DisableFloatNarrowing, out var result))
            {
                return result;
            }

            if (obj is HostTarget hostTarget)
            {
                return preserveHostTarget ? hostTarget : hostTarget.Target;
            }

            if (obj is HostItem hostItem)
            {
                return preserveHostTarget ? hostItem.Target : hostItem.Unwrap();
            }

            if (obj is ScriptItem)
            {
                return obj;
            }

            var scriptItem = V8ScriptItem.Wrap(this, obj);
            if (engineFlags.HasFlag(V8ScriptEngineFlags.EnableTaskPromiseConversion) && (obj is IV8Object v8Object) && v8Object.IsPromise)
            {
                return scriptItem.ToTask();
            }

            return scriptItem;
        }

        internal override object Execute(UniqueDocumentInfo documentInfo, string code, bool evaluate)
        {
            VerifyNotDisposed();

            return ScriptInvoke(() =>
            {
                if ((documentNames != null) && !documentInfo.Flags.GetValueOrDefault().HasFlag(DocumentFlags.IsTransient))
                {
                    documentNames.Add(documentInfo.UniqueName);
                }

                if (inContinuationTimerScope || (ContinuationCallback == null))
                {
                    if (ShouldAwaitDebuggerAndPause(documentInfo.Info))
                    {
                        proxy.AwaitDebuggerAndPause();
                    }

                    return ExecuteInternal(documentInfo, code, evaluate);
                }

                var state = new Timer[] { null };
                using (state[0] = new Timer(_ => OnContinuationTimer(state[0]), null, Timeout.Infinite, Timeout.Infinite))
                {
                    inContinuationTimerScope = true;
                    try
                    {
                        state[0].Change(continuationInterval, Timeout.Infinite);

                        if (ShouldAwaitDebuggerAndPause(documentInfo.Info))
                        {
                            proxy.AwaitDebuggerAndPause();
                        }

                        return ExecuteInternal(documentInfo, code, evaluate);
                    }
                    finally
                    {
                        inContinuationTimerScope = false;
                    }
                }
            });
        }

        internal override object ExecuteRaw(UniqueDocumentInfo documentInfo, string code, bool evaluate)
        {
            return proxy.Execute(documentInfo, code, evaluate);
        }

        internal override HostItemCollateral HostItemCollateral { get; }

        internal override void OnAccessSettingsChanged()
        {
            base.OnAccessSettingsChanged();
            ScriptInvoke(() => proxy.OnAccessSettingsChanged());
        }

        #endregion

        #region ScriptEngine overrides (script-side invocation)

        internal override void ScriptInvoke(Action action)
        {
            VerifyNotDisposed();
            using (CreateEngineScope())
            {
                proxy.InvokeWithLock(() => ScriptInvokeInternal(action));
            }
        }

        internal override T ScriptInvoke<T>(Func<T> func)
        {
            VerifyNotDisposed();
            using (CreateEngineScope())
            {
                var result = default(T);
                proxy.InvokeWithLock(() => result = ScriptInvokeInternal(func));
                return result;
            }
        }

        #endregion

        #region ScriptEngine overrides (disposal / finalization)

        /// <summary>
        /// Releases the unmanaged resources used by the script engine and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"><c>True</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        /// <remarks>
        /// This method is called by the public <c><see cref="ScriptEngine.Dispose()"/></c> method and the
        /// <c><see cref="ScriptEngine.Finalize">Finalize</see></c> method.
        /// <c><see cref="ScriptEngine.Dispose()"/></c> invokes the protected <c>Dispose(Boolean)</c>
        /// method with the <paramref name="disposing"/> parameter set to <c>true</c>.
        /// <c><see cref="ScriptEngine.Finalize">Finalize</see></c> invokes <c>Dispose(Boolean)</c> with
        /// <paramref name="disposing"/> set to <c>false</c>.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (disposedFlag.Set())
                {
                    base.Dispose(true);

                    ((IDisposable)script).Dispose();
                    proxy.Dispose();

                    if (usingPrivateRuntime)
                    {
                        runtime.Dispose();
                    }
                }
            }
            else
            {
                base.Dispose(false);
            }
        }

        #endregion

        #region IJavaScriptEngine implementation

        uint IJavaScriptEngine.BaseLanguageVersion => 8;

        CommonJSManager IJavaScriptEngine.CommonJSManager => CommonJSManager;

        JsonModuleManager IJavaScriptEngine.JsonModuleManager => JsonModuleManager;

        object IJavaScriptEngine.CreatePromiseForTask<T>(Task<T> task)
        {
            var scheduler = (engineFlags.HasFlag(V8ScriptEngineFlags.UseSynchronizationContexts) && MiscHelpers.Try(out var contextScheduler, TaskScheduler.FromCurrentSynchronizationContext)) ? contextScheduler : TaskScheduler.Current;
            return CreatePromise((resolve, reject) =>
            {
                task.ContinueWith(_ => CompletePromise(task, resolve, reject), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);
            });
        }

        object IJavaScriptEngine.CreatePromiseForTask(Task task)
        {
            var scheduler = (engineFlags.HasFlag(V8ScriptEngineFlags.UseSynchronizationContexts) && MiscHelpers.Try(out var contextScheduler, TaskScheduler.FromCurrentSynchronizationContext)) ? contextScheduler : TaskScheduler.Current;
            return CreatePromise((resolve, reject) =>
            {
                task.ContinueWith(_ => CompletePromise(task, resolve, reject), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);
            });
        }

        Task<object> IJavaScriptEngine.CreateTaskForPromise(ScriptObject promise)
        {
            if (!(promise is V8ScriptItem v8ScriptItem) || !v8ScriptItem.IsPromise)
            {
                throw new ArgumentException("The object is not a V8 promise", nameof(promise));
            }

            var source = new TaskCompletionSource<object>();
            var context = engineFlags.HasFlag(V8ScriptEngineFlags.UseSynchronizationContexts) ? SynchronizationContext.Current : null;

            Action<object> setResultWorker = result => source.SetResult(result);
            var setResult = (context == null) ? setResultWorker : result => context.Post(_ => setResultWorker(result), null);

            Action<Exception> setExceptionWorker = exception => source.SetException(exception);
            var setException = (context == null) ? setExceptionWorker : exception => context.Post(_ => setExceptionWorker(exception), null);

            Action<object> onResolved = result =>
            {
                setResult(result);
            };

            Action<object> onRejected = error =>
            {
                try
                {
                    var engineInternal = (ScriptObject)script.GetProperty("EngineInternal");
                    engineInternal.InvokeMethod("throwValue", error);
                }
                catch (Exception exception)
                {
                    setException(exception);
                }
            };

            v8ScriptItem.InvokeMethod(false, "then", onResolved, onRejected);

            return source.Task;
        }

        #endregion

        #region unit test support

        internal void EnableDocumentNameTracking()
        {
            documentNames = new List<string>();
        }

        internal IEnumerable<string> GetDocumentNames()
        {
            return documentNames;
        }

        #endregion

        #region Nested type: Statistics

        internal sealed class Statistics
        {
            public ulong ScriptCount;
            public ulong ModuleCount;
            public ulong ModuleCacheSize;
            public int CommonJSModuleCacheSize;
        }

        #endregion

        #region Nested type: TaskConverter

        private static class TaskConverter<T>
        {
            // ReSharper disable UnusedMember.Local

            public static object ToPromise(Task<T> task, V8ScriptEngine engine)
            {
                return task.ToPromise(engine);
            }

            // ReSharper restore UnusedMember.Local
        }

        #endregion

        #region Nested type: JsonHelper

        /// <exclude/>
        public sealed class JsonHelper : JsonConverter
        {
            private readonly ScriptObject stringify;
            private readonly HashSet<object> cycleDetectionSet = new HashSet<object>();

            /// <exclude/>
            public JsonHelper(V8ScriptEngine engine)
            {
                var json = (ScriptObject)engine.script.GetProperty("JSON");
                stringify = (ScriptObject)json.GetProperty("stringify");
            }

            /// <exclude/>
            public string ToJson(object key, object value)
            {
                key = MiscHelpers.EnsureNonBlank(key.ToString(), "[root]");

                if (cycleDetectionSet.Contains(value))
                {
                    throw new InvalidOperationException($"Cycle detected at key '{key}' during JSON serialization");
                }

                cycleDetectionSet.Add(value);
                try
                {
                    return JsonConvert.SerializeObject(value, this);
                }
                finally
                {
                    cycleDetectionSet.Remove(value);
                }
            }

            /// <exclude/>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var result = stringify.Invoke(false, value);
                writer.WriteRawValue(result as string ?? "null");
            }

            /// <exclude/>
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotImplementedException();

            /// <exclude/>
            public override bool CanConvert(Type objectType) => typeof(V8ScriptItem).IsAssignableFrom(objectType);
        }

        #endregion
    }
}
