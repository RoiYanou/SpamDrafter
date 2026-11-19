![SpamDrafter](src/SpamDrafter.App/Assets/banniere.png)

# SpamDrafter

Application de bureau **non officielle** pour les joueurs de **SPAM** : bibliothèque de cartes, deck builder, drafter, et génération de decks avec analyse, grâce à des duels simulés.

> SpamDrafter n'est ni produit ni approuvé par l'éditeur de SPAM. Les textes et les illustrations des cartes appartiennent à leurs auteurs.

## Pour les joueurs

- **Télécharger** : `SpamDrafter-win-Setup.exe` sur la page [Releases](../../releases/latest) (Windows 10 ou 11, 64 bits).
- **Mode d'emploi** : [GUIDE.md](GUIDE.md) (installation, message SmartScreen, les onglets, désinstallation).

Ce que fait l'application :

| Onglet | Rôle |
|---|---|
| **Bibliothèque** | Toutes les cartes : illustration, statistiques, résistances, capacités, talents. |
| **Deck builder** | Construire et sauvegarder des decks de 4 cartes avec leurs talents. |
| **Drafter** | Retrouver, parmi vos decks, ceux qui contiennent les cartes choisies. |
| **Génération de deck** | Compléter 1 à 4 cartes en un deck de 4 cartes, avec une note sur 100. |
| **Tester** | Noter un deck et lire sa fiche (verdict, carte clé, talents, Tox), ou comparer deux decks en duels simulés. |

Les mises à jour du programme et des données du jeu se font automatiquement. Sans connexion, les données peuvent être installées depuis un fichier (**Importer des données…**).

## Comment ça marche

- **Simulateur** (`SpamDrafter.Simulator`) : une réécriture indépendante des règles du duel de SPAM. Elle est déterministe : même graine, même duel. **Aucun code du jeu n'est inclus dans ce dépôt.**
- **Grand Joueur** : les joueurs artificiels du simulateur (heuristique, recherche).
- **Grand Cuisinier** et **Grand Analyseur** (`SpamDrafter.Engines`) : ils génèrent et analysent les decks en faisant jouer des milliers de duels simulés. Les adversaires sont une « méta synthétique » : 20 decks forts calculés pour chaque version de SPAM et livrés avec les données.
- **Données du jeu** : elles ne sont pas dans ce dépôt. Elles forment un paquet séparé (`SpamDrafter-donnees-<version>-r<N>.zip`, environ 30 Mo), avec une empreinte SHA-256 par fichier, publié sur la page Releases.

Les résultats sont des **estimations par simulation**, jouées par une IA. Ce ne sont pas des statistiques de vraies parties.

## Pour les développeurs

Prérequis : [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build SpamDrafter.slnx
dotnet test SpamDrafter.slnx
```

Pour lancer l'application depuis le code source, il faut les données du jeu :

1. téléchargez `SpamDrafter-donnees-<version>-r<N>.zip` depuis la page Releases et décompressez-le dans un dossier ;
2. indiquez ce dossier avec la variable `SPAMDRAFTER_KNOWLEDGE`, puis lancez :

```bash
dotnet run --project src/SpamDrafter.App
```

(Si l'application est déjà installée sur le PC, ses données installées sont aussi trouvées toutes seules.)

| Dossier | Contenu |
|---|---|
| `src/SpamDrafter.App` | Application de bureau (Avalonia), mises à jour (Velopack) |
| `src/SpamDrafter.Core` | Base de connaissances, decks, recherche, paquets de données |
| `src/SpamDrafter.Simulator` | Simulateur de duel et joueurs artificiels |
| `src/SpamDrafter.Engines` | Grand Cuisinier, Grand Analyseur, coordinateur, méta synthétique |
| `src/SpamDrafter.DuelCli` | Commande `spamdrafter-duel` : duels, bancs d'essai, génération et analyse en ligne de commande |
| `tests/` | Tests automatiques (xUnit) |

Variables utiles pour les essais :

| Variable | Rôle |
|---|---|
| `SPAMDRAFTER_KNOWLEDGE` | Dossier des données du jeu |
| `SPAMDRAFTER_DECKS` | Autre dossier de decks |
| `SPAMDRAFTER_SETTINGS` | Autre fichier de réglages (onglet, fenêtre, filtres) |
| `SPAMDRAFTER_DATA` | Autre dossier de données installées |
| `SPAMDRAFTER_UPDATE_SOURCE` | Autre source de mises à jour (dossier ou adresse web) |

## Licence

Le code de SpamDrafter est publié sous licence **MIT** (voir [LICENSE](LICENSE)). Cette licence couvre **le code seulement** : les textes et les illustrations de SPAM, livrés dans le paquet de données, restent la propriété de leurs auteurs.
