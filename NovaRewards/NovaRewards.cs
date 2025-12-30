using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Life;
using Life.Network;
using Life.UI;
using Life.DB;
using ModKit.Helper;
using ModKit.Internal;
using ModKit.Interfaces;
using ModKit.Utils;
using SQLite;
using Newtonsoft.Json;
using Logger = ModKit.Internal.Logger;

namespace NovaRewards
{
    public class NovaRewards : ModKit.ModKit
    {
        public SQLiteConnection _db;
        public PluginConfig Config;
        private string _dbPath;
        private string _configPath;
        private static readonly Random _random = new Random();
        private readonly object _redeemLock = new object();

        public NovaRewards(IGameAPI api) : base(api)
        {
            PluginInformations = new PluginInformations("NovaRewards", "1.2.0", "Robocnop");
        }

        public override void OnPluginInit()
        {
            base.OnPluginInit();

            string dir = Path.Combine(pluginsPath, "NovaRewards");
            _dbPath = Path.Combine(dir, "NovaRewards.db");
            _configPath = Path.Combine(dir, "config.json");

            if (!Directory.Exists(dir)) 
                Directory.CreateDirectory(dir);

            LoadConfig();

            try
            {
                _db = new SQLiteConnection(_dbPath);
                _db.CreateTable<RewardCode>();
                _db.CreateTable<RewardHistory>();
                Logger.LogSuccess("NovaRewards - Base de donnees", "Tables creees avec succes");
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - Erreur DB: {ex.Message}\n{ex.StackTrace}", "NovaRewards");
                return;
            }

            InsertMenu();

            new SChatCommand("/cadeau", "Recuperer un cadeau", "/cadeau [code]", (player, args) =>
            {
                if (args.Length > 0) 
                {
                    Task.Run(async () => await RedeemCodeAsync(player, args[0]));
                }
                else 
                {
                    player.Notify("Erreur", "Usage: /cadeau [CODE]", NotificationManager.Type.Error);
                }
            }).Register();

            new SChatCommand("/rewards", "Admin Panel", "/rewards", (player, args) =>
            {
                if (player.account.adminLevel >= 5) 
                    NovaRewardsPanel.OpenMainPanel(player, this);
                else 
                    player.Notify("Refuse", "Permissions insuffisantes.", NotificationManager.Type.Error);
            }).Register();

            Logger.LogSuccess("NovaRewards - Demarrage", $"v{PluginInformations.Version} est pret !");
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
            Task.Run(async () => await RedeemCodeAsync(player, codeInput));
        }

        public async Task RedeemCodeAsync(Player player, string codeInput)
        {
            try
            {
                string pSteamId = player.steamId.ToString();
                string codeUpper = codeInput.Trim().ToUpper();

                var code = _db.Table<RewardCode>()
                    .Where(c => c.Name.ToUpper() == codeUpper)
                    .FirstOrDefault();

                if (code == null)
                {
                    player.Notify("Erreur", "Code invalide.", NotificationManager.Type.Error);
                    return;
                }

                if (code.ExpirationDate.HasValue && DateTime.Now > code.ExpirationDate.Value)
                {
                    player.Notify("Expire", "Ce code a expire.", NotificationManager.Type.Error);
                    return;
                }

                lock (_redeemLock)
                {
                    int usesCount = _db.Table<RewardHistory>()
                        .Count(h => h.CodeName == code.Name);
                    
                    if (code.MaxUses > 0 && usesCount >= code.MaxUses)
                    {
                        player.Notify("Termine", "Ce code est epuise.", NotificationManager.Type.Error);
                        return;
                    }

                    bool alreadyUsed = _db.Table<RewardHistory>()
                        .Any(h => h.CodeName == code.Name && h.SteamId == pSteamId);
                    
                    if (alreadyUsed)
                    {
                        player.Notify("Deja Recu", "Vous avez deja utilise ce code.", NotificationManager.Type.Warning);
                        return;
                    }
                }

                string logReward = "";
                bool success = false;

                switch (code.Type)
                {
                    case "money":
                        player.AddMoney(code.Value, "Code Cadeau");
                        player.Notify("Succes", $"Vous avez recu {code.Value} euros !", NotificationManager.Type.Success);
                        logReward = $"{code.Value} EUR";
                        success = true;
                        break;

                    case "random_money":
                        int min = (int)code.Value;
                        int max = code.Quantity;
                        int won = _random.Next(min, max + 1);
                        player.AddMoney(won, "Code Loterie");
                        player.Notify("Jackpot", $"Gagne: {won} euros (sur {max} max) !", NotificationManager.Type.Success);
                        logReward = $"{won} EUR (Loterie {min}-{max})";
                        success = true;
                        break;

                    case "item":
                        player.setup.inventory.AddItem((int)code.Value, code.Quantity, "Code Cadeau");
                        player.Notify("Succes", "Objets ajoutes a l'inventaire.", NotificationManager.Type.Success);
                        logReward = $"{code.Quantity}x Item {code.Value}";
                        success = true;
                        break;

                    case "vehicle":
                        try
                        {
                            var models = JsonConvert.DeserializeObject<Dictionary<int, int>>(code.Data);
                            if (models != null && models.Count > 0)
                            {
                                List<string> givenVehicles = new List<string>();

                                foreach (var model in models)
                                {
                                    if (model.Key < 0 || model.Key >= Nova.v.vehicleModels.Length)
                                    {
                                        Logger.LogWarning($"NovaRewards - Model ID invalide: {model.Key}", "NovaRewards");
                                        continue;
                                    }

                                    if (Nova.v.vehicleModels[model.Key] == null)
                                    {
                                        Logger.LogWarning($"NovaRewards - Model null pour ID: {model.Key}", "NovaRewards");
                                        continue;
                                    }

                                    for (int i = 0; i < model.Value; i++)
                                    {
                                        await GiveVehicleAsync(player, model.Key);
                                    }

                                    string modelName = VehicleUtils.GetModelNameByModelId(model.Key);
                                    givenVehicles.Add($"{model.Value}x {modelName}");
                                }

                                if (givenVehicles.Count > 0)
                                {
                                    logReward = string.Join(", ", givenVehicles);
                                    player.Notify("Succes", "Vehicule(s) ajoute(s) au garage !", NotificationManager.Type.Success);
                                    success = true;
                                }
                                else
                                {
                                    player.Notify("Erreur", "Aucun vehicule valide dans ce code.", NotificationManager.Type.Error);
                                    return;
                                }
                            }
                        }
                        catch (Exception vEx)
                        {
                            Logger.LogError($"NovaRewards - Erreur vehicule: {vEx.Message}\n{vEx.StackTrace}", "NovaRewards");
                            player.Notify("Erreur", "Erreur lors de l'ajout du vehicule.", NotificationManager.Type.Error);
                            return;
                        }
                        break;

                    default:
                        player.Notify("Erreur", "Type de code inconnu.", NotificationManager.Type.Error);
                        return;
                }

                if (success)
                {
                    lock (_redeemLock)
                    {
                        _db.Insert(new RewardHistory 
                        { 
                            CodeName = code.Name, 
                            SteamId = pSteamId, 
                            DateClaimed = DateTime.Now 
                        });
                    }

                    SendDiscordLog(player, code.Name, logReward);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - Redeem Error: {ex.Message}\n{ex.StackTrace}", "NovaRewards");
                player.Notify("Erreur", "Erreur interne.", NotificationManager.Type.Error);
            }
        }

        private async Task GiveVehicleAsync(Player player, int modelId)
        {
            try
            {
                var permissions = new Life.PermissionSystem.Permissions()
                {
                    owner = new Life.PermissionSystem.Entity()
                    {
                        groupId = 0,
                        characterId = player.character.Id,
                    },
                    coOwners = new List<Life.PermissionSystem.Entity>()
                };
                
                string jsonPermission = JsonConvert.SerializeObject(permissions);
                
                if (!string.IsNullOrEmpty(jsonPermission))
                {
                    await LifeDB.CreateVehicle(modelId, jsonPermission);
                }
                else
                {
                    Logger.LogError("NovaRewards - Permissions JSON vides", "NovaRewards");
                    throw new Exception("Impossible de creer les permissions du vehicule");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - Give vehicle error: {ex.Message}\n{ex.StackTrace}", "NovaRewards");
                throw;
            }
        }

        public void CreateCode(RewardCode newCode)
        {
            try
            {
                var existing = _db.Table<RewardCode>()
                    .FirstOrDefault(c => c.Name == newCode.Name);
                
                if (existing != null) 
                    _db.Delete(existing);
                
                _db.Insert(newCode);
                Logger.LogSuccess("NovaRewards - Code cree", $"{newCode.Name} ({newCode.Type})");
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - CreateCode Error: {ex.Message}", "NovaRewards");
                throw;
            }
        }

        public void UpdateCode(RewardCode code)
        {
            try
            {
                _db.Update(code);
                Logger.LogSuccess("NovaRewards - Code mis a jour", code.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - UpdateCode Error: {ex.Message}", "NovaRewards");
                throw;
            }
        }

        public void DeleteCode(string name)
        {
            try
            {
                var code = _db.Table<RewardCode>()
                    .FirstOrDefault(c => c.Name == name);
                
                if (code != null)
                {
                    _db.Delete(code);
                    Logger.LogSuccess("NovaRewards - Code supprime", name);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - DeleteCode Error: {ex.Message}", "NovaRewards");
                throw;
            }
        }

        public void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    Config = new PluginConfig();
                    File.WriteAllText(_configPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
                    Logger.LogWarning("NovaRewards - Config", "Fichier config.json cree ! Pensez a ajouter le Webhook Discord.");
                }
                else
                {
                    Config = JsonConvert.DeserializeObject<PluginConfig>(File.ReadAllText(_configPath));
                    Logger.LogSuccess("NovaRewards - Config", "Configuration chargee");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"NovaRewards - LoadConfig Error: {ex.Message}", "NovaRewards");
                Config = new PluginConfig();
            }
        }

        public void SendDiscordLog(Player p, string code, string reward)
        {
            if (string.IsNullOrEmpty(Config.DiscordWebhookUrl)) return;
            
            Task.Run(async () =>
            {
                try
                {
                    var embed = new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "🎁 Cadeau Utilise",
                                color = 3066993,
                                fields = new[]
                                {
                                    new { 
                                        name = "👤 Joueur", 
                                        value = $"{p.FullName} ({p.steamId})", 
                                        inline = true 
                                    },
                                    new { 
                                        name = "🎟️ Code", 
                                        value = $"`{code}`", 
                                        inline = true 
                                    },
                                    new { 
                                        name = "💰 Gain", 
                                        value = reward, 
                                        inline = false 
                                    }
                                },
                                timestamp = DateTime.UtcNow.ToString("o"),
                                footer = new { text = "NovaRewards v1.2.0" }
                            }
                        }
                    };
                    
                    string payload = JsonConvert.SerializeObject(embed);
                    
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                        client.Encoding = Encoding.UTF8;
                        await client.UploadStringTaskAsync(Config.DiscordWebhookUrl, payload);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"NovaRewards - Discord webhook error: {ex.Message}", "NovaRewards");
                }
            });
        }

        public void SendAdminLog(Player admin, string action, string details)
        {
            if (string.IsNullOrEmpty(Config.DiscordWebhookUrl)) return;
            
            Task.Run(async () =>
            {
                try
                {
                    var embed = new
                    {
                        embeds = new[]
                        {
                            new
                            {
                                title = "⚙️ Administration NovaRewards",
                                color = 15105570,
                                fields = new[]
                                {
                                    new { 
                                        name = "👮 Admin", 
                                        value = $"{admin.FullName} ({admin.steamId})", 
                                        inline = true 
                                    },
                                    new { 
                                        name = "📋 Action", 
                                        value = action, 
                                        inline = true 
                                    },
                                    new { 
                                        name = "📝 Details", 
                                        value = details, 
                                        inline = false 
                                    }
                                },
                                timestamp = DateTime.UtcNow.ToString("o"),
                                footer = new { text = "NovaRewards v1.2.0" }
                            }
                        }
                    };
                    
                    string payload = JsonConvert.SerializeObject(embed);
                    
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                        client.Encoding = Encoding.UTF8;
                        await client.UploadStringTaskAsync(Config.DiscordWebhookUrl, payload);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"NovaRewards - Discord webhook error: {ex.Message}", "NovaRewards");
                }
            });
        }
    }
}