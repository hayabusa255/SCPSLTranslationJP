"""Manifest checks independent of Windows SDK packaging tools."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_msix import FOUNDATION, UAP10, RESCAP, manifest, validate_identity


class ManifestTests(unittest.TestCase):
    def test_store_manifest_is_full_trust_desktop(self):
        tree = manifest("SCPSLTranslationJP.Dev", "CN=SCPSLTranslationJP Dev",
                        "SCPSLTranslationJP Developers", "1.0.0.0")
        root = tree.getroot()
        identity = root.find(f"{{{FOUNDATION}}}Identity")
        self.assertEqual(identity.attrib["ProcessorArchitecture"], "x64")
        self.assertEqual(identity.attrib["Publisher"], "CN=SCPSLTranslationJP Dev")
        app = root.find(f"{{{FOUNDATION}}}Applications/{{{FOUNDATION}}}Application")
        self.assertEqual(app.attrib["Executable"], "SCPSLWinUI.exe")
        self.assertEqual(app.attrib[f"{{{UAP10}}}RuntimeBehavior"], "packagedClassicApp")
        self.assertEqual(app.attrib[f"{{{UAP10}}}TrustLevel"], "mediumIL")
        cap = root.find(f"{{{FOUNDATION}}}Capabilities/{{{RESCAP}}}Capability")
        self.assertEqual(cap.attrib["Name"], "runFullTrust")
        dependencies = root.findall(f"{{{FOUNDATION}}}Dependencies/{{{FOUNDATION}}}PackageDependency")
        self.assertEqual(
            {d.attrib["Name"] for d in dependencies},
            {"Microsoft.WindowsAppRuntime.1.8", "Microsoft.VCLibs.140.00.UWPDesktop"},
        )

    def test_partner_identity_is_not_invented(self):
        with self.assertRaises(ValueError):
            validate_identity("SCPSLTranslationJP", "", "1.0.0.0")
        with self.assertRaises(ValueError):
            validate_identity("bad identity", "CN=Dev", "1.0.0.0")
        with self.assertRaises(ValueError):
            validate_identity("SCPSLTranslationJP", "CN=Dev", "1.0.0.1")


if __name__ == "__main__":
    unittest.main()
