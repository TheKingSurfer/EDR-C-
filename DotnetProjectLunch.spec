# -*- mode: python ; coding: utf-8 -*-


a = Analysis(
    ['DotnetProjectLunch.py'],
    pathex=[],
    binaries=[],
    datas=[('C:/Users/Owner/Desktop/EDR/EDR project/Merged/EDR/EDR.sln', '.'), ('C:/Users/Owner/Desktop/EDR/EDR project/Merged/EDR/Agent Solution', 'Agent Solution'), ('C:/Users/Owner/Desktop/EDR/EDR project/Merged/EDR/Server Solution', 'Server Solution'), ('C:/Users/Owner/Desktop/EDR/EDR project/Merged/EDR/DataService', 'DataService'), ('C:/Users/Owner/Desktop/EDR/EDR project/Merged/EDR/VirusTotal Solution', 'VirusTotal Solution')],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='DotnetProjectLunch',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
