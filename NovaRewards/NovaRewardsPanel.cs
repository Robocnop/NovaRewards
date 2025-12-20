using System;
using System.Linq;
using Life;
using Life.Network;
using Life.UI;
using ModKit.Helper;

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
            panel.AddButton("Suivant", (ui) => {
                if(!string.IsNullOrEmpty(ui.inputText)) OpenCreateStep2(player, plugin, ui.inputText.Trim().ToUpper());
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
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep3(Player player, NovaRewards plugin, string name, string type)
        {
            UIPanel panel = new UIPanel("Etape 3/6 : Valeur", UIPanel.PanelType.Input);
            if(type == "money") panel.SetText("Montant en euros :");
            else if(type == "random_money") panel.SetText("Montant MINIMUM :");
            else panel.SetText("ID de l'Item :");

            panel.AddButton("Suivant", (ui) => {
                if(double.TryParse(ui.inputText, out double v)) OpenCreateStep4(player, plugin, name, type, v);
                else player.Notify("Erreur", "Valeur incorrecte.", NotificationManager.Type.Error);
            });
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep4(Player player, NovaRewards plugin, string name, string type, double val1)
        {
            if(type == "money") { OpenCreateStep5(player, plugin, name, type, val1, 0); return; }

            UIPanel panel = new UIPanel("Etape 4/6 : Detail", UIPanel.PanelType.Input);
            if(type == "random_money") panel.SetText($"Min: {val1}€. Entrez le MAXIMUM :");
            else panel.SetText("Quantite d'objets :");

            panel.AddButton("Suivant", (ui) => {
                if(int.TryParse(ui.inputText, out int v2)) OpenCreateStep5(player, plugin, name, type, val1, v2);
                else player.Notify("Erreur", "Nombre incorrect.", NotificationManager.Type.Error);
            });
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep5(Player player, NovaRewards plugin, string name, string type, double val1, int val2)
        {
            UIPanel panel = new UIPanel("Etape 5/6 : Validite", UIPanel.PanelType.Input);
            panel.SetText("Duree en JOURS ? (0 = Infini) :");
            panel.AddButton("Suivant", (ui) => {
                if(int.TryParse(ui.inputText, out int d)) {
                    DateTime? exp = d > 0 ? DateTime.Now.AddDays(d) : (DateTime?)null;
                    OpenCreateStep6(player, plugin, name, type, val1, val2, exp);
                }
            });
            player.ShowPanelUI(panel);
        }

        public static void OpenCreateStep6(Player player, NovaRewards plugin, string name, string type, double val1, int val2, DateTime? exp)
        {
            UIPanel panel = new UIPanel("Etape 6/6 : Limites", UIPanel.PanelType.Input);
            panel.SetText("Nombre max d'utilisations ? (0 = Infini)");
            panel.AddButton("CREER LE CODE", (ui) => {
                if(int.TryParse(ui.inputText, out int max)) {
                    plugin.CreateCode(new RewardCode { Name = name, Type = type, Value = val1, Quantity = val2, MaxUses = max, ExpirationDate = exp, CreatedBy = player.FullName, CreatedAt = DateTime.Now });
                    
                    player.Notify("Succes", "Code cree avec succes !", NotificationManager.Type.Success);
                    
                    // Log Discord
                    plugin.SendAdminLog(player, "Creation Code", $"Code: {name} | Type: {type}");
                    
                    OpenMainPanel(player, plugin);
                }
            });
            player.ShowPanelUI(panel);
        }

        // --- LISTE ---
        public static void OpenListPanel(Player player, NovaRewards plugin)
        {
            UIPanel panel = new UIPanel("Liste des Codes", UIPanel.PanelType.Text);
            
            var codes = plugin._db.Table<RewardCode>().ToList();
            
            if(codes.Count == 0) panel.SetText("Aucun code actif.");
            else
            {
                panel.SetText("Cliquez sur un code pour voir les détails.");
                
                foreach(var c in codes.OrderBy(x => x.ExpirationDate.HasValue && x.ExpirationDate < DateTime.Now)) 
                {
                    int used = plugin._db.Table<RewardHistory>().Count(h => h.CodeName == c.Name);
                    bool isDead = (c.MaxUses > 0 && used >= c.MaxUses) || (c.ExpirationDate != null && DateTime.Now > c.ExpirationDate);
                    string color = isDead ? "red" : "green";

                    // Affichage Simple (Nom coloré)
                    panel.AddButton($"<color={color}>{c.Name}</color>", (ui) => {
                        OpenManageCodePanel(player, plugin, c);
                    });
                }
            }
            panel.AddButton("Retour", (ui) => OpenMainPanel(player, plugin));
            player.ShowPanelUI(panel);
        }

        // --- GESTION ---
        public static void OpenManageCodePanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel($"Gérer: {code.Name}", UIPanel.PanelType.Text);
            
            int used = plugin._db.Table<RewardHistory>().Count(h => h.CodeName == code.Name);
            string etat = (code.ExpirationDate != null && DateTime.Now > code.ExpirationDate) ? "<color=red>EXPIRE (Date)</color>" : 
                          (code.MaxUses > 0 && used >= code.MaxUses) ? "<color=red>EPUISE (Max)</color>" : "<color=green>ACTIF</color>";

            string details = $"Type: {code.Type}\n" +
                             $"Valeur: {code.Value} / Qty: {code.Quantity}\n" +
                             $"Utilisations: {used} / {(code.MaxUses == 0 ? "Infini" : code.MaxUses.ToString())}\n" +
                             $"Expiration: {(code.ExpirationDate.HasValue ? code.ExpirationDate.Value.ToString("dd/MM/yyyy HH:mm") : "Jamais")}\n" +
                             $"Etat: {etat}";

            panel.SetText(details);

            panel.AddButton("Modifier la Date", (ui) => OpenEditDatePanel(player, plugin, code));
            panel.AddButton("Modifier la Limite Max", (ui) => OpenEditLimitPanel(player, plugin, code));
            panel.AddButton("<color=red>SUPPRIMER LE CODE</color>", (ui) => OpenDeleteConfirmPanel(player, plugin, code));
            panel.AddButton("Retour", (ui) => OpenListPanel(player, plugin));
            player.ShowPanelUI(panel);
        }

        public static void OpenEditLimitPanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel("Modifier Limite", UIPanel.PanelType.Input);
            panel.SetText($"Actuel: {(code.MaxUses == 0 ? "Infini" : code.MaxUses.ToString())}\nEntrez la nouvelle limite (0 = Infini) :");
            panel.SetInputPlaceholder(code.MaxUses.ToString());
            
            panel.AddButton("Valider", (ui) => {
                if(int.TryParse(ui.inputText, out int newMax)) {
                    int oldMax = code.MaxUses;
                    code.MaxUses = newMax;
                    plugin.UpdateCode(code);
                    
                    player.Notify("Succes", "Limite mise a jour !", NotificationManager.Type.Success);
                    plugin.SendAdminLog(player, "Modif Limite", $"Code: {code.Name} | Old: {oldMax} -> New: {newMax}");
                    
                    OpenManageCodePanel(player, plugin, code);
                }
            });
            panel.AddButton("Annuler", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        public static void OpenEditDatePanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel("Modifier Date", UIPanel.PanelType.Input);
            panel.SetText("Entrez le nombre de jours a partir de MAINTENANT (0 = Infini) :\nCela remplacera l'ancienne date.");
            
            panel.AddButton("Valider", (ui) => {
                if(int.TryParse(ui.inputText, out int days)) {
                    code.ExpirationDate = days > 0 ? DateTime.Now.AddDays(days) : (DateTime?)null;
                    plugin.UpdateCode(code);
                    
                    player.Notify("Succes", "Date mise a jour !", NotificationManager.Type.Success);
                    string newDate = code.ExpirationDate.HasValue ? code.ExpirationDate.Value.ToString("dd/MM") : "Infini";
                    plugin.SendAdminLog(player, "Modif Date", $"Code: {code.Name} -> {newDate}");

                    OpenManageCodePanel(player, plugin, code);
                }
            });
            panel.AddButton("Annuler", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        public static void OpenDeleteConfirmPanel(Player player, NovaRewards plugin, RewardCode code)
        {
            UIPanel panel = new UIPanel("Confirmation", UIPanel.PanelType.Text);
            panel.SetText($"<color=red>ATTENTION !</color>\nVoulez-vous vraiment supprimer le code {code.Name} ?");

            panel.AddButton("<color=red>OUI, SUPPRIMER</color>", (ui) => {
                plugin.DeleteCode(code.Name);
                
                player.Notify("Succes", "Code supprime avec succes.", NotificationManager.Type.Success);
                plugin.SendAdminLog(player, "Suppression Code", $"Code: {code.Name} supprime");

                OpenListPanel(player, plugin);
            });
            
            panel.AddButton("NON, Annuler", (ui) => OpenManageCodePanel(player, plugin, code));
            player.ShowPanelUI(panel);
        }

        // --- USER ---
        public static void OpenPlayerRedeemPanel(Player player, NovaRewards plugin)
        {
            UIPanel panel = new UIPanel("Code Cadeau", UIPanel.PanelType.Input);
            panel.SetText("Entrez votre code cadeau :");
            panel.SetInputPlaceholder("Ex: NOEL");
            panel.AddButton("Valider", (ui) => {
                player.ClosePanel(ui);
                if(!string.IsNullOrEmpty(ui.inputText)) plugin.RedeemCode(player, ui.inputText.Trim());
            });
            panel.AddButton("Annuler", (ui) => player.ClosePanel(ui));
            player.ShowPanelUI(panel);
        }
    }
}