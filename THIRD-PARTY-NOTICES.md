# Third-party notices

## EDAssets

The Frontier commander dashboard includes rank and faction artwork, and the
Route Workspace neutron indicator adapts the FSD-reboot symbol, obtained from
[EDAssets](https://edassets.org/) at repository commit
`5a9b2f82796fc65abace9439aea000155f1e6eb3`.

Source: [Venefilyn/EDAssets](https://github.com/Venefilyn/EDAssets)

The EDAssets repository is distributed under the MIT License:

> Copyright (c) 2015 Eric "SpyTec" Gustavsson
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

EDAssets identifies the Pilots Federation rank artwork and the Alliance,
Empire, and Federation insignias as designed by Frontier Developments plc. The
Independent dashboard mark is a community recreation credited by EDAssets to
CMDR SpyTec. The Route Workspace neutron indicator is adapted and recolored
from `public/static/img/engineer-effects/fsd-reboot.svg`.

Elite Dangerous and its related marks and artwork are the property of Frontier
Developments plc. SrvSurvey is a community project and is not endorsed by or
affiliated with Frontier Developments.

## EDMC-VoxStellar

SrvSurvey's optional VoxStellar journal uploader adapts the event selection,
payload envelope, and HMAC-SHA256 webhook protocol from
[EDMC-VoxStellar](https://github.com/SvenSapphire/EDMC-VoxStellar).

EDMC-VoxStellar is distributed under the MIT License:

> Copyright (c) 2023 Sven Ziereis
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Raven Colonial body visualization

The shared route body-type SVG and PNG assets adapt the body colors, relative
sizes, asteroid-cluster geometry, and barycentre treatment from
[RavenColonialWeb](https://github.com/njthomson/RavenColonialWeb) by N. J.
Thomson, inspected at repository commit
`baf0abe614e46c466c3787accf65f10f1fd1c560`.

The relevant source is `src/views/SystemView2/SitesBodyView.tsx` in that
repository. RavenColonialWeb and SrvSurvey are distributed under the GNU
General Public License version 3, so these adaptations remain available under
SrvSurvey's GPL-3.0 license.

# PipeWire.NET

SrvSurvey includes source derived from PipeWire.NET at commit
`58355f0e5b26d1c27391fbeab722775e69eb87b5`, with a local addition for XDG
desktop portal file-descriptor connections and CPU-readable capture
buffers. PipeWire.NET is distributed under the MIT License. Its license is
included at `src/ThirdParty/PipeWire.NET/LICENSE`.

## Noto symbol and emoji fonts

SrvSurvey bundles Noto Sans Symbols 2.003, Noto Sans Symbols 2 version 2.008,
and Noto Color Emoji 2.051 as deterministic fallbacks for the symbols used by
its overlays. The font files are distributed under the SIL Open Font License
1.1. A copy of that license is included beside each font under
`src/SrvSurvey.Desktop/Assets/Fonts`.

Sources:

- [Noto Sans Symbols 2.003](https://github.com/notofonts/symbols/releases/tag/NotoSansSymbols-v2.003)
- [Noto Sans Symbols 2 version 2.008](https://github.com/notofonts/symbols/releases/tag/NotoSansSymbols2-v2.008)
- [Noto Color Emoji 2.051](https://github.com/googlefonts/noto-emoji/releases/tag/v2.051)

## OpenVR client libraries

SrvSurvey redistributes Valve's OpenVR 2.15.6 client libraries through the
`JeppDev.OpenVR.Binaries` package. Source:
[ValveSoftware/openvr](https://github.com/ValveSoftware/openvr/tree/v2.15.6).

OpenVR is distributed under the BSD 3-Clause License:

> Copyright (c) 2015, Valve Corporation
>
> Redistribution and use in source and binary forms, with or without
> modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice,
> this list of conditions and the following disclaimer.
>
> 2. Redistributions in binary form must reproduce the above copyright notice,
> this list of conditions and the following disclaimer in the documentation
> and/or other materials provided with the distribution.
>
> 3. Neither the name of the copyright holder nor the names of its contributors
> may be used to endorse or promote products derived from this software without
> specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
> AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
> IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
> ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
> LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
> CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
> SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
> INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
> CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
> ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
> POSSIBILITY OF SUCH DAMAGE.
