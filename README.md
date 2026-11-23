<p align="center">
  <img src="site/assets/logo-256.png" alt="HKSS Decrypted logo" width="220" />
</p>

# HKSS Decrypted

An unofficial combo randomizer and [Archipelago](https://archipelago.gg) world for *Hollow Knight* and *Hollow Knight: Silksong*. One seed spans Hallownest and Pharloom: abilities and items from either game can turn up in the other, and you travel between the two games during the run.

- [Player hub](https://decryptu.github.io/hkss-decrypted/)
- [Architecture](docs/ARCHITECTURE.md)
- [Development setup](DEVELOPMENT.md)
- [Roadmap](ROADMAP.md)

## Status

Foundations. The design is in place and nothing is playable yet. The plan:

- **Both games on BepInEx 5.** Hollow Knight's randomizer and Archipelago client are ported to the current Hollow Knight build (Unity 6), next to [Silksong Decrypted](https://github.com/Decryptu/silksong-decrypted) for Silksong.
- **One Archipelago game**, `Hollow Knight x Silksong`: one YAML and one slot covering both games, with single-game modes.
- **HKSS Launcher**: installs everything into separate profiles (your Steam installs stay vanilla), starts the right game in the right save, and hands over when you travel between games.
- **Player hub**: setup checklist, YAML generator, and an item map of both worlds.

## Credits & license

Built on the work of the Hollow Knight randomizer and Archipelago communities and on Silksong Decrypted. See [NOTICE.md](NOTICE.md).

Unofficial fan project. Not affiliated with Team Cherry. Distributed under the [MIT License](LICENSE); code ported from LGPL-2.1 projects keeps that license.
