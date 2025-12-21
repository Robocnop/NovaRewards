using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Life;
using Life.Network;
using Life.UI;
using ModKit.Helper;
using ModKit.Internal;
using ModKit.Interfaces;
using SQLite;
using Newtonsoft.Json;
using Logger = ModKit.Internal.Logger;
using mk = ModKit.Helper.TextFormattingHelper;
using Life.DB;
using System.Collections.Generic;
using ModKit.Utils;

namespace NovaRewards
{
    public class NovaRewards : ModKit.ModKit
    {
        public SQLiteConnection _db;
        public PluginConfig Config;
        private string _dbPath;
        private string _configPath;

        public NovaRewards(IGameAPI api) : base(api)
        {
            PluginInformations = new PluginInformations("NovaRewards", "1.1.0", "Robocnop");
        }

        public override void OnPluginInit()
        {
            base.OnPluginInit();

            string dir = Path.Combine(pluginsPath, "NovaRewards");
            _dbPath = Path.Combine(dir, "NovaRewards.db");
            _configPath = Path.Combine(dir, "config.json");

            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            LoadConfig();

            try
            {
                _db = new SQLiteConnection(_dbPath);
                _db.CreateTable<RewardCode>();
                _db.CreateTable<RewardHistory>();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Erreur DB Plugin: {ex.Message}", "NovaRewards");
                return;
            }

            InsertMenu();

            new SChatCommand("/cadeau", "Recuperer un cadeau", "/cadeau [code]", (player, args) =>
            {
                if (args.Length > 0) RedeemCode(player, args[0]);
                else player.Notify("Erreur", "Usage: /cadeau [CODE]", NotificationManager.Type.Error);
            }).Register();

            new SChatCommand("/rewards", "Admin Panel", "/rewards", (player, args) =>
            {
                if (player.account.adminLevel >= 5) NovaRewardsPanel.OpenMainPanel(player, this);
                else player.Notify("Refuse", "Permissions insuffisantes.", NotificationManager.Type.Error);
            }).Register();

            Logger.LogSuccess("Demarrage", $"NovaRewards v{PluginInformations.Version} est pret !");
        }

        public void InsertMenu()
        {
            AAMenu.Menu.AddInteractionTabLine(PluginInformations, "Utiliser un code cadeau", (ui) =>
            {
                NovaRewardsPanel.OpenPlayerRedeemPanel(PanelHelper.ReturnPlayerFromPanel(ui), this);
            });

            AAMenu.Menu.AddAdminPluginTabLine(PluginInformations, 5, "NovaRewards", (ui) =>
            {
                NovaRewardsPanel.OpenMainPanel(PanelHelper.ReturnPlayerFromPanel(ui), this);
            }, 0);
        }

        public void RedeemCode(Player player, string codeInput)
        {
            try
            {
                string pSteamId = player.steamId.ToString();

                var code = _db.Table<RewardCode>().ToList()
                              .FirstOrDefault(c => c.Name.Equals(codeInput, StringComparison.OrdinalIgnoreCase));

                if (code == null)
                {
                    player.Notify("Erreur", "Code invalide.", NotificationManager.Type.Error);
                    return;
                }

                if (code.ExpirationDate != null && DateTime.Now > code.ExpirationDate)
                {
                    player.Notify("Expire", "Ce code a expire.", NotificationManager.Type.Error);
                    return;
                }

                int usesCount = _db.Table<RewardHistory>().Count(h => h.CodeName == code.Name);
                if (code.MaxUses > 0 && usesCount >= code.MaxUses)
                {
                    player.Notify("Termine", "Ce code est epuise.", NotificationManager.Type.Error);
                    return;
                }

                if (_db.Table<RewardHistory>().Any(h => h.CodeName == code.Name && h.SteamId == pSteamId))
                {
                    player.Notify("Deja Recu", "Vous avez deja utilise ce code.", NotificationManager.Type.Warning);
                    return;
                }

                // --- DISTRIBUTION ---
                string logReward = "";

                if (code.Type == "money")
                {
                    player.AddMoney(code.Value, "Code Cadeau");
                    player.Notify("Succes", $"Vous avez recu {code.Value} euros !", NotificationManager.Type.Success);
                    logReward = $"{code.Value}€";
                }
                else if (code.Type == "random_money")
                {
                    int min = (int)code.Value;
                    int max = code.Quantity;
                    int won = new Random().Next(min, max + 1);
                    player.AddMoney(won, "Code Loterie");
                    player.Notify("Jackpot", $"Gagne: {won} euros (sur {max} max) !", NotificationManager.Type.Success);
                    logReward = $"{won}€ (Loterie {min}-{max})";
                }
                else if (code.Type == "item")
                {
                    player.setup.inventory.AddItem((int)code.Value, code.Quantity, "Code Cadeau");
                    player.Notify("Succes", "Objets ajoutes a l'inventaire.", NotificationManager.Type.Success);
                    logReward = $"{code.Quantity}x Item {code.Value}";
                }
                else if (code.Type == "vehicle")
                {
                    Dictionary<int, int> models = JsonConvert.DeserializeObject<Dictionary<int, int>>(code.Data);
                    if (models != null)
                    {
                        List<string> givenVehicles = new List<string>();

                        foreach (KeyValuePair<int, int> model in models)
                        {
                            if (model.Value > 0)
                            {
                                for (int i = 0; i < model.Value; i++)
                                {
                                    GiveVehicle(player, model.Key);
                                }
                                string modelName = VehicleUtils.GetModelNameByModelId(model.Key);
                                givenVehicles.Add($"{model.Value}x {modelName}");
                            }
                        }
                        logReward = string.Join(", ", givenVehicles);
                        player.Notify("Succès", "Véhicule(s) ajouté(s) au garage !", NotificationManager.Type.Success);
                    }
                }

                // Sauvegarde
                _db.Insert(new RewardHistory { CodeName = code.Name, SteamId = pSteamId, DateClaimed = DateTime.Now });
                SendDiscordLog(player, code.Name, logReward);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Redeem Error: {ex.Message}", "NovaRewards");
                player.Notify("Erreur", "Erreur interne.", NotificationManager.Type.Error);
            }
        }

        private async void GiveVehicle(Player player, int modelId)
        {
            try
            {
                Life.PermissionSystem.Permissions permissions = new Life.PermissionSystem.Permissions()
                {
                    owner = new Life.PermissionSystem.Entity()
                    {
                        groupId = 0,
                        characterId = player.character.Id,
                    },
                    coOwners = new System.Collections.Generic.List<Life.PermissionSystem.Entity>()
                };
                
                string jsonPermission = JsonConvert.SerializeObject(permissions);
                
                if (permissions != null && !string.IsNullOrEmpty(jsonPermission))
                {
                    await LifeDB.CreateVehicle(modelId, jsonPermission);
                }
                else
                {
                     Logger.LogError("NovaRewards", "Error creating permissions for vehicle.");
                }
            }
            catch (Exception ex)
            {
                player.Notify("Erreur", "Impossible de donner le véhicule.", NotificationManager.Type.Error);
                Logger.LogError("NovaRewards", $"Give vehicle error : {ex.Message}");
            }
        }

        public void CreateCode(RewardCode newCode)
        {
            var existing = _db.Table<RewardCode>().FirstOrDefault(c => c.Name == newCode.Name);
            if (existing != null) _db.Delete(existing);
            _db.Insert(newCode);
        }

        public void UpdateCode(RewardCode code)
        {
            _db.Update(code);
        }

        public void DeleteCode(string name)
        {
            var code = _db.Table<RewardCode>().FirstOrDefault(c => c.Name == name);
            if (code != null) _db.Delete(code);
        }

        public void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                Config = new PluginConfig();
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
                Logger.LogWarning("Config", "Fichier config.json cree ! Pensez a ajouter le Webhook Discord.");
            }
            else Config = JsonConvert.DeserializeObject<PluginConfig>(File.ReadAllText(_configPath));
        }

        public void SendDiscordLog(Player p, string code, string reward)
        {
            if (string.IsNullOrEmpty(Config.DiscordWebhookUrl)) return;
            Task.Run(() =>
            {
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                        string playerInfo = $"{p.FullName} ({p.steamId})";
                        string payload = "{\"embeds\": [{\"title\": \"Cadeau Utilise\",\"color\": 3066993,\"fields\": [{\"name\": \"Joueur\",\"value\": \"" + playerInfo + "\",\"inline\": true},{\"name\": \"Code\",\"value\": \"`" + code + "`\",\"inline\": true},{\"name\": \"Gain\",\"value\": \"" + reward + "\"}],\"timestamp\": \"" + DateTime.UtcNow.ToString("o") + "\"}]}";
                        client.UploadData(Config.DiscordWebhookUrl, "POST", Encoding.UTF8.GetBytes(payload));
                    }
                }
                catch { }
            });
        }

        public void SendAdminLog(Player admin, string action, string details)
        {
            if (string.IsNullOrEmpty(Config.DiscordWebhookUrl)) return;
            Task.Run(() =>
            {
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                        string adminInfo = $"{admin.FullName} ({admin.steamId})";
                        string payload = "{\"embeds\": [{\"title\": \"Administration NovaRewards\",\"color\": 15105570,\"fields\": [{\"name\": \"Admin\",\"value\": \"" + adminInfo + "\",\"inline\": true},{\"name\": \"Action\",\"value\": \"" + action + "\",\"inline\": true},{\"name\": \"Details\",\"value\": \"" + details + "\"}],\"timestamp\": \"" + DateTime.UtcNow.ToString("o") + "\"}]}";
                        client.UploadData(Config.DiscordWebhookUrl, "POST", Encoding.UTF8.GetBytes(payload));
                    }
                }
                catch { }
            });
        }
    }
}