# Library Covers — plugin Jellyfin

Des couvertures de médiathèques générées à partir de vos propres affiches, avec le nombre de films, séries ou chaînes **toujours à jour**.

Chaque couverture (1920×1080) montre un mur d'affiches incliné, un fondu bleu nuit, un halo à la couleur de la médiathèque, son nom en grand et un compteur (« 209 films », « 22 séries », « 25 chaînes »…). Quand vous ajoutez ou supprimez des médias, la couverture concernée est refaite toute seule.

> *English: a Jellyfin 12 plugin that renders library cover images from your own posters (tilted poster wall, title, live item count) and keeps them up to date as items are added or removed. Live TV gets a channel-logo wall. The settings page and labels are in French.*

## Fonctionnalités

- **Toutes les médiathèques** : films, séries, collections, musique, clips, mixtes. Le compteur s'adapte : films, séries (ou épisodes s'il y a moins de 2 séries), collections, albums (ou pistes).
- **TV en direct** : la vue « TV en direct » n'est pas une médiathèque, Jellyfin ne lui génère aucune image. Le plugin lui fait une couverture avec un mur des logos de vos chaînes et leur nombre.
- **Médiathèques vides** : cartes teintées à la couleur de la médiathèque au lieu d'un fond vide.
- **Mise à jour automatique** : le plugin écoute les ajouts et suppressions, attend que ça se calme (120 s par défaut) puis régénère uniquement les couvertures dont le compteur, le titre ou la couleur a changé.
- **Tâche planifiée** « Mettre à jour les couvertures des médiathèques », tous les jours à 4 h, plus une vérification 2 minutes après le démarrage de Jellyfin.
- **Page de réglages** (Tableau de bord → Extensions → Library Covers) : par médiathèque, activer ou non, titre, couleur, unité au singulier et au pluriel, avec un aperçu instantané. Bouton pour tout régénérer.

## Installation depuis le catalogue (recommandé)

1. Tableau de bord → **Extensions** → **Dépôts** → **+**
2. Nom : `Library Covers`, URL :
   ```
   https://raw.githubusercontent.com/jfrag/jellyfin-plugin-library-covers/main/manifest.json
   ```
3. Dans le **Catalogue**, installez **Library Covers**, puis redémarrez Jellyfin.

Les nouvelles versions apparaîtront ensuite dans le catalogue comme pour les plugins officiels.

## Installation manuelle

Téléchargez `library-covers_<version>.zip` depuis les [releases](https://github.com/jfrag/jellyfin-plugin-library-covers/releases), décompressez-le dans un dossier `Library Covers_<version>` du répertoire `plugins` de Jellyfin (par ex. `/var/lib/jellyfin/plugins/`), donnez-le à l'utilisateur de Jellyfin, puis redémarrez.

## Compatibilité

- Jellyfin **12.x** (targetAbi 12.0.0.0, .NET 10).
- Les couvertures sont enregistrées comme image principale de chaque médiathèque : elles remplacent l'image existante. Pensez à sauvegarder vos images personnalisées si vous y tenez (dossier `root/default/<médiathèque>/` des données de Jellyfin).
- Interface et libellés en français.

## Compiler

Avec le SDK .NET 10 :

```sh
dotnet build Jellyfin.Plugin.LibraryCovers/Jellyfin.Plugin.LibraryCovers.csproj -c Release
```

Ou sans rien installer, avec Docker : `./build.sh` (produit `out/Library Covers_<version>/` et le `.zip`).

## Publier une version

1. Mettez à jour `version` et `changelog` dans `meta.json` (et la version dans le `.csproj`).
2. Poussez un tag identique : `git tag v1.2.0.0 && git push origin v1.2.0.0`.

Le workflow GitHub Actions compile le plugin, publie la release avec le `.zip` et ajoute la version dans `manifest.json`.

## Licence

Code sous licence [GPL-3.0](LICENSE). Polices embarquées : [Unbounded](https://github.com/googlefonts/unbounded) et [Noto Sans](https://github.com/notofonts/latin-greek-cyrillic), sous SIL Open Font License 1.1 (voir `Jellyfin.Plugin.LibraryCovers/Fonts/`).
