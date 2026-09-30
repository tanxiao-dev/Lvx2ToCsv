Lvx2ToCsv
==========

LVX2 to CSV Converter for Livox Mid-360

Author
------
Tan Xiao

Version
-------
1.0.0

Copyright
---------
Copyright (c) 2026 Tan Xiao.
All rights reserved.

Description
-----------
Lvx2ToCsv is a lightweight Windows command-line tool for converting
Livox Viewer 2 LVX2 recordings into CSV format.

The current version is intended for Livox Mid-360 LVX2 v2 recordings
using Cartesian 32-bit point data.

Exported point fields include:

- FrameIndex
- PacketTimestampNs
- UdpCounter
- PointIndexInPacket
- LidarIP
- X_m
- Y_m
- Z_m
- Reflectivity
- Tag

Implementation Note
-------------------
LVX2 parsing is independently implemented based on publicly available
Livox LVX2 format and protocol information.

Usage
-----

Basic conversion:

Lvx2ToCsv.exe input.lvx2

Specify output file:

Lvx2ToCsv.exe input.lvx2 -o output.csv

Export only points above a reflectivity threshold:

Lvx2ToCsv.exe input.lvx2 --min-reflectivity 200

Skip zero-coordinate points:

Lvx2ToCsv.exe input.lvx2 --skip-zero

Show help:

Lvx2ToCsv.exe --help

Show version information:

Lvx2ToCsv.exe --version

Drag and Drop
-------------
You can also drag an .lvx2 file directly onto Lvx2ToCsv.exe.

Notes
-----
- Coordinates are exported in meters.
- Reflectivity values are exported without modification.
- The current version is intended for Livox Mid-360 LVX2 v2 recordings using Cartesian 32-bit point data.
- Unsupported LVX2 point formats will result in an error rather than being silently converted incorrectly.

Disclaimer
----------
This software is an independent utility created by Tan Xiao.

It is not an official Livox product and is not affiliated with or endorsed by Livox.

Livox and related product names are trademarks of their respective owners.