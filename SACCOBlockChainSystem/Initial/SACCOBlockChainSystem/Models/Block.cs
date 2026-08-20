using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    public class Block
    {
        [Key]
        public int BlockId { get; set; }

        [Required]
        [StringLength(255)]
        public string BlockHash { get; set; } = null!;

        [StringLength(255)]
        public string? PreviousHash { get; set; }

        public DateTime Timestamp { get; set; }

        [StringLength(255)]
        public string? MerkleRoot { get; set; }

        public int Nonce { get; set; } = 0;

        public bool Confirmed { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual ICollection<BlockchainTransaction> Transactions { get; set; } = new List<BlockchainTransaction>();
    }
}