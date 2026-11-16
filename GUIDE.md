# Guide du joueur — SpamDrafter

SpamDrafter est une application **non officielle**, faite par un joueur pour les joueurs de **SPAM**. Elle n'est ni produite ni approuvée par l'éditeur du jeu. Les textes et les illustrations des cartes appartiennent à leurs auteurs.

Ce guide explique comment l'installer et comment se servir de ses quatre onglets.

---

## 1. Installer

**Il faut** : Windows 10 ou 11 (64 bits). Rien d'autre à installer : l'application contient tout ce dont elle a besoin.

1. Sur la page **Releases** du projet GitHub, téléchargez `SpamDrafter-win-Setup.exe` (environ 56 Mo).
2. Lancez-le. L'installation prend quelques secondes et crée un raccourci « SpamDrafter (non officiel) » sur le bureau et dans le menu Démarrer.

### Le message « Windows a protégé votre ordinateur »

SpamDrafter n'est pas signé avec un certificat payant. Windows (SmartScreen) peut donc afficher un écran bleu « Windows a protégé votre ordinateur » avec la mention « éditeur inconnu ». Pour continuer :

1. cliquez sur **Informations complémentaires** ;
2. puis sur **Exécuter quand même**.

Ne le faites que si vous avez téléchargé le fichier depuis la page Releases officielle du projet.

### Premier lancement : les données du jeu

L'installateur ne contient que le programme. Les **données du jeu** (cartes, talents, Tox, textes et illustrations, environ 30 Mo) sont un paquet séparé :

- **avec Internet** : au premier lancement, SpamDrafter les télécharge tout seul (progression en haut à droite). Cliquez ensuite sur **Redémarrer pour mettre à jour** ;
- **sans Internet** : récupérez le fichier `SpamDrafter-donnees-<version>-r<N>.zip` (sur la page Releases, depuis un autre ordinateur par exemple). Dans SpamDrafter, cliquez sur **Importer des données…** en haut à droite, choisissez ce fichier, puis cliquez sur **Redémarrer pour mettre à jour**.

L'application vérifie l'empreinte de chaque fichier du paquet avant de l'utiliser. Un paquet abîmé ou modifié est refusé, et les données déjà installées restent intactes.

---

## 2. Mises à jour

À chaque démarrage, SpamDrafter regarde s'il existe une nouvelle version **du programme**, puis une nouvelle révision **des données du jeu**. Quand une mise à jour est prête, le bouton **Redémarrer pour mettre à jour** apparaît en haut à droite. Vous pouvez aussi cliquer sur **Rechercher une mise à jour**.

En haut de la fenêtre, vous voyez la version du programme et celle des données (par exemple « SPAM 1.30 · 151 cartes · données révision 4 »).

Les mises à jour ne touchent jamais à vos decks.

---

## 3. Les quatre onglets

Dans les listes de cartes, vous pouvez **rechercher une carte** par son nom et filtrer par **élément** et par **rareté**. Un **double-clic** sur une carte fait la même chose que le bouton d'ajout de l'onglet.

### Bibliothèque

Toutes les cartes du jeu, avec leur illustration, leur élément, leur rareté, leurs PV, leur revenu, leurs résistances, leurs capacités et leurs trois talents.

> Certaines descriptions de talents contiennent des valeurs calculées par le jeu. Elles ne sont pas encore affichées : l'application indique alors « description chiffrée : à venir ».

### Deck builder

Pour construire vos decks : **4 cartes différentes** et **un talent par carte**.

- **Ajouter au deck (ou double-clic)**, **Retirer**, puis donnez un **Nom du deck** et cliquez sur **Sauvegarder**. Un deck incomplet est sauvegardé comme brouillon.
- **Mes decks** : vos decks sauvegardés. Cliquez sur l'un d'eux pour l'ouvrir et le modifier. **Nouveau deck** repart de zéro ; **Supprimer** efface le deck ouvert.

### Drafter

Pour retrouver, parmi **vos decks sauvegardés**, ceux qui contiennent certaines cartes. Choisissez 1 à 4 cartes (**Ajouter à la recherche**) : la liste montre les decks qui les contiennent toutes, complets ou brouillons. **Ouvrir dans le Deck builder** ouvre le deck choisi.

### Génération de deck

Le **Grand Cuisinier** complète une sélection de 1 à 4 cartes en un deck de 4 cartes, avec les talents, en faisant jouer des milliers de **duels simulés**.

1. Choisissez 1 à 4 cartes (**Imposer cette carte**). Pour chacune, laissez « Talent au choix du Cuisinier » ou imposez un talent.
2. Choisissez l'**Effort** : Rapide, Normal (environ 1 minute) ou Précis (2 à 3 fois plus de duels).
3. Cliquez sur **Générer des decks**. Vous pouvez **Annuler** à tout moment.

Avec **une seule carte** et le talent au choix (effort rapide ou normal), les propositions s'affichent **tout de suite** : elles ont été calculées à l'avance.

Chaque proposition affiche un **score** : les victoires plus la moitié des égalités, avec une marge d'erreur (« ± »). **Sauvegarder** l'ajoute à vos decks.

**Fiche du deck** (environ 15 à 20 secondes) donne l'essentiel pour jouer ce deck :

- **Verdict** : sa force contre les adversaires ;
- **Pourquoi ça marche** : les éléments et les liens entre cartes qui font sa force ;
- **Les cartes** : la carte clé, la plus faible, celle qui encaisse ;
- **Talents** et **Tox à acheter**, quand une mesure les distingue nettement ;
- **À craindre** : les adversaires qui le battent.

**Analyse complète** (environ 1 minute) ajoute les liens entre chaque paire de cartes. **Analyser un deck sauvegardé** fait la même analyse sur l'un de vos decks.

### Contre qui sont mesurés les decks ?

Contre les decks de la **méta synthétique** : 20 decks forts, calculés pour cette version de SPAM par le Grand Cuisinier lui-même, et livrés avec les données. Vos propres decks ne servent jamais d'adversaires.

Quand une nouvelle méta est calculée, elle arrive avec une mise à jour des données, comme le reste.

### Ce qu'il faut savoir sur les résultats

- Les scores viennent d'un **simulateur** : une réécriture indépendante des règles du duel, jouée par une intelligence artificielle. Ce ne sont pas des statistiques de vraies parties.
- L'IA ne joue pas comme un humain : certains decks réels sont sous-estimés en simulation. Prenez les propositions comme des **idées à essayer**, pas comme des certitudes.
- Avec les mêmes cartes et les mêmes réglages, l'application donne toujours les mêmes propositions.

---

## 4. Où sont mes fichiers ?

| Quoi | Où |
|---|---|
| Vos decks | `%APPDATA%\SpamDrafter\decks` (un fichier par deck) |
| Le programme | `%LOCALAPPDATA%\SpamDrafter` |
| Les données du jeu | `%LOCALAPPDATA%\SpamDrafter\data` |

Pour sauvegarder vos decks, copiez le dossier `decks`.

## 5. Désinstaller

Paramètres de Windows → **Applications** → « SpamDrafter (non officiel) » → **Désinstaller**. Le programme, les données du jeu et les raccourcis sont supprimés. **Vos decks sont conservés** dans `%APPDATA%\SpamDrafter\decks`. Supprimez ce dossier vous-même si vous n'en voulez plus.

## 6. Un problème ?

Ouvrez un ticket (« Issues ») sur la page GitHub du projet. Indiquez la version affichée en haut de la fenêtre et la révision des données.
