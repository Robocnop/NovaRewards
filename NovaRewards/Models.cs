using SQLite;
using System;

namespace NovaRewards
{
    public class RewardCode
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Unique]
        public string Name { get; set; }

        public string Type { get; set; } // "money", "item", "random_money", "vehicle"
        public double Value { get; set; } // Montant ou ID Item ou Min (Random)
        public string Data { get; set; } = string.Empty; // Données supplémentaires (JSON Véhicules)
        public int Quantity { get; set; } // Qty Item ou Max (Random)
        
        public int MaxUses { get; set; } // 0 = Infini
        public DateTime? ExpirationDate { get; set; } // Date limite

        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class RewardHistory
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public string CodeName { get; set; }
        [Indexed]
        public string SteamId { get; set; }
        public DateTime DateClaimed { get; set; }
    }

    public class PluginConfig
    {
        public string DiscordWebhookUrl { get; set; } = "";
    }
}