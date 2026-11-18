#!/usr/bin/env python3
"""Prepare pinned Microsoft WinApp SDK 1.8 x64 self-contained components.
Compile the generated WinRT fusion manifest into the inner native GUI EXE.
The app-local resources.pri is required for XamlControlsResources().
"""
import argparse
import hashlib
import shutil
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile,ZIP_DEFLATED

VERSIONS = {
    "foundation": "1.8.260803002",
    "interactive": "1.8.260708001",
    "winui": "1.8.260803003",
    "dwrite": "1.8.25122902",
    "widgets": "1.8.251231004",
}
PREFIX="runtimes-framework/win-x64/native/"
NS_ASM="urn:schemas-microsoft-com:asm.v1"
NS_WINRT="urn:schemas-microsoft-com:winrt.v1"

def package_path(cache:Path,short:str,version:str)->Path:
    local=cache/(short+".nupkg")
    if local.exists():
        return local
    cache.mkdir(parents=True,exist_ok=True)
    pkg="microsoft.windowsappsdk."+("interactiveexperiences" if short=="interactive" else short)
    url="https://api.nuget.org/v3-flatcontainer/"+pkg+"/"+version+"/"+pkg+"."+version+".nupkg"
    print("Downloading Microsoft package:",pkg,version,flush=True)
    with urllib.request.urlopen(url,timeout=90) as response:
        data=response.read()
    if len(data)>300_000_000:
        raise ValueError("NuGet package unexpectedly large")
    local.write_bytes(data)
    return local

def prepare(cache:Path,build:Path):
    runtime=build/"runtime"
    runtime.mkdir(parents=True,exist_ok=True)
    bindings={}
    count=0
    for short,version in VERSIONS.items():
        with ZipFile(package_path(cache,short,version)) as pkg:
            for entry in pkg.infolist():
                if entry.is_dir() or not entry.filename.startswith(PREFIX):
                    continue
                rel=Path(entry.filename[len(PREFIX):])
                if rel.is_absolute() or '..' in rel.parts:
                    raise ValueError("Unsafe SDK archive member: "+entry.filename)
                dst=runtime/rel
                dst.parent.mkdir(parents=True,exist_ok=True)
                dst.write_bytes(pkg.read(entry))
                count+=1
            fragment="runtimes-framework/package.appxfragment"
            if fragment not in pkg.namelist():
                continue
            f=ET.fromstring(pkg.read(fragment))
            for server in f.iter():
                if server.tag.rsplit("}",1)[-1]!="InProcessServer":
                    continue
                dll=next((e.text for e in server if e.tag.rsplit("}",1)[-1]=="Path"),None)
                if not dll:
                    continue
                for child in server:
                    if child.tag.rsplit("}",1)[-1]=="ActivatableClass":
                        bindings.setdefault(dll,{})[child.attrib["ActivatableClassId"]]=child.attrib["ThreadingModel"]
    control=runtime/"Microsoft.UI.Xaml.Controls.pri"
    if not control.is_file():
        raise ValueError("WinUI Controls PRI missing from pinned package")
    shutil.copy2(control,runtime/"resources.pri")
    ET.register_namespace("",NS_ASM)
    ET.register_namespace("winrt",NS_WINRT)
    doc=ET.Element("{"+NS_ASM+"}assembly",{"manifestVersion":"1.0"})
    ET.SubElement(doc,"{"+NS_ASM+"}assemblyIdentity",
                  {"name":"SCPSL.NativeWinUI.SelfContained","version":"3.2.0.0","type":"win32"})
    total=0
    available={item.name.casefold() for item in runtime.iterdir() if item.is_file()}
    for dll,classes in sorted(bindings.items()):
        if dll.casefold() not in available:
            # Optional WebView2 component is unused; do not ship WebView2.
            if dll.casefold()=="microsoft.web.webview2.core.dll":
                continue
            raise ValueError("SDK component missing: "+dll)
        element=ET.SubElement(doc,"{"+NS_ASM+"}file",{"name":dll})
        for name,model in sorted(classes.items()):
            ET.SubElement(element,"{"+NS_WINRT+"}activatableClass",
                          {"name":name,"threadingModel":model.lower()})
            total+=1
    compatibility=ET.SubElement(doc,"{urn:schemas-microsoft-com:compatibility.v1}compatibility")
    application=ET.SubElement(compatibility,"{urn:schemas-microsoft-com:compatibility.v1}application")
    ET.SubElement(application,"{urn:schemas-microsoft-com:compatibility.v1}supportedOS",
                  {"Id":"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"})
    apps=ET.SubElement(doc,"{urn:schemas-microsoft-com:asm.v3}application")
    settings=ET.SubElement(apps,"{urn:schemas-microsoft-com:asm.v3}windowsSettings")
    ET.SubElement(settings,"{http://schemas.microsoft.com/SMI/2016/WindowsSettings}dpiAwareness").text="PerMonitorV2"
    manifest=build/"app-selfcontained.manifest"
    manifest.write_bytes(ET.tostring(doc,encoding="utf-8",xml_declaration=True))
    print("Prepared",count,"SDK files,",total,"WinRT classes; manifest:",manifest)

def pack(build:Path):
    runtime=build/"runtime"
    assert (runtime/"SCPSLWinUI.exe").is_file()
    assert (runtime/"resources.pri").is_file()
    out=build/"SCPSL-WinUI-1.8-selfcontained-bundle.zip"
    with ZipFile(out,"w",ZIP_DEFLATED,compresslevel=9) as zipfile:
        for item in sorted(runtime.rglob("*")):
            if item.is_file():
                zipfile.write(item,item.relative_to(runtime).as_posix())
    with ZipFile(out) as z:
        assert z.testzip() is None
        print("Packed",len(z.namelist()),"files; MiB=",round(out.stat().st_size/1048576,2))
    print("Bundle SHA256:",hashlib.sha256(out.read_bytes()).hexdigest())
    return out

def main():
    cli=argparse.ArgumentParser()
    cli.add_argument("--cache",type=Path,required=True)
    cli.add_argument("--output",type=Path,required=True)
    cli.add_argument("--pack-only",action="store_true")
    opts=cli.parse_args()
    if opts.pack_only:
        pack(opts.output)
    else:
        prepare(opts.cache,opts.output)
if __name__=="__main__":
    main()
