#!/usr/bin/env python3
"""Create a WinUI 3 full-trust desktop MSIX from the self-contained native runtime.

The Microsoft Store re-signs submitted MSIX packages after certification.
A developer-signed MSIX is for local testing ONLY and is never a Store release.
"""
from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image

FOUNDATION = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
UAP = "http://schemas.microsoft.com/appx/manifest/uap/windows10"
UAP10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
RESCAP = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
ET.register_namespace("", FOUNDATION)
ET.register_namespace("uap", UAP)
ET.register_namespace("uap10", UAP10)
ET.register_namespace("rescap", RESCAP)


def validate_identity(name: str, publisher: str, version: str) -> None:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{1,79}", name):
        raise ValueError("Invalid MSIX identity Name; copy it exactly from Partner Center")
    if not publisher.startswith("CN=") or len(publisher) > 512:
        raise ValueError("Invalid Publisher; copy the complete CN=... value from Partner Center")
    parts = version.split(".")
    if len(parts) != 4 or any(not p.isdigit() or int(p) > 65535 for p in parts):
        raise ValueError("MSIX version must be four integers in range 0..65535")
    if int(parts[3]) != 0:
        raise ValueError("For Store distribution, the fourth version component must be 0")


def manifest(name: str, publisher: str, display_publisher: str, version: str) -> ET.ElementTree:
    validate_identity(name, publisher, version)
    if not display_publisher.strip():
        raise ValueError("PublisherDisplayName must match Partner Center")
    root = ET.Element(f"{{{FOUNDATION}}}Package", {"IgnorableNamespaces": "uap uap10 rescap"})
    ET.SubElement(root, f"{{{FOUNDATION}}}Identity", {
        "Name": name, "Publisher": publisher, "Version": version, "ProcessorArchitecture": "x64"
    })
    props = ET.SubElement(root, f"{{{FOUNDATION}}}Properties")
    ET.SubElement(props, f"{{{FOUNDATION}}}DisplayName").text = "SCPSLTranslationJP"
    ET.SubElement(props, f"{{{FOUNDATION}}}PublisherDisplayName").text = display_publisher
    ET.SubElement(props, f"{{{FOUNDATION}}}Logo").text = r"Assets\StoreLogo.png"
    deps = ET.SubElement(root, f"{{{FOUNDATION}}}Dependencies")
    for package_name, minimum in (
        ("Microsoft.WindowsAppRuntime.1.8", "8000.0.0.0"),
        ("Microsoft.VCLibs.140.00.UWPDesktop", "14.0.30704.0"),
    ):
        ET.SubElement(deps, f"{{{FOUNDATION}}}PackageDependency", {
            "Name": package_name,
            "Publisher": "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            "MinVersion": minimum,
        })
    ET.SubElement(deps, f"{{{FOUNDATION}}}TargetDeviceFamily", {
        "Name": "Windows.Desktop", "MinVersion": "10.0.22000.0",
        "MaxVersionTested": "10.0.26100.0"
    })
    resources = ET.SubElement(root, f"{{{FOUNDATION}}}Resources")
    for lang in ("ja-JP", "en-US"):
        ET.SubElement(resources, f"{{{FOUNDATION}}}Resource", {"Language": lang})
    apps = ET.SubElement(root, f"{{{FOUNDATION}}}Applications")
    app = ET.SubElement(apps, f"{{{FOUNDATION}}}Application", {
        "Id": "App", "Executable": "SCPSLWinUI.exe",
        "EntryPoint": "Windows.FullTrustApplication",
        f"{{{UAP10}}}RuntimeBehavior": "packagedClassicApp",
        f"{{{UAP10}}}TrustLevel": "mediumIL"
    })
    ET.SubElement(app, f"{{{UAP}}}VisualElements", {
        "DisplayName": "SCPSLTranslationJP",
        "Description": "Japanese translation installer for SCP: Secret Laboratory",
        "Square150x150Logo": r"Assets\Square150x150Logo.png",
        "Square44x44Logo": r"Assets\Square44x44Logo.png",
        "BackgroundColor": "#12385C"
    })
    caps = ET.SubElement(root, f"{{{FOUNDATION}}}Capabilities")
    ET.SubElement(caps, f"{{{RESCAP}}}Capability", {"Name": "runFullTrust"})
    ET.indent(root, space="  ")
    return ET.ElementTree(root)


def build_stage(runtime: Path, core: Path, assets: Path, stage: Path,
                name: str, publisher: str, publisher_display: str, version: str) -> None:
    gui = runtime / "SCPSLWinUI.exe"
    if not gui.is_file():
        raise FileNotFoundError(f"Missing compiled WinUI executable: {gui}")
    if not core.is_file():
        raise FileNotFoundError(f"Missing Rust engine: {core}")
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)
    # Store MSIX uses the Microsoft-signed Windows App SDK framework package.
    # Do not ship the unpackaged self-contained WinUI runtime in WindowsApps.
    shutil.copy2(gui, stage / "SCPSLWinUI.exe")
    shutil.copy2(core, stage / "scpsl-native-core.exe")
    for filename in ("researcher.jpg", "scp-emblem.png"):
        shutil.copy2(assets / filename, stage / filename)
    images = stage / "Assets"
    images.mkdir()
    with Image.open(assets / "app.ico") as icon:
        icon = icon.convert("RGBA")
        for filename, size in (("Square44x44Logo.png", (44, 44)),
                               ("Square150x150Logo.png", (150, 150)),
                               ("StoreLogo.png", (50, 50))):
            icon.resize(size, Image.Resampling.LANCZOS).save(images / filename)
    manifest(name, publisher, publisher_display, version).write(
        stage / "AppxManifest.xml", encoding="utf-8", xml_declaration=True
    )
    # The app runs in medium IL and accesses user-writable Steam libraries
    # with normal user permissions; it never requests UAC elevation.
    print(f"Staged {len(list(stage.rglob('*')))} items for {name} ({version})")


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--runtime", type=Path, required=True)
    p.add_argument("--core", type=Path, required=True)
    p.add_argument("--assets", type=Path, required=True)
    p.add_argument("--stage", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--publisher", required=True)
    p.add_argument("--publisher-display", required=True)
    p.add_argument("--version", default="1.0.0.0")
    p.add_argument("--makeappx", type=Path, required=True)
    args = p.parse_args()
    if not args.makeappx.is_file():
        raise FileNotFoundError(f"Windows SDK MakeAppx not found: {args.makeappx}")
    build_stage(args.runtime, args.core, args.assets, args.stage,
                args.name, args.publisher, args.publisher_display, args.version)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(args.makeappx), "pack", "/d", str(args.stage),
                    "/p", str(args.output), "/o", "/v"], check=True)
    print(f"Built MSIX: {args.output} ({args.output.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
