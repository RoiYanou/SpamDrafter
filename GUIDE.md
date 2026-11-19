# Guide du joueur — SpamDrafter

SpamDrafter est une application **non officielle**, faite par un joueur pour les joueurs de **SPAM**. Elle n'est ni produite ni approuvée par l'éditeur du jeu. Les textes et les illustrations des cartes appartiennent à leurs auteurs.

Ce guide explique comment l'installer et comment se servir de ses onglets.

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

## 3. Les onglets

Dans les listes de cartes, vous pouvez **rechercher une carte** par son nom, par le nom d'une de ses **capacités** ou d'un de ses **talents**, et filtrer par **élément** et par **rareté**. La recherche ignore les accents et les majuscules (« panthere » trouve « Panthère imaginaire », « piou-piou-piou » trouve « Oisillons de Midas ») ; plusieurs mots se cumulent. **Échap** vide la recherche. Un **double-clic** sur une carte fait la même chose que le bouton d'ajout de l'onglet.

SpamDrafter se souvient de la dernière fois : onglet ouvert, taille et position de la fenêtre, filtres d'élément et de rareté, tri de la liste des decks.

### Bibliothèque

La Bibliothèque a **quatre sous-onglets** : Cartes, Talents, Tox et Artistes.

- **Talents** : les talents que des cartes possèdent, par ordre alphabétique (sans tenir compte des accents), avec une recherche. Choisir un talent affiche sa description chiffrée et la **liste des cartes qui le possèdent** (avec la place du talent sur la carte : « Talent 2 »). Cliquer sur une carte ouvre sa fiche dans « Cartes ».
- **Tox** : les Tox du jeu par ordre alphabétique. Choisir un Tox affiche, **pour chaque rareté où il existe**, sa **valeur**, son **prix à la boutique en Spamtons** (« 45 Spamtons » ; ou « Pas vendu en boutique ») et sa description avec la valeur de cette rareté, puis les cartes qui le possèdent de base. Les passifs de talent (sans image, sans carte qui les possède de base et non vendus en boutique) ne sont pas des Tox et ne sont pas listés.
- **Artistes** : les artistes qui ont des cartes jouables (29), par ordre alphabétique, avec le nombre de cartes de chacun ; leur lien public (réseau social) s'ouvre avec « Ouvrir le lien », et leurs cartes sont listées.
- **Cartes** : la fiche détaillée décrite ci-dessous. Le **nom de l'artiste** est écrit en bas à droite de l'illustration (« Illustration : Atto »).

#### Sous-onglet Cartes

Toutes les cartes du jeu, avec leur illustration, leur élément, leur rareté, leurs PV, leur revenu, leurs résistances, leurs capacités et leurs trois talents, **chacun avec sa description chiffrée**.

- **Icônes du jeu** : l'icône de l'élément et celle de la rareté s'affichent dans les listes de cartes, les filtres, l'en-tête de la fiche, les cartes du Deck builder et les compteurs du deck.
- **Résistances** : en pourcentage, avec l'icône de chaque élément (une valeur négative est une faiblesse, par exemple « Ombre -20 % »).
- **Capacités** : sous chaque capacité, sa **précision**, ses **chances de coup critique** avec les **dégâts critiques** (« Critique 1 % (×2) »), ou « Pas de coup critique », et sa **recharge** (« 1 tour de recharge », « 3 tours de recharge »). Exemple : « Précision 100 % · Critique 1 % (×2) · 3 tours de recharge ».
- **Tox de base** : les Tox (passifs) que la carte possède dès le départ, avec leur image dans un cadre de la **couleur de leur rareté**, leur nom, leur rareté et leur description (« Phénix », « Reset »…). La section n'apparaît pas pour une carte sans Tox de base.
- **Lore** : à la fin de la fiche, l'espèce, la région et la période de la carte, puis son histoire. Les **talents** sont numérotés (Talent 1, Talent 2, Talent 3), comme dans le Deck builder.
- **Filtres** : sous la recherche, le panneau « Espèce, région, période, tri » (replié par défaut) filtre par **espèce**, **région** et **période** (de la plus ancienne à la plus récente : Aube primordiale, Âge des Divinités, Grand Cataclysme, Malédiction des Rois, Ère prospère) et règle le **tri** (ordre du jeu, nom, PV, revenu, élément, rareté). Les filtres se cumulent avec la recherche, l'élément et la rareté ; « Réinitialiser les filtres » remet tout à zéro. Ces filtres existent aussi dans le Deck builder, le Drafter et la Génération de deck.
- **Dans mes decks** : les decks qui contiennent la carte (★ favori, ⚙ généré) ; cliquer sur l'un d'eux l'ouvre dans le Deck builder.

> Les descriptions chiffrées donnent les valeurs de base du jeu, telles qu'il les affiche pour une carte hors partie : en duel, les niveaux d'élément et les bonus les modifient. Quelques valeurs qui dépendent de la partie (par exemple selon les PV manquants) sont notées « X ».

### Deck builder

Pour construire vos decks : **4 cartes différentes** et **un talent par carte**.

- **Ajouter au deck (ou double-clic)**, **Retirer**, puis donnez un **Nom du deck** et cliquez sur **Sauvegarder** (Ctrl+S). Un deck incomplet est sauvegardé comme brouillon. **Deux decks ne peuvent pas avoir le même nom** (les accents et les majuscules ne comptent pas) ; si c'est déjà le cas dans vos anciens decks, « (2) » est ajouté automatiquement, rien n'est supprimé.
- **Annuler / Rétablir** (Ctrl+Z, Ctrl+Y) reviennent sur vos modifications. L'indication « ● modifications non sauvegardées » apparaît, et SpamDrafter vous demande confirmation avant de les perdre (changer de deck, créer un nouveau deck, coller un deck, fermer l'application).
- **Favori** (★) : s'enregistre tout de suite. **Étiquettes** : mots libres séparés par des virgules (« ranked, essai »), enregistrés avec le deck.
- **Copier le deck** place le deck en texte dans le presse-papiers (nom, cartes, talents) ; **Coller un deck…** crée un deck à partir d'un tel texte, que vous pouvez envoyer à quelqu'un. Les accents et les majuscules sont tolérés.
- **Dupliquer** (Ctrl+D) enregistre une copie du deck en cours sous le nom « … (copie) » et l'ouvre ; l'original reste intact.
- **Decks générés** (⚙, affiché avant le nom) : leurs cartes et leurs talents sont **verrouillés**. Vous pouvez les renommer, les étiqueter, les mettre en favori ; pour les modifier, utilisez **Dupliquer** : la copie est un deck à vous, modifiable.
- **Compteurs en direct** : sous les étiquettes, pour les cartes du deck : éléments (« Feu ×2 · Eau ×1 »), PV totaux, revenu total, **résistances moyennes** de l'équipe à chaque élément (en %) et **points faibles** (cartes dont la résistance à un élément est négative). Ce sont les valeurs des cartes, sans simulation : ils se mettent à jour à chaque carte ajoutée ou retirée. (Le jeu n'a pas de « coût » de carte, seulement un revenu : il n'y a donc pas de courbe de coûts.)
- **Version de SPAM** : chaque deck garde la **version du jeu avec laquelle il a été créé** (« Créé avec SPAM 1.30 · version actuelle : 1.31 »). Si le jeu a changé depuis, le Deck builder liste les **différences qui touchent ce deck** : PV, revenu, élément, rareté, résistances, capacités et talents modifiés, carte ou talent supprimé (« ⚠ Ce deck n'est plus valide »). Retirer ou remplacer l'élément modifié fait disparaître la ligne. Une fois lu, **« J'ai vu les différences : marquer ce deck comme vérifié »** passe le deck à la version actuelle sans toucher à ses cartes. Les decks touchés ont un ⚠ dans « Mes decks » et un filtre « ⚠ Modifiés par une mise à jour de SPAM » (affiché seulement si au moins un deck est concerné).
- **Quoi de neuf** : quand SPAM change de version (nouvelles données), un bandeau en haut résume les cartes et talents ajoutés, retirés ou modifiés, avec « Voir le détail », « Voir mes decks touchés (n) » et « Fermer » ; une fois fermé, il ne revient pas pour cette version. Si les différences ne sont pas disponibles pour votre version de départ, le bandeau le dit au lieu d'inventer.
- **Mes decks** : vos decks sauvegardés, avec une étoile pour les favoris, ⚙ pour ceux qui viennent du générateur (le ⚙ reste même si vous les renommez) et leurs étiquettes. Recherche (nom, carte, talent, étiquette), tri (nom, modifiés récemment, favoris d'abord), filtres (favoris, faits à la main / générés, étiquette). Cliquez sur un deck pour l'ouvrir et le modifier. **Nouveau deck** (Ctrl+N) repart de zéro ; **Supprimer** efface le deck ouvert.
- **Sauvegarder mes decks…** enregistre tous vos decks dans un fichier .zip ; **Restaurer des decks…** remet ceux d'un fichier .zip : elle n'efface jamais rien, ajoute les decks absents et ne remplace un deck que si la copie sauvegardée est plus récente.

### Drafter

Pour retrouver, parmi **vos decks sauvegardés**, ceux qui contiennent certaines cartes. Choisissez 1 à 4 cartes (**Ajouter à la recherche**) : la liste montre les decks qui les contiennent toutes, complets ou brouillons. **Ouvrir dans le Deck builder** ouvre le deck choisi.

### Génération de deck

Le **Grand Cuisinier** complète une sélection de 1 à 4 cartes en un deck de 4 cartes, avec les talents, en faisant jouer des milliers de **duels simulés**.

1. Choisissez 1 à 4 cartes (**Imposer cette carte**). Pour chacune, laissez « Talent au choix du Cuisinier » ou imposez un talent.
2. Cliquez sur **Lancer la cuisine** (environ 1 minute ; la génération se fait toujours à l'effort « normal »).
Vous pouvez **Annuler** à tout moment.

Avec **une seule carte** et le talent au choix, les propositions s'affichent **tout de suite** : elles ont été calculées à l'avance.

Chaque proposition affiche une **note sur 100** et un qualificatif. La note vient du pourcentage de duels simulés que le deck gagne contre les 20 decks de la méta (une égalité compte pour une demi-victoire), ramené à une échelle de 0 à 100 : **0 = il ne gagne presque jamais, 100 = il gagne la moitié de ses duels ou plus contre la méta, donc il est au niveau d'un deck de la méta.** **Intankable** (0, rouge) ; **Faible** (1 à 34, orange) ; **Passable** (35 à 54, jaune) ; **Bon** (55 à 74, vert) ; **Excellent** (75 à 99, bleu) ; **Parfait** (100, multicolore). **La proposition et la fiche d'un même deck affichent toujours la même note** (la note de chaque proposition est mesurée avec les mêmes duels que sa fiche). Passez la souris sur la note pour voir le nombre de duels, le pourcentage de victoires et la marge d'erreur ; « à confirmer » s'affiche quand la mesure repose sur trop peu de duels. Pour une seule carte, les propositions préparées à l'avance s'affichent tout de suite, puis leur note est mesurée (environ 30 secondes la première fois, instantané ensuite grâce à l'historique). **Sauvegarder** ajoute le deck à vos decks (un deck généré garde son badge ⚙ ; ses cartes et ses talents sont verrouillés, voir le Deck builder). Le panneau **Comment ça marche ?**, en haut de l'onglet, explique le Cuisinier et la note en quelques lignes.

**Tester ce deck →** (sous chaque proposition) sauvegarde la proposition puis l'ouvre dans l'onglet **Tester**, qui donne sa fiche détaillée.

**Résultat** : la colonne de droite affiche les decks proposés, chacun avec sa note, **Tester ce deck →** et **Sauvegarder**. Les 30 dernières générations sont gardées en mémoire : Redemander la même chose (mêmes cartes, mêmes talents, même effort) est **instantané** : « Résultat retrouvé en mémoire ». La mémoire est remise à zéro quand les données du jeu changent (nouvelle version de SPAM, nouvelle méta).

### Tester

Pour juger un deck. Choisissez un deck de 4 cartes dans la liste de gauche (mêmes recherche, tri et filtres que « Mes decks » ; les brouillons ne sont pas proposés ; la ✕ à côté du nom retire le deck choisi). Deux sous-onglets :

**Un deck** : **Tester ce deck** (environ 15 secondes) calcule la **note sur 100** (même note que dans la Génération de deck) et la **Fiche rapport** :

- **Verdict** : sa force contre les adversaires ;
- **Pourquoi ça marche** : les éléments et les liens entre cartes qui font sa force ;
- **Les cartes** : la carte clé, la plus faible, celle qui encaisse ;
- **Talents** et **Tox à acheter**, quand une mesure les distingue nettement ;

**Analyse complète** (environ 1 minute) ajoute les liens entre chaque paire de cartes ; **Voir le détail** affiche alors le rapport complet. Un rapport déjà calculé s'affiche **tout de suite** quand vous choisissez le deck (« Rapport retrouvé en mémoire »).

**Comparer 2 decks** : un clic sur un deck de la liste le place en **A**, le suivant en **B** (les boutons « Deck A » et « Deck B » choisissent le camp à remplir ; **Retirer** vide un camp ; **⇄** échange A et B). **Comparer ces decks** les fait jouer l'un contre l'autre : 300 duels simulés, chaque deck commençant autant de fois que l'autre. Vous obtenez :

- le **verdict** en haut, avec une jauge orange (Deck A) et bleue (Deck B) : un deck n'est déclaré meilleur que si l'écart dépasse la marge d'erreur, sinon « pas d'écart net » ; le texte parle de « Deck A » et « Deck B » (le vrai nom s'affiche au survol) ;
- les **notes** des deux decks contre la méta, dans la couleur de leur niveau ;
- le résultat selon **qui commence** (cela change une fois sur deux) et la **durée** des duels ;
- un **tableau par deck** : l'illustration de chaque carte avec son talent, puis les moyennes par duel (dégâts infligés, soins, dégâts subis, éliminations, fréquence de chute).

Les confrontations déjà faites sont gardées (« Confrontation retrouvée en mémoire »). Ce sont des parties simulées, pas de vraies parties.

### Contre qui sont mesurés les decks ?

Contre les decks de la **méta synthétique** : 20 decks forts, calculés pour cette version de SPAM par le Grand Cuisinier lui-même, et livrés avec les données. Vos propres decks ne servent jamais d'adversaires.

Quand une nouvelle méta est calculée, elle arrive avec une mise à jour des données, comme le reste.

### Ce qu'il faut savoir sur les résultats

- Les scores viennent d'un **simulateur** : une réécriture indépendante des règles du duel, jouée par une intelligence artificielle. Ce ne sont pas des statistiques de vraies parties.
- L'IA ne joue pas comme un humain : certains decks réels sont sous-estimés en simulation. Prenez les propositions comme des **idées à essayer**, pas comme des certitudes.
- Avec les mêmes cartes et les mêmes réglages, l'application donne toujours les mêmes propositions.

### Ma collection (cartes et talents possédés)

SPAM ne permet pas encore à SpamDrafter de lire ta collection : tu la saisis toi-même.

- **Interrupteur** (sous l'en-tête) : **Toutes les cartes et talents** (tout le jeu) ou **Cartes et talents possédés**. Dans le second mode, la Bibliothèque, les listes de cartes, « Mes decks », le Drafter et le Générateur n'utilisent que ce que tu possèdes : un deck n'est montré que si ses cartes et ses talents sont tous à toi, et le Générateur ne propose que des decks que tu peux essayer dans SPAM (il faut au moins 4 cartes possédées).
- **Ma collection…** : **Importation complète** (les cartes défilent une à une, dans l'ordre du jeu : *Pas de talent*, *Talent 1*, *Talent 2*, *Talent 3* ou *Carte non possédée*) ou **Importation précise** (tu cherches une carte et tu règles ce que tu en possèdes). *Talent 2* veut dire que tu possèdes aussi le Talent 1, *Talent 3* que tu possèdes aussi les Talents 1 et 2.
- Pendant l'importation complète : **Continuer plus tard** (tu reprendras où tu t'es arrêté), **Annuler l'importation** (ta collection ne change pas) et **← Carte précédente**. Ta collection n'est remplacée qu'à la fin.

---

## 4. Où sont mes fichiers ?

| Quoi | Où |
|---|---|
| Vos decks | `%APPDATA%\SpamDrafter\decks` (un fichier par deck) |
| Le programme | `%LOCALAPPDATA%\SpamDrafter` |
| Les données du jeu | `%LOCALAPPDATA%\SpamDrafter\data` |
| Historique des générations, rapports et comparaisons | `%APPDATA%\SpamDrafter\history.json` (le supprimer vide l'historique) |
| Ta collection de cartes et talents | `%APPDATA%\SpamDrafter\collection.json` (le supprimer vide la collection) |
| Ce dont l'application se souvient (onglet, fenêtre, filtres) | `%APPDATA%\SpamDrafter\settings.json` (le supprimer remet tout à zéro) |

Pour sauvegarder vos decks, utilisez **Sauvegarder mes decks…** dans le Deck builder (ou copiez le dossier `decks`).

## 5. Désinstaller

Paramètres de Windows → **Applications** → « SpamDrafter (non officiel) » → **Désinstaller**. Le programme, les données du jeu et les raccourcis sont supprimés. **Vos decks sont conservés** dans `%APPDATA%\SpamDrafter\decks`. Supprimez ce dossier vous-même si vous n'en voulez plus.

## 6. Un problème ?

Ouvrez un ticket (« Issues ») sur la page GitHub du projet. Indiquez la version affichée en haut de la fenêtre et la révision des données.
