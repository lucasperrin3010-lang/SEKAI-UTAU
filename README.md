# SEKAI UTAU

Éditeur de chant dérivé d'[OpenUtau](https://github.com/stakira/OpenUtau) (licence MIT,
© 2014 StAkira), avec une interface sombre de type DAW : notes vertes arrondies et
translucides, clavier jouable en bas de fenêtre, couleurs personnalisables,
enregistrement vidéo avec le son interne, et import de projets MIDI / UST / USTX /
VSQ / VSQX / SVP / CCS / MusicXML / MXL.

**Site du projet :** <https://lucasperrin3010-lang.github.io/SEKAI-UTAU/>

Tous les noms du programme sont en SEKAI UTAU : exécutable `SekaiUtau`, assembly
`SekaiUtau`, bundle macOS `SEKAI UTAU.app`, dossiers de projet `SekaiUtau*`.

## Téléchargement

Les paquets prêts à l'emploi sont dans les
[**Releases**](https://github.com/lucasperrin3010-lang/SEKAI-UTAU/releases) :

| Système | Fichier |
|---|---|
| Windows 64 bits | [SEKAI-UTAU-win-x64.zip](https://github.com/lucasperrin3010-lang/SEKAI-UTAU/releases/download/v1.0.6/SEKAI-UTAU-win-x64.zip) |
| Linux x86_64 | [SEKAI-UTAU-linux-x64.tar.gz](https://github.com/lucasperrin3010-lang/SEKAI-UTAU/releases/download/v1.0.6/SEKAI-UTAU-linux-x64.tar.gz) |
| macOS Apple Silicon | [SEKAI-UTAU-osx-arm64.zip](https://github.com/lucasperrin3010-lang/SEKAI-UTAU/releases/download/v1.0.6/SEKAI-UTAU-osx-arm64.zip) |

Chaque paquet contient une notice d'installation (`A-LIRE-AVANT-INSTALLATION.txt`)
et les empreintes SHA-256 sont fournies dans `SHA256SUMS.txt`.

## Installation en bref

**Windows** — dézipper, ouvrir le dossier `SEKAI-UTAU`, lancer `SekaiUtau.exe`
(avertissement SmartScreen : *Informations complémentaires* → *Exécuter quand même*).

**Linux** — `tar -xzf SEKAI-UTAU-linux-x64.tar.gz`, puis
`chmod +x SEKAI-UTAU/SekaiUtau` et `./SEKAI-UTAU/SekaiUtau`.

**macOS** — dézipper, puis dans un Terminal :

```
chmod +x "SEKAI UTAU.app/Contents/MacOS/SekaiUtau"
xattr -dr com.apple.quarantine "SEKAI UTAU.app"
codesign --force --deep -s - "SEKAI UTAU.app"
open "SEKAI UTAU.app"
```

Ces commandes sont nécessaires car l'application n'est pas signée par Apple ;
les binaires fournis sont arm64 uniquement (pas de Mac Intel).

## Chanteurs (voicebanks)

Aucune voix n'est incluse. Déposer une banque **UTAU** (dossier avec `oto.ini`) ou
**DiffSinger** (dossier avec `dsconfig.yaml`) dans `Singers/` à côté du programme,
puis relancer l'application. Les banques **VOCALOID** (Vocaloid 3/4/5, formats
chiffrés `.vvd`/`.vvb`) ne sont pas compatibles.

## Compilation

Le projet se compile avec le SDK .NET 10 :

```
dotnet publish SekaiUtau -c Release -r win-x64 --self-contained true -o bin/win-x64
```

Autres cibles : `linux-x64`, `osx-arm64`. Sur un hôte non Windows, ajouter
`-p:OS=Unix` pour viser `net10.0` au lieu de `net10.0-windows`.

## Licence

MIT — voir `LICENSE.txt`. La redistribution doit conserver le fichier de licence
et la mention de copyright d'OpenUtau.
