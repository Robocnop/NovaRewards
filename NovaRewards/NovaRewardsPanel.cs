using Life;
using Life.Network;
using Life.UI;
using ModKit.Helper;
using ModKit.Utils;
using Newtonsoft.Json;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NovaRewards
{
    public static class NovaRewardsPanel
    {
        public static void OpenMainPanel(Player player, NovaRewards plugin)
        {
            UIPanel panel = new UIPanel("NovaRewards - Admin", UIPanel.PanelType.Text);
            panel.SetText("Gestionnaire de recompenses.\nChoisissez une action :");
            panel.AddButton("Creer un Code", (ui) => OpenCreateStep1(player, plugin));
            panel.AddButton("Liste des Codes", (ui) => OpenListPanel(player, plugin));
            panel.AddButton("Fermer", (ui) => player.ClosePanel(ui));
            player.ShowPanelUI(panel);
        }

        // --- WIZARD CREATION ---
        public static void OpenCreateStep1(Player player, NovaRewards plugin)
        {
            UIPanel panel = new UIPanel("Etape 1/6 : Nom", UIPanel.PanelType.Input);
            panel.SetText("Entrez le NOM du code (ex: BIENVENUE) :");
            panel.SetInputPlaceholder("BIENVENUE");
            panel.AddButton("Suivant", (ui) => 
            {
                if (!string.IsNullOrEmpty(ui.inputText))
                {
                    string codeName = ui.inputText.Trim().ToUpper();
                    
                    // Vérification si le code existe déjà
                    var existing = plugin._db.Table<RewardCode>()
                        .FirstOrDefault(c => c.Name == codeName);
                    
                    if (existing != null)
                    {
                        player.Notify("Attention", "Ce code existe deja ! Il sera remplace.", NotificationManager.Type.Warning);
                    }
                    
                    OpenCreateStep2(player, plugin, codeName);
                }
                else 
                {
                    player.Notify("Erreur", "Veuillez entrer un nom.", NotificationManager.Type.Error);
                }
            });
            panel.AddButton("Annuler", (ui) => OpenMainPanel(player, plugin));
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep2(Player player, NovaRewards plugin, string name)
        {
            UIPanel panel = new UIPanel("Etape 2/6 : Type", UIPanel.PanelType.Text);
            panel.SetText($"Code: {name}\nQuel type de cadeau ?");
            panel.AddButton("Argent Fixe", (ui) => OpenCreateStep3(player, plugin, name, "money"));
            panel.AddButton("Argent Aleatoire", (ui) => OpenCreateStep3(player, plugin, name, "random_money"));
            panel.AddButton("Objet (Item)", (ui) => OpenCreateStep3(player, plugin, name, "item"));
            panel.AddButton("Vehicule", (ui) => RedirectToVehicleSteps(player, plugin, name, "vehicle", 0, 0));
            panel.AddButton("Retour", (ui) => OpenCreateStep1(player, plugin));
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep3(Player player, NovaRewards plugin, string name, string type)
        {
            UIPanel panel = new UIPanel("Etape 3/6 : Valeur", UIPanel.PanelType.Input);
            
            if (type == "money") 
                panel.SetText("Montant en euros :");
            else if (type == "random_money") 
                panel.SetText("Montant MINIMUM :");
            else 
                panel.SetText("ID de l'Item :");

            panel.AddButton("Suivant", (ui) => 
            {
                if (double.TryParse(ui.inputText, out double v))
                {
                    if (v >= 0)
                    {
                        OpenCreateStep4(player, plugin, name, type, v);
                    }
                    else
                    {
                        player.Notify("Erreur", "La valeur doit etre positive.", NotificationManager.Type.Error);
                    }
                }
                else 
                {
                    player.Notify("Erreur", "Valeur incorrecte.", NotificationManager.Type.Error);
                }
            });
            panel.AddButton("Retour", (ui) => OpenCreateStep2(player, plugin, name));
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep4(Player player, NovaRewards plugin, string name, string type, double val1)
        {
            if (type == "money") 
            { 
                OpenCreateStep5(player, plugin, name, type, val1, 0, null); 
                return; 
            }

            UIPanel panel = new UIPanel("Etape 4/6 : Detail", UIPanel.PanelType.Input);
            
            if (type == "random_money") 
                panel.SetText($"Min: {val1}€. Entrez le MAXIMUM :");
            else 
                panel.SetText("Quantite d'objets :");

            panel.AddButton("Suivant", (ui) => 
            {
                if (int.TryParse(ui.inputText, out int v2))
                {
                    if (type == "random_money" && v2 <= val1)
                    {
                        player.Notify("Erreur", $"Le maximum doit etre superieur a {val1}.", NotificationManager.Type.Error);
                        return;
                    }
                    
                    if (v2 > 0)
                    {
                        OpenCreateStep5(player, plugin, name, type, val1, v2, null);
                    }
                    else
                    {
                        player.Notify("Erreur", "La quantite doit etre superieure a 0.", NotificationManager.Type.Error);
                    }
                }
                else 
                {
                    player.Notify("Erreur", "Nombre incorrect.", NotificationManager.Type.Error);
                }
            });
            panel.AddButton("Retour", (ui) => OpenCreateStep3(player, plugin, name, type));
            player.ShowPanelUI(panel);
        }

        #region Vehicle

        public static void RedirectToVehicleSteps(Player player, NovaRewards plugin, string name, string type, double val1, int val2, Dictionary<int, int> vehicles = null)
        {
            if (vehicles == null)
                vehicles = new Dictionary<int, int>();

            UIPanel panel = new UIPanel("Vehicules", UIPanel.PanelType.TabPrice);
            
            panel.AddButton("Fermer", ui => 
            {
                player.ClosePanel(panel);
                OpenMainPanel(player, plugin);
            });
            
            panel.AddButton("<color=green>Finir</color>", ui =>
            {
                if (vehicles.Count == 0)
                {
                    player.Notify("Erreur", "Ajoutez au moins un vehicule.", NotificationManager.Type.Error);
                    return;
                }
                
                OpenCreateStep5(player, plugin, name, type, val1, val2, JsonConvert.SerializeObject(vehicles));
            });
            
            panel.AddButton("<color=green>Ajouter</color>", async ui =>
            {
                player.ClosePanel(panel);
                
                int modelId = await OpenInputModelIdPanel(player);
                if (modelId == -1)
                {
                    RedirectToVehicleSteps(player, plugin, name, type, val1, val2, vehicles);
                    return;
                }
                
                int quantity = await OpenInputQuantityPanel(player);
                if (quantity == -1)
                {
                    RedirectToVehicleSteps(player, plugin, name, type, val1, val2, vehicles);
                    return;
                }
                
                if (vehicles.ContainsKey(modelId))
                {
                    vehicles[modelId] += quantity;
                }
                else
                {
                    vehicles.Add(modelId, quantity);
                }
                
                player.Notify("Succes", $"Vehicule ajoute ({quantity}x).", NotificationManager.Type.Success);
                RedirectToVehicleSteps(player, plugin, name, type, val1, val2, vehicles);
            });
            
            panel.AddButton("<color=red>Supprimer</color>", ui => ui.SelectTab());
            
            foreach (var pair in vehicles.ToDictionary(k => k.Key, v => v.Value))
            {
                int modelId = pair.Key;
                int quantity = pair.Value;
                string modelName = VehicleUtils.GetModelNameByModelId(modelId);
                
                panel.AddTabLine(modelName, $"x{quantity}", VehicleUtils.GetIconId(modelId), ui =>
                {
                    vehicles.Remove(modelId);
                    player.Notify("Succes", $"{modelName} retire.", NotificationManager.Type.Success);
                    player.ClosePanel(panel);
                    RedirectToVehicleSteps(player, plugin, name, type, val1, val2, vehicles);
                });
            }
            
            player.ShowPanelUI(panel);
        }

        private static async Task<int> OpenInputModelIdPanel(Player player)
        {
            try
            {
                TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                UIPanel panel = new UIPanel("Choix du model (Vehicule)", UIPanel.PanelType.Input);
                panel.SetText("Id du Model du vehicule :");
                panel.SetInputPlaceholder("Ex: 45");
                
                panel.AddButton("Fermer", ui =>
                {
                    player.ClosePanel(panel);
                    tcs.TrySetResult(-1);
                });
                
                panel.AddButton("Valider", ui =>
                {
                    if (int.TryParse(ui.inputText, out int modelId))
                    {
                        // Validation robuste du modelId
                        if (modelId >= 0 && 
                            modelId < Nova.v.vehicleModels.Length && 
                            Nova.v.vehicleModels[modelId] != null)
                        {
                            player.ClosePanel(panel);
                            tcs.TrySetResult(modelId);
                        }
                        else
                        {
                            player.Notify("Erreur", $"Vehicule invalide (0-{Nova.v.vehicleModels.Length - 1}).", NotificationManager.Type.Error);
                        }
                    }
                    else
                    {
                        player.Notify("Erreur", "ID invalide.", NotificationManager.Type.Error);
                    }
                });
                
                player.ShowPanelUI(panel);
                return await tcs.Task;
            }
            catch (TaskCanceledException) 
            { 
                return -1; 
            }
        }

        private static async Task<int> OpenInputQuantityPanel(Player player)
        {
            try
            {
                TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                UIPanel panel = new UIPanel("Quantite (Vehicule)", UIPanel.PanelType.Input);
                panel.SetText("Nombre de vehicules :");
                panel.SetInputPlaceholder("Ex: 1");
                
                panel.AddButton("Fermer", ui =>
                {
                    player.ClosePanel(panel);
                    tcs.TrySetResult(-1);
                });
                
                panel.AddButton("Valider", ui =>
                {
                    if (int.TryParse(ui.inputText, out int quantity) && quantity > 0)
                    {
                        player.ClosePanel(panel);
                        tcs.TrySetResult(quantity);
                    }
                    else
                    {
                        player.Notify("Erreur", "Quantite invalide (minimum 1).", NotificationManager.Type.Error);
                    }
                });
                
                player.ShowPanelUI(panel);
                return await tcs.Task;
            }
            catch (TaskCanceledException) 
            { 
                return -1; 
            }
        }

        #endregion

        public static void OpenCreateStep5(Player player, NovaRewards plugin, string name, string type, double val1, int val2, string data)
        {
            UIPanel panel = new UIPanel("Etape 5/6 : Validite", UIPanel.PanelType.Input);
            panel.SetText("Duree en JOURS ? (0 = Infini) :");
            panel.SetInputPlaceholder("Ex: 30");
            
            panel.AddButton("Suivant", (ui) => 
            {
                if (int.TryParse(ui.inputText, out int d) && d >= 0)
                {
                    DateTime? exp = d > 0 ? DateTime.Now.AddDays(d) : (DateTime?)null;
                    OpenCreateStep6(player, plugin, name, type, val1, data, val2, exp);
                }
                else
                {
                    player.Notify("Erreur", "Valeur invalide.", NotificationManager.Type.Error);
                }
            });
            
            panel.AddButton("Retour", (ui) => 
            {
                if (type == "vehicle")
                {
                    var vehicles = string.IsNullOrEmpty(data) ? new Dictionary<int, int>() : JsonConvert.DeserializeObject<Dictionary<int, int>>(data);
                    RedirectToVehicleSteps(player, plugin, name, type, val1, val2, vehicles);
                }
                else
                {
                    OpenCreateStep4(player, plugin, name, type, val1);
                }
            });
            
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep6(Player player, NovaRewards plugin, string name, string type, double val1, string data, int val2, DateTime? exp)
        {
            UIPanel panel = new UIPanel("Etape 6/6 : Limites", UIPanel.PanelType.Input);
            panel.SetText("Nombre max d'utilisations ? (0 = Infini)");
            panel.SetInputPlaceholder("Ex: 100");
            
            panel.AddButton("CREER LE CODE", (ui) => 
            {
                if (int.TryParse(ui.inputText, out int max) && max >= 0)
                {
                    try
                    {
                        RewardCode newCode = new RewardCode 
                        { 
                            Name = name, 
                            Type = type, 
                            Value = val1, 
                            Data = data ?? string.Empty,
                            Quantity = val2, 
                            MaxUses = max, 
                            ExpirationDate = exp, 
                            CreatedBy = player.FullName, 
                            CreatedAt = DateTime.Now 
                        };
                        
                        plugin.CreateCode(newCode);
                        
                        player.Notify("Succes", $"Code '{name}' cree avec succes !", NotificationManager.Type.Success);
                        
                        // Log Discord détaillé
                        string details = $"Code: `{name}` | Type: {type}";
                        if (type == "money") details += $" | Montant: {val1}€";
                        else if (type == "random_money") details += $" | Montant: {val1}-{val2}€";
                        else if (type == "item") details += $" | Item: {val1} x{val2}";
                        else if (type == "vehicle") details += $" | Vehicules: {val2}";
                        
                        if (max > 0) details += $" | Max: {max}";
                        if (exp.HasValue) details += $" | Expire: {exp.Value:dd/MM/yyyy}";
                        
                        plugin.SendAdminLog(player, "Creation Code", details);
                        
                        player.ClosePanel(ui);
                        OpenMainPanel(player, plugin);
                    }
                    catch (Exception ex)
                    {
                        player.Notify("Erreur", "Erreur lors de la creation.", NotificationManager.Type.Error);
                        ModKit.Internal.Logger.LogError($"CreateCode: {ex.Message}", "NovaRewards");
                    }
                }
                else
                {
                    player.Notify("Erreur", "Valeur invalide.", NotificationManager.Type.Error);
                }
            });
            
            panel.AddButton("Retour", (ui) => OpenCreateStep5(player, plugin, name, type, val1, val2, data));
            player.ShowPanelUI(panel);
        }

        // --- LISTE ---
        public static void OpenListPanel(Player player, NovaRewards plugin)
        {
            UIPanel panel = new UIPanel("Liste des Codes", UIPanel.PanelType.Text);
            
            var codes = plugin._db.Table<RewardCode>().ToList();
            
            if (codes.Count == 0) 
            {
                panel.SetText("Aucun code actif.");
                panel.AddButton("Retour", (ui) => OpenMainPanel(player, plugin));
            }
            else
            {
                panel.SetText($"Total: {codes.Count} code(s)\nCliquez pour voir les details.");
                
                // Tri: codes actifs en premier, puis par date de création
                var sortedCodes = codes
                    .OrderBy(c => 
                    {
                        int used = plugin._db.Table<RewardHistory>().Count(h => h.CodeName == c.Name);
                        bool isDead = (c.MaxUses > 0 && used >= c.MaxUses) || 
                                     (c.ExpirationDate.HasValue && DateTime.Now > c.ExpirationDate.Value);
                        return isDead ? 1 : 0;
                    })
                    .ThenByDescending(c => c.CreatedAt);
                
                foreach (var c in sortedCodes) 
                {
                    int used = plugin._db.Table<RewardHistory>().Count(h => h.CodeName == c.Name);
                    bool isDead = (c.MaxUses > 0 && used >= c.MaxUses) || 
                                 (c.ExpirationDate.HasValue && DateTime.Now > c.ExpirationDate.Value);
                    string color = isDead ? "red" : "green";
                    string status = isDead ? " [INACTIF]" : "";

                    panel.AddButton($"<color={color}>{c.Name}</color>{status}", (ui) => 
                    {
                        OpenManageCodePanel(player, plugin, c);
                    });
                }
                
                panel.AddButton("Retour", (ui) => OpenMainPanel(player, plugin));
            }
            
            player.ShowPanelUI(panel);
        }

        // --- GESTION ---
        public static void OpenManageCodePanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel($"Gerer: {code.Name}", UIPanel.PanelType.Text);
            
            int used = plugin._db.Table<RewardHistory>().Count(h => h.CodeName == code.Name);
            bool isExpired = code.ExpirationDate.HasValue && DateTime.Now > code.ExpirationDate.Value;
            bool isMaxed = code.MaxUses > 0 && used >= code.MaxUses;
            
            string etat = isExpired ? "<color=red>EXPIRE (Date)</color>" : 
                         isMaxed ? "<color=red>EPUISE (Max)</color>" : 
                         "<color=green>ACTIF</color>";

            string typeLabel = code.Type switch
            {
                "money" => $"Argent fixe: {code.Value}€",
                "random_money" => $"Argent aleatoire: {code.Value}-{code.Quantity}€",
                "item" => $"Item ID {code.Value} x{code.Quantity}",
                "vehicle" => $"Vehicule(s)",
                _ => code.Type
            };

            string details = $"<b>Type:</b> {typeLabel}\n" +
                           $"<b>Utilisations:</b> {used} / {(code.MaxUses == 0 ? "Infini" : code.MaxUses.ToString())}\n" +
                           $"<b>Expiration:</b> {(code.ExpirationDate.HasValue ? code.ExpirationDate.Value.ToString("dd/MM/yyyy HH:mm") : "Jamais")}\n" +
                           $"<b>Cree par:</b> {code.CreatedBy}\n" +
                           $"<b>Cree le:</b> {code.CreatedAt:dd/MM/yyyy HH:mm}\n" +
                           $"<b>Etat:</b> {etat}";

            panel.SetText(details);

            panel.AddButton("Modifier la Date", (ui) => OpenEditDatePanel(player, plugin, code));
            panel.AddButton("Modifier la Limite Max", (ui) => OpenEditLimitPanel(player, plugin, code));
            panel.AddButton("Voir l'historique", (ui) => OpenHistoryPanel(player, plugin, code));
            panel.AddButton("<color=red>SUPPRIMER LE CODE</color>", (ui) => OpenDeleteConfirmPanel(player, plugin, code));
            panel.AddButton("Retour", (ui) => OpenListPanel(player, plugin));
            player.ShowPanelUI(panel);
        }

        public static void OpenHistoryPanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel($"Historique: {code.Name}", UIPanel.PanelType.Text);
            
            var history = plugin._db.Table<RewardHistory>()
                .Where(h => h.CodeName == code.Name)
                .OrderByDescending(h => h.DateClaimed)
                .Take(10)
                .ToList();
            
            if (history.Count == 0)
            {
                panel.SetText("Aucune utilisation enregistree.");
            }
            else
            {
                string text = $"Dernieres utilisations ({history.Count}):\n\n";
                foreach (var h in history)
                {
                    text += $"• SteamID: {h.SteamId}\n  Le {h.DateClaimed:dd/MM/yyyy HH:mm}\n\n";
                }
                panel.SetText(text);
            }
            
            panel.AddButton("Retour", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        public static void OpenEditLimitPanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel("Modifier Limite", UIPanel.PanelType.Input);
            panel.SetText($"Actuel: {(code.MaxUses == 0 ? "Infini" : code.MaxUses.ToString())}\nEntrez la nouvelle limite (0 = Infini) :");
            panel.SetInputPlaceholder(code.MaxUses.ToString());
            
            panel.AddButton("Valider", (ui) => 
            {
                if (int.TryParse(ui.inputText, out int newMax) && newMax >= 0)
                {
                    int oldMax = code.MaxUses;
                    code.MaxUses = newMax;
                    
                    try
                    {
                        plugin.UpdateCode(code);
                        player.Notify("Succes", "Limite mise a jour !", NotificationManager.Type.Success);
                        plugin.SendAdminLog(player, "Modif Limite", $"Code: `{code.Name}` | {oldMax} → {newMax}");
                        OpenManageCodePanel(player, plugin, code);
                    }
                    catch
                    {
                        player.Notify("Erreur", "Erreur lors de la mise a jour.", NotificationManager.Type.Error);
                    }
                }
                else
                {
                    player.Notify("Erreur", "Valeur invalide.", NotificationManager.Type.Error);
                }
            });
            
            panel.AddButton("Annuler", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        public static void OpenEditDatePanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel("Modifier Date", UIPanel.PanelType.Input);
            string currentDate = code.ExpirationDate.HasValue ? code.ExpirationDate.Value.ToString("dd/MM/yyyy") : "Infini";
            panel.SetText($"Actuel: {currentDate}\n\nEntrez le nombre de jours a partir de MAINTENANT (0 = Infini) :");
            panel.SetInputPlaceholder("Ex: 30");
            
            panel.AddButton("Valider", (ui) => 
            {
                if (int.TryParse(ui.inputText, out int days) && days >= 0)
                {
                    code.ExpirationDate = days > 0 ? DateTime.Now.AddDays(days) : (DateTime?)null;
                    
                    try
                    {
                        plugin.UpdateCode(code);
                        player.Notify("Succes", "Date mise a jour !", NotificationManager.Type.Success);
                        
                        string newDate = code.ExpirationDate.HasValue ? code.ExpirationDate.Value.ToString("dd/MM/yyyy") : "Infini";
                        plugin.SendAdminLog(player, "Modif Date", $"Code: `{code.Name}` → {newDate}");
                        
                        OpenManageCodePanel(player, plugin, code);
                    }
                    catch
                    {
                        player.Notify("Erreur", "Erreur lors de la mise a jour.", NotificationManager.Type.Error);
                    }
                }
                else
                {
                    player.Notify("Erreur", "Valeur invalide.", NotificationManager.Type.Error);
                }
            });
            
            panel.AddButton("Annuler", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        public static void OpenDeleteConfirmPanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel("Confirmation", UIPanel.PanelType.Text);
            panel.SetText($"<color=red>ATTENTION !</color>\n\nVoulez-vous vraiment supprimer le code '{code.Name}' ?\n\nCette action est irreversible.");

            panel.AddButton("<color=red>OUI, SUPPRIMER</color>", (ui) => 
            {
                try
                {
                    plugin.DeleteCode(code.Name);
                    player.Notify("Succes", $"Code '{code.Name}' supprime.", NotificationManager.Type.Success);
                    plugin.SendAdminLog(player, "Suppression Code", $"Code: `{code.Name}` supprime definitivement");
                    OpenListPanel(player, plugin);
                }
                catch
                {
                    player.Notify("Erreur", "Erreur lors de la suppression.", NotificationManager.Type.Error);
                    OpenManageCodePanel(player, plugin, code);
                }
            });
            
            panel.AddButton("NON, Annuler", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        // --- USER ---
        public static void OpenPlayerRedeemPanel(Player player, NovaRewards plugin)
        {
            UIPanel panel = new UIPanel("Code Cadeau", UIPanel.PanelType.Input);
            panel.SetText("Entrez votre code cadeau :");
            panel.SetInputPlaceholder("Ex: NOEL2025");
            
            panel.AddButton("Valider", (ui) => 
            {
                player.ClosePanel(ui);
                if (!string.IsNullOrEmpty(ui.inputText))
                {
                    plugin.RedeemCode(player, ui.inputText.Trim());
                }
                else
                {
                    player.Notify("Erreur", "Veuillez entrer un code.", NotificationManager.Type.Error);
                }
            });
            
            panel.AddButton("Annuler", (ui) => player.ClosePanel(ui));
            player.ShowPanelUI(panel);
        }
    }
}