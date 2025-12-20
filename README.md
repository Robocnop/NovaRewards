# 🎁 NovaRewards

![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![Jeux](https://img.shields.io/badge/Jeux-Nova%20Life-orange.svg)
![License](https://img.shields.io/badge/license-GNU%20GPL%20v3-red.svg)

**NovaRewards** est un système complet de gestion de codes cadeaux pour les serveurs Nova Life. Il permet aux administrateurs de créer, gérer et distribuer des codes promotionnels offrant de l'argent, de l'argent aléatoire (loterie) ou des objets aux joueurs.

## ✨ Fonctionnalités

### Pour les Administrateurs
* **Création intuitive** : Assistant de création en 6 étapes via une interface graphique (GUI).
* **Types de récompenses variés** :
    * 💰 **Argent fixe** : Montant défini.
    * 🎲 **Argent aléatoire** : Le joueur gagne un montant entre un Min et un Max défini (Type "Loterie").
    * 📦 **Objets (Items)** : Don d'objets directement dans l'inventaire.
* **Contrôle total** :
    * 📅 **Date d'expiration** : Définissez une date de fin de validité.
    * 🔢 **Limites d'utilisation** : Nombre maximum d'activations globales (ex: pour les 10 premiers).
* **Gestion avancée** : Possibilité de modifier les dates ou les limites d'un code existant sans le supprimer.
* **Logs Discord** :
    * Logs **Verts** : Quand un joueur utilise un code.
    * Logs **Oranges** : Quand un admin crée, modifie ou supprime un code (avec SteamID).

### Pour les Joueurs
* Interface simple pour entrer les codes via commande ou menu.
* Feedback immédiat (Succès, Code expiré, Déjà utilisé, etc.).

## 🚀 Installation

1.  Téléchargez la dernière version de `NovaRewards.dll`.
2.  Placez le fichier dans le dossier `Plugins/` de votre serveur Nova Life.
3.  Démarrez le serveur une première fois pour générer les fichiers de configuration.
4.  Configurez le Webhook Discord (voir ci-dessous).

## ⚙️ Configuration

Au premier lancement, un fichier `config.json` est créé dans `Plugins/NovaRewards/`.

Ouvrez ce fichier et ajoutez votre URL de Webhook Discord pour activer les logs :

```json
{
  "DiscordWebhookUrl": "[https://discord.com/api/webhooks/"
}