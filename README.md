# aprs-cs

Packet.Aprs: an APRS packet encoder and decoder for .NET, published on NuGet as [`Packet.Aprs`](https://www.nuget.org/packages/Packet.Aprs). Usage is in [src/Packet.Aprs/README.md](src/Packet.Aprs/README.md); design and validation notes are in [docs/](docs).

Conforms to [aprs-vectors](https://github.com/packet-net/aprs-vectors) 1.0: every case passes.

It runs the language-neutral conformance cases in [packet-net/aprs-vectors](https://github.com/packet-net/aprs-vectors), which is the `spec/aprs` submodule here. Other implementations are listed in that repository.

```sh
git clone --recurse-submodules https://github.com/packet-net/aprs-cs
dotnet test Packet.Aprs.slnx
```

Releases: push a `vX.Y.Z` tag; `publish.yml` tests, packs and pushes to nuget.org with Trusted Publishing. Versions continue from the 0.54.0 published from packet-net/packet.net.

Licence: AGPL-3.0-or-later.
