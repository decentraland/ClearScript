#!/bin/bash

v8testedrev=14.7.173.23
v8testedcommit=
v8cherrypicks=

if [[ $v8testedcommit == "" ]]; then
    v8testedcommit=$v8testedrev
fi

jsontag=v3.10.4

function usage {
    echo
    echo 'Downloads and builds V8 for use with ClearScript.'
    echo
    echo 'V8Update.sh [-n] [-y] [cpu] [mode] [revision]'
    echo
    echo '  -n        Do not download; use previously downloaded files if possible.'
    echo '  -y        Reply "yes" automatically when prompted to continue.'
    echo '  cpu       Target CPU: "x86", "x64", "arm", or "arm64".'
    echo '  mode      Build mode: "Debug" or "Release" (default).'
    echo '  revision  V8 revision: "Latest", "Tested" (default) or branch/commit/tag.'
    echo '            * Examples: "candidate", "3.29.88.16".'
    echo '            * View history at https://chromium.googlesource.com/v8/v8.git.'
    echo
    exit $1
}

function abort {
    exit 1
}

function fail {
    echo "*** THE PREVIOUS STEP FAILED ***"
    abort
}

function continue {
    echo "$1"
    if [[ $autoreply == false ]]; then
        read -p "Continue (y/N)? " -r
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then exit 1; fi
    fi
}

v8rev=$v8testedrev
v8commit=$v8testedcommit
download=true
mode=Release
isdebug=false
autoreply=false

shopt -s nocasematch

while [[ $# -gt 0 ]]; do
    if [[ $1 == "-h" ]]; then
        usage 0
    elif [[ $1 == "-n" ]]; then
        download=false
    elif [[ $1 == "-y" ]]; then
        autoreply=true
    elif [[ $1 == x86 ]]; then
        cpu=x86
    elif [[ $1 == x64 ]]; then
        cpu=x64
    elif [[ $1 == arm ]]; then
        cpu=arm
    elif [[ $1 == arm64 ]]; then
        cpu=arm64
    elif [[ $1 == debug ]]; then
        mode=Debug
        isdebug=true
    elif [[ $1 == release ]]; then
        mode=Release
        isdebug=false
    else
        v8rev=$1
        v8commit=$1
    fi
    shift
done

if [[ $cpu == "" ]]; then
    arch=`uname -m`
    if [[ $arch == i386 ]]; then
        cpu=x86
    elif [[ $arch == x86_64 ]]; then
        cpu=x64
    elif [[ $arch == arm ]]; then
        cpu=arm
    elif [[ $arch == arm32 ]]; then
        cpu=arm
    elif [[ $arch == aarch32 ]]; then
        cpu=arm
    elif [[ $arch == arm64 ]]; then
        cpu=arm64
    elif [[ $arch == aarch64 ]]; then
        cpu=arm64
    else
        echo "Error: Unsupported machine architecture '$arch'"
        abort
    fi
fi

kernel=`uname -s`
if [[ $kernel == Linux ]]; then
    linux=true
else
    linux=false
fi

shopt -u nocasematch

if [[ $cpu != x86 && $cpu != x64 && $cpu != arm && $cpu != arm64 ]]; then
    echo "Error: Unsupported target CPU '$cpu'"
    abort
fi

echo "Build: $cpu $mode"
cd ../V8 || abort

if [[ $download == false && ! -d build ]]; then
    continue '*** BUILD DIRECTORY NOT FOUND; DOWNLOAD REQUIRED ***'
    download=true
fi

if [[ $download == true ]]; then

    shopt -s nocasematch

    if [[ $v8rev == tested || $v8rev == $v8testedrev ]]; then
        v8rev=$v8testedrev
        v8commit=$v8testedcommit
        echo "V8 revision: Tested ($v8testedrev)"
    elif [[ $v8rev == latest ]]; then
        v8rev=master
        v8commit=master
        echo "V8 revision: Latest"
        continue '*** WARNING: THIS V8 REVISION MAY NOT BE COMPATIBLE WITH CLEARSCRIPT ***'
    else
        echo "V8 revision: $v8rev"
        continue '*** WARNING: THIS V8 REVISION MAY NOT BE COMPATIBLE WITH CLEARSCRIPT ***'
    fi

    shopt -u nocasematch

    if [[ -d build ]]; then
        echo "Removing old build directory ..."
        rm -rf build || fail
    fi

    echo "Creating build directory ..."
    mkdir build || fail

    cd build || abort

    echo "Downloading Depot Tools ..."
    git clone https://chromium.googlesource.com/chromium/tools/depot_tools.git 2>depot_tools.log || fail
    PATH=$PWD/depot_tools:$PATH

    echo "Downloading V8 and dependencies ..."
    gclient config https://chromium.googlesource.com/v8/v8 >config.log 2>config.log || fail
    gclient sync -r $v8commit >sync.log 2>sync.log || fail

    echo "Applying patches ..."
    cd v8 || abort
    git config user.name ClearScript || fail
    git config user.email "clearscript@clearfoundry.net" || fail
    if [[ $v8cherrypicks != "" ]]; then
        git cherry-pick --allow-empty-message --keep-redundant-commits $v8cherrypicks >apply-cherry-picks.log 2>apply-cherry-picks.log || fail
    fi
    patch_failed=false
    git apply --index --reject --ignore-whitespace ../../V8Patch.txt 2>apply-patch.log || patch_failed=true
    cd build || abort
    git apply --index --reject --ignore-whitespace ../../../BuildPatch.txt 2>apply-patch.log || patch_failed=true
    cd ..
    cd third_party/icu || abort
    git apply --index --reject --ignore-whitespace ../../../../ICUPatch.txt 2>apply-patch.log || patch_failed=true
    cd ../..
    cd ..
    if [[ $patch_failed == true ]]; then
        fail
    fi
    
    echo "Downloading additional libraries ..."
    git clone -n https://github.com/nlohmann/json.git 2>clone-json.log || fail
    cd json || abort
    git reset --hard $jsontag >resetJson.log || fail
    cd ..

    cd ..

else

    cd build || abort
    PATH=$PWD/depot_tools:$PATH
    cd ..

fi

cd build/v8 || abort

echo "Creating/updating patches ..."
git diff --ignore-space-change --ignore-space-at-eol HEAD >V8Patch.txt 2>create-patch.log || fail
cd build || abort
git diff --ignore-space-change --ignore-space-at-eol HEAD >BuildPatch.txt 2>create-patch.log || fail
cd ..
cd third_party/icu || abort
git diff --ignore-space-change --ignore-space-at-eol HEAD >ICUPatch.txt 2>create-patch.log || fail
cd ../..

if [[ $linux == true ]]; then
    echo "Installing sysroots ..."
    build/linux/sysroot_scripts/install-sysroot.py --arch=x64 >install-x64-sysroot.log || fail
    build/linux/sysroot_scripts/install-sysroot.py --arch=i386 >install-i386-sysroot.log || fail
    build/linux/sysroot_scripts/install-sysroot.py --arch=$cpu >install-$cpu-sysroot.log || fail
fi

echo "Building V8 ..."
gn gen out/$cpu/$mode --args="fatal_linker_warnings=false is_cfi=false is_component_build=false is_debug=$isdebug target_cpu=\"$cpu\" use_clang_modules=false use_custom_libcxx=false use_thin_lto=false v8_embedder_string=\"-ClearScript\" v8_enable_fuzztest=false v8_enable_pointer_compression=false v8_enable_31bit_smis_on_64bit_arch=false v8_enable_temporal_support=false v8_monolithic=true v8_use_external_startup_data=false v8_target_cpu=\"$cpu\"" >gn-$cpu-$mode.log 2>gn-$cpu-$mode.log || fail
gn args out/$cpu/$mode --list >out/$cpu/$mode/allArgs.txt || fail
ninja -C out/$cpu/$mode obj/libv8_monolith.a >build-$cpu-$mode.log || fail

cd ../..

echo "Importing patches ..."
cp build/v8/V8Patch.txt . || fail
cp build/v8/build/BuildPatch.txt . || fail
cp build/v8/third_party/icu/ICUPatch.txt . || fail

echo "Importing ICU data ..."
cp build/v8/out/$cpu/$mode/icudtl.dat . || fail

echo "Succeeded!"
