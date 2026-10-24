"""Generates src/FabGames.Web/Resources/SharedResource.{es,en}.resx from tools/strings.py."""
import os
import sys
from xml.sax.saxutils import escape

sys.path.insert(0, os.path.dirname(__file__))
from strings import S  # noqa: E402

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
"""

out = os.path.join(os.path.dirname(__file__), "..", "src", "FabGames.Web", "Resources")
os.makedirs(out, exist_ok=True)
for i, lang in enumerate(("es", "en")):
    path = os.path.join(out, f"SharedResource.{lang}.resx")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(HEADER)
        for key, values in S.items():
            f.write(f'  <data name="{escape(key)}" xml:space="preserve"><value>{escape(values[i])}</value></data>\n')
        f.write("</root>\n")
print(f"{len(S)} strings x 2 languages")
